using FilesaCOChat.Core.Protocol;
using FilesaCOChat.Core.Security;

namespace FilesaCOChat.Tests;

public static class SecurityTests
{
    private static string FindCoreDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FilesaCOChat.Core");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new CheckException("cannot locate Core source directory");
    }

    public static void Register()
    {
        TestRunner.Add("T05", "远程命令执行已彻底移除（策略 + 源码扫描）", "D1", () =>
        {
            Check.False(CommandPolicy.RemoteExecutionEnabled, "feature flag is false");
            var e = CommandPolicy.EvaluateRemote("alice", "whoami && calc.exe", "alice");
            Check.False(e.Execute, "no execution authorised");
            Check.True(e.DisplayText.Contains("calc", StringComparison.OrdinalIgnoreCase), "only a transcript is kept");
            Check.True(e.Reason.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase), "reason logged");
            var local = CommandPolicy.Evaluate("alice", "dir", "alice", false);
            Check.False(local.Execute, "local entry is display-only too");

            var forbidden = new[] { "Process.Start", "ProcessStartInfo", "new Process(", "subprocess", "shell=True", "cmd.exe /c", "/bin/sh -c", "ShellExecute" };
            var core = FindCoreDir();
            var sources = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                .Select(f => (Path: f, Text: File.ReadAllText(f))).ToList();
            Check.True(sources.Count >= 8, "core sources scanned: " + sources.Count);
            foreach (var s in sources)
            {
                // Strip // and /// comments: documentation legitimately mentions the removed APIs.
                var code = string.Join("\n", s.Text.Split('\n')
                    .Select(line => { var i = line.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? line[..i] : line; }));
                foreach (var token in forbidden)
                    Check.False(code.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"forbidden shell/process API '{token}' in {Path.GetFileName(s.Path)}");
            }
            return Task.CompletedTask;
        });

        TestRunner.Add("T06", "文件名白名单 / 大小上限 / 重名递增 / SHA-256", "D5", () =>
        {
            string[] bad =
            {
                "../../evil.exe", @"..\..\evil.exe", @"C:\Windows\System32\evil.dll",
                "sub/../../x.txt", "report.txt:ADS", "CON.txt", "NUL", "..", "", "a|b.txt", "x?.txt"
            };
            foreach (var raw in bad)
                Check.False(FilePolicy.TrySanitizeRemoteFileName(raw, out _, out var why),
                    "rejected: " + raw + " (" + why + ")");
            Check.True(FilePolicy.TrySanitizeRemoteFileName("季度 报告.pdf", out var good, out _), "unicode name accepted");
            Check.Equal("季度 报告.pdf", good, "name preserved");
            Check.False(FilePolicy.ValidateSize(3L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024, out _), "oversize rejected");
            Check.True(FilePolicy.ValidateSize(1024, 2L * 1024 * 1024 * 1024, out _), "normal size accepted");

            var temp = Path.Combine(Path.GetTempPath(), "fc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var first = FilePolicy.ResolveCollision(temp, "a.txt");
                File.WriteAllText(first, "abc");
                var second = FilePolicy.ResolveCollision(temp, "a.txt");
                Check.Equal("a_1.txt", Path.GetFileName(second), "collision name_1.ext");
                File.WriteAllText(second, "x");
                var third = FilePolicy.ResolveCollision(temp, "a.txt");
                Check.Equal("a_2.txt", Path.GetFileName(third), "collision name_2.ext");
                Check.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                    FilePolicy.Sha256Hex(first), "sha256 hexdigest matches Python hashlib");
            }
            finally { Directory.Delete(temp, true); }
            return Task.CompletedTask;
        });

        TestRunner.Add("T07", "迁移：确定性选举 / 任期幂等 / 确认超时", "D6", async () =>
        {
            var a = new MigrationCandidate("bob", "192.168.1.9");
            var b = new MigrationCandidate("alice", "192.168.1.2");
            var c = new MigrationCandidate("carol", "192.168.1.5");
            var one = MigrationCoordinator.Elect(new[] { a, b, c });
            var two = MigrationCoordinator.Elect(new[] { c, a, b });
            Check.NotNull(one, "winner exists");
            Check.Equal(two!.Username, one!.Username, "election independent of input order");
            Check.Equal("192.168.1.2", one.Ip, "smallest IPv4 wins");

            var m = new MigrationCoordinator();
            Check.True(m.ShouldApply(5, "192.168.1.2"), "term 5 applied");
            Check.False(m.ShouldApply(5, "192.168.1.2"), "duplicate term ignored (no double server)");
            Check.False(m.ShouldApply(3, "192.168.1.9"), "stale term ignored");
            Check.True(m.ShouldApply(6, "192.168.1.5"), "newer term applied");

            var n = new MigrationCoordinator();
            var term = n.BeginTerm("10.0.0.2");
            n.RecordAck(term, "10.0.0.99");
            Check.False(await n.WaitForAckAsync(term, TimeSpan.FromMilliseconds(80)), "wrong-peer ack timed out");
            var term2 = n.BeginTerm("10.0.0.2");
            n.RecordAck(term2, "10.0.0.2");
            Check.True(await n.WaitForAckAsync(term2, TimeSpan.FromMilliseconds(200)), "correct ack completes");
        });

        TestRunner.Add("T07b", "保留前缀阻断与用户名合法性", "D3 / D4", () =>
        {
            Check.True(LegacyCodec.IsReservedChatText("COMMAND:bob:whoami"), "COMMAND-like text blocked");
            Check.True(LegacyCodec.IsReservedChatText("USERLIST:a,b"), "USERLIST-like text blocked");
            Check.False(LegacyCodec.IsReservedChatText("普通消息:带冒号"), "normal text allowed");
            Check.True(LegacyCodec.IsReservedUsername("JOIN"), "reserved username");
            Check.True(LegacyCodec.IsReservedUsername("a,b"), "comma in username");
            Check.True(LegacyCodec.IsReservedUsername("a:b"), "colon in username");
            Check.False(LegacyCodec.IsReservedUsername("alice"), "normal username");
            return Task.CompletedTask;
        });
    }
}
