using System.Runtime.InteropServices;
using FilesaCOChat.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinRT.Interop;

namespace FilesaCOChat.WinUI;

/// <summary>
/// Windows 系统级通知（通知中心 toast）。
///
/// 这里的“通知”指的是操作系统通知，由 Windows 通知中心负责展示与留存，
/// 不再使用早前应用内右下角自绘的 InfoBar 提示条。
///
/// 未打包（WindowsPackageType=None）的桌面应用必须先调用
/// <see cref="AppNotificationManager.Register"/>：Windows App SDK 会在
/// HKCU 下登记 AUMID、显示名称与激活用的 COM 服务器，之后 Show() 才能
/// 把 toast 投递到系统通知中心。注册失败时静默降级，绝不影响聊天本身。
/// </summary>
internal static class SystemNotifier
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static bool _initialized;
    private static bool _available;

    /// <summary>True when registration succeeded and toast delivery is possible.</summary>
    public static bool IsAvailable => _available;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _available = true;
        }
        catch
        {
            // 系统策略禁用 / COM 注册失败：聊天与文件传输照常工作。
            _available = false;
        }
    }

    /// <summary>Clicking a toast brings the existing window back to the foreground.</summary>
    private static void OnNotificationInvoked(AppNotificationManager sender,
                                              AppNotificationActivatedEventArgs args)
    {
        var window = App.Main;
        if (window is null) return;
        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                window.Activate();
                var hwnd = WindowNative.GetWindowHandle(window);
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            catch { /* activation is best effort */ }
        });
    }

    /// <summary>Chat message notification: sender as title, text as body.</summary>
    public static void Show(ChatEntry entry) => Show(entry.Sender, entry.Text);

    public static void ShowSample() =>
        Show("Filesa COChat", "这是一条系统级通知示例：收到新消息时会在这里提醒你。");

    public static void Show(string title, string message)
    {
        if (!_available) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch { /* never let a notification failure break the chat */ }
    }

    /// <summary>Human readable OS permission state, shown on the Notifications page.</summary>
    public static string DescribeSetting()
    {
        if (!_available) return "Windows 通知权限：不可用，未能注册通知组件（可能被系统策略拦截）。";
        try
        {
            return AppNotificationManager.Default.Setting switch
            {
                AppNotificationSetting.Enabled => "Windows 通知权限：已允许，消息可以出现在通知中心。",
                AppNotificationSetting.DisabledForApplication =>
                    "Windows 通知权限：已被系统关闭。请在「设置 › 系统 › 通知」里允许 Filesa COChat 显示通知。",
                AppNotificationSetting.DisabledForUser =>
                    "Windows 通知权限：当前用户已关闭通知。请在「设置 › 系统 › 通知」中重新打开。",
                AppNotificationSetting.DisabledByGroupPolicy =>
                    "Windows 通知权限：被组策略禁用，请联系设备管理员。",
                _ => "Windows 通知权限：状态未知。"
            };
        }
        catch
        {
            return "Windows 通知权限：状态未知。";
        }
    }
}
