namespace FilesaCOChat.Core;

public enum ChatEntryKind { Chat, System, CommandResult, Error }

/// <summary>A single line in the chat area (add_message in main.py).</summary>
public sealed record ChatEntry(string Sender, string Text, ChatEntryKind Kind, DateTime Timestamp)
{
    public static ChatEntry Chat(string sender, string text) =>
        new(sender, text, ChatEntryKind.Chat, DateTime.Now);
    public static ChatEntry Sys(string text) =>
        new(UiText.System, text, ChatEntryKind.System, DateTime.Now);
    public static ChatEntry Error(string text) =>
        new(UiText.System, text, ChatEntryKind.Error, DateTime.Now);
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
