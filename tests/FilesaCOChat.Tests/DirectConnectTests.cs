using System.Collections.Concurrent;
using FilesaCOChat.Core;

namespace FilesaCOChat.Tests;

/// <summary>
/// Login tab "连接指定频道": address:port parsing (optional port), the directed
/// TCP join path, and the guarantee that LAN login still uses its configured port.
/// </summary>
public static class DirectConnectTests
{
    private static bool HasChat(ConcurrentBag<ChatEntry> bag, string text) =>
        bag.Any(e => e.Kind == ChatEntryKind.Chat && e.Text == text);

    private static ConcurrentBag<ChatEntry> Capture(ChatApplication app)
    {
        var bag = new ConcurrentBag<ChatEntry>();
        app.MessageAdded += bag.Add;
        return bag;
    }

    public static void Register()
    {
        TestRunner.Add("T32", "定向地址解析：端口缺省 / 显式端口 / IPv6 / 主机名", "UI-5", () =>
        {
            Check.True(DirectEndpoint.TryParse("192.168.1.8", out var a, out _), "host only");
            Check.Equal("192.168.1.8", a.Host, "host");
            Check.Equal(ProtocolOptions.DefaultChatPort, a.Port, "default = wire chat port");
            Check.Equal(DirectEndpoint.DefaultPort, a.Port, "default constant");

            Check.True(DirectEndpoint.TryParse("192.168.1.8:", out var b, out _), "trailing colon");
            Check.Equal(DirectEndpoint.DefaultPort, b.Port, "trailing colon -> default");

            Check.True(DirectEndpoint.TryParse("192.168.1.8:12000", out var c, out _), "explicit port");
            Check.Equal("192.168.1.8", c.Host, "explicit host");
            Check.Equal(12000, c.Port, "explicit port");
            Check.Equal("192.168.1.8:12000", c.ToString(), "display form");

            Check.True(DirectEndpoint.TryParse("[::1]:12001", out var d, out _), "bracketed IPv6");
            Check.Equal("::1", d.Host, "IPv6 host without brackets");
            Check.Equal(12001, d.Port, "IPv6 port");
            Check.Equal("[::1]:12001", d.ToString(), "IPv6 display form");

            Check.True(DirectEndpoint.TryParse("localhost", out var e, out _), "DNS name");
            Check.Equal(DirectEndpoint.DefaultPort, e.Port, "DNS name -> default port");

            Check.True(DirectEndpoint.TryParse("::1", out var f, out _), "bare IPv6");
            Check.Equal("::1", f.Host, "bare IPv6 is one host, not host:port");
            Check.Equal(DirectEndpoint.DefaultPort, f.Port, "bare IPv6 -> default port");
            return Task.CompletedTask;
        });

        TestRunner.Add("T33", "定向地址校验：空值 / 非法端口 / 坏括号被拒绝", "UI-5", () =>
        {
            foreach (var (text, label) in new[]
                     {
                         ("", "empty"), ("   ", "blank"), (":11451", "no host"),
                         ("1.2.3.4:abc", "non-numeric port"), ("1.2.3.4:0", "port 0"),
                         ("1.2.3.4:70000", "port > 65535"), ("1.2.3.4:-1", "negative port"),
                         ("[::1", "missing bracket"), ("[::1]x:5", "garbage after bracket"),
                         ("1.2.3.4:1:2", "two colons")
                     })
            {
                Check.False(DirectEndpoint.TryParse(text, out _, out var error), label + " rejected");
                Check.True(error.Length > 0, label + " has a reason");
            }
            return Task.CompletedTask;
        });

        TestRunner.Add("T34", "连接指定频道：按解析出的自定义端口直连并收发消息", "UI-5", async () =>
        {
            // Server listens on 23200; the client is configured for a DIFFERENT
            // port (23210) and must switch to 23200 because the address says so.
            // Both share capability port 23202 so the FCCH probe can succeed.
            var serverOpts = TestOptions.Make(23200, cap: 23202);
            var clientOpts = TestOptions.Make(23210, cap: 23202);
            var server = await TestOptions.StartServerAsync(serverOpts, "server");
            var client = new ChatApplication(clientOpts) { MigrateOnClose = false };
            var serverMessages = Capture(server);
            try
            {
                Check.True(DirectEndpoint.TryParse("127.0.0.1:23200", out var endpoint, out _),
                    "parse address:port");
                Check.True(await client.ConnectChannelAsync("bob", endpoint), "direct connect succeeded");
                Check.Equal(23200, client.Options.ChatPort, "parsed port applied to session");
                Check.Equal("127.0.0.1", client.Session!.ServerIp, "connected to requested host");
                Check.False(client.IsServer, "client role (no room created)");
                await TestOptions.WaitUntilAsync(() => server.Session!.Users.Count == 2, "2 users online");

                Check.True(await client.SendMessageAsync("direct hello"), "direct client sent");
                await TestOptions.WaitUntilAsync(() => HasChat(serverMessages, "direct hello"),
                    "server received the message");
            }
            finally
            {
                await client.CloseAsync();
                await server.CloseAsync();
            }
        });

        TestRunner.Add("T35", "端口缺省走默认端口，且 LAN 登录恢复构造时的端口", "UI-5", async () =>
        {
            var app = new ChatApplication(TestOptions.Make(23220)) { MigrateOnClose = false };
            try
            {
                Check.True(DirectEndpoint.TryParse("127.0.0.1", out var endpoint, out _), "parse host only");
                Check.Equal(ProtocolOptions.DefaultChatPort, endpoint.Port, "default port");
                // Nothing listens on 11451: the join is refused, but the port the
                // client dialled must be the default one, not its configured one.
                Check.False(await app.ConnectChannelAsync("alice", endpoint),
                    "connect refused with no server on the default port");
                Check.Equal(ProtocolOptions.DefaultChatPort, app.Options.ChatPort,
                    "default port applied by the directed connect");

                // Switching back to 搜索LAN must not inherit the directed port.
                await app.LoginAsync("alice");
                Check.Equal(23220, app.Options.ChatPort, "LAN login restores configured port");
            }
            finally
            {
                await app.CloseAsync();
            }
        });
    }
}
