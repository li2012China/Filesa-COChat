using FilesaCOChat.Core;
using FilesaCOChat.WinUI.ViewModels;

namespace FilesaCOChat.Tests;

/// <summary>
/// Head-less verification of the two chat-display fixes. ChatLine / ChatTimeline
/// are linked from the WinUI project (see the csproj), so these are the exact
/// rules used by the real templates.
/// </summary>
public static class TimelineTests
{
    private static readonly DateTime Base = new(2026, 11, 14, 18, 1, 0);

    private static ChatLine Sys(int second, ChatEntryKind kind = ChatEntryKind.System) =>
        new(new ChatEntry(UiText.System, "系统消息", kind, Base.AddSeconds(second)));

    private static ChatLine Chat(string sender, int second) =>
        new(new ChatEntry(sender, "聊天内容", ChatEntryKind.Chat, Base.AddSeconds(second)));

    private static List<ChatLine> Grouped(params ChatLine[] lines)
    {
        var list = lines.ToList();
        ChatTimeline.Refresh(list, null);
        return list;
    }

    private static string Flags(IReadOnlyList<ChatLine> lines) =>
        string.Join("", lines.Select(l => l.ShowTimestamp ? "1" : "0"));

    private static string Anims(IReadOnlyList<ChatLine> lines) =>
        string.Join("", lines.Select(l => l.PlayEntranceAnimation ? "1" : "0"));

    public static void Register()
    {
        TestRunner.Add("T21", "同分钟连续系统消息只在组末显示一次时间", "UI-1", () =>
        {
            var lines = Grouped(Sys(0), Sys(2), Sys(4));
            Check.Equal("001", Flags(lines), "only the group tail shows HH:mm");
            Check.Equal(1, lines.Count(l => l.ShowTimestamp), "exactly one timestamp");
            Check.Equal("18:01", lines[2].Timestamp.ToString("HH:mm"), "group time is on the tail");
            Check.Equal("000", Anims(lines), "bulk load never animates");
            return Task.CompletedTask;
        });

        TestRunner.Add("T22", "跨分钟的系统消息按分钟分组各自显示一次时间", "UI-1", () =>
        {
            // 18:01:00, 18:01:02 | 18:02:01, 18:02:03
            var lines = Grouped(Sys(0), Sys(2), Sys(61), Sys(63));
            Check.Equal("0101", Flags(lines), "two groups, one timestamp each");
            Check.Equal(2, lines.Count(l => l.ShowTimestamp), "exactly two timestamps");
            return Task.CompletedTask;
        });

        TestRunner.Add("T23", "聊天消息打断分组且已有气泡时间不受影响", "UI-1", () =>
        {
            var lines = Grouped(Sys(0), Chat("alice", 1), Sys(2), Chat("bob", 3));
            Check.Equal("1111", Flags(lines), "chat lines keep their header time");
            Check.Equal("alice", lines[1].Sender, "existing line data unchanged");
            Check.Equal("聊天内容", lines[1].Text, "existing line text unchanged");
            Check.True(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.Error, Base)), "Error is a system line");
            Check.True(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.CommandResult, Base)), "CommandResult is a system line");
            Check.True(ChatTimeline.IsSystemLine(
                new ChatEntry(UiText.System, "x", ChatEntryKind.Chat, Base)), "reserved sender is a system line");
            Check.False(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.Chat, Base)), "normal chat is not a system line");
            return Task.CompletedTask;
        });

        TestRunner.Add("T24", "空列表与单条系统消息的边界", "UI-1", () =>
        {
            Check.Equal("", Flags(Grouped()), "empty timeline");
            Check.Equal("1", Flags(Grouped(Sys(0))), "a lone system line still shows its time");
            return Task.CompletedTask;
        });

        TestRunner.Add("T25", "同分钟判定包含完整日期", "UI-1", () =>
        {
            var a = new ChatLine(new ChatEntry(UiText.System, "a", ChatEntryKind.System,
                new DateTime(2026, 11, 14, 23, 59, 30)));
            var b = new ChatLine(new ChatEntry(UiText.System, "b", ChatEntryKind.System,
                new DateTime(2026, 11, 15, 0, 0, 10)));
            Check.Equal("11", Flags(Grouped(a, b)), "23:59 / next-day 00:00 are separate groups");
            Check.False(ChatTimeline.SameMinute(a.Timestamp, b.Timestamp), "minute boundary");
            Check.True(ChatTimeline.SameMinute(a.Timestamp, a.Timestamp.AddSeconds(20)), "same minute");
            Check.False(ChatTimeline.SameMinute(a.Timestamp,
                new DateTime(2026, 11, 15, 23, 59, 30)), "same clock time on another day");
            return Task.CompletedTask;
        });

        TestRunner.Add("T26", "入场动画标记唯一且只给新气泡", "UI-2", () =>
        {
            var lines = new List<ChatLine> { Chat("alice", 0), Chat("我", 10), Sys(20) };
            lines[0].PlayEntranceAnimation = true; // set before Add, as the VM does
            ChatTimeline.Refresh(lines, ChatTimeline.EntranceTarget(lines[0]));
            Check.Equal("100", Anims(lines), "only the designated line animates");
            Check.Equal(1, lines.Count(l => l.PlayEntranceAnimation), "exactly one animated line");

            var added = Chat("我", 30);
            added.PlayEntranceAnimation = true;
            lines.Add(added);
            ChatTimeline.Refresh(lines, ChatTimeline.EntranceTarget(added));
            Check.Equal("0001", Anims(lines), "old target cleared, only the newest animates");
            Check.False(lines[2].PlayEntranceAnimation, "system line never animates");

            var sys = Sys(40); // no flag raised for a system line
            lines.Add(sys);
            ChatTimeline.Refresh(lines, ChatTimeline.EntranceTarget(sys));
            Check.Equal("00000", Anims(lines), "appending a system line animates nothing");
            Check.True(ChatTimeline.EntranceTarget(sys) is null, "system lines have no animation target");

            added.PlayEntranceAnimation = false; // consumed by the container
            ChatTimeline.Refresh(lines, ChatTimeline.EntranceTarget(added));
            Check.False(added.PlayEntranceAnimation, "a consumed flag is never re-enabled");
            ChatTimeline.Refresh(lines, null);
            Check.Equal("00000", Anims(lines), "bulk refresh leaves nothing pending");
            return Task.CompletedTask;
        });

        TestRunner.Add("T27", "追加消息时分组动态收敛且消息顺序不变", "UI-1", () =>
        {
            var lines = new List<ChatLine> { Sys(0) };
            ChatTimeline.Refresh(lines, null);
            Check.Equal("1", Flags(lines), "single system line shows its time");

            var flips = 0;
            lines[0].PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChatLine.ShowTimestamp)) flips++;
            };

            lines.Add(Sys(2));
            ChatTimeline.Refresh(lines, null);
            Check.Equal("01", Flags(lines), "group tail moves to the new line");
            Check.Equal(1, flips, "previous tail hidden by one property change");

            lines.Add(Sys(4));
            ChatTimeline.Refresh(lines, null);
            Check.Equal("001", Flags(lines), "three lines still share one timestamp");
            Check.Equal(1, flips, "no redundant notifications for unchanged flags");

            lines.Add(Chat("alice", 10));
            ChatTimeline.Refresh(lines, null);
            Check.Equal("0011", Flags(lines), "chat line breaks the run and keeps its time");

            lines.Add(Sys(70)); // 18:02:10 -> new group
            ChatTimeline.Refresh(lines, null);
            Check.Equal("00111", Flags(lines), "next-minute group gets its own timestamp");
            Check.Equal(5, lines.Count, "no message inserted, removed or reordered");
            Check.Equal("alice", lines[3].Sender, "append order preserved");
            return Task.CompletedTask;
        });
    }
}
