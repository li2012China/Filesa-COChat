using System.Net;
using System.Text;

namespace FilesaCOChat.Core.Protocol;

/// <summary>
/// Byte-for-byte reproduction of the shipped 5.1 wire format, reconstructed from
/// main.pyc constants (see FilesaCOChat_协议参考.md). Nothing in this class may change
/// an emitted byte: legacy peers must keep interoperating.
/// </summary>
public static class LegacyCodec
{
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public const string DiscoveryPrefix = "COCHAT_DISCOVERY:";
    public const string ResponsePrefix = "COCHAT_RESPONSE:";
    public const string JoinPrefix = "JOIN:";
    public const string UserListPrefix = "USERLIST:";
    public const string UserListRequest = "USERLIST_REQUEST";
    public const string UsernameExists = "USERNAME_EXISTS";
    public const string MigratePrefix = "MIGRATE:";
    public const string PromotePrefix = "PROMOTE:";
    public const string CommandPrefix = "COMMAND:";
    public const string CapabilityProbe = "COCHAT_CAP:1";
    public const string CapabilityReply = "COCHAT_CAP:FCCH1";

    public const string Accept = "ACCEPT";
    public const string Reject = "REJECT";
    public const string SendHash = "SEND_HASH";

    public static readonly string[] ReservedPrefixes =
    {
        DiscoveryPrefix, ResponsePrefix, JoinPrefix, UserListPrefix, UserListRequest,
        UsernameExists, MigratePrefix, PromotePrefix, CommandPrefix, CapabilityProbe
    };

    public static byte[] B(string s) => Utf8.GetBytes(s);
    public static string S(byte[] bytes, int count) => Utf8.GetString(bytes, 0, count);

    public static byte[] DiscoveryRequest(int port) => B(DiscoveryPrefix + port);
    public static byte[] DiscoveryResponse(int port) => B(ResponsePrefix + port);
    public static bool IsDiscoveryRequest(string s) => s.StartsWith(DiscoveryPrefix, StringComparison.Ordinal);
    public static bool IsExpectedResponse(string s, int port) => s == ResponsePrefix + port;

    public static byte[] Join(string username) => B(JoinPrefix + username);
    public static byte[] UserListRequestFrame() => B(UserListRequest);
    public static byte[] UsernameExistsFrame() => B(UsernameExists);
    public static byte[] UserList(IEnumerable<string> users) => B(UserListPrefix + string.Join(",", users));
    public static byte[] Chat(string username, string text) => B(username + ":" + text);
    public static byte[] Migrate(string ip) => B(MigratePrefix + ip);
    public static byte[] Promote(string ip) => B(PromotePrefix + ip);
    public static byte[] Command(string target, string command) => B(CommandPrefix + target + ":" + command);
    public static byte[] FileInfo(string fileName, long size) => B(fileName + "|" + size);

    /// <summary>D3 fix: usernames may never collide with a control token.</summary>
    public static bool IsReservedUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64) return true;
        foreach (var p in ReservedPrefixes)
            if (username.StartsWith(p.TrimEnd(':'), StringComparison.OrdinalIgnoreCase)) return true;
        if (username == UserListRequest || username == UsernameExists) return true;
        foreach (var c in username)
            if (c is ':' or ',' or '|' or '\r' or '\n' or '\0' || char.IsControl(c)) return true;
        return false;
    }

    /// <summary>D3 fix: local chat text that looks like a control frame is blocked at the sender.</summary>
    public static bool IsReservedChatText(string text) =>
        !string.IsNullOrEmpty(text) && ReservedPrefixes.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

public enum MessageKind
{
    Unknown, Malformed, Join, UsernameExists, UserListRequest, UserList,
    Migrate, Promote, Command, Chat
}

/// <summary>Context of the *link* the frame arrived on. D3/D4 fix: parsing is role/state scoped.</summary>
public sealed class ParseContext
{
    /// <summary>True when the local node is the room server.</summary>
    public bool IsServer { get; init; }
    /// <summary>True when the remote peer has completed JOIN (server-side links only).</summary>
    public bool Registered { get; init; }
    /// <summary>Username bound to this TCP connection, assigned by the server.</summary>
    public string? BoundUsername { get; init; }
    /// <summary>Client-side only: this link is the current, trusted server connection.</summary>
    public bool FromServerLink { get; init; }
    public int MaxFrameBytes { get; init; } = 1024;
}

public sealed class ParsedMessage
{
    public MessageKind Kind { get; init; } = MessageKind.Unknown;
    public string Raw { get; init; } = "";
    public bool Accepted { get; init; } = true;
    public string RejectReason { get; init; } = "";
    public string Username { get; init; } = "";
    public string Text { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Target { get; init; } = "";
    public string Command { get; init; } = "";
    public string[] Users { get; init; } = Array.Empty<string>();
    /// <summary>D1 fix: always false. Remote COMMAND frames are never executed.</summary>
    public bool ExecutionAllowed => false;

    public static ParsedMessage Reject(string raw, string reason) =>
        new() { Kind = MessageKind.Malformed, Raw = raw, Accepted = false, RejectReason = reason };
}

public static class LegacyParser
{
    /// <summary>
    /// Strict, ordered, role-scoped replacement of receive_messages' naive startswith chain.
    /// The original treated any "COMMAND:..."/"USERLIST:..." string as control regardless
    /// of sender; here a control token is only honoured in the direction/state where it
    /// can legitimately appear, and a chat frame is bound to the connection's username.
    /// </summary>
    public static ParsedMessage Parse(string raw, ParseContext ctx)
    {
        if (string.IsNullOrEmpty(raw)) return ParsedMessage.Reject(raw, "empty frame");
        if (LegacyCodec.Utf8.GetByteCount(raw) > ctx.MaxFrameBytes)
            return ParsedMessage.Reject(raw, "frame exceeds 1024-byte legacy limit");

        // --- exact tokens -------------------------------------------------
        if (raw == LegacyCodec.UsernameExists)
            return ctx is { IsServer: false, FromServerLink: true }
                ? new ParsedMessage { Kind = MessageKind.UsernameExists, Raw = raw }
                : ParsedMessage.Reject(raw, "USERNAME_EXISTS is only valid from the server link");

        if (raw == LegacyCodec.UserListRequest)
            return ctx is { IsServer: true, Registered: true }
                ? new ParsedMessage { Kind = MessageKind.UserListRequest, Raw = raw }
                : ParsedMessage.Reject(raw, "USERLIST_REQUEST is only valid from a registered client link");

        // --- JOIN ----------------------------------------------------------
        if (raw.StartsWith(LegacyCodec.JoinPrefix, StringComparison.Ordinal))
        {
            if (!ctx.IsServer || ctx.Registered)
                return ParsedMessage.Reject(raw, "JOIN is only valid once, on a fresh server-side link");
            var name = raw[LegacyCodec.JoinPrefix.Length..];
            if (LegacyCodec.IsReservedUsername(name))
                return ParsedMessage.Reject(raw, "invalid or reserved username");
            return new ParsedMessage { Kind = MessageKind.Join, Raw = raw, Username = name };
        }

        // --- USERLIST ------------------------------------------------------
        if (raw.StartsWith(LegacyCodec.UserListPrefix, StringComparison.Ordinal))
        {
            if (ctx.IsServer || !ctx.FromServerLink)
                return ParsedMessage.Reject(raw, "USERLIST is only accepted from the current server");
            var body = raw[LegacyCodec.UserListPrefix.Length..];
            var users = body.Length == 0
                ? Array.Empty<string>()
                : body.Split(',').Where(u => !LegacyCodec.IsReservedUsername(u)).ToArray();
            return new ParsedMessage { Kind = MessageKind.UserList, Raw = raw, Users = users };
        }

        // --- MIGRATE / PROMOTE ---------------------------------------------
        foreach (var (prefix, kind) in new[]
                 {
                     (LegacyCodec.MigratePrefix, MessageKind.Migrate),
                     (LegacyCodec.PromotePrefix, MessageKind.Promote)
                 })
        {
            if (!raw.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (ctx.IsServer || !ctx.FromServerLink)
                return ParsedMessage.Reject(raw, "migration frames are only accepted from the current server");
            var ip = raw[prefix.Length..];
            // Legacy PROMOTE:/MIGRATE: carry only an IP; an optional ":term" suffix is a
            // new-version extension and is ignored by 5.1 peers (additive, not a format break).
            var term = 0L;
            var colon = ip.LastIndexOf(':');
            if (colon > 0 && long.TryParse(ip[(colon + 1)..], out var parsedTerm) &&
                IPAddress.TryParse(ip[..colon], out _))
            {
                term = parsedTerm;
                ip = ip[..colon];
            }
            if (!IPAddress.TryParse(ip, out _))
                return ParsedMessage.Reject(raw, "invalid server IP in migration frame");
            return new ParsedMessage { Kind = kind, Raw = raw, Ip = ip, Text = term.ToString() };
        }

        // --- COMMAND (parsed, never executed: D1) --------------------------
        if (raw.StartsWith(LegacyCodec.CommandPrefix, StringComparison.Ordinal))
        {
            var parts = raw.Split(':', 3);
            if (parts.Length != 3 || parts[1].Length == 0)
                return ParsedMessage.Reject(raw, "malformed COMMAND frame");
            return new ParsedMessage
            {
                Kind = MessageKind.Command, Raw = raw,
                Target = parts[1], Command = parts[2],
                RejectReason = UiText.RemoteCommandDisabled
            };
        }

        // --- plain chat ----------------------------------------------------
        var idx = raw.IndexOf(':');
        if (idx <= 0) return ParsedMessage.Reject(raw, "not a chat frame");
        var user = raw[..idx];
        var text = raw[(idx + 1)..];
        if (LegacyCodec.IsReservedUsername(user))
            return ParsedMessage.Reject(raw, "reserved username in chat frame");
        if (ctx.IsServer)
        {
            if (!ctx.Registered) return ParsedMessage.Reject(raw, "chat before JOIN");
            if (ctx.BoundUsername is not null && !string.Equals(user, ctx.BoundUsername, StringComparison.Ordinal))
                return ParsedMessage.Reject(raw, "chat username does not match the bound session (spoofing)");
        }
        return new ParsedMessage { Kind = MessageKind.Chat, Raw = raw, Username = user, Text = text };
    }
}
