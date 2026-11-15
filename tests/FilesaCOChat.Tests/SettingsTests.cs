using FilesaCOChat.Core;

namespace FilesaCOChat.Tests;

/// <summary>
/// Regression tests for the persisted preferences store introduced with the
/// sidebar settings + theme switcher. These cover the pure Core logic only;
/// the ComboBox / dialog behaviour is verified by the visual self-check.
/// </summary>
public static class SettingsTests
{
    private static string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "fc-tests-settings-" +
            Guid.NewGuid().ToString("N")[..8] + ".json");
        if (File.Exists(path)) File.Delete(path);
        return path;
    }

    public static void Register()
    {
        TestRunner.Add("T28", "设置默认值与首次加载", "UI-3", () =>
        {
            var path = TempPath();
            Check.False(File.Exists(path), "no file on first run");
            var s = SettingsStore.Load(path);
            Check.Eq(ThemePreference.System, s.Theme, "default theme follows the system");
            Check.Equal(14, (int)s.FontSize, "default font size");
            Check.True(s.NotificationsEnabled, "notifications on by default");
            Check.True(string.IsNullOrEmpty(s.PreSharedKey), "no PSK by default");
            return Task.CompletedTask;
        });

        TestRunner.Add("T29", "主题与界面偏好保存后可恢复", "UI-3", () =>
        {
            var path = TempPath();
            var original = new AppSettings
            {
                Theme = ThemePreference.Dark,
                FontFamily = "Consolas",
                FontSize = 18,
                Bold = true,
                NotificationsEnabled = false
            };
            SettingsStore.Save(original, path);
            var loaded = SettingsStore.Load(path);
            Check.Eq(ThemePreference.Dark, loaded.Theme, "dark theme survives restart");
            Check.Equal("Consolas", loaded.FontFamily, "font family survives restart");
            Check.Equal(18, (int)loaded.FontSize, "font size survives restart");
            Check.True(loaded.Bold, "bold survives restart");
            Check.False(loaded.NotificationsEnabled, "notification choice survives restart");
            return Task.CompletedTask;
        });

        TestRunner.Add("T30", "预共享密钥永不落盘", "UI-3", () =>
        {
            var path = TempPath();
            var withPsk = new AppSettings { PreSharedKey = "secret-key", Theme = ThemePreference.Light };
            SettingsStore.Save(withPsk, path);
            var raw = File.ReadAllText(path);
            Check.False(raw.Contains("secret-key"), "PSK must not appear in the settings file");
            var loaded = SettingsStore.Load(path);
            Check.True(string.IsNullOrEmpty(loaded.PreSharedKey), "PSK is not restored from disk");
            Check.Eq(ThemePreference.Light, loaded.Theme, "other settings still load");
            return Task.CompletedTask;
        });

        TestRunner.Add("T31", "损坏或越界的设置文件回退到默认值", "UI-3", () =>
        {
            var path = TempPath();
            File.WriteAllText(path, "{ this is not json");
            var s = SettingsStore.Load(path);
            Check.Eq(ThemePreference.System, s.Theme, "corrupt file yields default theme");
            Check.Equal(14, (int)s.FontSize, "corrupt file yields default font size");

            File.WriteAllText(path,
                """{"FontSize":999,"FontFamily":"","Theme":"Dark"}""");
            s = SettingsStore.Load(path);
            Check.Equal(14, (int)s.FontSize, "out-of-range font size is clamped to default");
            Check.Equal("Microsoft YaHei UI", s.FontFamily, "empty font family falls back");
            Check.Eq(ThemePreference.Dark, s.Theme, "valid parts of the file are still honoured");
            return Task.CompletedTask;
        });
    }
}
