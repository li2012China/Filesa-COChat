using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FilesaCOChat.Core.Protocol;

/// <summary>
/// D3/D4 fix for new-version <-> new-version peers: typed, length-framed JSON,
/// optionally HMAC-authenticated. Format matches the frame proposed in
/// protocol_reference.py (!4sBHI = magic + version + reserved + body length).
/// Legacy peers never see these bytes: capability negotiation chooses the mode.
/// </summary>
public static class FrameTypes
{
    public const string Hello = "hello";
    public const string Join = "join";
    public const string UserList = "userlist";
    public const string UserListRequest = "userlist_request";
    public const string UsernameExists = "username_exists";
    public const string Chat = "chat";
    public const string Command = "command";
    public const string CommandRefused = "command_refused";
    public const string Migrate = "migrate";
    public const string Promote = "promote";
    public const string Ack = "ack";
    public const string Bye = "bye";
    public const string System = "system";
}

public sealed record Frame
{
    public string Type { get; set; } = "";
    public string? Sid { get; set; }
    public long Seq { get; set; }
    public string? Nonce { get; set; }
    public string? User { get; set; }
    public string? Text { get; set; }
    public string? Ip { get; set; }
    public string? Target { get; set; }
    public string? Command { get; set; }
    public string? FileName { get; set; }
    public long? Size { get; set; }
    public long? Term { get; set; }
    public string? Reason { get; set; }
    public string[]? Users { get; set; }
    public string? Mac { get; set; }

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(8));
    public static string NewSessionId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static Frame Make(string type, string? sid, long seq) =>
        new() { Type = type, Sid = sid, Seq = seq, Nonce = NewNonce() };
}

public static class SecureFrameCodec
{
    public static readonly byte[] Magic = "FCCH"u8.ToArray();
    public const byte Version = 1;
    public const int HeaderSize = 11; // !4s B H I
    public const int MaxBodyBytes = 64 * 1024;

    public static byte[] DeriveKey(string? preSharedKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("FCCH|" + (preSharedKey ?? "")));
    public static bool IsSecure(string? preSharedKey) => !string.IsNullOrEmpty(preSharedKey);

    public static byte[] Header(int bodyLength)
    {
        var h = new byte[HeaderSize];
        Magic.CopyTo(h, 0);
        h[4] = Version;
        BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(5, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(7, 4), (uint)bodyLength);
        return h;
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> h, out int bodyLength, out string error)
    {
        bodyLength = 0; error = "";
        if (h.Length < HeaderSize) { error = "short header"; return false; }
        if (!h[..4].SequenceEqual(Magic)) { error = "bad magic"; return false; }
        if (h[4] != Version) { error = "unsupported version"; return false; }
        var len = BinaryPrimitives.ReadUInt32BigEndian(h[7..11]);
        if (len > MaxBodyBytes) { error = "frame too large"; return false; }
        bodyLength = (int)len;
        return true;
    }

    private static byte[] CanonicalJson(Frame f)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", f.Type);
            if (f.Sid is not null) w.WriteString("sid", f.Sid);
            w.WriteNumber("seq", f.Seq);
            if (f.Nonce is not null) w.WriteString("nonce", f.Nonce);
            if (f.User is not null) w.WriteString("user", f.User);
            if (f.Text is not null) w.WriteString("text", f.Text);
            if (f.Ip is not null) w.WriteString("ip", f.Ip);
            if (f.Target is not null) w.WriteString("target", f.Target);
            if (f.Command is not null) w.WriteString("command", f.Command);
            if (f.FileName is not null) w.WriteString("filename", f.FileName);
            if (f.Size is not null) w.WriteNumber("size", f.Size.Value);
            if (f.Term is not null) w.WriteNumber("term", f.Term.Value);
            if (f.Reason is not null) w.WriteString("reason", f.Reason);
            if (f.Users is not null)
            {
                w.WriteStartArray("users");
                foreach (var u in f.Users) w.WriteStringValue(u);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static byte[] FullJson(Frame f, string? mac)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", f.Type);
            if (f.Sid is not null) w.WriteString("sid", f.Sid);
            w.WriteNumber("seq", f.Seq);
            if (f.Nonce is not null) w.WriteString("nonce", f.Nonce);
            if (f.User is not null) w.WriteString("user", f.User);
            if (f.Text is not null) w.WriteString("text", f.Text);
            if (f.Ip is not null) w.WriteString("ip", f.Ip);
            if (f.Target is not null) w.WriteString("target", f.Target);
            if (f.Command is not null) w.WriteString("command", f.Command);
            if (f.FileName is not null) w.WriteString("filename", f.FileName);
            if (f.Size is not null) w.WriteNumber("size", f.Size.Value);
            if (f.Term is not null) w.WriteNumber("term", f.Term.Value);
            if (f.Reason is not null) w.WriteString("reason", f.Reason);
            if (f.Users is not null)
            {
                w.WriteStartArray("users");
                foreach (var u in f.Users) w.WriteStringValue(u);
                w.WriteEndArray();
            }
            if (mac is not null) w.WriteString("mac", mac);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static string ComputeMac(byte[] key, byte[] canonical)
    {
        var input = new byte[Magic.Length + 1 + canonical.Length];
        Magic.CopyTo(input, 0);
        input[Magic.Length] = Version;
        canonical.CopyTo(input, Magic.Length + 1);
        return Convert.ToHexString(HMACSHA256.HashData(key, input)).ToLowerInvariant();
    }

    public static byte[] Encode(Frame frame, byte[] key, bool useMac)
    {
        var canonical = CanonicalJson(frame);
        var mac = useMac ? ComputeMac(key, canonical) : null;
        var body = FullJson(frame, mac);
        return Header(body.Length).Concat(body).ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> body, byte[] key, bool requireMac,
                                 out Frame frame, out string error)
    {
        frame = new Frame(); error = "";
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            string? Get(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            long? GetLong(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : null;

            frame = new Frame
            {
                Type = Get("type") ?? "",
                Sid = Get("sid"), Nonce = Get("nonce"), User = Get("user"), Text = Get("text"),
                Ip = Get("ip"), Target = Get("target"), Command = Get("command"),
                FileName = Get("filename"), Reason = Get("reason"), Mac = Get("mac"),
                Seq = GetLong("seq") ?? 0, Size = GetLong("size"), Term = GetLong("term")
            };
            if (root.TryGetProperty("users", out var arr) && arr.ValueKind == JsonValueKind.Array)
                frame.Users = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                                 .Select(e => e.GetString()!).ToArray();
            if (string.IsNullOrEmpty(frame.Type)) { error = "missing type"; return false; }

            if (requireMac)
            {
                if (string.IsNullOrEmpty(frame.Mac)) { error = "missing MAC"; return false; }
                var expected = ComputeMac(key, CanonicalJson(frame));
                var a = Convert.FromHexString(expected);
                var b = Convert.FromHexString(frame.Mac);
                if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
                { error = "bad MAC"; return false; }
            }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}

/// <summary>D4 fix: monotonic sequence + nonce replay protection per session/sender.</summary>
public sealed class ReplayGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _lastSeq = new();
    private readonly Queue<string> _nonces = new();
    private readonly HashSet<string> _nonceSet = new();
    private const int Capacity = 1024;

    public bool Accept(string sender, string sid, long seq, string? nonce)
    {
        lock (_gate)
        {
            var key = sid + "|" + sender;
            if (_lastSeq.TryGetValue(key, out var last) && seq <= last) return false;
            if (!string.IsNullOrEmpty(nonce))
            {
                if (_nonceSet.Contains(nonce)) return false;
                _nonceSet.Add(nonce); _nonces.Enqueue(nonce);
                while (_nonces.Count > Capacity) _nonceSet.Remove(_nonces.Dequeue());
            }
            _lastSeq[key] = seq;
            return true;
        }
    }

    public void Reset() { lock (_gate) { _lastSeq.Clear(); _nonces.Clear(); _nonceSet.Clear(); } }
}
