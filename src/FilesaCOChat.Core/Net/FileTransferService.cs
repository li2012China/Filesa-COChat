using System.Net;
using System.Net.Sockets;
using FilesaCOChat.Core.Protocol;
using FilesaCOChat.Core.Security;

namespace FilesaCOChat.Core.Net;

/// <summary>
/// Port 11450 replacement for start_file_transfer_server / handle_file_receive /
/// receive_file / send_file / calculate_file_hash. Handshake, 4096-byte chunks,
/// SEND_HASH + SHA-256 hex digest and files/name_1.ext naming are byte-compatible
/// with 5.1. D5 adds filename sanitisation, a size cap and destructive handling
/// of a hash mismatch.
/// </summary>
public sealed class FileTransferService : IAsyncDisposable
{
    private readonly ProtocolOptions _options;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _started;

    /// <summary>UI supplies the accept/reject dialog. Default (no handler) = reject.</summary>
    public Func<FileRequestArgs, Task<bool>>? ConfirmFileRequest;

    public event Action<FileFinishedArgs>? FileFinished;
    public event Action<ChatEntry>? MessageAdded;
    public event Action<string>? Log;
    public event Action<FileRequestArgs>? RequestRejected;

    public FileTransferService(ProtocolOptions options) => _options = options;

    private void EmitSys(string text) => MessageAdded?.Invoke(ChatEntry.Sys(text));
    private void EmitError(string text) => MessageAdded?.Invoke(ChatEntry.Error(text));

    public Task StartAsync()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _options.FilePort);
            _listener.Start(5);
            _cts = new CancellationTokenSource();
            _started = true;
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Log?.Invoke("文件传输服务器已启动，端口: " + _options.FilePort);
        }
        catch (Exception ex)
        {
            // Multiple local instances (tests) may contend for the same port.
            Log?.Invoke("启动文件传输服务器失败: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var socket = await _listener!.AcceptSocketAsync(ct);
                _ = Task.Run(() => HandleReceiveAsync(socket, ct));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log?.Invoke("文件传输服务器错误: " + ex.Message); }
    }

    private static async Task<string> ReadHandshakeAsync(NetworkStream stream, Socket socket, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var acc = new List<byte>();
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            if (acc.Count > maxBytes * 4) throw new InvalidDataException("handshake too large");
            await Task.Delay(25, ct);
            if (socket.Available > 0) continue;
            break;
        }
        return LegacyCodec.S(acc.ToArray(), acc.Count);
    }

    private async Task HandleReceiveAsync(Socket socket, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeoutCts.Token;
        NetworkStream? stream = null;
        try
        {
            stream = new NetworkStream(socket, ownsSocket: true);
            var handshake = await ReadHandshakeAsync(stream, socket, 1024, token);
            var split = handshake.LastIndexOf('|');
            if (split <= 0 || !long.TryParse(handshake[(split + 1)..].Trim(), out var size))
                throw new InvalidDataException("bad file-info handshake");

            var rawName = handshake[..split];
            if (!FilePolicy.TrySanitizeRemoteFileName(rawName, out var fileName, out var reason))
                throw new InvalidDataException("filename rejected: " + reason);
            if (!FilePolicy.ValidateSize(size, _options.MaxFileBytes, out var sizeReason))
                throw new InvalidDataException("size rejected: " + sizeReason);

            var request = new FileRequestArgs(EndpointIp(socket), fileName, size);
            var accept = ConfirmFileRequest is not null && await ConfirmFileRequest(request);
            if (!accept)
            {
                RequestRejected?.Invoke(request);
                EmitSys("已拒绝来自 " + request.RemoteIp + " 的文件传输");
                await WriteAsync(stream, LegacyCodec.B(LegacyCodec.Reject), token);
                return;
            }
            await WriteAsync(stream, LegacyCodec.B(LegacyCodec.Accept), token);

            Directory.CreateDirectory(_options.ReceiveDirectory);
            var path = FilePolicy.ResolveCollision(_options.ReceiveDirectory, fileName);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[_options.FileChunkSize];
                long remaining = size;
                while (remaining > 0)
                {
                    var want = (int)Math.Min(buffer.Length, remaining);
                    var n = await stream.ReadAsync(buffer.AsMemory(0, want), token);
                    if (n == 0) throw new EndOfStreamException("file data ended early");
                    await output.WriteAsync(buffer.AsMemory(0, n), token);
                    remaining -= n;
                }
                await output.FlushAsync(token);
            }

            await WriteAsync(stream, LegacyCodec.B(LegacyCodec.SendHash), token);
            var hashReply = await ReadHandshakeAsync(stream, socket, 1024, token);
            var remoteHash = hashReply.Trim();
            var localHash = FilePolicy.Sha256Hex(path);
            var ok = string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                // D5 fix: 5.1 only warned; a corrupt file was left in files/.
                try { File.Delete(path); } catch { /* best effort */ }
                EmitError(UiText.FileHashMismatch);
                FileFinished?.Invoke(new FileFinishedArgs(false, fileName, "", "SHA-256 不一致，文件已删除"));
                return;
            }
            EmitSys(UiText.FileReceiveDone + fileName);
            EmitSys("文件保存路径: " + Path.GetFullPath(path));
            FileFinished?.Invoke(new FileFinishedArgs(true, fileName, Path.GetFullPath(path), "SHA-256 校验通过"));
            Log?.Invoke("文件接收完成: " + fileName);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EmitError("文件接收失败: " + ex.Message);
            Log?.Invoke("处理文件接收时出错: " + ex.Message);
            FileFinished?.Invoke(new FileFinishedArgs(false, "", "", ex.Message));
            if (stream is not null)
                try { await WriteAsync(stream, LegacyCodec.B(LegacyCodec.Reject), CancellationToken.None); } catch { }
        }
        finally { try { stream?.Dispose(); } catch { } try { socket.Close(); } catch { } }
    }

    private static string EndpointIp(Socket socket) =>
        socket.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "0.0.0.0";

    private static Task WriteAsync(NetworkStream stream, byte[] data, CancellationToken ct) =>
        stream.WriteAsync(data, ct).AsTask();

    public async Task<bool> SendFileAsync(string targetIp, string filePath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath)) { EmitError("文件不存在: " + filePath); return false; }
            var info = new FileInfo(filePath);
            if (!FilePolicy.ValidateSize(info.Length, _options.MaxFileBytes, out var why))
            { EmitError("文件超过大小上限: " + why); return false; }
            var fileName = Path.GetFileName(filePath);
            EmitSys(UiText.SendingFile + fileName + " 给 " + targetIp);

            using var client = new TcpClient();
            var connect = client.ConnectAsync(targetIp, _options.FilePort, ct).AsTask();
            if (await Task.WhenAny(connect, Task.Delay(_options.FileConnectTimeout, ct)) != connect)
            { EmitError("连接超时，请检查目标用户是否在线"); return false; }
            await connect;
            using var stream = client.GetStream();

            await WriteAsync(stream, LegacyCodec.FileInfo(fileName, info.Length), ct);
            var answer = await ReadHandshakeAsync(stream, client.Client, 1024, ct);
            if (answer.Contains(LegacyCodec.Reject, StringComparison.Ordinal))
            { EmitSys(targetIp + UiText.FileRejected); return false; }
            if (!answer.Contains(LegacyCodec.Accept, StringComparison.Ordinal))
            { EmitSys("未知响应: " + answer); return false; }

            EmitSys(targetIp + UiText.FileAccepted);
            await using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[_options.FileChunkSize];
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                    await WriteAsync(stream, buffer[..n], ct);
            }
            await stream.FlushAsync(ct);

            var hashRequest = await ReadHandshakeAsync(stream, client.Client, 1024, ct);
            if (!hashRequest.Contains(LegacyCodec.SendHash, StringComparison.Ordinal))
                throw new InvalidDataException("missing SEND_HASH");
            await WriteAsync(stream, LegacyCodec.B(FilePolicy.Sha256Hex(filePath)), ct);
            EmitSys("已发送文件哈希值用于验证");
            EmitSys(UiText.FileSendDone);
            FileFinished?.Invoke(new FileFinishedArgs(true, fileName, filePath, "已发送并回传哈希"));
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            EmitError("发送文件时出错: " + ex.Message);
            FileFinished?.Invoke(new FileFinishedArgs(false, Path.GetFileName(filePath), "", ex.Message));
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started) return;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        if (_acceptTask is not null) try { await _acceptTask; } catch { }
        _cts?.Dispose();
    }
}

