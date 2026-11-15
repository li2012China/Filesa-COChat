namespace FilesaCOChat.Core;

/// <summary>
/// All "magic numbers" observed in main.pyc (FilesaCOChat.__init__, line 297) live here.
/// Defaults reproduce the shipped 5.1 behaviour exactly; tests inject different ports.
/// </summary>
public sealed class ProtocolOptions
{
    // --- unchanged wire-level constants (must NOT be altered) ---
    public const int DefaultChatPort = 11451;
    public const int DefaultDiscoveryPort = 11452;
    public const int DefaultFilePort = 11450;
    /// <summary>
    /// Product version, not a wire constant: it is only used for the update
    /// check (User-Agent + tag comparison), never serialised into a frame, so
    /// bumping it keeps 5.1 wire compatibility intact.
    /// </summary>
    public const string AppVersion = AppInfo.Version;
    public const string DefaultUpdateUrl =
        "https://api.github.com/repos/li2012China/Filesa-COChat/releases/latest";
    public const string ReleasesPageUrl =
        "https://github.com/li2012China/Filesa-COChat/releases";

    public int ChatPort { get; set; } = DefaultChatPort;
    public int DiscoveryPort { get; set; } = DefaultDiscoveryPort;
    public int FilePort { get; set; } = DefaultFilePort;

    /// <summary>Optional additive capability probe (new WinUI3 peers only). Legacy wire format is untouched.</summary>
    public int CapabilityPort { get; set; } = 11453;

    public string Version { get; set; } = AppVersion;
    public string ReceiveDirectory { get; set; } = "files";
    public string UpdateUrl { get; set; } = DefaultUpdateUrl;

    /// <summary>Broadcast target. Tests use 127.0.0.1 to stay on loopback.</summary>
    public string DiscoveryBroadcastAddress { get; set; } = "255.255.255.255";

    // --- original timing / sizing behaviour ---
    public TimeSpan DiscoveryWindow { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan DiscoverySocketTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ScanConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public int ScanParallelism { get; set; } = 50;
    public int MaxChatFrameBytes { get; set; } = 1024;   // original recv(1024)
    public int FileChunkSize { get; set; } = 4096;       // original 4096-byte chunks
    public TimeSpan FileConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan AutoUpdateDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>D3 fix: quiet window used to coalesce a fragmented legacy (unframed) message.</summary>
    public TimeSpan LegacyQuietWindow { get; set; } = TimeSpan.FromMilliseconds(25);
    /// <summary>D3 fix: new-version senders leave a gap between two legacy frames.</summary>
    public TimeSpan LegacySendGap { get; set; } = TimeSpan.FromMilliseconds(30);

    /// <summary>D4 fix: when set, every FCCH frame is HMAC-SHA256 authenticated and legacy peers are refused.</summary>
    public string? PreSharedKey { get; set; }

    /// <summary>D5 fix: hard upper bound for an incoming file (0 = use 2 GiB safety default).</summary>
    public long MaxFileBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>D6 fix: migration acknowledgement timeout, replaces the original fixed sleep(1).</summary>
    public TimeSpan MigrationAckTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>Disable the /24 scan in tests where the network is not reachable.</summary>
    public bool EnablePortScan { get; set; } = true;
    /// <summary>Test hook: force the local /24 base instead of NIC discovery.</summary>
    public string? LocalIpOverride { get; set; }

    public ProtocolOptions Clone() => (ProtocolOptions)MemberwiseClone();
}
