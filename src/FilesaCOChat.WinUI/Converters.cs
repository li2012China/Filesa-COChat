using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using FilesaCOChat.Core;

namespace FilesaCOChat.WinUI;

/// <summary>System / error lines are visually distinct, as in the Tk version.</summary>
public sealed class KindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var kind = value is ChatEntryKind k ? k : ChatEntryKind.Chat;
        return kind switch
        {
            ChatEntryKind.System => new SolidColorBrush(Colors.DodgerBlue),
            ChatEntryKind.Error => new SolidColorBrush(Colors.OrangeRed),
            ChatEntryKind.CommandResult => new SolidColorBrush(Colors.MediumPurple),
            ChatEntryKind.CommandRequest => new SolidColorBrush(Colors.DarkOrange),
            ChatEntryKind.CommandReceived => new SolidColorBrush(Colors.DeepSkyBlue),
            _ => new SolidColorBrush(Application.Current.RequestedTheme == ApplicationTheme.Dark
                     ? Colors.White : Colors.Black)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Timeline label. "Full" (tooltip) keeps seconds and the date so two messages
/// inside the same minute can still be ordered exactly. Whether the label is
/// actually rendered is decided per line by ChatLine.ShowTimestamp (ChatTimeline).
/// </summary>
public sealed class TimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime dt) return string.Empty;
        return string.Equals(parameter as string, "Full", StringComparison.OrdinalIgnoreCase)
            ? dt.ToString("yyyy-MM-dd HH:mm:ss")
            : dt.ToString("HH:mm");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hides the timestamp on all but the last line of a system-message group.
/// Uses Collapsed (not Hidden) so the group keeps exactly one line of spacing.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
