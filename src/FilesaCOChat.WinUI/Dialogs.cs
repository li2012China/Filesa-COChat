using FilesaCOChat.Core;
using FilesaCOChat.Core.Security;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesaCOChat.WinUI;

public sealed class CommandDialogParts
{
    public required ContentDialog Dialog { get; init; }
    public required ComboBox Target { get; init; }
    public required TextBox Command { get; init; }
}

/// <summary>
/// ContentDialogs are built in code to keep the project small. Text is taken from
/// main_strings_by_func.txt so the WinUI3 build keeps the 5.1 user-visible wording.
/// Settings are no longer a dialog: they live in their own window
/// (SettingsWindow.xaml), because the compact login window cannot host the
/// two-column settings layout.
/// </summary>
internal static class Dialogs
{
    /// <summary>
    /// ContentDialog renders inside its own popup root, so it does NOT inherit
    /// the window's RequestedTheme. Every dialog therefore receives the current
    /// window theme explicitly, otherwise a light window would open a dark dialog
    /// when the OS theme is dark.
    /// </summary>
    public static ContentDialog Info(XamlRoot root, string title, string message,
                                     ElementTheme theme = ElementTheme.Default) => new()
    {
        XamlRoot = root, Title = title, Content = message,
        RequestedTheme = theme,
        CloseButtonText = "确定", DefaultButton = ContentDialogButton.Close
    };

    public static ContentDialog FileRequest(XamlRoot root, FileRequestArgs args,
                                            ElementTheme theme = ElementTheme.Default)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = "来自 " + args.RemoteIp + " 的文件传输请求:" });
        body.Children.Add(new TextBlock { Text = "文件名: " + args.FileName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "文件大小: " + FilePolicy.FormatSize(args.Size) });
        body.Children.Add(new TextBlock
        {
            Text = "文件将保存到 files/ 目录，并在传输结束后用 SHA-256 校验。",
            FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), TextWrapping = TextWrapping.Wrap
        });
        return new ContentDialog
        {
            XamlRoot = root, Title = UiText.FileRequestTitle, Content = body,
            RequestedTheme = theme,
            PrimaryButtonText = "接受", SecondaryButtonText = "拒绝",
            DefaultButton = ContentDialogButton.Primary
        };
    }

    public static CommandDialogParts Command(XamlRoot root, IEnumerable<string> users, string selfName,
                                              string? preselectedTarget = null,
                                              ElementTheme theme = ElementTheme.Default)
    {
        var target = new ComboBox { Header = "目标用户", MinWidth = 260, PlaceholderText = "选择用户" };
        foreach (var user in users.Where(u => !string.Equals(u, selfName, StringComparison.Ordinal)))
            target.Items.Add(user);
        // Pre-select the user that was right-clicked, but fall back to a manual
        // pick when the roster changed since the menu was opened.
        if (!string.IsNullOrEmpty(preselectedTarget) &&
            target.Items.Contains(preselectedTarget))
            target.SelectedItem = preselectedTarget;
        else if (target.Items.Count > 0)
            target.SelectedIndex = 0;
        var input = new TextBox { Header = "请输入要发送给该用户的命令:", PlaceholderText = "命令内容", MinWidth = 260 };
        var warning = new TextBlock
        {
            Text = UiText.RemoteCommandDisabled,
            Foreground = new SolidColorBrush(Colors.OrangeRed), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(target); stack.Children.Add(input); stack.Children.Add(warning);
        return new CommandDialogParts
        {
            Dialog = new ContentDialog
            {
                XamlRoot = root, Title = UiText.CommandLine, Content = stack,
                RequestedTheme = theme,
                PrimaryButtonText = "发送", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = true
            },
            Target = target, Command = input
        };
    }

    /// <summary>Index → theme mapping for the appearance ComboBox (0/1/2).</summary>
    public static ThemePreference ThemeFromIndex(int index) => index switch
    {
        1 => ThemePreference.Light,
        2 => ThemePreference.Dark,
        _ => ThemePreference.System
    };

    /// <summary>Persisted theme → ComboBox index, used when the settings window opens.</summary>
    public static int IndexFromTheme(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => 1,
        ThemePreference.Dark => 2,
        _ => 0
    };
}
