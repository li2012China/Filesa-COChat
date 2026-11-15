using FilesaCOChat.Core;

namespace FilesaCOChat.WinUI.ViewModels;

/// <summary>
/// Pure presentation rules for the chat list. Deliberately free of WinUI types
/// so the very same source is linked into the head-less verification suite.
/// It never reorders, inserts or removes messages: the list stays in receive
/// (append) order and only two per-line display flags are recomputed.
/// </summary>
/// <summary>
/// Result of the WinUI-free classification shared by ChatTemplateSelector and
/// the head-less verification suite. Kept here (not in the selector) so the
/// "why is this line not a bubble?" decision can be unit-tested without XAML.
/// </summary>
public enum BubbleCategory { Other, Mine, System, CommandRequest, CommandReceived }

public static class ChatTimeline
{
    /// <summary>Both command categories are terminal-style bubbles, never centred lines.</summary>
    public static bool IsCommandBubble(ChatEntryKind kind) =>
        kind is ChatEntryKind.CommandRequest or ChatEntryKind.CommandReceived;

    /// <summary>
    /// A "system line" is anything rendered by the centred SystemLine template:
    /// System / Error / CommandResult entries, plus any entry sent by the
    /// reserved "系统" name.
    /// <para>
    /// The kinds are listed explicitly (rather than "everything except Chat")
    /// so a bubble-only category can be added without accidentally becoming a
    /// centred system line and losing its timestamp / entrance animation.
    /// Command bubbles are excluded first: a received command used to be emitted
    /// as Chat + Sender == "系统", which this sender rule forced into SystemLine.
    /// </para>
    /// </summary>
    public static bool IsSystemLine(ChatEntry entry)
    {
        if (IsCommandBubble(entry.Kind)) return false;
        return entry.Kind is ChatEntryKind.System or ChatEntryKind.Error or ChatEntryKind.CommandResult ||
               string.Equals(entry.Sender, UiText.System, StringComparison.Ordinal);
    }

    /// <summary>
    /// Authoritative template decision. Order matters: command kinds are checked
    /// before IsSystemLine so they can never fall through to the centred line.
    /// </summary>
    public static BubbleCategory Classify(ChatEntry entry, string? localUsername)
    {
        if (entry.Kind == ChatEntryKind.CommandRequest) return BubbleCategory.CommandRequest;
        if (entry.Kind == ChatEntryKind.CommandReceived) return BubbleCategory.CommandReceived;
        if (IsSystemLine(entry)) return BubbleCategory.System;
        return !string.IsNullOrEmpty(localUsername) &&
               string.Equals(entry.Sender, localUsername, StringComparison.Ordinal)
            ? BubbleCategory.Mine
            : BubbleCategory.Other;
    }

    /// <summary>
    /// A command bubble already prints UiText.RemoteCommandDisabled in its footer.
    /// Core still emits the same wording as a separate Error entry for logging /
    /// audit, so the UI may drop it only when it immediately follows a command
    /// bubble. Pure function: the decision is unit-testable without a dispatcher.
    /// </summary>
    public static bool IsRedundantCommandNotice(ChatEntry entry, ChatEntry? previous) =>
        entry.Kind == ChatEntryKind.Error &&
        entry.Text == UiText.RemoteCommandDisabled &&
        previous is not null &&
        IsCommandBubble(previous.Kind);

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
