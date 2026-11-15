using System.Net.Sockets;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Core.Net;

public enum WireMode { Legacy, Framed }

/// <summary>One inbound frame, already classified. Legacy frames stay raw text.</summary>
public sealed record IncomingFrame(WireMode Mode, string? Text, Frame? Frame, string? Error)
{
    public static IncomingFrame Legacy(string text) => new(WireMode.Legacy, text, null, null);
    public static IncomingFrame New(Frame f) => new(WireMode.Framed, null, f, null);
    public static IncomingFrame Failure(string error) => new(WireMode.Framed, null, null, error);
}

/// <summary>Thin socket wrapper with a push-back buffer so the first bytes can be sniffed.</summary>
public sealed class SocketTransport : IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private byte[] _pending = Array.Empty<byte>();
    public ProtocolOptions Options { get; }

    public SocketTransport(Socket socket, ProtocolOptions options)
    {
        _socket = socket;
        Options = options;
        socket.NoDelay = true;
        socket.ReceiveBufferSize = 64 * 1024;
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    public string RemoteIp =>
        _socket.RemoteEndPoint is System.Net.IPEndPoint ep ? ep.Address.ToString() : "0.0.0.0";

    public void PushBack(byte[] bytes) =>
        _pending = _pending.Length == 0 ? bytes : _pending.Concat(bytes).ToArray();

    public int Available => _pending.Length + (int)Math.Min(int.MaxValue, _socket.Available);

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_pending.Length > 0)
        {
            var n = Math.Min(buffer.Length, _pending.Length);
            _pending.AsSpan(0, n).CopyTo(buffer.Span);
            _pending = _pending[n..];
            return n;
        }
        return await _stream.ReadAsync(buffer, ct);
    }

    public async Task<bool> ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var n = await ReadAsync(buffer[done..], ct);
            if (n == 0) return false;
            done += n;
        }
        return true;
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    public void Close()
    {
        try { _stream.Close(); } catch { /* best effort */ }
        try { _socket.Close(); } catch { /* best effort */ }
    }

    public void Dispose() => Close();
}

public interface IMessageReader
{
    Task<IncomingFrame?> ReadAsync(CancellationToken ct);
}

/// <summary>D3 fix: typed, length-framed, MAC-verified and replay-checked reader.</summary>
public sealed class FramedMessageReader : IMessageReader
{
    private readonly SocketTransport _transport;
    private readonly byte[] _key;
    private readonly bool _requireMac;
    private readonly ReplayGuard _guard;
    private readonly string _guardKey;

    public FramedMessageReader(SocketTransport transport, ProtocolOptions options, string guardKey, ReplayGuard? guard = null)
    {
        _transport = transport;
        _key = SecureFrameCodec.DeriveKey(options.PreSharedKey);
        _requireMac = SecureFrameCodec.IsSecure(options.PreSharedKey);
        _guardKey = guardKey;
        _guard = guard ?? new ReplayGuard();
    }

    public async Task<IncomingFrame?> ReadAsync(CancellationToken ct)
    {
        var header = new byte[SecureFrameCodec.HeaderSize];
        if (!await _transport.ReadExactlyAsync(header, ct)) return null;
        if (!SecureFrameCodec.TryParseHeader(header, out var length, out var error))
            return IncomingFrame.Failure(error);
        var body = new byte[length];
        if (!await _transport.ReadExactlyAsync(body, ct)) return null;
        if (!SecureFrameCodec.TryDecode(body, _key, _requireMac, out var frame, out error))
            return IncomingFrame.Failure(error);
        if (!_guard.Accept(_guardKey, frame.Sid ?? "", frame.Seq, frame.Nonce))
            return IncomingFrame.Failure("replay / out-of-order sequence");
        return IncomingFrame.New(frame);
    }
}

/// <summary>
/// D3 mitigation for *legacy* peers only. The shipped protocol has no length field or
/// delimiter, so a quiet window coalesces TCP fragments of one send into one frame.
/// New-version peers never use this path (they negotiate FCCH framing).
/// </summary>
public sealed class LegacyMessageReader : IMessageReader
{
    private readonly SocketTransport _transport;
    private readonly ProtocolOptions _options;

    public LegacyMessageReader(SocketTransport transport, ProtocolOptions options)
    {
        _transport = transport; _options = options;
    }

    public async Task<IncomingFrame?> ReadAsync(CancellationToken ct)
    {
        var acc = new List<byte>(_options.MaxChatFrameBytes);
        var buffer = new byte[_options.MaxChatFrameBytes];
        while (true)
        {
            var n = await _transport.ReadAsync(buffer, ct);
            if (n == 0) return acc.Count > 0 ? IncomingFrame.Legacy(LegacyCodec.S(acc.ToArray(), acc.Count)) : null;
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            await Task.Delay(_options.LegacyQuietWindow, ct);
            if (_transport.Available > 0) continue;
            if (acc.Count > _options.MaxChatFrameBytes)
            {
                acc.Clear();
                return IncomingFrame.Failure("legacy frame exceeds 1024 bytes");
            }
            return IncomingFrame.Legacy(LegacyCodec.S(acc.ToArray(), acc.Count));
        }
    }
}
