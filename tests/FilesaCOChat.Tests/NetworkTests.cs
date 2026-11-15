using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using FilesaCOChat.Core;
using FilesaCOChat.Core.Net;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Tests;

/// <summary>Raw TCP peer used to emulate a shipped 5.1 client or a hostile host.</summary>
internal sealed class RawPeer : IDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream _stream = null!;
    private readonly ProtocolOptions _options;
    public RawPeer(ProtocolOptions options) => _options = options;

    public async Task ConnectAsync(string ip = "127.0.0.1")
    {
        await _client.ConnectAsync(ip, _options.ChatPort);
        _stream = _client.GetStream();
    }

    public Task SendLegacyAsync(string text) => SendBytesAsync(LegacyCodec.B(text));
    public Task SendBytesAsync(byte[] data) => _stream.WriteAsync(data).AsTask();

    public async Task<string?> ReadLegacyAsync(int timeoutMs = 1500)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[4096];
        try
        {
            var n = await _stream.ReadAsync(buffer, cts.Token);
            return n == 0 ? null : Encoding.UTF8.GetString(buffer, 0, n);
        }
        catch (OperationCanceledException) { return null; }
    }

    public async Task<Frame?> ReadFrameAsync(byte[] key, bool requireMac, int timeoutMs = 2500)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var header = new byte[SecureFrameCodec.HeaderSize];
            await ReadExactlyAsync(header, cts.Token);
            if (!SecureFrameCodec.TryParseHeader(header, out var len, out _)) return null;
            var body = new byte[len];
            await ReadExactlyAsync(body, cts.Token);
            return SecureFrameCodec.TryDecode(body, key, requireMac, out var f, out _) ? f : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (EndOfStreamException) { return null; }
    }

    private async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken ct)
    {
        var done = 0;
        while (done < destination.Length)
        {
            var n = await _stream.ReadAsync(destination[done..], ct);
            if (n == 0) throw new EndOfStreamException();
            done += n;
        }
    }

    /// <summary>Drains hello/userlist frames until the predicate matches.</summary>
    public async Task<Frame?> ReadUntilAsync(Func<Frame, bool> predicate, byte[] key, bool requireMac, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var remaining = (int)Math.Max(100, timeoutMs - sw.ElapsedMilliseconds);
            var f = await ReadFrameAsync(key, requireMac, Math.Min(remaining, 900));
            if (f is not null && predicate(f)) return f;
        }
        return null;
    }

    public Frame Frame(string type, long seq, string sid = "RAW") =>
        FilesaCOChat.Core.Protocol.Frame.Make(type, sid, seq);

    public Task SendFrameAsync(Frame f, byte[]? key = null, bool mac = false) =>
        SendBytesAsync(SecureFrameCodec.Encode(f, key ?? SecureFrameCodec.DeriveKey(null), mac));

    public void Dispose() { try { _stream?.Dispose(); } catch { } _client.Dispose(); }
}

public static class NetworkTests
{
    private static ConcurrentBag<ChatEntry> Capture(ChatApplication app)
    {
        var bag = new ConcurrentBag<ChatEntry>();
        app.MessageAdded += e => bag.Add(e);
        return bag;
    }
    private static bool HasChat(ConcurrentBag<ChatEntry> bag, string text) =>
        bag.Any(e => e.Kind == ChatEntryKind.Chat && e.Text == text);
    private static bool HasText(ConcurrentBag<ChatEntry> bag, string text) =>
        bag.Any(e => e.Text.Contains(text, StringComparison.Ordinal));

    public static void Register()
    {
        TestRunner.Add("T09", "双客户端消息互通：服务端显式广播（含长消息/保留前缀）", "D2 / D3", async () =>
        {
            var o = TestOptions.Make(21451);
            var server = await TestOptions.StartServerAsync(o, "server");
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var b = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var mS = Capture(server); var mA = Capture(a); var mB = Capture(b);
            try
            {
                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "alice joined");
                Check.True(await b.JoinDirectAsync("bob", "127.0.0.1", true), "bob joined");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Count == 3, "3 users");

                Check.True(await a.SendMessageAsync("hello from A"), "A sent");
                await TestOptions.WaitUntilAsync(() => HasChat(mB, "hello from A") && HasChat(mS, "hello from A"),
                    "server + bob receive A's message");
                Check.True(HasChat(mA, "hello from A"), "sender still displays locally");

                Check.True(await b.SendMessageAsync("hello from B"), "B sent");
                await TestOptions.WaitUntilAsync(() => HasChat(mA, "hello from B") && HasChat(mS, "hello from B"),
                    "server + alice receive B's message");

                Check.False(await a.SendMessageAsync("COMMAND:bob:whoami"), "reserved control prefix blocked");
                Check.True(HasText(mA, "协议保留前缀"), "user-visible warning shown");
                Check.False(await a.SendMessageAsync(new string('x', 1100)), "over-1024 message blocked");
                Check.True(HasText(mA, "1024 字节"), "length warning shown");
            }
            finally
            {
                await a.CloseAsync(); await b.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T10", "旧版裸报文互操作 + 用户名伪造 / USERLIST 注入被拒绝", "兼容 / D3 / D4", async () =>
        {
            var o = TestOptions.Make(21461);
            var server = await TestOptions.StartServerAsync(o, "server");
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var mA = Capture(a);
            var raw = new RawPeer(o);
            try
            {
                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "framed client joined");
                await raw.ConnectAsync();
                await raw.SendLegacyAsync("JOIN:legacy1");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("legacy1"), "legacy peer registered");

                Check.True(await a.SendMessageAsync("from framed"), "framed send");
                var sawFramedBroadcast = false;
                for (var i = 0; i < 6 && !sawFramedBroadcast; i++)
                {
                    var line = await raw.ReadLegacyAsync(900);
                    if (line is not null && line.Contains("alice:from framed", StringComparison.Ordinal)) sawFramedBroadcast = true;
                }
                Check.True(sawFramedBroadcast, "framed chat translated to legacy 'user:text'");

                await raw.SendLegacyAsync("legacy1:hi legacy");
                await TestOptions.WaitUntilAsync(() => HasChat(mA, "hi legacy"), "legacy chat reaches framed client");

                await raw.SendLegacyAsync("alice:forged");
                await raw.SendLegacyAsync("USERLIST:hacker,evil");
                await Task.Delay(350);
                Check.False(HasText(mA, "forged"), "spoofed chat not forwarded");
                Check.False(server.Session!.Users.Contains("hacker"), "forged USERLIST ignored");
            }
            finally
            {
                raw.Dispose(); await a.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T11", "UDP 广播发现 / 能力探测 / 无服务器返回空", "兼容", async () =>
        {
            var o = TestOptions.Make(21471);
            var server = await TestOptions.StartServerAsync(o, "server");
            try
            {
                var client = new DiscoveryService(o.Clone());
                var ip = await client.FindServerAsync();
                Check.Equal("127.0.0.1", ip ?? "", "broadcast discovery finds local server");
                Check.True(await client.ProbeCapabilityAsync(ip!), "FCCH capability advertised");
                await client.DisposeAsync();

                var empty = TestOptions.Make(21475);
                var absent = new DiscoveryService(empty);
                var none = await absent.FindServerAsync();
                Check.True(none is null, "no server -> null");
                Check.False(await absent.ProbeCapabilityAsync("127.0.0.1"), "no capability reply");
                await absent.DisposeAsync();
            }
            finally { await server.CloseAsync(); }
        });

        TestRunner.Add("T12", "FCCH 帧粘连一次写入 / 分片两次写入均正确解析", "D3", async () =>
        {
            var o = TestOptions.Make(21481);
            var server = await TestOptions.StartServerAsync(o, "server");
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var mA = Capture(a);
            var raw = new RawPeer(o);
            var key = SecureFrameCodec.DeriveKey(null);
            try
            {
                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "client joined");
                await raw.ConnectAsync();
                await raw.SendFrameAsync(raw.Frame(FrameTypes.Join, 1) with { User = "framedRaw" });
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("framedRaw"), "raw framed peer registered");

                var f1 = SecureFrameCodec.Encode(raw.Frame(FrameTypes.Chat, 2) with { User = "framedRaw", Text = "coalesced-1" }, key, false);
                var f2 = SecureFrameCodec.Encode(raw.Frame(FrameTypes.Chat, 3) with { User = "framedRaw", Text = "coalesced-2" }, key, false);
                await raw.SendBytesAsync(f1.Concat(f2).ToArray()); // two frames, one TCP write
                await TestOptions.WaitUntilAsync(() => HasChat(mA, "coalesced-1") && HasChat(mA, "coalesced-2"),
                    "both coalesced frames delivered");

                var f3 = SecureFrameCodec.Encode(raw.Frame(FrameTypes.Chat, 4) with { User = "framedRaw", Text = "fragmented" }, key, false);
                await raw.SendBytesAsync(f3[..7]);
                await Task.Delay(8);
                await raw.SendBytesAsync(f3[7..]);
                await TestOptions.WaitUntilAsync(() => HasChat(mA, "fragmented"), "fragmented frame reassembled");
            }
            finally
            {
                raw.Dispose(); await a.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T15", "COMMAND 帧被转发但所有接收端拒绝执行", "D1", async () =>
        {
            var o = TestOptions.Make(21491);
            var server = await TestOptions.StartServerAsync(o, "server");
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var mA = Capture(a);
            var raw = new RawPeer(o);
            try
            {
                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "target client joined");
                await raw.ConnectAsync();
                await raw.SendFrameAsync(raw.Frame(FrameTypes.Join, 1) with { User = "attacker" });
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("attacker"), "attacker registered");
                await raw.SendFrameAsync(raw.Frame(FrameTypes.Command, 2) with
                { Target = "alice", Command = "whoami && calc.exe" });
                await TestOptions.WaitUntilAsync(() => HasText(mA, "远程命令执行已在 WinUI3 版禁用"),
                    "target shows refusal notice");
                Check.False(mA.Any(e => e.Kind == ChatEntryKind.CommandResult), "no command result produced");
                Check.False(HasText(mA, "[命令结果]"), "no shell output displayed");
                Check.True(HasText(mA, "[收到命令]"), "command transcript retained for audit");
            }
            finally
            {
                raw.Dispose(); await a.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T16", "服务器迁移：确定性提升 + 任期 + ACK，非固定 sleep", "D6", async () =>
        {
            var o = TestOptions.Make(21501);
            o.MigrationAckTimeout = TimeSpan.FromSeconds(3);
            var server = await TestOptions.StartServerAsync(o, "server");
            server.MigrateOnClose = false;
            var alice = new RawPeer(o);
            var bob = new RawPeer(o);
            var key = SecureFrameCodec.DeriveKey(null);
            try
            {
                await alice.ConnectAsync(); await bob.ConnectAsync();
                await alice.SendFrameAsync(alice.Frame(FrameTypes.Join, 1) with { User = "alice" });
                await bob.SendFrameAsync(bob.Frame(FrameTypes.Join, 1) with { User = "bob" });
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Count == 3, "3 participants");

                static bool IsMigration(Frame f) => f.Type is FrameTypes.Promote or FrameTypes.Migrate;
                Task<Frame?> aliceRead = alice.ReadUntilAsync(IsMigration, key, false, 5000);
                Task<Frame?> bobRead = bob.ReadUntilAsync(IsMigration, key, false, 5000);
                var migrationTask = server.Session!.InitiateMigrationAsync();
                var frames = await Task.WhenAll(aliceRead, bobRead).WaitAsync(TimeSpan.FromSeconds(6));
                Check.NotNull(frames[0], "alice got a migration frame");
                Check.NotNull(frames[1], "bob got a migration frame");
                Check.True(frames[0]!.Type == FrameTypes.Promote, "deterministic winner (alice) promoted");
                Check.True(frames[1]!.Type == FrameTypes.Migrate, "other peer told to migrate");
                Check.True((frames[1]!.Ip ?? "").Length > 0, "migrate carries new server IP");
                Check.True(frames[0]!.Term > 0, "promotion carries a term");

                await alice.SendFrameAsync(alice.Frame(FrameTypes.Ack, 2) with { Term = frames[0]!.Term, Ip = "127.0.0.1" });
                Check.True(await migrationTask, "migration confirmed by ACK");
                Check.True(server.Session!.ActiveTerm >= 1, "term recorded");
            }
            finally
            {
                alice.Dispose(); bob.Dispose(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T17", "PSK 模式：拒绝旧版明文 peer，接受 HMAC 帧 peer", "D4", async () =>
        {
            var o = TestOptions.Make(21511);
            o.PreSharedKey = "room-secret";
            var server = await TestOptions.StartServerAsync(o, "server");
            server.MigrateOnClose = false;
            var raw = new RawPeer(o);
            var a = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            try
            {
                await raw.ConnectAsync();
                await raw.SendLegacyAsync("JOIN:legacy");
                await Task.Delay(350);
                Check.False(server.Session!.Users.Contains("legacy"), "plaintext legacy peer refused under PSK");

                Check.True(await a.JoinDirectAsync("alice", "127.0.0.1", true), "HMAC client joined");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("alice"), "authenticated peer registered");
            }
            finally
            {
                raw.Dispose(); await a.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T18", "重名用户收到 USERNAME_EXISTS", "兼容", async () =>
        {
            var o = TestOptions.Make(21521);
            var server = await TestOptions.StartServerAsync(o, "server");
            server.MigrateOnClose = false;
            var one = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var two = new ChatApplication(o.Clone()) { MigrateOnClose = false };
            var m2 = Capture(two);
            try
            {
                Check.True(await one.JoinDirectAsync("dup", "127.0.0.1", true), "first joined");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Contains("dup"), "first registered");
                Check.True(await two.JoinDirectAsync("dup", "127.0.0.1", true), "second connected");
                await TestOptions.WaitUntilAsync(() => HasText(m2, "用户名已存在"), "duplicate rejected");
                Check.Equal(2, server.Session!.Users.Count, "user list unchanged");
            }
            finally
            {
                await one.CloseAsync(); await two.CloseAsync(); await server.CloseAsync();
            }
        });

        TestRunner.Add("T19", "并发 JOIN 下状态一致（线程安全）", "D9", async () =>
        {
            var o = TestOptions.Make(21531);
            var server = await TestOptions.StartServerAsync(o, "server");
            server.MigrateOnClose = false;
            var clients = Enumerable.Range(0, 5)
                .Select(_ => new ChatApplication(o.Clone()) { MigrateOnClose = false }).ToArray();
            try
            {
                var joins = clients.Select((c, i) => c.JoinDirectAsync("u" + i, "127.0.0.1", true)).ToArray();
                var results = await Task.WhenAll(joins);
                Check.True(results.All(x => x), "all concurrent joins connected");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Count == 6, "6 users (server + 5)");
                Check.Equal(6, server.Session!.Users.Distinct().Count(), "no duplicate/lost user");
            }
            finally
            {
                foreach (var c in clients) await c.CloseAsync();
                await server.CloseAsync();
            }
        });
    }
}
