using System.Text.Json;
using System.Text.Json.Serialization;

namespace FilesaCOChat.Core;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%\FilesaCOChat.
/// The 5.1 Tkinter client kept settings in memory only; the WinUI 3 build
/// persists them so the theme (and font / notification choices) survive restarts.
/// <see cref="AppSettings.PreSharedKey"/> is deliberately NOT written to disk:
/// it is a shared secret and the original client never persisted it either.
/// </summary>
public static class SettingsStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesaCOChat", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // "Theme": "Dark" instead of "Theme": 2 — readable and future-proof when
        // new enum members are inserted.
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Loads settings from disk; any problem (missing / corrupt / future
    /// schema) yields the defaults instead of throwing at startup.</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (loaded is null) return new AppSettings();
            // Clamp values that come from an untrusted file.
            if (loaded.FontSize is < 8 or > 48) loaded.FontSize = 14;
            if (string.IsNullOrWhiteSpace(loaded.FontFamily)) loaded.FontFamily = "Microsoft YaHei UI";
            loaded.PreSharedKey = null; // never loaded from disk
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Best-effort save; failures are swallowed (read-only dir, disk full)
    /// because losing preferences must never crash the chat.</summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Persist a copy without the PSK.
            var onDisk = new AppSettings
            {
                FontFamily = settings.FontFamily,
                FontSize = settings.FontSize,
                Bold = settings.Bold,
                NotificationsEnabled = settings.NotificationsEnabled,
                Theme = settings.Theme,
                PreSharedKey = null
            };
            File.WriteAllText(path, JsonSerializer.Serialize(onDisk, Options));
        }
        catch
        {
            // preferences are a convenience, not a requirement
        }
    }
}
