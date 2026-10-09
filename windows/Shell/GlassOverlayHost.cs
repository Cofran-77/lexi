using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Lexi.Services;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Lexi.Shell;

/// <summary>Blurs an in-memory snapshot when an overlay opens or the window resizes.
/// Snapshots are never persisted. Business controls remain outside the blurred layer.</summary>
public sealed class GlassOverlayHost : IDisposable
{
    private readonly Control _background;
    private readonly Window _window;
    private readonly HashSet<Control> _modals=[];
    private readonly HashSet<object> _popups=[];
    private readonly HashSet<FlyoutBase> _tracked=[];
    private readonly Image _backdrop;
    private Bitmap? _blurredBitmap;
    private readonly IDisposable _popupSubscription;
    private IInputElement? _previousFocus;
    public bool IsOpen => _modals.Any(m=>m.IsVisible) || _popups.Count>0;
    public bool IsBlurred => _blurredBitmap!=null && _backdrop.IsVisible;
    public bool ForceSolid { get; set; }
    public double LastCaptureMilliseconds { get; private set; }
    public long RetainedBackdropBytes => _blurredBitmap==null?0:(long)_blurredBitmap.PixelSize.Width*_blurredBitmap.PixelSize.Height*4;
    public GlassOverlayHost(Window window, Control background)
    {
        _window=window; _background=background;
        _backdrop=new Image {IsVisible=false,IsHitTestVisible=false,Stretch=Stretch.Fill};
        _backdrop.SetValue(Panel.ZIndexProperty,180);
        ((Panel)background.Parent!).Children.Add(_backdrop);
        _popupSubscription=Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup,e)=>
        {
            var target=popup.PlacementTarget;
            if(target==null || TopLevel.GetTopLevel(target)!=_window)return;
            if(popup.IsOpen)
            {
                ApplyPopupPalette(popup);
                Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(popup.IsOpen)ApplyPopupPalette(popup);});
            }
            // Tooltips stay local and never blur the entire reading surface.
            if(popup.Child is ToolTip)return;
            if(popup.IsOpen)_popups.Add(popup);else _popups.Remove(popup);
            Refresh();
        });
        background.SizeChanged+=(_,_)=>{if(IsOpen)RebuildBackdrop();};
        window.AddHandler(InputElement.KeyDownEvent,TrapModalFocus,Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private void ApplyPopupPalette(Popup popup)
    {
        if(popup.Child is not TemplatedControl presenter)return;
        var ink=(IBrush)_window.FindResource(_window.ActualThemeVariant,"InkBrush")!;
        // Native PopupRoot is a separate theme scope; mirror the owning window.
        presenter.Resources["InkBrush"]=ink;
        presenter.Resources["GlassEdgeBrush"]=_window.FindResource(_window.ActualThemeVariant,"GlassEdgeBrush")!;
        presenter.Foreground=ink;
        presenter.Background=(IBrush)_window.FindResource(_window.ActualThemeVariant,ForceSolid || !OverlayMaterialPolicy.TransparencyEnabled?"DialogSurfaceBrush":"GlassSurfaceBrush")!;
        foreach(var item in presenter.GetVisualDescendants().OfType<MenuItem>())item.Foreground=ink;
        foreach(var label in presenter.GetVisualDescendants().OfType<TextBlock>())
            if(!label.GetVisualAncestors().OfType<Button>().Any())label.Foreground=ink;
    }

    public void Register(Border overlay)
    {
        if(_modals.Contains(overlay))return;
        _modals.Add(overlay);
        overlay.SetValue(Panel.ZIndexProperty,200);
        overlay.Background=new SolidColorBrush(Color.FromArgb(35,15,25,40));
        if(overlay.Child is Border surface) Decorate(surface);
        overlay.PropertyChanged+=OnVisibilityChanged;
    }
    public void Track(FlyoutBase flyout)
    {
        flyout.Opened+=(_,_)=>{_popups.Add(flyout);Refresh();};
        flyout.Closed+=(_,_)=>{_popups.Remove(flyout);Refresh();};
    }
    public void Unregister(Border overlay)
    {
        overlay.PropertyChanged-=OnVisibilityChanged;_modals.Remove(overlay);Refresh();
    }
    public void TrackOnce(FlyoutBase flyout)
    {
        _popups.Add(flyout);
        if(_tracked.Add(flyout)) flyout.Closed+=(_,_)=>{_popups.Remove(flyout);Refresh();};
        Refresh();
    }
    private void OnVisibilityChanged(object? sender,AvaloniaPropertyChangedEventArgs e)
    {
        if(e.Property!=Visual.IsVisibleProperty) return;
        if(sender is Control {IsVisible:true} control)
        {
            _previousFocus=_window.FocusManager?.GetFocusedElement();
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>control.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>b.IsEnabled && !b.Classes.Contains("danger"))?.Focus());
        }
        Refresh();
    }
    public void Refresh()
    {
        var modalOpen=_modals.Any(m=>m.IsVisible);
        _background.IsHitTestVisible=!modalOpen;
        _background.IsEnabled=!modalOpen;
        foreach(var modal in _modals)
            if(modal is Border {Child: Border surface})
                surface.Bind(Border.BackgroundProperty,surface.GetResourceObservable(ForceSolid || !OverlayMaterialPolicy.TransparencyEnabled?"DialogSurfaceBrush":"GlassSurfaceBrush"));
        if(IsOpen && OverlayMaterialPolicy.TransparencyEnabled && !ForceSolid)
        { if(_blurredBitmap==null)RebuildBackdrop(); }
        else { _background.Opacity=1;_backdrop.IsVisible=false;_backdrop.Source=null;_blurredBitmap?.Dispose();_blurredBitmap=null; }
        if(!IsOpen) { _previousFocus?.Focus(); _previousFocus=null; }
    }
    private void RebuildBackdrop()
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var width=(int)_background.Bounds.Width;var height=(int)_background.Bounds.Height;
        if(width<1||height<1)return;
        if(ForceSolid || !OverlayMaterialPolicy.TransparencyEnabled)return;
        try
        {
        // Render only our application layer; never the desktop or the foreground dialog.
        using var rendered=new RenderTargetBitmap(new PixelSize(width,height),new Vector(96,96));
        _background.Opacity=1;_backdrop.IsVisible=false;
        var surfaces=_modals.Where(m=>m.IsVisible).Select(m=>(m,m.Opacity)).ToArray();
        foreach(var (modal,_) in surfaces)modal.Opacity=0;
        try {rendered.Render(_window);} finally {foreach(var (modal,opacity) in surfaces)modal.Opacity=opacity;}
        using var original=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul));
        rendered.CopyPixels(new PixelRect(0,0,width,height),original.GetPixels(),original.ByteCount,original.RowBytes);
        using var output=new SKBitmap(original.Info);
        using(var canvas=new SKCanvas(output))
        using(var filter=SKImageFilter.CreateBlur(8,8))
        using(var paint=new SKPaint {ImageFilter=filter}) canvas.DrawBitmap(original,0,0,paint);
        var replacement=new WriteableBitmap(new PixelSize(width,height),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
        using(var frame=replacement.Lock())
        {
            var pixels=output.Bytes;
            for(var row=0;row<height;row++)Marshal.Copy(pixels,row*output.RowBytes,frame.Address+row*frame.RowBytes,width*4);
        }
        _backdrop.Source=replacement;_blurredBitmap?.Dispose();_blurredBitmap=replacement;_backdrop.IsVisible=true;_background.Opacity=0;
        }
        catch
        {
            // Unsupported renderers keep a readable same-theme solid surface.
            _background.Opacity=1;_backdrop.IsVisible=false;_backdrop.Source=null;_blurredBitmap?.Dispose();_blurredBitmap=null;
            foreach(var modal in _modals)if(modal is Border {Child: Border surface})surface.Bind(Border.BackgroundProperty,surface.GetResourceObservable("DialogSurfaceBrush"));
        }
        finally{LastCaptureMilliseconds=watch.Elapsed.TotalMilliseconds;}
    }
    private void TrapModalFocus(object? sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Tab)return;
        var modal=_modals.LastOrDefault(m=>m.IsVisible);
        if(modal==null)return;
        var candidates=modal.GetVisualDescendants().OfType<Control>().Where(c=>c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.TabIndex>=0).ToList();
        if(candidates.Count==0){e.Handled=true;return;}
        var index=candidates.IndexOf(_window.FocusManager?.GetFocusedElement() as Control);
        index=(index+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?-1:1)+candidates.Count)%candidates.Count;
        candidates[index].Focus();e.Handled=true;
    }
    public static void Decorate(Border surface)
    {
        surface.Classes.Remove("card"); surface.Classes.Add("glass-surface");
        surface.CornerRadius=new CornerRadius(14);surface.BorderThickness=new Thickness(1);
        if(surface.Padding==default)surface.Padding=new Thickness(24);
        surface.Bind(Border.BackgroundProperty,surface.GetResourceObservable("GlassSurfaceBrush"));
        surface.Bind(Border.BorderBrushProperty,surface.GetResourceObservable("GlassEdgeBrush"));
        surface.BoxShadow=BoxShadows.Parse("0 8 30 0 #20203045");
    }
    public void Dispose()
    {
        foreach(var modal in _modals) modal.PropertyChanged-=OnVisibilityChanged;
        _popupSubscription.Dispose();_modals.Clear();_popups.Clear();_background.Effect=null;_background.Opacity=1;_previousFocus=null;
        _background.IsEnabled=true;_background.IsHitTestVisible=true;
        _window.RemoveHandler(InputElement.KeyDownEvent,TrapModalFocus);
        _backdrop.Source=null;_blurredBitmap?.Dispose();_blurredBitmap=null;
        if(_backdrop.Parent is Panel parent)parent.Children.Remove(_backdrop);
    }
}
