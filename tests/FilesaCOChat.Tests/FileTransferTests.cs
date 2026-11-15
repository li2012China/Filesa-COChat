using System.Net.Sockets;
using System.Text;
using FilesaCOChat.Core;
using FilesaCOChat.Core.Net;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Tests;

public static class FileTransferTests
{
    private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs = 2500)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[1024];
        var n = await stream.ReadAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, n);
    }

    private static async Task<TcpClient> ConnectFilePortAsync(ProtocolOptions o)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", o.FilePort);
        return client;
    }

    public static void Register()
    {
        TestRunner.Add("T13", "文件传输：4096 分块 / SHA-256 校验 / 路径穿越与超限拒绝", "D5 / 兼容", async () =>
        {
            var o = TestOptions.Make(21541);
            var temp = Path.Combine(Path.GetTempPath(), "fc-file-" + Guid.NewGuid().ToString("N"));
            var sendDir = Path.Combine(temp, "send");
            var recvDir = Path.Combine(temp, "files");
            Directory.CreateDirectory(sendDir);
            Directory.CreateDirectory(recvDir);
            var recvOptions = o.Clone();
            recvOptions.ReceiveDirectory = recvDir;
            var receiver = new FileTransferService(recvOptions) { ConfirmFileRequest = _ => Task.FromResult(true) };
            var results = new List<FileFinishedArgs>();
            var rejected = new List<FileRequestArgs>();
            receiver.FileFinished += a => { lock (results) results.Add(a); };
            receiver.RequestRejected += a => { lock (rejected) rejected.Add(a); };
            await receiver.StartAsync();
            try
            {
                var payload = Encoding.UTF8.GetBytes("Filesa COChat 5.1 文件传输校验\n第二行:带冒号");
                var source = Path.Combine(sendDir, "source.bin");
                await File.WriteAllBytesAsync(source, payload);

                var sender = new FileTransferService(o.Clone());
                var ok = await sender.SendFileAsync("127.0.0.1", source);
                Check.True(ok, "send completed");
                await TestOptions.WaitUntilAsync(() => { lock (results) return results.Count >= 1; }, "receive finished");
                FileFinishedArgs result;
                lock (results) result = results[0];
                Check.True(result.Success, "SHA-256 verified: " + result.Detail);
                Check.True(File.Exists(result.SavedPath), "file saved in files/");
                Check.Bytes(payload, await File.ReadAllBytesAsync(result.SavedPath), "byte-identical content");
                Check.Equal(FilePolicySha(source), FilePolicySha(result.SavedPath), "local hash matches source");

                // collision rule: send the same file again
                await sender.SendFileAsync("127.0.0.1", source);
                await TestOptions.WaitUntilAsync(() => { lock (results) return results.Count >= 2; }, "second receive finished");
                var saved2 = results.Last(r => r.Success && r.FileName == "source.bin").SavedPath;
                Check.Equal("source_1.bin", Path.GetFileName(saved2), "name_1.ext collision rule");

                // path traversal must be rejected, nothing written outside files/
                var escape = "fc_escape_" + Guid.NewGuid().ToString("N") + ".txt";
                using (var c = await ConnectFilePortAsync(recvOptions))
                {
                    var s = c.GetStream();
                    await s.WriteAsync(LegacyCodec.FileInfo("../" + escape, 5));
                    var answer = await ReadLineAsync(s);
                    Check.True(answer.Contains("REJECT", StringComparison.Ordinal), "traversal rejected");
                }
                Check.False(File.Exists(Path.Combine(temp, escape)), "no file written outside receive dir");

                // size cap
                using (var c = await ConnectFilePortAsync(recvOptions))
                {
                    var s = c.GetStream();
                    await s.WriteAsync(LegacyCodec.FileInfo("huge.bin", long.MaxValue / 2));
                    var answer = await ReadLineAsync(s);
                    Check.True(answer.Contains("REJECT", StringComparison.Ordinal), "oversize rejected");
                }
                Check.Equal(0, rejected.Count, "validation rejects never reach the user confirmation dialog");
                await sender.DisposeAsync();
            }
            finally
            {
                await receiver.DisposeAsync();
                Directory.Delete(temp, true);
            }
        });

        TestRunner.Add("T14", "哈希不一致时删除损坏文件并给出 ✗ 提示", "D5", async () =>
        {
            var o = TestOptions.Make(21551);
            var temp = Path.Combine(Path.GetTempPath(), "fc-hash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var ro = o.Clone();
            ro.ReceiveDirectory = temp;
            var receiver = new FileTransferService(ro) { ConfirmFileRequest = _ => Task.FromResult(true) };
            var results = new List<FileFinishedArgs>();
            var messages = new List<ChatEntry>();
            receiver.FileFinished += a => { lock (results) results.Add(a); };
            receiver.MessageAdded += e => { lock (messages) messages.Add(e); };
            await receiver.StartAsync();
            try
            {
                var data = Encoding.UTF8.GetBytes("corrupt-me");
                using var c = await ConnectFilePortAsync(ro);
                var s = c.GetStream();
                await s.WriteAsync(LegacyCodec.FileInfo("bad.bin", data.Length));
                var accepted = await ReadLineAsync(s);
                Check.True(accepted.Contains("ACCEPT", StringComparison.Ordinal), "ACCEPT received");
                await s.WriteAsync(data);
                var hashRequest = await ReadLineAsync(s);
                Check.True(hashRequest.Contains("SEND_HASH", StringComparison.Ordinal), "SEND_HASH requested");
                await s.WriteAsync(Encoding.UTF8.GetBytes(new string('0', 64))); // deliberately wrong hash

                await TestOptions.WaitUntilAsync(() => { lock (results) return results.Count >= 1; }, "verify finished");
                FileFinishedArgs r;
                lock (results) r = results[0];
                Check.False(r.Success, "mismatch reported as failure");
                Check.False(File.Exists(Path.Combine(temp, "bad.bin")), "corrupt file deleted");
                Check.True(messages.Any(m => m.Text.Contains("✗", StringComparison.Ordinal)), "user-visible ✗ warning");
            }
            finally
            {
                await receiver.DisposeAsync();
                Directory.Delete(temp, true);
            }
        });
    }

    private static string FilePolicySha(string path) =>
        FilesaCOChat.Core.Security.FilePolicy.Sha256Hex(path);
}
