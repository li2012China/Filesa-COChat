using System.Net;
using System.Text;
using System.Text.Json;
using FilesaCOChat.Core;
using FilesaCOChat.Core.Net;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Tests;

public static class CodecTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    public static void Register()
    {
        TestRunner.Add("T01", "旧版报文逐字节一致（线格式冻结）", "兼容基线", () =>
        {
            Check.Bytes(U("COCHAT_DISCOVERY:11451"), LegacyCodec.DiscoveryRequest(11451), "discovery request");
            Check.Bytes(U("COCHAT_RESPONSE:11451"), LegacyCodec.DiscoveryResponse(11451), "discovery response");
            Check.Bytes(U("JOIN:alice"), LegacyCodec.Join("alice"), "JOIN");
            Check.Bytes(U("USERLIST:alice,bob"), LegacyCodec.UserList(new[] { "alice", "bob" }), "USERLIST");
            Check.Bytes(U("USERLIST_REQUEST"), LegacyCodec.UserListRequestFrame(), "USERLIST_REQUEST");
            Check.Bytes(U("USERNAME_EXISTS"), LegacyCodec.UsernameExistsFrame(), "USERNAME_EXISTS");
            Check.Bytes(U("alice:hi:there"), LegacyCodec.Chat("alice", "hi:there"), "chat keeps colons");
            Check.Bytes(U("MIGRATE:192.168.1.7"), LegacyCodec.Migrate("192.168.1.7"), "MIGRATE");
            Check.Bytes(U("PROMOTE:192.168.1.2"), LegacyCodec.Promote("192.168.1.2"), "PROMOTE");
            Check.Bytes(U("COMMAND:bob:whoami"), LegacyCodec.Command("bob", "whoami"), "COMMAND");
            Check.Bytes(U("a.txt|12345"), LegacyCodec.FileInfo("a.txt", 12345), "file-info");
            // Not a wire constant any more: the product version drives the update
            // check only, so just require that it stays a parseable version.
            Check.True(Version.TryParse(ProtocolOptions.AppVersion, out _),
                "APP_VERSION is a valid version (" + ProtocolOptions.AppVersion + ")");
            Check.Equal(11451, ProtocolOptions.DefaultChatPort, "chat port");
            Check.Equal(11452, ProtocolOptions.DefaultDiscoveryPort, "discovery port");
            Check.Equal(11450, ProtocolOptions.DefaultFilePort, "file port");
            return Task.CompletedTask;
        });

        TestRunner.Add("T02", "严格解析：控制帧按角色/状态限定，聊天帧绑定会话", "D3 / D4", () =>
        {
            var serverFresh = new ParseContext { IsServer = true, Registered = false, FromServerLink = false };
            var serverRegistered = new ParseContext { IsServer = true, Registered = true, BoundUsername = "alice", FromServerLink = false };
            var clientFromServer = new ParseContext { IsServer = false, Registered = true, FromServerLink = true };
            var clientOther = new ParseContext { IsServer = false, Registered = true, FromServerLink = false };

            Check.Eq(MessageKind.Join, LegacyParser.Parse("JOIN:alice", serverFresh).Kind, "JOIN on fresh server link");
            Check.False(LegacyParser.Parse("JOIN:alice", serverRegistered).Accepted, "duplicate JOIN rejected");
            Check.False(LegacyParser.Parse("JOIN:alice", clientFromServer).Accepted, "client cannot receive JOIN");

            Check.Eq(MessageKind.UserList, LegacyParser.Parse("USERLIST:alice,bob", clientFromServer).Kind, "USERLIST from server");
            Check.False(LegacyParser.Parse("USERLIST:hacker,evil", serverRegistered).Accepted, "server ignores forged USERLIST");
            Check.False(LegacyParser.Parse("USERLIST:a,b", clientOther).Accepted, "non-server link cannot set user list");

            Check.Eq(MessageKind.UserListRequest, LegacyParser.Parse("USERLIST_REQUEST", serverRegistered).Kind, "request on server");
            Check.False(LegacyParser.Parse("USERLIST_REQUEST", clientFromServer).Accepted, "client ignores request");
            Check.Eq(MessageKind.UsernameExists, LegacyParser.Parse("USERNAME_EXISTS", clientFromServer).Kind, "exists on client");
            Check.False(LegacyParser.Parse("USERNAME_EXISTS", serverRegistered).Accepted, "server ignores exists");

            var chat = LegacyParser.Parse("alice:hello", serverRegistered);
            Check.Eq(MessageKind.Chat, chat.Kind, "chat parsed");
            Check.Equal("hello", chat.Text, "chat text");
            Check.False(LegacyParser.Parse("bob:forged", serverRegistered).Accepted, "username spoof rejected");
            Check.False(LegacyParser.Parse("alice:hello", serverFresh).Accepted, "chat before JOIN rejected");
            Check.False(LegacyParser.Parse("JOIN:hi", serverRegistered).Accepted, "reserved username chat rejected");

            var cmd = LegacyParser.Parse("COMMAND:bob:whoami", clientFromServer);
            Check.Eq(MessageKind.Command, cmd.Kind, "COMMAND recognised");
            Check.False(cmd.ExecutionAllowed, "COMMAND never authorises execution");
            Check.Equal("whoami", cmd.Command, "command transcript retained");
            return Task.CompletedTask;
        });

        TestRunner.Add("T03", "FCCH 帧头 / HMAC / 重放保护", "D3 / D4", () =>
        {
            var key = SecureFrameCodec.DeriveKey("room-secret");
            var frame = Frame.Make(FrameTypes.Chat, "SID", 7) with { User = "alice", Text = "hello" };
            var bytes = SecureFrameCodec.Encode(frame, key, useMac: true);
            Check.True(bytes.AsSpan(0, 4).SequenceEqual(SecureFrameCodec.Magic), "magic FCCH");
            Check.Equal((byte)1, bytes[4], "version");
            Check.True(SecureFrameCodec.TryParseHeader(bytes, out var len, out var err), "header: " + err);
            Check.Equal(bytes.Length - SecureFrameCodec.HeaderSize, len, "body length");
            Check.True(SecureFrameCodec.TryDecode(bytes.AsSpan(SecureFrameCodec.HeaderSize), key, true, out var decoded, out err), "decode: " + err);
            Check.Equal("hello", decoded.Text ?? "", "text roundtrip");
            Check.False(string.IsNullOrEmpty(decoded.Mac), "MAC present");

            var wrong = SecureFrameCodec.DeriveKey("other-secret");
            Check.False(SecureFrameCodec.TryDecode(bytes.AsSpan(SecureFrameCodec.HeaderSize), wrong, true, out _, out _), "wrong key rejected");
            // Mutate the body while keeping the original MAC -> must fail.
            var originalBody = Encoding.UTF8.GetString(bytes.AsSpan(SecureFrameCodec.HeaderSize).ToArray());
            var forgedBody = Encoding.UTF8.GetBytes(originalBody.Replace("\"text\":\"hello\"", "\"text\":\"HELLO\""));
            Check.False(SecureFrameCodec.TryDecode(forgedBody, key, true, out _, out _), "tampered body rejected");

            var unsigned = SecureFrameCodec.Encode(frame, key, false);
            Check.False(SecureFrameCodec.TryDecode(unsigned.AsSpan(SecureFrameCodec.HeaderSize), key, true, out _, out _), "missing MAC rejected in PSK mode");
            Check.True(SecureFrameCodec.TryDecode(unsigned.AsSpan(SecureFrameCodec.HeaderSize), key, false, out _, out _), "unsigned frame allowed in compat mode");
            var uniBytes = SecureFrameCodec.Encode(Frame.Make(FrameTypes.Chat, "S", 9) with { Text = "你好 世界" }, key, false);
            Check.True(SecureFrameCodec.TryDecode(uniBytes.AsSpan(SecureFrameCodec.HeaderSize), key, false, out var uni, out _), "unicode frame decoded");
            Check.Equal("你好 世界", uni.Text ?? "", "unicode roundtrip");

            var guard = new ReplayGuard();
            Check.True(guard.Accept("peer", "SID", 1, "n1"), "seq 1 accepted");
            Check.False(guard.Accept("peer", "SID", 1, "n1"), "duplicate seq rejected");
            Check.False(guard.Accept("peer", "SID", 0, "n0"), "older seq rejected");
            Check.True(guard.Accept("peer", "SID", 2, "n2"), "seq 2 accepted");
            Check.False(guard.Accept("peer", "SID", 3, "n2"), "reused nonce rejected");
            return Task.CompletedTask;
        });

        TestRunner.Add("T04", "两个 FCCH 帧粘连 / 分片仍可完整解析", "D3", () =>
        {
            var key = SecureFrameCodec.DeriveKey(null);
            var a = SecureFrameCodec.Encode(Frame.Make(FrameTypes.Chat, "S", 1) with { User = "a", Text = "one" }, key, false);
            var b = SecureFrameCodec.Encode(Frame.Make(FrameTypes.Chat, "S", 2) with { User = "b", Text = "two" }, key, false);
            var stream = a.Concat(b).ToArray();
            var offset = 0;
            var texts = new List<string>();
            while (offset < stream.Length)
            {
                Check.True(SecureFrameCodec.TryParseHeader(stream.AsSpan(offset), out var len, out _), "header at " + offset);
                var body = stream.AsSpan(offset + SecureFrameCodec.HeaderSize, len);
                Check.True(SecureFrameCodec.TryDecode(body, key, false, out var f, out _), "decode at " + offset);
                texts.Add(f.Text ?? "");
                offset += SecureFrameCodec.HeaderSize + len;
            }
            Check.Equal(2, texts.Count, "two frames");
            Check.Equal("one", texts[0], "first frame");
            Check.Equal("two", texts[1], "second frame");
            Check.Equal(stream.Length, offset, "no leftover bytes");
            return Task.CompletedTask;
        });

        TestRunner.Add("T08", "更新检查：HTTPS / tag_name 形状 / 发布页 URL 校验", "D8", async () =>
        {
            static UpdateChecker Checker(string body, ProtocolOptions o) =>
                new(o, new HttpClient(new StubHandler(body)));

            var good = await Checker("{\"tag_name\":\"v7.1\",\"html_url\":\"https://github.com/li2012China/Filesa-COChat/releases/tag/v7.1\"}",
                TestOptions.Make(23001)).CheckAsync();
            Check.True(good.HasUpdate, "v7.1 > " + ProtocolOptions.AppVersion);
            Check.Equal("7.1", good.LatestVersion, "stripped v");
            Check.True(good.ReleaseUrl.StartsWith("https://github.com/li2012China/Filesa-COChat/", StringComparison.Ordinal), "trusted release URL");

            var evilTag = await Checker("{\"tag_name\":\"../../evil\",\"html_url\":\"https://github.com/li2012China/Filesa-COChat/x\"}",
                TestOptions.Make(23002)).CheckAsync();
            Check.False(evilTag.HasUpdate, "malformed tag ignored");

            var evilUrl = await Checker("{\"tag_name\":\"v9.9\",\"html_url\":\"https://evil.example/payload\"}",
                TestOptions.Make(23003)).CheckAsync();
            Check.True(evilUrl.ReleaseUrl == ProtocolOptions.ReleasesPageUrl, "untrusted URL replaced");

            var httpOnly = TestOptions.Make(23004);
            httpOnly.UpdateUrl = "http://api.github.com/repos/li2012China/Filesa-COChat/releases/latest";
            var badScheme = await Checker("{}", httpOnly).CheckAsync();
            Check.False(badScheme.HasUpdate, "non-HTTPS endpoint refused");
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) });
    }
}
