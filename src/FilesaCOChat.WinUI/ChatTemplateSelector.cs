using FilesaCOChat.Core;
using FilesaCOChat.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesaCOChat.WinUI;

public enum BubbleSide { Other, Mine, System }

/// <summary>
/// Picks one of three chat-line templates. Authoritative rule:
///   - system / error / command-result entries, and entries whose sender is the
///     reserved "system" name, are centred plain lines (no bubble);
///   - chat entries whose sender equals the local username are outgoing (right);
///   - everything else is incoming (left).
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

    public BubbleSide Classify(ChatLine line) => Classify(line.Entry);

    public BubbleSide Classify(ChatEntry entry)
    {
        if (ChatTimeline.IsSystemLine(entry))
            return BubbleSide.System;

        var me = UsernameProvider?.Invoke() ?? string.Empty;
        return me.Length > 0 && string.Equals(entry.Sender, me, StringComparison.Ordinal)
            ? BubbleSide.Mine
            : BubbleSide.Other;
    }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        BubbleSide side;
        if (item is ChatLine line) side = Classify(line);
        else if (item is ChatEntry entry) side = Classify(entry);
        else return base.SelectTemplateCore(item);
        DataTemplate? chosen = side switch
        {
            BubbleSide.Mine => MineTemplate,
            BubbleSide.System => SystemTemplate,
            _ => OtherTemplate
        };
        return chosen ?? base.SelectTemplateCore(item);
    }

    // WinUI calls the container overload for ListView items; keep both in sync.
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
