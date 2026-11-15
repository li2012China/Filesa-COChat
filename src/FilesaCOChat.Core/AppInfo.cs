namespace FilesaCOChat.Core;

/// <summary>
/// Product identity. This is the single place the version is bumped: the window
/// title, the About page, the update checker and the MSI generator
/// (installer/gen-wxs.py parses <see cref="Version"/> out of this file) all read
/// it from here, so they cannot drift apart.
/// </summary>
public static class AppInfo
{
    public const string Name = "Filesa COChat";

    /// <summary>Display / comparison version. Three-part form is generated for MSI.</summary>
    public const string Version = "7.0";

    public const string WindowTitle = Name + " " + Version + " · WinUI 3";

    public const string AboutVersionLine = "版本号: " + Version + " (WinUI 3 重写版)";
}
