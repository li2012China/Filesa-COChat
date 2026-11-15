using System.Collections.Concurrent;
using FilesaCOChat.Core;
using FilesaCOChat.Core.Net;
using FilesaCOChat.Core.Protocol;
using FilesaCOChat.WinUI.ViewModels;
using static FilesaCOChat.WinUI.ViewModels.LinkParser;

namespace FilesaCOChat.Tests;

/// <summary>
/// Head-less coverage for the three UI changes: URL tokenisation (the exact
/// source linked from the WinUI project), command-request model / classification,
/// and the real ChatSession send path emitting the new category.
/// </summary>
public static class RenderTests
{
    private static string Reassemble(IReadOnlyList<Segment> parts) =>
        string.Concat(parts.Select(p => p.Text));

    public static void Register()
    {
        TestRunner.Add("T36", "消息中的 http/https/www 链接被识别且原文不丢失", "UI-3", () =>
        {
            var text = "文档 https://learn.microsoft.com/windows/apps/ 见 www.example.com，谢谢。";
            var parts = LinkParser.Tokenize(text);
            Check.Equal(text, Reassemble(parts), "every input character survives tokenisation");
            var links = parts.Where(p => p.Kind == SegmentKind.Link).ToArray();
            Check.Equal(2, links.Length, "two links found");
            Check.Equal("https://learn.microsoft.com/windows/apps/", links[0].Text, "https link");
            Check.Equal("https://www.example.com/", links[1].Uri!, "www. normalised to https");

            var punctuation = LinkParser.Tokenize("看 (https://example.com/a?x=1), 然后 https://b.org/test).");
            Check.Equal("https://example.com/a?x=1", punctuation[1].Text, "trailing ) and , not part of URL");
            Check.Equal("https://b.org/test", punctuation[3].Text, "trailing ). trimmed");
            Check.Equal(2, punctuation.Count(p => p.Kind == SegmentKind.Link), "both links still found");

            Check.Equal("https://a.com", LinkParser.Tokenize("看https://a.com的内容")[1].Text,
                "CJK characters terminate a URL");
            Check.Equal(0, LinkParser.Tokenize("ftp://host/file").Count(p => p.Kind == SegmentKind.Link),
                "non-http scheme is left as plain text");
            Check.Equal(0, LinkParser.Tokenize("没有链接的普通消息").Count(p => p.Kind == SegmentKind.Link),
                "plain message has no link");
            Check.Equal(0, LinkParser.Tokenize("").Count, "empty input");
            Check.Equal(1, LinkParser.Tokenize("https://a.com").Count, "single link only");
            return Task.CompletedTask;
        });

        TestRunner.Add("T37", "命令行请求是独立气泡类别而非系统行", "UI-3", () =>
        {
            var entry = ChatEntry.CommandRequest("我", "alice", "dir C:\\Users");
            Check.Eq(ChatEntryKind.CommandRequest, entry.Kind, "dedicated kind");
            Check.Equal("[命令] 发送给 alice: dir C:\\Users", entry.Text, "audit wording unchanged");
            Check.False(ChatTimeline.IsSystemLine(entry), "rendered as a bubble, not a centred system line");
            Check.True(ChatTimeline.EntranceTarget(new ChatLine(entry)) is not null,
                "command bubble keeps its entrance animation");

            var line = new ChatLine(entry);
            Check.True(line.IsCommandRequest, "ChatLine exposes the flag");
            Check.Equal("→ alice", line.CommandTarget, "header target");
            Check.Equal("dir C:\\Users", line.CommandBody, "monospace body is the command only");

            // Existing categories keep their original classification.
            Check.True(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.Error, DateTime.Now)), "Error still a system line");
            Check.True(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.CommandResult, DateTime.Now)),
                "CommandResult still a system line");
            Check.False(ChatTimeline.IsSystemLine(
                new ChatEntry("alice", "x", ChatEntryKind.Chat, DateTime.Now)), "Chat still a bubble");
            return Task.CompletedTask;
        });

        TestRunner.Add("T38", "SendCommandAsync 发出 CommandRequest 且仍不执行", "UI-3/D1", async () =>
        {
            var session = new ChatSession(new ProtocolOptions());
            var entries = new List<ChatEntry>();
            session.MessageAdded += e => entries.Add(e);
            try
            {
                await session.SendCommandAsync("alice", "whoami");
                var request = entries.Single(e => e.Kind == ChatEntryKind.CommandRequest);
                Check.Equal("alice", request.Target!, "target preserved");
                Check.Equal("whoami", request.Command!, "command preserved");
                Check.False(entries.Any(e => e.Kind == ChatEntryKind.CommandResult),
                    "no shell output / command result");
                Check.True(entries.Any(e => e.Kind == ChatEntryKind.Error &&
                                           e.Text == UiText.RemoteCommandDisabled),
                    "security notice still emitted");
            }
            finally { await session.DisposeAsync(); }
        });

        TestRunner.Add("T39", "收到的命令是独立气泡类别且重复安全提示可折叠", "UI-3/D1", () =>
        {
            var received = ChatEntry.CommandReceived("bob", "我", "cmd /c whoami");
            Check.Eq(ChatEntryKind.CommandReceived, received.Kind, "dedicated kind");
            Check.Equal("[收到命令] cmd /c whoami", received.Text, "5.1 audit wording unchanged");
            Check.Equal("bob", received.Sender, "sender is the peer, never the reserved 系统 name");
            Check.Equal("bob", received.Origin!, "origin preserved");
            Check.Equal("我", received.Target!, "target preserved");
            Check.Equal("cmd /c whoami", received.Command!, "command preserved");
            Check.False(ChatTimeline.IsSystemLine(received), "rendered as a bubble, not a centred line");
            Check.Eq(BubbleCategory.CommandReceived, ChatTimeline.Classify(received, "我"),
                "selector picks the received-command template");
            Check.True(ChatTimeline.EntranceTarget(new ChatLine(received)) is not null,
                "received command keeps its entrance animation");

            var line = new ChatLine(received);
            Check.True(line.IsCommandReceived, "ChatLine exposes the flag");
            Check.Equal("← bob", line.CommandSource, "header origin");
            Check.Equal("→ 我", line.CommandTarget, "header target");
            Check.Equal("cmd /c whoami", line.CommandBody, "monospace body is the command only");
            Check.Equal(UiText.RemoteCommandDisabled, line.CommandNotice, "footer carries full refusal text");

            // Legacy COMMAND:<target>:<cmd> has no origin field.
            var legacy = ChatEntry.CommandReceived(null, "我", "whoami");
            Check.Equal(UiText.UnknownCommandSender, legacy.Sender, "fallback sender for legacy frames");
            Check.Equal(string.Empty, new ChatLine(legacy).CommandSource, "no misleading origin shown");
            Check.Eq(BubbleCategory.CommandReceived, ChatTimeline.Classify(legacy, "我"),
                "legacy received command is still a bubble");

            // Reproduce the original defect: Chat + Sender == 系统 was forced into
            // the centred SystemLine template by the sender-based fallback rule.
            var old = new ChatEntry(UiText.System, UiText.ReceivedCommand + "whoami",
                ChatEntryKind.Chat, DateTime.Now);
            Check.True(ChatTimeline.IsSystemLine(old), "original bug: classified as a system line");
            Check.Eq(BubbleCategory.System, ChatTimeline.Classify(old, "我"),
                "original entry really used the centred template");

            // Duplicate notice suppression must be tightly scoped.
            var notice = ChatEntry.Error(UiText.RemoteCommandDisabled);
            Check.True(ChatTimeline.IsRedundantCommandNotice(notice, received),
                "notice right after a command bubble is collapsed");
            Check.False(ChatTimeline.IsRedundantCommandNotice(notice, null), "no predecessor -> keep");
            Check.False(ChatTimeline.IsRedundantCommandNotice(notice, old),
                "predecessor is not a command bubble -> keep");
            Check.False(ChatTimeline.IsRedundantCommandNotice(ChatEntry.Error("别的安全错误"), received),
                "a different error is never suppressed");

            Check.Eq(BubbleCategory.CommandRequest,
                ChatTimeline.Classify(ChatEntry.CommandRequest("我", "alice", "dir"), "我"),
                "sent request still maps to the warm outgoing template");
            return Task.CompletedTask;
        });

        TestRunner.Add("T40", "真实 FCCH COMMAND 帧到达时产生 CommandReceived 气泡数据", "UI-3/D1", async () =>
        {
            var o = TestOptions.Make(21495);
            var server = await TestOptions.StartServerAsync(o, "server");
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var messages = new ConcurrentBag<ChatEntry>();
            a.MessageAdded += e => messages.Add(e);
            var raw = new RawPeer(o);
            try
            {
                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "target client joined");
                await raw.ConnectAsync();
                await raw.SendFrameAsync(raw.Frame(FrameTypes.Join, 1) with { User = "attacker" });
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("attacker"),
                    "attacker registered");
                await raw.SendFrameAsync(raw.Frame(FrameTypes.Command, 2) with
                { Target = "alice", Command = "whoami" });
                await TestOptions.WaitUntilAsync(
                    () => messages.Any(e => e.Kind == ChatEntryKind.CommandReceived),
                    "received command emitted with the bubble kind");

                var e = messages.First(x => x.Kind == ChatEntryKind.CommandReceived);
                Check.Equal("[收到命令] whoami", e.Text, "audit text on the wire-derived entry");
                Check.Equal("whoami", e.Command!, "command payload");
                Check.Equal("alice", e.Target!, "target payload");
                Check.Equal("attacker", e.Origin!, "relay propagates the issuing peer's name");
                Check.False(messages.Any(x => x.Kind == ChatEntryKind.CommandResult),
                    "no shell output / execution");
                Check.Eq(BubbleCategory.CommandReceived, ChatTimeline.Classify(e, "alice"),
                    "classifies as a received-command bubble");
            }
            finally
            {
                raw.Dispose(); await a.CloseAsync(); await server.CloseAsync();
            }
        });
    }
}
