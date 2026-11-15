namespace FilesaCOChat.Core;

/// <summary>
/// CommandRequest / CommandReceived are UI-only categories: the local transcript
/// of a command request the user sent, and a COMMAND frame received from a peer.
/// Both are bubbles (not centred system lines) with their own terminal-style
/// templates. Appended last so the numeric values of the existing wire-derived
/// kinds never change; no wire format is affected.
/// </summary>
public enum ChatEntryKind { Chat, System, CommandResult, Error, CommandRequest, CommandReceived }

/// <summary>A single line in the chat area (add_message in main.py).</summary>
public sealed record ChatEntry(string Sender, string Text, ChatEntryKind Kind, DateTime Timestamp)
{
    /// <summary>Structured payload for a command-line message (display only, never executed).</summary>
    public string? Target { get; init; }
    public string? Command { get; init; }
    /// <summary>Peer that issued a received COMMAND frame, when the wire carries it.</summary>
    public string? Origin { get; init; }

    public static ChatEntry Chat(string sender, string text) =>
        new(sender, text, ChatEntryKind.Chat, DateTime.Now);
    public static ChatEntry Sys(string text) =>
        new(UiText.System, text, ChatEntryKind.System, DateTime.Now);
    public static ChatEntry Error(string text) =>
        new(UiText.System, text, ChatEntryKind.Error, DateTime.Now);

    /// <summary>
    /// Local transcript of a user-issued command request. Text keeps the 5.1
    /// audit wording; Target/Command let the UI render a dedicated bubble
    /// without parsing that string. Remote execution stays disabled (D1).
    /// </summary>
    public static ChatEntry CommandRequest(string sender, string target, string command) =>
        new(sender, "[命令] 发送给 " + target + ": " + command,
            ChatEntryKind.CommandRequest, DateTime.Now)
        { Target = target, Command = command };

    /// <summary>
    /// Transcript of an incoming COMMAND frame. Text keeps the 5.1 audit wording
    /// ("[收到命令] ...") so logs and existing assertions stay valid, while the
    /// dedicated kind lets the UI render a left-aligned terminal bubble instead of
    /// a centred system line. Sender is the peer that issued it (never the
    /// reserved "系统" name, which IsSystemLine treats as a centred line).
    /// Remote execution stays disabled (D1).
    /// </summary>
    public static ChatEntry CommandReceived(string? origin, string target, string command) =>
        new(string.IsNullOrWhiteSpace(origin) ? UiText.UnknownCommandSender : origin,
            UiText.ReceivedCommand + command,
            ChatEntryKind.CommandReceived, DateTime.Now)
        { Target = target, Command = command, Origin = origin };
}

public enum AppStatus { Idle, LoggingIn, Searching, Scanning, Connecting, Connected, Serving, Closing, Closed }

public sealed record StatusChangedArgs(AppStatus Status, string Detail);
public sealed record UserListChangedArgs(IReadOnlyList<string> Users);
public sealed record ProgressArgs(int Done, int Total, string Detail);
public sealed record FileRequestArgs(string RemoteIp, string FileName, long Size);
public sealed record FileFinishedArgs(bool Success, string FileName, string SavedPath, string Detail);
public sealed record UpdateInfo(bool HasUpdate, string CurrentVersion, string LatestVersion, string ReleaseUrl, string Detail);
public sealed record MigrationArgs(string NewServerIp, long Term, bool Promote);

/// <summary>Window theme requested by the user (resolved against the OS theme).</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>User-configurable, persisted settings (open_settings in main.py).</summary>
public sealed class AppSettings
{
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 14;
    public bool Bold { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string? PreSharedKey { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.System;
}
