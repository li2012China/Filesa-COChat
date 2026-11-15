using System.Collections.Specialized;
using System.Runtime.InteropServices;
using FilesaCOChat.Core;
using FilesaCOChat.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace FilesaCOChat.WinUI;

/// <summary>
/// WinUI 3 replacement for the Tk main window. All core events arrive on worker
/// threads and are marshalled by MainViewModel; this class only touches XAML.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Toasts are only useful when the window is not on screen.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    // Immersive dark title bar (attribute 20 on Win11, 19 on Win10 1809+).
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                    ref int value, int size);

    private const int LoginWindowWidth = 420;
    private const int ChatWindowWidth = 1100;
    private const int ChatWindowHeight = 760;

    /// <summary>
    /// Distance (DIP) below which the message pane counts as "at the bottom".
    /// Absorbs fractional layout offsets so a 0.5 px gap does not stop the
    /// auto-scroll.
    /// </summary>
    private const double FollowBottomThreshold = 24;

    private readonly ChatApplication _app;
    private readonly MainViewModel _vm;
    private ScrollViewer? _messageScroll;
    private bool _scrollHooked;
    // "Follow live messages": true while the newest line is in view. A user
    // scroll that leaves the bottom clears it, so history reading is never
    // interrupted by an incoming message.
    private bool _stickToBottom = true;
    private bool _noticeDismissed;
    private string? _notifiedVersion;
    private string? _previewUsername;
    private int _entranceAnimations;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.WindowTitle;

        // Persisted preferences (theme / font / notifications) are loaded before
        // the application core is constructed so it picks them up on first use.
        _app = new ChatApplication(settings: SettingsStore.Load());
        _vm = new MainViewModel(_app, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _vm.Attach();
        // "Notifications" now means OS notifications in the Windows notification
        // centre, and only fire while the main window is minimised: when the chat
        // is on screen the new line is already visible in the message list, so a
        // toast would be pure noise.
        _vm.NotificationRequested += entry =>
        {
            if (IsWindowMinimized()) SystemNotifier.Show(entry);
        };
        _vm.UpdateNoticed += ShowUpdateNotice;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        MessageList.ItemsSource = _vm.Messages;
        UserList.ItemsSource = _vm.Users;
        // Per-line entrance animation is triggered by Loaded / DataContextChanged
        // on the template roots (see ChatLineRoot_*); the one-shot ChatLine flag
        // restricts it to the single line that was just appended.

        // The selector resolves the local username lazily: login (or preview)
        // happens after the templates have been created.
        if (RootGrid.Resources["BubbleSelector"] is ChatTemplateSelector selector)
            selector.UsernameProvider = () => _previewUsername ?? _vm.App.Username;

        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ApplyBubbleTheme();
            ApplyWindowChromeTheme();
        };
        _vm.Messages.CollectionChanged += OnMessagesChanged;
        // Restoring from the taskbar (or returning from another app) can make the
        // virtualising panel drop its scroll anchor. If the user was at the live
        // end before, re-pin it instead of leaving the list parked at the top.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            if (_stickToBottom) ScrollToLatest();
        };
        ApplyTheme();
        ApplyResponsiveLayout(RootGrid.ActualWidth);

        LoadLogo();
        ApplyFont();
        _app.ConfirmFileRequest = ShowFileRequestAsync;
        RegisterClosing();
        ApplyWindowIcon();
        StatusLabel.Text = _vm.Status;

        // The login page is a compact card, so the window is resized to exactly
        // that content instead of leaving a large empty canvas around it. After a
        // successful login the chat layout takes over and the window grows.
        if (Environment.GetEnvironmentVariable("FILESA_UI_PREVIEW") == "1")
        {
            ApplyPreviewWindowSize();
            ShowBubblePreview();
        }
        else
        {
            Title = "Filesa COChat";
            ApplyLoginWindowSize();
            // Re-measure once after the visual tree is live (fonts/theme applied).
            LoginPanel.Loaded += (_, _) => ApplyLoginWindowSize();
        }

        // Visual self-check helpers (no effect unless explicitly requested).
        if (Environment.GetEnvironmentVariable("FILESA_UI_SETTINGS") == "1")
            DispatcherQueue.TryEnqueue(ShowSettingsWindow);
        if (Environment.GetEnvironmentVariable("FILESA_UI_TOAST") == "1")
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(1200);
                SystemNotifier.ShowSample();
                await Task.Delay(400);
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "fc-ui-toast.txt"),
                        $"available={SystemNotifier.IsAvailable} status={SystemNotifier.DescribeSetting()}");
                }
                catch { /* diagnostics only */ }
            });
    }

    // ---- visual state ----------------------------------------------------
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Status):
                StatusLabel.Text = _vm.Status;
                LoginStatus.Text = _vm.Status;
                break;
            case nameof(MainViewModel.Busy):
                SearchProgress.Visibility = _vm.Busy ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(MainViewModel.LoggedIn):
                if (_vm.LoggedIn)
                {
                    LoginPanel.Visibility = Visibility.Collapsed;
                    ChatPanel.Visibility = Visibility.Visible;
                    Title = AppInfo.WindowTitle;
                    // The compact login window becomes a normal chat window.
                    DispatcherQueue.TryEnqueue(ApplyChatWindowSize);
                    RoomLabel.Text = (_app.IsServer ? "聊天室 " : "已加入 ") + (_app.Session?.ServerIp ?? "");
                    // The pane is live now: start following the newest message.
                    ScrollToLatest();
                }
                break;
            case nameof(MainViewModel.Users):
                UserCountLabel.Text = UiText.UserCountPrefix + _vm.Users.Count;
                break;
        }
    }

    private void LoadLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
            if (File.Exists(path)) LogoImage.Source = new BitmapImage(new Uri(path));
        }
        catch { /* optional asset */ }
    }

    private void ApplyFont()
    {
        MessageList.FontFamily = new FontFamily(_vm.Settings.FontFamily);
        MessageList.FontSize = _vm.Settings.FontSize;
        MessageList.FontWeight = _vm.Settings.Bold ? FontWeights.Bold : FontWeights.Normal;
    }

    // ---- update notice (InfoBar) -----------------------------------------
    /// <summary>
    /// Shows the update bar. This is the ONLY caller of <see cref="ShowNotice"/>:
    /// the bar is reserved for "a newer release exists", so routine status text
    /// (server IP, scanning progress, ...) never opens it.
    /// </summary>
    private void ShowUpdateNotice(UpdateInfo info)
    {
        if (!info.HasUpdate) return;                 // defensive: nothing to announce
        var version = string.IsNullOrWhiteSpace(info.LatestVersion) ? "" : info.LatestVersion;
        if (!string.IsNullOrWhiteSpace(version) &&
            string.Equals(_notifiedVersion, version, StringComparison.Ordinal))
            return;                                  // same release, already announced once

        _notifiedVersion = info.LatestVersion;
        _noticeDismissed = false;                    // a different release re-opens the bar
        var message = string.IsNullOrWhiteSpace(info.Detail) ? UiText.UpdateAvailable : info.Detail;
        var uri = Uri.TryCreate(info.ReleaseUrl, UriKind.Absolute, out var parsed)
            ? parsed
            : new Uri(ProtocolOptions.ReleasesPageUrl);
        var title = "发现新版本" + (string.IsNullOrWhiteSpace(version) ? "" : " " + version);
        ShowNotice(title, message, ("查看更新", uri));
    }

    /// <summary>
    /// Single presentation path for the update bar: content + optional action
    /// button. A bar the user closed stays closed until a different release is
    /// announced (guarded by <see cref="_notifiedVersion"/>).
    /// </summary>
    private void ShowNotice(string title, string message, (string Text, Uri Uri)? action = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        NoticeBar.Title = title;
        NoticeBar.Message = message;
        NoticeBar.Severity = InfoBarSeverity.Success;
        NoticeBar.ActionButton = action is { } a ? CreateNoticeAction(a.Text, a.Uri) : null;
        if (_noticeDismissed) return;
        NoticeBar.IsOpen = true;
        // The bar changes the message pane's height; keep the live end in view
        // instead of leaving a gap at the bottom.
        KeepBottomAfterResize();
    }

    private static Button CreateNoticeAction(string text, Uri uri)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) =>
        {
            try { await Launcher.LaunchUriAsync(uri); }
            catch { /* no default browser handler */ }
        };
        return button;
    }

    private void NoticeBar_CloseButtonClick(InfoBar sender, object args)
    {
        _noticeDismissed = true;
        // Closing the bar gives the message pane its height back.
        KeepBottomAfterResize();
    }

    /// <summary>True while the main window is minimised (toast gate).</summary>
    private bool IsWindowMinimized()
    {
        try { return IsIconic(WindowNative.GetWindowHandle(this)); }
        catch { return false; }
    }

    /// <summary>
    /// Resolves the effective window theme. The FILESA_UI_PREVIEW override (used
    /// only by the visual self-check) wins over the persisted preference;
    /// "System" maps to <see cref="ElementTheme.Default"/>, which follows the OS.
    /// </summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = Environment.GetEnvironmentVariable("FILESA_UI_THEME") switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => _vm.Settings.Theme switch
            {
                Core.ThemePreference.Light => ElementTheme.Light,
                Core.ThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            }
        };
        ApplyBubbleTheme();
        ApplyWindowChromeTheme();
    }

    /// <summary>
    /// The non-client title bar is drawn by DWM from the system theme, so a dark
    /// app on a light-title-bar Windows build showed a white strip above a dark
    /// page. Opt into the immersive dark title bar; both attribute ids are tried
    /// because 20 (Win11) and 19 (Win10 1809+) differ. Cosmetic only.
    /// </summary>
    private void ApplyWindowChromeTheme()
    {
        try
        {
            var dark = RootGrid.ActualTheme == ElementTheme.Dark ? 1 : 0;
            var hwnd = WindowNative.GetWindowHandle(this);
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch { /* title bar styling is best effort */ }
    }

    /// <summary>Sets the taskbar / title-bar icon from the bundled ico asset.</summary>
    private void ApplyWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (!File.Exists(path)) return;
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            AppWindow.GetFromWindowId(id)?.SetIcon(path);
        }
        catch { /* cosmetic only */ }
    }

    // ---- bubble layout ---------------------------------------------------
    /// <summary>Keeps bubble colours readable in both light and dark themes.</summary>
    private void ApplyBubbleTheme()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        SetBrush("MineBubbleBrush", dark ? (0x2B, 0x52, 0x78) : (0xD6, 0xE8, 0xF8));
        SetBrush("MineBubbleBorderBrush", dark ? (0x4A, 0x80, 0xB0) : (0xA9, 0xCB, 0xE8));
        // Incoming bubbles need a clearly visible container on top of the card
        // background, which is #2B2B2B (dark) / #F9F9F9 (light).
        SetBrush("OtherBubbleBrush", dark ? (0x3A, 0x3C, 0x42) : (0xEC, 0xEE, 0xF2));
        SetBrush("OtherBubbleBorderBrush", dark ? (0x5A, 0x5E, 0x66) : (0xC9, 0xCF, 0xD7));
    }

    private void SetBrush(string key, (int R, int G, int B) rgb)
    {
        if (RootGrid.Resources[key] is SolidColorBrush brush)
            brush.Color = new Windows.UI.Color
            { A = 0xFF, R = (byte)rgb.R, G = (byte)rgb.G, B = (byte)rgb.B };
    }

    /// <summary>
    /// Adapts the two-column chat layout to window width. The bubble templates
    /// themselves use proportional (0.72*) columns, so they shrink with the
    /// message pane without any extra code here.
    /// </summary>
    private void ApplyResponsiveLayout(double width)
    {
        var w = width > 0 ? width : 1100;
        // The roster is the entry point for the per-user actions, so it is kept
        // visible down to 660 px; below that the message pane wins.
        var users = w >= 980 ? 220 : w >= 760 ? 180 : w >= 660 ? 150 : 0;
        UsersColumn.Width = new GridLength(users);
        UsersPanel.Visibility = users > 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageBorder.Margin = new Thickness(0, 0, users > 0 ? (w >= 980 ? 10 : 8) : 0, 0);
        ChatPanel.Padding = new Thickness(w >= 980 ? 12 : w >= 760 ? 10 : 8);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
        if (Environment.GetEnvironmentVariable("FILESA_UI_PREVIEW") != "1") return;
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "fc-ui-metrics.txt"),
                $"root={RootGrid.ActualWidth:F0}x{RootGrid.ActualHeight:F0} " +
                $"message={MessageBorder.ActualWidth:F0} users={UsersColumn.ActualWidth:F0} " +
                $"usersVisible={UsersPanel.Visibility} padding={ChatPanel.Padding.Left:F0} " +
                $"timeline={string.Join("", _vm.Messages.Select(m => m.ShowTimestamp ? "1" : "0"))} " +
                $"animated={_vm.Messages.Count(m => m.PlayEntranceAnimation)}");
        }
        catch { /* diagnostics only */ }
    }

    /// <summary>Preview-only: FILESA_UI_SIZE=600x640 exercises the narrow layout.</summary>
    private void ApplyPreviewWindowSize()
    {
        var spec = Environment.GetEnvironmentVariable("FILESA_UI_SIZE");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) ||
            !int.TryParse(parts[1], out var h)) return;
        try
        {
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            AppWindow.GetFromWindowId(id)?.Resize(new Windows.Graphics.SizeInt32(w, h));
        }
        catch { /* unsupported presenter */ }
    }

    // ---- window sizing ---------------------------------------------------
    /// <summary>
    /// Sizes the window to the login card. The height comes from measuring the
    /// login panel at the fixed compact width, so there is no large blank canvas
    /// and no clipped status text. The window is locked in this state because the
    /// login page has no use for extra room.
    /// </summary>
    private void ApplyLoginWindowSize()
    {
        try
        {
            LoginPanel.Measure(new Windows.Foundation.Size(LoginWindowWidth, double.PositiveInfinity));
            var height = LoginPanel.DesiredSize.Height;
            if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0) height = 480;
            height = Math.Clamp(height + 8, 420, 720);

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
            ResizeClient(LoginWindowWidth, height);
            CenterOnScreen();
        }
        catch { /* presenter restrictions are best effort */ }
    }

    /// <summary>Restores the normal resizable chat window after login.</summary>
    private void ApplyChatWindowSize()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
            }
            // Never exceed the work area on small / scaled displays.
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var scale = DpiScale();
            var maxW = (int)Math.Floor(area.Width / scale) - 48;
            var maxH = (int)Math.Floor(area.Height / scale) - 48;
            var width = Math.Min(ChatWindowWidth, Math.Max(720, maxW));
            var height = Math.Min(ChatWindowHeight, Math.Max(520, maxH));
            ResizeClient(width, height);
            CenterOnScreen();
        }
        catch { /* unsupported presenter */ }
    }

    private double DpiScale()
    {
        try
        {
            var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
        catch { return 1.0; }
    }

    /// <summary>AppWindow sizes are physical pixels; callers work in DIPs.</summary>
    private void ResizeClient(double widthDip, double heightDip)
    {
        var scale = DpiScale();
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Round(widthDip * scale), (int)Math.Round(heightDip * scale)));
    }

    /// <summary>Centres the window inside the work area of its monitor.</summary>
    private void CenterOnScreen()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var size = AppWindow.Size;
            var x = area.X + Math.Max(0, (area.Width - size.Width) / 2);
            var y = area.Y + Math.Max(0, (area.Height - size.Height) / 2);
            AppWindow.Move(new PointInt32(x, y));
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// New lines are appended in receive order. The viewport follows the newest
    /// message only while it is already showing the bottom of the conversation;
    /// once the user scrolls up to read history, incoming messages arrive without
    /// moving the view. Timestamp grouping and the entrance-animation flag are set
    /// by MainViewModel / ChatTimeline before the new container is realised.
    /// </summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)) return;
        if (_vm.Messages.Count == 0) return;
        // Add: honour the user's scroll position, except for the user's own
        // message (sending always brings the view back to the live end).
        // Reset means the list was cleared / reloaded, where the bottom is the
        // only sensible target.
        if (e.Action == NotifyCollectionChangedAction.Add && !_stickToBottom && !IsOwnLatestLine()) return;
        ScrollToLatest();
    }

    /// <summary>True when the line just appended was produced by this user.</summary>
    private bool IsOwnLatestLine() =>
        _vm.Messages[^1] is { IsSystemLine: false } line &&
        string.Equals(line.Sender, _vm.App.Username, StringComparison.Ordinal);

    private void MessageList_Loaded(object sender, RoutedEventArgs e)
    {
        if (EnsureMessageScroll() is null) return;
        if (_vm.Messages.Count == 0) return;
        if (!_stickToBottom) return;
        ScrollToLatest();
    }

    /// <summary>
    /// Caches the ListView's inner ScrollViewer and subscribes to its ViewChanged
    /// once. The ScrollViewer only exists after the template is applied, so this is
    /// called lazily from every path that needs it.
    /// </summary>
    private ScrollViewer? EnsureMessageScroll()
    {
        var scroll = _messageScroll ??= FindScrollViewer(MessageList);
        if (scroll is null) return null;
        if (!_scrollHooked)
        {
            _scrollHooked = true;
            scroll.ViewChanged += OnMessageScrollViewChanged;
        }
        return scroll;
    }

    /// <summary>
    /// Records whether the pane is parked at the bottom. Only final (non
    /// intermediate) changes count: intermediate ones fire for every animation
    /// frame, including the app's own ScrollIntoView, and would flip the flag
    /// back and forth. Extent growth from a new bubble does not raise this event
    /// at all, so the pre-append position stays authoritative.
    /// </summary>
    private void OnMessageScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        var scroll = _messageScroll;
        if (scroll is null) return;
        _stickToBottom = scroll.ScrollableHeight - scroll.VerticalOffset <= FollowBottomThreshold;
    }

    /// <summary>
    /// Scrolls to the last line. Deferred by one dispatcher tick because the new
    /// container has not been measured yet when CollectionChanged fires, so
    /// ScrollableHeight still describes the previous content.
    /// <para>
    /// Deliberately does NOT use ListView.ScrollIntoView: on a virtualising
    /// ItemsStackPanel that call re-anchors the panel and was what made the list
    /// occasionally snap back to the very top. A plain ChangeView on the inner
    /// ScrollViewer keeps the offset authoritative, and the jump is instant
    /// (disableAnimation) so a half-finished animation can never leave the view
    /// parked in the middle of the history.
    /// </para>
    /// </summary>
    private void ScrollToLatest() => DispatcherQueue.TryEnqueue(
        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => SnapToBottom(allowRetry: true));

    /// <summary>
    /// Jumps to the bottom once the extent is known. A second pass runs on the
    /// next tick: the panel may still report an extent that grows after the
    /// first layout, and stopping short is the only case worth correcting
    /// (nothing else can move the offset inside one dispatcher tick).
    /// </summary>
    private void SnapToBottom(bool allowRetry)
    {
        var scroll = EnsureMessageScroll();
        if (scroll is null) return;
        // Force measurement first: a Low-priority dispatcher callback can run
        // before the render pass that arranges the new bubble, in which case
        // ScrollableHeight still describes the previous content and the jump
        // would stop short of the newest line.
        try { MessageList.UpdateLayout(); } catch { /* transient layout */ }
        var extent = scroll.ScrollableHeight;
        // extent <= 0 means "not measured yet" (or the content fits): jumping to
        // 0 there would be a jump to the TOP, which is exactly the bug.
        if (extent <= 0) return;
        if (extent - scroll.VerticalOffset > 1) scroll.ChangeView(null, extent, null, true);
        if (!allowRetry) { _stickToBottom = true; return; }
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var sv = EnsureMessageScroll();
            if (sv is null) return;
            if (sv.ScrollableHeight - sv.VerticalOffset > 1) SnapToBottom(allowRetry: false);
            else _stickToBottom = true;
        });
    }

    /// <summary>
    /// Keeps the live end in view when the message pane changes height (update
    /// bar opening / closing) and the user had not scrolled away.
    /// </summary>
    private void KeepBottomAfterResize()
    {
        if (!_stickToBottom) return;
        ScrollToLatest();
    }

    // ---- single-bubble entrance animation --------------------------------
    /// <summary>
    /// Replaces the old "all bubbles animate" behaviour. The stock ListView
    /// transitions are cleared in XAML; the animation is attached to the bubble
    /// root itself and gated by ChatLine.PlayEntranceAnimation, which is set on
    /// exactly one line (the newly appended bubble) and consumed on first use,
    /// so scrolling / recycling never replays it.
    /// </summary>
    private void ChatLineRoot_Loaded(object sender, RoutedEventArgs e) => TryAnimateLineRoot(sender);

    private void ChatLineRoot_DataContextChanged(FrameworkElement sender,
                                                 DataContextChangedEventArgs args) =>
        TryAnimateLineRoot(sender);

    private void TryAnimateLineRoot(object sender)
    {
        if (sender is not FrameworkElement root || root.DataContext is not ChatLine line) return;
        if (!line.PlayEntranceAnimation) return;
        line.PlayEntranceAnimation = false; // one-shot, exactly one bubble
        _entranceAnimations++;
        // Defer one dispatcher tick so the root is attached to the tree before
        // its opacity/transform are changed.
        DispatcherQueue.TryEnqueue(() => _ = AnimateEntranceAsync(root, line));
    }

    private static async Task AnimateEntranceAsync(FrameworkElement target, ChatLine line)
    {
        var slide = new TranslateTransform { Y = 10 };
        target.RenderTransform = slide;
        target.Opacity = 0;
        const int steps = 8;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(18);
            if (!ReferenceEquals(target.DataContext, line))
            {
                // Recycled or unloaded mid-animation: restore a neutral state so
                // the next item shown by this container renders normally.
                target.Opacity = 1;
                slide.Y = 0;
                return;
            }
            var p = i / (double)steps;
            target.Opacity = p;
            slide.Y = 10 * (1 - p);
        }
        target.Opacity = 1;
        slide.Y = 0;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>Sample timeline for visual verification (no network, no login).</summary>
    private void ShowBubblePreview()
    {
        _previewUsername = "我";
        // A fixed base keeps the grouping reproducible regardless of the wall
        // clock minute the preview happens to start in.
        var t0 = DateTime.Today.AddHours(18).AddMinutes(1);
        void Add(string sender, string text, ChatEntryKind kind, int seconds) =>
            _vm.AddPreviewLine(new ChatEntry(sender, text, kind, t0.AddSeconds(seconds)));

        LoginPanel.Visibility = Visibility.Collapsed;
        ChatPanel.Visibility = Visibility.Visible;
        RoomLabel.Text = "聊天室 192.168.1.8 · 界面预览";
        StatusLabel.Text = "Serving";
        UserCountLabel.Text = "用户: 3";
        // The preview deliberately starts at the oldest line, so the follow flag
        // begins cleared and the list is not yanked to the bottom on load.
        _stickToBottom = false;
        _vm.Users.Add("我");
        _vm.Users.Add("alice");
        _vm.Users.Add("bob");

        // Three consecutive system lines in 18:01 share ONE 18:01 timestamp,
        // drawn under the last line of the group.
        Add(UiText.System, "欢迎 我 使用 Filesa COChat", ChatEntryKind.System, 0);
        Add(UiText.System, "已在 192.168.1.8 创建新的聊天室", ChatEntryKind.System, 2);
        Add(UiText.System, "用户列表已同步", ChatEntryKind.System, 4);
        Add("alice", "你好，能看到这条消息吗？", ChatEntryKind.Chat, 65);
        Add("我", "可以，左右气泡和时间都已经生效了。", ChatEntryKind.Chat, 130);
        Add("alice",
            "这是一条比较长的消息，用来验证气泡在长文本下会自动换行并且不会撑破容器：" +
            "Filesa COChat 使用 TCP 11451 传输聊天消息、UDP 11452 做局域网广播发现、" +
            "TCP 11450 传输文件，接收方会校验 SHA-256 哈希。即使窗口被拉窄，气泡也应该按可用宽度折行。",
            ChatEntryKind.Chat, 200);
        Add("我", "短消息", ChatEntryKind.Chat, 260);
        // A second group (separated from the first by chat lines), and a lone
        // error line: each shows its own single timestamp.
        Add(UiText.System, "bob 加入了聊天室", ChatEntryKind.System, 320);
        Add(UiText.System, "已同步在线用户", ChatEntryKind.System, 322);
        Add("bob", "收到", ChatEntryKind.Chat, 380);
        Add(UiText.System, UiText.RemoteCommandDisabled, ChatEntryKind.Error, 440);

        _vm.RefreshTimeline(); // bulk load: apply grouping, no entrance animation

        DispatcherQueue.TryEnqueue(() =>
        {
            EnsureMessageScroll()?.ChangeView(null, 0, null, false);
        });

        // FILESA_UI_APPEND=1: append one line through the real message path and
        // report how many containers actually played the entrance animation.
        // Expected animatedContainers=1 (not 11), pending=0.
        if (Environment.GetEnvironmentVariable("FILESA_UI_APPEND") == "1")
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await Task.Delay(900); // let the bulk load finish rendering
                    var scroll = EnsureMessageScroll();
                    if (scroll is not null) scroll.ChangeView(null, scroll.ScrollableHeight, null, false);
                    await Task.Delay(200);
                    _vm.AppendPreviewLine(new ChatEntry("我", "追加的一条新消息（动画验证）",
                        ChatEntryKind.Chat, t0.AddSeconds(500)));
                    await Task.Delay(900);
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "fc-ui-animation.txt"),
                        $"animatedContainers={_entranceAnimations} messages={_vm.Messages.Count} " +
                        $"timeline={string.Join("", _vm.Messages.Select(m => m.ShowTimestamp ? "1" : "0"))} " +
                        $"pending={_vm.Messages.Count(m => m.PlayEntranceAnimation)}");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "fc-ui-animation.txt"),
                        "error=" + ex.Message);
                }
            });
        }
    }

    // ---- login / send ----------------------------------------------------
    private async void Login_Click(object sender, RoutedEventArgs e) => await DoLoginAsync();

    private async void UsernameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await DoLoginAsync();
    }

    private async Task DoLoginAsync()
    {
        var name = UsernameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await Dialogs.Info(RootGrid.XamlRoot, UiText.Warning, UiText.MustEnterUsername,
                RootGrid.RequestedTheme).ShowAsync();
            return;
        }
        LoginButton.IsEnabled = false;
        var ok = await _vm.LoginAsync(name);
        if (!ok) LoginButton.IsEnabled = true;
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await DoSendAsync();

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await DoSendAsync();
    }

    private async Task DoSendAsync()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        InputBox.Text = "";
        await _vm.SendAsync(text);
    }

    // ---- settings (independent window) ----------------------------------
    /// <summary>
    /// Settings live in their own window. The main window can be as small as the
    /// compact login card, which made the old ContentDialog clip its right-hand
    /// column; a top-level window with a fixed minimum size always shows the full
    /// page. The same window is opened from the login page and the chat toolbar.
    /// </summary>
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    internal void ShowSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Activate();
    }

    internal void OnSettingsWindowClosed() => _settingsWindow = null;

    // ---- accessors used by SettingsWindow --------------------------------
    internal MainViewModel ViewModel => _vm;
    internal ElementTheme CurrentTheme => RootGrid.RequestedTheme;
    internal void ApplyThemeFromSettings() => ApplyTheme();
    internal void ApplyFontFromSettings() => ApplyFont();

    /// <summary>The PSK only lives in memory and applies to the next session.</summary>
    internal void ApplyPreSharedKeyFromSettings() =>
        _app.Options.PreSharedKey = _vm.Settings.PreSharedKey;

    // ---- per-user context menu (replaces the toolbar buttons) -----------
    /// <summary>Right-click must select the row it hit, so the flyout acts on
    /// the user under the cursor rather than a previously selected one.</summary>
    private void UserList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var item = FindAncestor<ListViewItem>(source);
        if (item?.Content is string user) UserList.SelectedItem = user;
    }

    /// <summary>Disables both actions unless a valid (non-self) target is selected.</summary>
    private void UserMenu_Opening(object? sender, object e)
    {
        var user = UserList.SelectedItem as string;
        var valid = !string.IsNullOrEmpty(user) &&
                    _vm.Users.Contains(user) &&
                    !string.Equals(user, _vm.App.Username, StringComparison.Ordinal);
        UserCommandItem.IsEnabled = valid;
        UserSendFileItem.IsEnabled = valid;
    }

    private async void UserCommand_Click(object sender, RoutedEventArgs e)
    {
        var target = UserList.SelectedItem as string;
        if (string.IsNullOrEmpty(target) ||
            string.Equals(target, _vm.App.Username, StringComparison.Ordinal))
        {
            await Dialogs.Info(RootGrid.XamlRoot, "提示", "请先选择一个其他用户",
                RootGrid.RequestedTheme).ShowAsync();
            return;
        }
        // Target comes from the right-clicked row; the combo box is pre-selected
        // so the flow stays identical when the room roster has changed meanwhile.
        var parts = Dialogs.Command(RootGrid.XamlRoot, _vm.Users, _vm.App.Username, target,
            RootGrid.RequestedTheme);
        if (await parts.Dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var command = parts.Command.Text.Trim();
        if (string.IsNullOrEmpty(command)) return;
        await _vm.SendCommandAsync(target, command);
    }

    private async void UserSendFile_Click(object sender, RoutedEventArgs e)
    {
        var target = UserList.SelectedItem as string;
        if (string.IsNullOrEmpty(target) ||
            string.Equals(target, _vm.App.Username, StringComparison.Ordinal))
        {
            await Dialogs.Info(RootGrid.XamlRoot, "提示", "请先选择一个其他用户",
                RootGrid.RequestedTheme).ShowAsync();
            return;
        }
        string? path;
        try { path = await PickFileAsync(); }
        catch (Exception ex)
        {
            await Dialogs.Info(RootGrid.XamlRoot, "提示", "选择文件失败: " + ex.Message,
                RootGrid.RequestedTheme).ShowAsync();
            return;
        }
        if (path is not null) await _vm.SendFileAsync(target, path);
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private async Task<string?> PickFileAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private Task<bool> ShowFileRequestAsync(FileRequestArgs args)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await Dialogs.FileRequest(RootGrid.XamlRoot, args,
                    RootGrid.RequestedTheme).ShowAsync();
                tcs.TrySetResult(result == ContentDialogResult.Primary);
            }
            catch { tcs.TrySetResult(false); }
        });
        return tcs.Task;
    }

    // ---- context menu ----------------------------------------------------
    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (MessageList.SelectedItem is not ChatLine line) return;
        try
        {
            var package = new DataPackage();
            package.SetText(line.Text);
            Clipboard.SetContent(package);
        }
        catch { /* clipboard unavailable in unpackaged context */ }
    }

    private void ClearMessages_Click(object sender, RoutedEventArgs e) => _vm.Messages.Clear();

    // ---- close => server migration ---------------------------------------
    private void RegisterClosing()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Closing += OnAppWindowClosing;
    }

    private bool _closeHandled;

    /// <summary>
    /// AppWindow.Closing has no deferral in Windows App SDK 1.6, so cancel the first
    /// close, run the (async) server migration, then close for real.
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeHandled) return;
        _closeHandled = true;
        args.Cancel = true;
        try { _settingsWindow?.Close(); } catch { /* already closed */ }
        try { await _app.CloseAsync(); }
        catch { /* never block window close */ }
        DispatcherQueue.TryEnqueue(() => Close());
    }
}
