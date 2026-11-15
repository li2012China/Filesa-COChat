using FilesaCOChat.Core;

namespace FilesaCOChat.WinUI.ViewModels;

/// <summary>
/// Pure presentation rules for the chat list. Deliberately free of WinUI types
/// so the very same source is linked into the head-less verification suite.
/// It never reorders, inserts or removes messages: the list stays in receive
/// (append) order and only two per-line display flags are recomputed.
/// </summary>
public static class ChatTimeline
{
    /// <summary>
    /// A "system line" is anything rendered by the centred SystemLine template:
    /// System / Error / CommandResult entries, plus any chat entry sent by the
    /// reserved "系统" name (see ChatTemplateSelector).
    /// </summary>
    public static bool IsSystemLine(ChatEntry entry) =>
        entry.Kind != ChatEntryKind.Chat ||
        string.Equals(entry.Sender, UiText.System, StringComparison.Ordinal);

    /// <summary>Full-date comparison: 23:59 and next-day 00:00 are not the same minute.</summary>
    public static bool SameMinute(DateTime a, DateTime b) =>
        a.Year == b.Year && a.Month == b.Month && a.Day == b.Day &&
        a.Hour == b.Hour && a.Minute == b.Minute;

    /// <summary>
    /// Grouping rule: a maximal run of consecutive system lines within one minute
    /// is one group; only its LAST line shows the timestamp underneath the group.
    /// A chat line breaks the run and always keeps its own header timestamp.
    /// </summary>
    public static bool ShouldShowTimestamp(IReadOnlyList<ChatLine> lines, int index)
    {
        var line = lines[index];
        if (!line.IsSystemLine) return true;
        return index + 1 >= lines.Count ||
               !lines[index + 1].IsSystemLine ||
               !SameMinute(line.Timestamp, lines[index + 1].Timestamp);
    }

    /// <summary>
    /// Recomputes grouping for the whole list and CLEARS the animation flag on
    /// every line except <paramref name="entranceTarget"/> (the one line allowed
    /// to animate). The flag itself is raised by the append path before the item
    /// enters the collection, and consumed by the first container that renders
    /// it: Refresh must never re-enable an already-consumed flag, or a recycled
    /// container would replay the animation. Pass null for bulk loads / previews.
    /// </summary>
    public static void Refresh(IReadOnlyList<ChatLine> lines, ChatLine? entranceTarget)
    {
        for (var i = 0; i < lines.Count; i++)
            lines[i].ShowTimestamp = ShouldShowTimestamp(lines, i);
        foreach (var line in lines)
            if (!ReferenceEquals(line, entranceTarget)) line.PlayEntranceAnimation = false;
    }

    /// <summary>Only a newly appended bubble may animate; system lines never do.</summary>
    public static ChatLine? EntranceTarget(ChatLine line) => line.IsSystemLine ? null : line;
}
