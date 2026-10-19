using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Media;
using Lexi.Social;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lexi;

public partial class MainWindow
{
    private ScrollViewer? _socialPage;
    private StackPanel? _socialBody;
    private Button? _socialNav;
    private TextBlock? _socialMessage;
    private Border? _socialMessageCard;
    private SocialCloudOptions? _socialOptions;
    private string? _socialConfigurationError;
    private SupabaseSocialClient? _socialClient;
    private SocialStore? _socialStore;
    private SocialSyncCoordinator? _socialSync;
    private WindowsSocialSecretStore? _socialSecrets;
    private WindowsOAuthBrowser? _socialOAuthBrowser;
    private readonly SocialOAuthFlow _socialOAuth = new();
    private TaskCompletionSource<Uri>? _socialProtocolCallback;
    private CancellationTokenSource _socialCancellation = new();
    private long _socialGeneration;
    private bool _socialLoginPending;
    private DispatcherTimer? _socialSyncTimer;
    private bool _socialRecoveryPending;
    private bool _socialInitialized;
    private bool _socialCloudUnavailable;
    private bool _socialClosedSubscribed;
    private string SocialDirectory => Path.GetDirectoryName(_vocabService.DatabasePath)!;
    private bool SocialCanNavigate => _socialInitialized && _databaseAvailable && !_restoring && !_isForceClose;
    private bool FocusCanNavigate => SocialCanNavigate;
    private static bool SocialAutomatedTestRun => Environment.GetCommandLineArgs().Any(arg =>
        arg is "--self-test" or "--ui-smoke" or "--visual-test" or "--visual123-test" or "--glass124-test"
            or "--learning-integration" or "--focus-integration" or "--quick-test" or "--language-test"
            or "--redesign-test" or "--interaction-test" or "--improvement-test" or "--social-tests");
    private sealed record SocialLocalizedText(string DisplayedText, string? Chinese = null, string? English = null);

    private static string T(string chinese) => UiText.Text(chinese);
    private static string TF(FormattableString value) => UiText.Format(value);

    private void InitializeSocial()
    {
        if (_socialInitialized) return;
        var navHost = this.FindControl<StackPanel>("LearningNavHost");
        var pagesHost = this.FindControl<Panel>("PagesHost");
        if (navHost is null || pagesHost is null) return;

        _socialInitialized = true;
        _socialNav = new Button
        {
            Name = "NavSocial",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        _socialNav.Classes.Add("nav");
        SocialLocalize(_socialNav, "一起学习", "Study Together");
        _socialNav.Click += (_, _) => { if (SocialCanNavigate) ShowPage("social"); };
        navHost.Children.Add(_socialNav);

        _socialBody = new StackPanel { Spacing = 16, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        _socialPage = new ScrollViewer
        {
            Name = "SocialPage",
            IsVisible = false,
            Margin = new Thickness(30, 24, 30, 20),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = _socialBody
        };
        pagesHost.Children.Add(_socialPage);
        // Leaving the page or closing the window must stop every chart timer it owns.
        _socialPage.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Visual.IsVisibleProperty && !_socialPage.IsVisible)
            {
                _socialOAuth.Cancel();
                SocialAbortPendingProtocolCallback();
                _socialOAuthBrowser?.Dispose();
                _socialOAuthBrowser = null;
                _socialLoginPending = false;
                _socialSyncTimer?.Stop();
                SocialStopCharts();
            }
        };
        _socialSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _socialSyncTimer.Tick += (_, _) =>
        {
            _socialSyncTimer.Stop();
            if (_socialClient?.UserId is { } actor) _ = SocialRefreshAsync(actor);
        };

        LanguageToggleBtn.Click += (_, _) => Dispatcher.UIThread.Post(RefreshSocialLanguage);
        SettingsLanguageCombo.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshSocialLanguage);
        if (!_socialClosedSubscribed)
        {
            _socialClosedSubscribed = true;
            Closed += (_, _) =>
            {
                ++_socialGeneration;
                _socialOAuth.Cancel();
                SocialAbortPendingProtocolCallback();
                _socialOAuthBrowser?.Dispose();
                _socialOAuthBrowser = null;
                _socialLoginPending = false;
                _socialCancellation.Cancel();
                SocialStopCharts();
                _socialSyncTimer?.Stop();
                _socialClient?.Dispose();
                _socialCancellation.Dispose();
            };
        }
        SocialResumeOnStartup();
        RenderSocialEntry();
    }

    /// <summary>重启后如果本地已保存登录态，就让后台同步继续跑，而不是等用户再打开社交页。</summary>
    private void SocialResumeOnStartup()
    {
        if (SocialAutomatedTestRun) return;
        try
        {
            if (!SocialEnsureServices() || _socialClient?.UserId is not { } actor) return;
            SocialSessionReady();
            _ = SocialRefreshAsync(actor);
        }
        catch
        {
            // 启动阶段的社交异常只影响云同步，本地学习与缓存保持不变。
        }
    }

    private void SetSocialPageVisible(bool visible)
    {
        if (!_socialInitialized || _socialPage is null) return;
        _socialPage.IsVisible = visible;
        _socialNav?.Classes.Set("active", visible);
        if (visible) OnSocialPageShown();
        else
        {
            _socialOAuth.Cancel();
            SocialAbortPendingProtocolCallback();
            _socialOAuthBrowser?.Dispose();
            _socialOAuthBrowser = null;
            _socialLoginPending = false;
            _socialSyncTimer?.Stop();
            SocialStopCharts();
        }
    }

    private void OnSocialPageShown()
    {
        if (!_socialInitialized || _socialPage is null) return;
        try
        {
            if (!SocialEnsureServices())
            {
                RenderSocialEntry();
                return;
            }
            RenderSocialEntry();
            if (_socialClient?.UserId is { } actor) _ = SocialRefreshAsync(actor);
        }
        catch
        {
            _socialConfigurationError = UiText.Bilingual(
                "社交服务暂时不可用；本地学习数据和缓存均未改动。",
                "The social service is unavailable. Local study data and cache were not changed.");
            RenderSocialEntry();
        }
    }

    private bool SocialEnsureServices()
    {
        if (_socialClient is not null && _socialStore is not null && _socialSync is not null) return true;
        try
        {
            _socialOptions ??= SocialCloudOptions.FromConfiguration(SocialDirectory, SocialAutomatedTestRun);
            if (_socialOptions is null)
            {
                _socialCloudUnavailable = true;
                _socialConfigurationError = UiText.Bilingual(
                    "云服务未配置：请在 social-public.json 或 LEXI_SUPABASE_* 中配置公开项目地址与 publishable key。",
                    "Cloud service is not configured. Add the public project URL and publishable key to social-public.json or LEXI_SUPABASE_*.");
                return false;
            }
            _socialCloudUnavailable = false;
            _socialStore ??= new SocialStore(SocialDirectory);
            _socialSecrets ??= new WindowsSocialSecretStore(SocialDirectory);
            _socialClient ??= new SupabaseSocialClient(_socialOptions, _socialSecrets);
            _socialSync ??= new SocialSyncCoordinator(_socialStore, _socialClient);
            _socialConfigurationError = null;
            return true;
        }
        catch
        {
            _socialCloudUnavailable = _socialOptions is null;
            _socialConfigurationError = UiText.Bilingual(
                "社交服务配置或登录状态暂无法读取；原文件和账号信息已保留。",
                "Social configuration or sign-in state could not be read. Original files and account information were preserved.");
            return false;
        }
    }

    private TextBlock SocialText(string text, double size = 14)
    {
        var control = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        SocialLocalize(control, text);
        return control;
    }

    private void SocialLocalize(Control control, string text)
    {
        var displayed = UiText.Redisplay(text);
        control.Tag = new SocialLocalizedText(displayed);
        SocialSetLocalizedValue(control, displayed);
    }

    private void SocialLocalize(Control control, string chinese, string english)
    {
        var displayed = UiText.Language == "en" ? english : chinese;
        control.Tag = new SocialLocalizedText(displayed, chinese, english);
        SocialSetLocalizedValue(control, displayed);
    }

    private static void SocialSetLocalizedValue(Control control, string displayed)
    {
        switch (control)
        {
            case TextBlock label: label.Text = displayed; break;
            case CheckBox check: check.Content = displayed; break;
            case Button button: button.Content = displayed; break;
            case TextBox box: box.Watermark = displayed; break;
            case Expander expander: expander.Header = displayed; break;
        }
    }

    private TextBox SocialInput(string name, string watermark, int maxLength)
    {
        var input = new TextBox { Name = name, MaxLength = maxLength };
        SocialLocalize(input, watermark);
        return input;
    }

    private TextBlock SocialTextPair(string chinese, string english, double size = 14)
    {
        var control = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        SocialLocalize(control, chinese, english);
        return control;
    }

    private Expander SocialExpander(string header, Control content)
    {
        var expander = new Expander { Content = content };
        SocialLocalize(expander, header);
        return expander;
    }

    private void SocialSetMessage(string text)
    {
        if (_socialMessage is null) return;
        SocialLocalize(_socialMessage, text);
        if (_socialMessageCard is not null) _socialMessageCard.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private void SocialSetMessage(string chinese, string english)
    {
        if (_socialMessage is null) return;
        SocialLocalize(_socialMessage, chinese, english);
        if (_socialMessageCard is not null) _socialMessageCard.IsVisible = !string.IsNullOrWhiteSpace(chinese);
    }

    private void RefreshSocialLanguage()
    {
        if (_socialNav is not null) SocialLocalize(_socialNav, "一起学习", "Study Together");
        if (_socialPage is null) return;
        var selfName = _socialProfile?.Nickname ?? _socialClient?.Nickname;
        for (var index = 0; index < _socialSeries.Count; index++)
            if (_socialSeries[index].IsSelf)
                _socialSeries[index] = _socialSeries[index] with
                { Label = string.IsNullOrWhiteSpace(selfName) ? T("我") : TF($"自己 · {selfName}") };
        foreach (var control in SocialLogicalControls(_socialPage))
        {
            if (control.Tag is SocialLocalizedText localized)
            {
                if (localized.Chinese is not null && localized.English is not null)
                    SocialLocalize(control, localized.Chinese, localized.English);
                else SocialLocalize(control, localized.DisplayedText);
            }
            if (control is SocialWeeklyChart chart)
            {
                chart.Title = UiText.Redisplay(chart.Title);
                chart.Unit = UiText.Redisplay(chart.Unit);
                chart.InvalidateVisual();
            }
            else if (control is SocialCurveOverlay curve)
                curve.InvalidateVisual();
        }
    }

    private static IEnumerable<Control> SocialLogicalControls(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is Control control) yield return control;
            if (child is ILogical logical)
                foreach (var nested in SocialLogicalControls(logical)) yield return nested;
        }
    }

    private Button SocialButton(string text, string name, Func<Task> action)
    {
        var button = new Button { Name = name };
        button.Classes.Add(name is "SocialChromeLogin" or "SocialCreateInvite" or "SocialClaimInvite" or "SocialBindProfile"
            ? "primary" : "secondary");
        SocialLocalize(button, text);
        button.Click += async (_, _) =>
        {
            if (!FocusCanNavigate) return;
            button.IsEnabled = false;
            var generation = _socialGeneration;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (SocialCloudException error) when (error.RequiresBinding)
            {
                if (generation == _socialGeneration)
                    SocialSetMessage("请先绑定当前学习档案，再保存昵称、头像或创建邀请码。",
                        "Link this study profile before saving a nickname or avatar, or creating an invitation.");
            }
            catch { if (generation == _socialGeneration) SocialSetMessage("社交操作暂未完成，请稍后重试。原有学习数据已保留。"); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void RenderSocialEntry()
    {
        if (_socialBody is null) return;
        _socialBody.Children.Clear();
        _socialBody.Children.Add(SocialText("一起学习", 28));
        _socialBody.Children.Add(SocialText("和朋友各自完成今天的学习计划，共同积累学习天数。", 15));
        _socialMessage = SocialText(_socialConfigurationError ?? "");
        _socialMessageCard = new Border { Classes = { "card" }, Child = _socialMessage,
            IsVisible = !string.IsNullOrWhiteSpace(_socialConfigurationError) };
        _socialBody.Children.Add(_socialMessageCard);
        if (_socialClient?.UserId is { } actor)
        {
            var account = new StackPanel { Spacing = 8 };
            account.Children.Add(string.IsNullOrWhiteSpace(_socialClient.Nickname)
                ? SocialText("已登录 · 学习者") : SocialText(TF($"已登录 · {_socialClient.Nickname}")));
            account.Children.Add(SocialButton("退出 GitHub 登录", "SocialLogout", async () =>
            {
                // Drop every friend, invite, detail and cached avatar before the network revocation.
                SocialClearPrivateView(); SocialCancelOperations(); _socialFriends = null;
                var generation = _socialGeneration; var client = _socialClient;
                // Signing out also forgets the cached joint windows and every unsent duration or
                // curve fact, so the next session starts from the server's own authorization state.
                try { if (client.UserId is { } signedOut) _socialStore?.ForgetTransientSharing(signedOut); }
                catch (Exception error) when (error is not OperationCanceledException) { }
                await client.LogoutAsync(); if (generation == _socialGeneration) RenderSocialEntry();
            }));
            _socialBody.Children.Add(new Border { Classes = { "card" }, Child = account });
            var bindingState = RenderSocialBinding(actor);
            if (bindingState == SocialBindingViewState.Active)
            {
                RenderSocialWorkspace(actor);
            }
            else
            {
                SocialClearPrivateView();
                _socialFriendHost = null;
                _socialInviteHost = null;
                _socialProfileHost = null;
                _socialDetailHost = null;
            }
            return;
        }
        var loginPanel = new StackPanel { Spacing = 10 };
        loginPanel.Children.Add(SocialText("开始与朋友一起学习", 20));
        loginPanel.Children.Add(SocialText("使用 GitHub 登录，创建自己的学习档案，然后邀请一位朋友。"));
        loginPanel.Children.Add(SocialButton("使用浏览器登录 GitHub", "SocialChromeLogin", async () =>
        {
            if (SocialAutomatedTestRun)
            {
                SocialSetMessage("自动测试模式：浏览器授权已禁用。", "Automated test mode: browser authorization is disabled.");
                return;
            }
            if (_socialOptions is null || _socialClient is null)
            {
                SocialSetMessage("社交服务当前不可用，请稍后重试。", "The social service is unavailable. Try again later.");
                return;
            }
            await SocialBeginLoginAsync();
        }));
        loginPanel.Children.Add(SocialButton("取消登录", "SocialCancelLogin", () =>
        {
            SocialCancelOperations(); SocialSetMessage("已取消登录。"); return Task.CompletedTask;
        }));
        _socialBody.Children.Add(new Border { Classes = { "card" }, Child = loginPanel });
    }

    private void SocialCancelOperations()
    {
        // Any reply still in flight belongs to the previous generation and cannot repopulate the view.
        SocialClearPrivateView(); SocialStopCharts();
        ++_socialGeneration; _socialOAuth.Cancel(); _socialLoginPending = false;
        SocialAbortPendingProtocolCallback();
        _socialSyncTimer?.Stop();
        _socialCancellation.Cancel(); _socialCancellation.Dispose(); _socialCancellation = new();
    }

    /// <summary>
    /// Releases a sign-in that is waiting for the Windows protocol callback. Without this the
    /// awaiting login would never observe navigation, cancellation, or window close.
    /// </summary>
    private void SocialAbortPendingProtocolCallback()
    {
        var pending = _socialProtocolCallback;
        _socialProtocolCallback = null;
        pending?.TrySetCanceled();
    }

    private async Task SocialBeginLoginAsync()
    {
        SocialCancelOperations();
        var generation = _socialGeneration;
        var token = _socialCancellation.Token;
        // The loopback callback is the interoperable default because the existing Supabase project
        // already permits localhost callbacks without requiring a Windows-only redirect allow-list
        // entry. The per-user protocol remains a fallback for machines where localhost is blocked.
        WindowsOAuthBrowser? browser = null;
        Uri callbackBase;
        Exception? loopbackError = null;
        try
        {
            browser = new WindowsOAuthBrowser();
            callbackBase = browser.CallbackBase;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            loopbackError = error;
            if (!WindowsProtocolRegistration.TryEnsureCallback(out callbackBase, out _))
                throw new InvalidOperationException("No OAuth callback channel is available.", loopbackError);
        }
        _socialOAuthBrowser?.Dispose();
        _socialOAuthBrowser = browser;
        var pendingProtocol = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var start = _socialOAuth.Begin(_socialOptions!.ProjectUrl, DateTimeOffset.UtcNow, callbackBase);
            // Hand off before the browser can redirect, so an early callback is never dropped.
            if (browser is null) _socialProtocolCallback = pendingProtocol;
            WindowsOAuthBrowser.Open(start, _socialOptions);
            _socialLoginPending = true;
            SocialSetMessage("等待浏览器中的 GitHub 授权…", "Waiting for GitHub authorization in your browser...");
            var exchange = browser is not null
                ? await browser.WaitForCallbackAsync(_socialOAuth, token)
                : await SocialAwaitProtocolCallbackAsync(pendingProtocol, token);
            if (generation != _socialGeneration || _socialClient is null) return;
            _socialLoginPending = false;
            await SocialExchangeAsync(exchange, generation, token);
        }
        catch (OperationCanceledException)
        {
            if (generation == _socialGeneration)
                SocialSetMessage("已取消登录。", "Sign-in was cancelled.");
        }
        catch (Exception)
        {
            if (generation != _socialGeneration) return;
            SocialSetMessage(
                _socialClient?.UserId is not null
                    ? "GitHub 已登录。请继续绑定当前学习档案。"
                    : "GitHub 登录暂未完成，请重试或重新登录。",
                _socialClient?.UserId is not null
                    ? "You are signed in to GitHub. Continue by linking this study profile."
                    : "GitHub sign-in did not complete. Retry or sign in again.");
        }
        finally
        {
            _socialLoginPending = false;
            if (ReferenceEquals(_socialProtocolCallback, pendingProtocol)) _socialProtocolCallback = null;
            if (browser is not null && ReferenceEquals(_socialOAuthBrowser, browser))
            {
                _socialOAuthBrowser = null;
                browser.Dispose();
            }
        }
    }

    /// <summary>Waits for the single-instance pipe to hand back the browser callback URI.</summary>
    private async Task<SocialOAuthExchange> SocialAwaitProtocolCallbackAsync(
        TaskCompletionSource<Uri> pending, CancellationToken token)
    {
        using var registration = token.Register(static state => ((TaskCompletionSource<Uri>)state!).TrySetCanceled(), pending);
        var callback = await pending.Task;
        var accepted = _socialOAuth.TryConsumeCallback(callback, DateTimeOffset.UtcNow, out var exchange, out var publicError);
        if (!accepted ||
            exchange is null)
            throw new InvalidOperationException(publicError ?? "The GitHub sign-in callback was not accepted.");
        return exchange;
    }

    /// <summary>Exchanges a consumed callback for a session and repaints the page.</summary>
    private async Task SocialExchangeAsync(SocialOAuthExchange exchange, long generation, CancellationToken token)
    {
        if (_socialClient is null) return;
        try
        {
            await _socialClient.ExchangeAsync(exchange, token);
            if (generation != _socialGeneration) return;
            SocialSessionReady();
            RenderSocialEntry();
            ShowAndActivate();
            if (SocialCanNavigate) ShowPage("social");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generation != _socialGeneration) return;
            if (_socialClient.UserId is not null)
                SocialSetMessage("GitHub 已登录。请继续绑定当前学习档案。", "You are signed in to GitHub. Continue by linking this study profile.");
            else
                SocialSetMessage("GitHub 登录暂未完成，请重新登录。");
        }
        finally { _socialLoginPending = false; }
    }

    private void SocialSessionReady()
    {
        _socialRecoveryPending = true;
        if (_socialClient?.UserId is not { } actor || _socialSync is null) return;
        try { _socialSync.SetSecondarySharing(actor, false, false); }
        catch { return; } // Foreign owner stays blocked; the explicit profile action is rendered below.
        _socialSyncTimer?.Start();
    }

    private void SocialEnterPage()
    {
        // Do not reconstruct focused inputs when the page is entered or a background read completes.
        if (_socialClient?.UserId is { } actor) _ = SocialRefreshAsync(actor);
    }

    internal async Task SocialProtocolActivatedAsync(Uri callback)
    {
        if (!WindowsProtocolRegistration.IsCallbackUri(callback.OriginalString)) return;
        // A pending sign-in consumes the callback itself, so the one-time state is never spent twice.
        if (_socialLoginPending && _socialProtocolCallback is { } pending && pending.TrySetResult(callback)) return;
        // Automated startup never constructs this client. Isolated UI fixtures inject only fake HTTP.
        if (_socialClient is null) return;
        if (!_socialOAuth.TryConsumeCallback(callback, DateTimeOffset.UtcNow, out var exchange, out var error))
        {
            if (error is not null) SocialSetMessage(error);
            return;
        }
        var generation = _socialGeneration;
        await SocialExchangeAsync(exchange!, generation, _socialCancellation.Token);
    }
}
