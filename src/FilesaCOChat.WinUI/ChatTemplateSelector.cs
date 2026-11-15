using FilesaCOChat.Core;
using FilesaCOChat.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesaCOChat.WinUI;

/// <summary>
/// Thin XAML adapter over <see cref="ChatTimeline.Classify"/>. The decision
/// itself lives in the WinUI-free timeline file so it can be unit-tested:
///   - CommandRequest  -> right-aligned "sent command" terminal bubble;
///   - CommandReceived -> left-aligned "received command" terminal bubble;
///   - System / Error / CommandResult / Sender == "系统" -> centred plain line;
///   - Sender == local username -> right-aligned normal bubble;
///   - everything else -> left-aligned normal bubble.
/// Ordering is NOT changed here: the ListView keeps the ObservableCollection's
/// append order. Timestamp grouping is a per-line flag resolved by ChatTimeline,
/// not a template decision.
/// </summary>
public sealed class ChatTemplateSelector : DataTemplateSelector
{
    /// <summary>Resolved lazily so the username is read after login / preview setup.</summary>
    public Func<string>? UsernameProvider { get; set; }

    public DataTemplate? OtherTemplate { get; set; }
    public DataTemplate? MineTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? CommandTemplate { get; set; }
    public DataTemplate? ReceivedCommandTemplate { get; set; }

    public BubbleCategory Classify(ChatLine line) => Classify(line.Entry);

    public BubbleCategory Classify(ChatEntry entry) =>
        ChatTimeline.Classify(entry, UsernameProvider?.Invoke() ?? string.Empty);

    protected override DataTemplate SelectTemplateCore(object item)
    {
        BubbleCategory category;
        if (item is ChatLine line) category = Classify(line);
        else if (item is ChatEntry entry) category = Classify(entry);
        else return base.SelectTemplateCore(item);
        DataTemplate? chosen = category switch
        {
            BubbleCategory.Mine => MineTemplate,
            BubbleCategory.System => SystemTemplate,
            BubbleCategory.CommandRequest => CommandTemplate,
            BubbleCategory.CommandReceived => ReceivedCommandTemplate,
            _ => OtherTemplate
        };
        return chosen ?? base.SelectTemplateCore(item);
    }

    // WinUI calls the container overload for ListView items; keep both in sync.
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
