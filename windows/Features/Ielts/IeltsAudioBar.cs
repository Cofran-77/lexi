using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lexi.Features.Ielts;

/// <summary>
/// 独立紧凑章节音频播放器：
/// 支持播放、暂停、停止、前进/后退 10 秒、拖动进度 Seek 与时间指示。
/// 无资源时诚实解释原因，单一播放所有者。
/// </summary>
public sealed class IeltsAudioBar : Border, IDisposable
{
    private readonly Func<IWordAudioPlayer> _playerProvider;
    private readonly Action<string> _setStatus;
    private readonly DispatcherTimer _pollTimer;

    private readonly TextBlock _titleBlock;
    private readonly Button _rewindBtn;
    private readonly Button _playPauseBtn;
    private readonly Button _forwardBtn;
    private readonly Button _stopBtn;
    private readonly TextBlock _currentTimeBlock;
    private readonly Slider _seekSlider;
    private readonly TextBlock _durationBlock;
    private readonly TextBlock _noAudioNotice;
    private readonly Grid _controlsGrid;

    private string? _currentAudioFile;
    private string _currentSectionTitle = "";
    private bool _isDraggingSlider;
    private bool _hasAudio;

    public IeltsAudioBar(Func<IWordAudioPlayer> playerProvider, Action<string> setStatus)
    {
        _playerProvider = playerProvider;
        _setStatus = setStatus;

        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(20, 10);
        MinHeight = 52;
        Background = Brushes.Transparent;
        this.Bind(BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var mainLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        // 左侧：录音章节名称
        _titleBlock = new TextBlock
        {
            FontWeight = FontWeight.Medium,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0)
        };
        _titleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        Grid.SetColumn(_titleBlock, 0);
        mainLayout.Children.Add(_titleBlock);

        // 中部：播放器控件栏
        _controlsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,8,Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        _rewindBtn = new Button
        {
            Content = "-10s",
            Classes = { "secondary" },
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        _rewindBtn.Click += (_, _) => SeekRelative(-10);
        Grid.SetColumn(_rewindBtn, 0);
        _controlsGrid.Children.Add(_rewindBtn);

        _playPauseBtn = new Button
        {
            Content = IeltsI18n.T("播放"),
            Classes = { "secondary" },
            Padding = new Thickness(14, 6),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        _playPauseBtn.Click += (_, _) => TogglePlayPause();
        Grid.SetColumn(_playPauseBtn, 1);
        _controlsGrid.Children.Add(_playPauseBtn);

        _forwardBtn = new Button
        {
            Content = "+10s",
            Classes = { "secondary" },
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        _forwardBtn.Click += (_, _) => SeekRelative(10);
        Grid.SetColumn(_forwardBtn, 2);
        _controlsGrid.Children.Add(_forwardBtn);

        _stopBtn = new Button
        {
            Content = IeltsI18n.T("停止"),
            Classes = { "secondary" },
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 12
        };
        _stopBtn.Click += (_, _) => Stop();
        Grid.SetColumn(_stopBtn, 3);
        _controlsGrid.Children.Add(_stopBtn);

        _currentTimeBlock = new TextBlock
        {
            Text = "00:00",
            Classes = { "muted" },
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(_currentTimeBlock, 5);
        _controlsGrid.Children.Add(_currentTimeBlock);

        _seekSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        };
        _seekSlider.AddHandler(InputElement.PointerPressedEvent, (_, _) => _isDraggingSlider = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seekSlider.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            _isDraggingSlider = false;
            var player = _playerProvider();
            player.Seek(_seekSlider.Value);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && _isDraggingSlider)
            {
                _currentTimeBlock.Text = FormatTime(_seekSlider.Value);
            }
        };
        Grid.SetColumn(_seekSlider, 6);
        _controlsGrid.Children.Add(_seekSlider);

        _durationBlock = new TextBlock
        {
            Text = "00:00",
            Classes = { "muted" },
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(_durationBlock, 7);
        _controlsGrid.Children.Add(_durationBlock);

        Grid.SetColumn(_controlsGrid, 1);
        mainLayout.Children.Add(_controlsGrid);

        // 无音频录音时的诚实提示
        _noAudioNotice = new TextBlock
        {
            Text = IeltsI18n.T("当前章节暂无独立音频录音。"),
            Classes = { "muted" },
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        Grid.SetColumn(_noAudioNotice, 1);
        mainLayout.Children.Add(_noAudioNotice);

        Child = mainLayout;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 700;
            _titleBlock.IsVisible = !compact;
            mainLayout.ColumnDefinitions[0].Width = compact ? new GridLength(0) : GridLength.Auto;
        };

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    public void LoadSection(LearningSection section)
    {
        _currentSectionTitle = section.Title;
        _currentAudioFile = IeltsCatalog.ResolveAsset(section.AudioPath);
        _hasAudio = !string.IsNullOrEmpty(_currentAudioFile);

        _titleBlock.Text = IeltsI18n.T("章节录音") + " · " + section.Title;
        _controlsGrid.IsVisible = _hasAudio;
        _noAudioNotice.IsVisible = !_hasAudio;

        // 切章时停止之前章节的录音播放
        Stop();
    }

    private void TogglePlayPause()
    {
        if (!_hasAudio || string.IsNullOrEmpty(_currentAudioFile))
        {
            _setStatus(IeltsI18n.T("当前章节暂无独立音频录音。"));
            return;
        }

        var player = _playerProvider();
        if (player.IsPlaying)
        {
            player.TogglePause();
            _playPauseBtn.Content = IeltsI18n.T("播放");
        }
        else
        {
            if (player.Position > 0 && player.Position < player.Duration - 1)
            {
                player.TogglePause();
            }
            else
            {
                player.Play("", _currentAudioFile);
            }
            _playPauseBtn.Content = IeltsI18n.T("暂停");
        }
    }

    private void SeekRelative(double seconds)
    {
        if (!_hasAudio) return;
        var player = _playerProvider();
        player.Seek(player.Position + seconds);
    }

    public void Stop()
    {
        var player = _playerProvider();
        player.Stop();
        _playPauseBtn.Content = IeltsI18n.T("播放");
        _seekSlider.Value = 0;
        _currentTimeBlock.Text = "00:00";
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_hasAudio) return;
        var player = _playerProvider();
        var isPlaying = player.IsPlaying;

        if (isPlaying)
        {
            _playPauseBtn.Content = IeltsI18n.T("暂停");
            var pos = player.Position;
            var dur = player.Duration;

            if (dur > 0 && _seekSlider.Maximum != dur)
                _seekSlider.Maximum = dur;

            if (!_isDraggingSlider)
            {
                _seekSlider.Value = pos;
                _currentTimeBlock.Text = FormatTime(pos);
            }
            _durationBlock.Text = FormatTime(dur);

            // 播放完成回位
            if (dur > 0 && pos >= dur - 0.3)
            {
                _playPauseBtn.Content = IeltsI18n.T("播放");
            }
        }
        else if (_playPauseBtn.Content?.ToString() == IeltsI18n.T("暂停"))
        {
            _playPauseBtn.Content = IeltsI18n.T("播放");
        }
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void RefreshLanguage()
    {
        _titleBlock.Text = IeltsI18n.T("章节录音") + (_currentSectionTitle.Length > 0 ? " · " + _currentSectionTitle : "");
        _noAudioNotice.Text = IeltsI18n.T("当前章节暂无独立音频录音。");
        _stopBtn.Content = IeltsI18n.T("停止");

        var player = _playerProvider();
        _playPauseBtn.Content = player.IsPlaying ? IeltsI18n.T("暂停") : IeltsI18n.T("播放");
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        Stop();
    }
}
