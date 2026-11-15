using System.Security.Cryptography;

namespace FilesaCOChat.Core.Security;

/// <summary>
/// DEFECT D5 (report §7, severity 中): receiver used os.path.join(dir, peer_filename)
/// with no basename cleaning, no size cap and no action on hash mismatch.
/// </summary>
public static class FilePolicy
{
    public const string FixId = "D5";

    private static readonly char[] InvalidChars = { '<', '>', '"', '/', '\\', '|', '?', '*', ':' };
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    /// <summary>Returns false for traversal / ADS / device names instead of writing outside files/.</summary>
    public static bool TrySanitizeRemoteFileName(string raw, out string safeName, out string reason)
    {
        safeName = ""; reason = "";
        if (string.IsNullOrWhiteSpace(raw)) { reason = "empty filename"; return false; }
        if (raw.Length > 255) { reason = "filename too long"; return false; }

        var normalized = raw.Replace('\\', '/');
        if (normalized.Split('/').Any(seg => seg == "..")) { reason = "path traversal (..)"; return false; }
        if (raw.IndexOf(':') >= 0) { reason = "drive / alternate data stream"; return false; }

        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (name is "" or "." or "..") { reason = "invalid basename"; return false; }
        if (name.Any(c => c < 32 || InvalidChars.Contains(c))) { reason = "invalid character"; return false; }
        if (name.EndsWith('.') || name.EndsWith(' ')) { reason = "trailing dot/space"; return false; }

        var stem = Path.GetFileNameWithoutExtension(name);
        if (DeviceNames.Contains(stem)) { reason = "reserved device name"; return false; }
        safeName = name;
        return true;
    }

    public static bool ValidateSize(long size, long maxBytes, out string reason)
    {
        reason = "";
        if (size < 0) { reason = "negative size"; return false; }
        if (size > maxBytes) { reason = $"size {size} exceeds limit {maxBytes}"; return false; }
        return true;
    }

    /// <summary>Original name_1.ext / name_2.ext collision rule (receive_file, line 1835).</summary>
    public static string ResolveCollision(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(directory, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
        >= 1024 => $"{bytes / 1024.0:F2} KB",
        _ => $"{bytes} B"
    };

    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
