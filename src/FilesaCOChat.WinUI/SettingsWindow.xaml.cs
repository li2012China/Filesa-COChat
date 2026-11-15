using System.Runtime.InteropServices;
using FilesaCOChat.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace FilesaCOChat.WinUI;

/// <summary>
/// Standalone settings window. It is opened from the login page and from the chat
/// toolbar and is shared by both. Theme and font are applied live to the main
/// window; the notification switch and the PSK are committed as they change /
/// when the window closes.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                    ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    /// <summary>GWLP_HWNDPARENT: makes this window an OWNED window of the main one.</summary>
    private const int GwlpHwndParent = -8;

    private const int DefaultWidth = 880;
    private const int DefaultHeight = 660;

    private readonly MainWindow _owner;
    private bool _initializing = true;

    public SettingsWindow(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        Title = "设置 · " + AppInfo.Name;
        VersionText.Text = AppInfo.AboutVersionLine;

        AttachToOwner();
        ApplyWindowIcon();
        ApplyWindowSize();
        RootGrid.RequestedTheme = owner.CurrentTheme;
        RootGrid.ActualThemeChanged += (_, _) => ApplyWindowChromeTheme();
        ApplyWindowChromeTheme();

        var settings = owner.ViewModel.Settings;

        // ---- appearance ----
        ThemeCombo.SelectedIndex = Dialogs.IndexFromTheme(settings.Theme);
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        ThemeCombo.DropDownClosed += (_, _) => CommitTheme();

        // ---- font ----
        foreach (var family in new[] { "Microsoft YaHei UI", "Segoe UI", "Consolas", "SimSun", "KaiTi" })
            FontCombo.Items.Add(family);
        FontCombo.SelectedItem = settings.FontFamily;
        FontSizeBox.Value = settings.FontSize;
        BoldSwitch.IsOn = settings.Bold;
        FontCombo.SelectionChanged += (_, _) => UpdateFontPreview();
        FontSizeBox.ValueChanged += (_, _) => UpdateFontPreview();
        BoldSwitch.Toggled += (_, _) => UpdateFontPreview();
        UpdateFontPreview();

        // ---- notifications (OS level) ----
        NotificationsSwitch.IsOn = settings.NotificationsEnabled;
        NotificationsSwitch.Toggled += NotificationsSwitch_Toggled;
        NotificationsHint.Text = settings.NotificationsEnabled
            ? "已开启：收到新消息时由 Windows 通知中心提醒。"
            : "已关闭：不再发送系统通知，消息仍会显示在聊天窗口。";
        NotifyStatusText.Text = SystemNotifier.DescribeSetting();
        TestNotificationButton.IsEnabled = SystemNotifier.IsAvailable;
        TestNotificationButton.Click += (_, _) => SystemNotifier.ShowSample();

        // ---- security ----
        PskBox.Password = settings.PreSharedKey ?? "";

        Nav.SelectionChanged += Nav_SelectionChanged;
        Nav.SelectedIndex = 0;
        ShowPage(0);

        Closed += OnClosed;
        _initializing = false;
    }

    // ---- window chrome ---------------------------------------------------
    /// <summary>
    /// Makes this window OWNED by the main window (GWLP_HWNDPARENT). Owned
    /// windows always float above their owner and are minimised with it, so the
    /// settings page can no longer disappear behind the chat window that opened
    /// it. Nothing is forced above other applications, which keeps Alt+Tab and
    /// task switching behaving normally.
    /// </summary>
    private void AttachToOwner()
    {
        try
        {
            var owner = WindowNative.GetWindowHandle(_owner);
            var hwnd = WindowNative.GetWindowHandle(this);
            if (owner == IntPtr.Zero || hwnd == IntPtr.Zero) return;
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GwlpHwndParent, owner);
            else SetWindowLong32(hwnd, GwlpHwndParent, owner.ToInt32());
        }
        catch { /* falls back to an unowned window */ }
    }

    private void ApplyWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(path)) AppWindow.SetIcon(path);
        }
        catch { /* cosmetic only */ }
    }

    /// <summary>Keeps the DWM title bar in step with the app theme (see MainWindow).</summary>
    private void ApplyWindowChromeTheme()
    {
        try
        {
            var dark = RootGrid.ActualTheme == ElementTheme.Dark ? 1 : 0;
            var hwnd = WindowNative.GetWindowHandle(this);
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch { /* cosmetic */ }
    }

    private void ApplyWindowSize()
    {
        try
        {
            var scale = DpiScale();
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var maxW = (int)Math.Floor(area.Width / scale) - 40;
            var maxH = (int)Math.Floor(area.Height / scale) - 40;
            var width = Math.Min(DefaultWidth, Math.Max(700, maxW));
            var height = Math.Min(DefaultHeight, Math.Max(520, maxH));

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
            }

            AppWindow.ResizeClient(new SizeInt32(
                (int)Math.Round(width * scale), (int)Math.Round(height * scale)));

            var size = AppWindow.Size;
            AppWindow.Move(new PointInt32(
                area.X + Math.Max(0, (area.Width - size.Width) / 2),
                area.Y + Math.Max(0, (area.Height - size.Height) / 2)));
        }
        catch { /* fall back to the default size */ }
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

    // ---- navigation ------------------------------------------------------
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowPage(Nav.SelectedIndex);

    private void ShowPage(int index)
    {
        if (AppearancePage is null) return;
        AppearancePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        FontPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        NotifyPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        SecurityPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- appearance ------------------------------------------------------
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Committing while the popup is open rebuilds the ComboBox template
        // underneath the pointer, so only commit once it has closed.
        if (!ThemeCombo.IsDropDownOpen) CommitTheme();
    }

    private void CommitTheme()
    {
        if (_initializing) return;
        var settings = _owner.ViewModel.Settings;
        var preference = Dialogs.ThemeFromIndex(ThemeCombo.SelectedIndex);
        if (preference == settings.Theme) return;
        settings.Theme = preference;
        SettingsStore.Save(settings);
        DispatcherQueue.TryEnqueue(() =>
        {
            _owner.ApplyThemeFromSettings();
            RootGrid.RequestedTheme = _owner.CurrentTheme;
        });
    }

    // ---- font ------------------------------------------------------------
    private void UpdateFontPreview()
    {
        if (FontPreview is null) return;
        FontPreview.FontFamily = new FontFamily(
            FontCombo.SelectedItem?.ToString() ?? "Microsoft YaHei UI");
        FontPreview.FontSize = double.IsNaN(FontSizeBox.Value) ? 14 : FontSizeBox.Value;
        FontPreview.FontWeight = BoldSwitch.IsOn ? FontWeights.Bold : FontWeights.Normal;
    }

    private void FontApply_Click(object sender, RoutedEventArgs e)
    {
        var settings = _owner.ViewModel.Settings;
        settings.FontFamily = FontCombo.SelectedItem?.ToString() ?? "Microsoft YaHei UI";
        settings.FontSize = double.IsNaN(FontSizeBox.Value) ? 14 : FontSizeBox.Value;
        settings.Bold = BoldSwitch.IsOn;
        SettingsStore.Save(settings);
        _owner.ApplyFontFromSettings();
        FontResultText.Text = "字体设置已成功应用到聊天界面";
    }

    // ---- notifications ---------------------------------------------------
    private void NotificationsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var settings = _owner.ViewModel.Settings;
        settings.NotificationsEnabled = NotificationsSwitch.IsOn;
        SettingsStore.Save(settings);
        NotificationsHint.Text = NotificationsSwitch.IsOn
            ? "已开启：收到新消息时由 Windows 通知中心提醒。"
            : "已关闭：不再发送系统通知，消息仍会显示在聊天窗口。";
        NotifyStatusText.Text = SystemNotifier.DescribeSetting();
    }

    // ---- about -----------------------------------------------------------
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        AboutResultText.Text = "检查中...";
        try
        {
            var info = await _owner.ViewModel.CheckUpdateAsync();
            AboutResultText.Text = info.Detail +
                (string.IsNullOrEmpty(info.LatestVersion) ? "" : $"（最新版本 {info.LatestVersion}）");
        }
        catch (Exception ex)
        {
            AboutResultText.Text = "检查失败: " + ex.Message;
        }
    }

    private async void OpenGithub_Click(object sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(ProtocolOptions.ReleasesPageUrl)); }
        catch (Exception ex) { AboutResultText.Text = "打开失败: " + ex.Message; }
    }

    private void Credits_Click(object sender, RoutedEventArgs e) =>
        AboutResultText.Text = "感谢所有为 Filesa COChat 做出贡献的开发者和用户！\n" +
                               "特别感谢：测试用户、开源社区、Python 社区、Tkinter、Pillow、Message-Pop-UP。\n" +
                               "WinUI 3 移植版基于静态逆向还原的协议实现。";

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- close -----------------------------------------------------------
    private void OnClosed(object sender, WindowEventArgs args)
    {
        var settings = _owner.ViewModel.Settings;
        settings.PreSharedKey = string.IsNullOrEmpty(PskBox.Password) ? null : PskBox.Password;
        _owner.ApplyPreSharedKeyFromSettings();
        SettingsStore.Save(settings);
        _owner.OnSettingsWindowClosed();
    }
}
