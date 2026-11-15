using FilesaCOChat.Core.Net;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Core;

/// <summary>
/// UI-agnostic façade replacing FilesaCOChat (main.py:296-2020). Owns discovery,
/// chat session, file transfer and update checking. Every event is raised on a
/// worker thread; the WinUI layer marshals to DispatcherQueue (D9).
/// </summary>
public sealed class ChatApplication : IAsyncDisposable
{
    private readonly ProtocolOptions _options;
    private readonly DiscoveryService _discovery;
    private readonly FileTransferService _files;
    private readonly UpdateChecker _updates;
    private readonly CancellationTokenSource _lifetime = new();
    private ChatSession? _session;
    private string _username = "";
    private bool _responderStarted;
    private bool _fileServerStarted;
    private bool _closing;

    public AppSettings Settings { get; }
    public ProtocolOptions Options => _options;
    public ChatSession? Session => _session;
    public string Username => _username;
    public bool IsServer => _session?.IsServer ?? false;
    /// <summary>Test / embedding hook: skip migration when closing a room.</summary>
    public bool MigrateOnClose { get; set; } = true;

    public Func<FileRequestArgs, Task<bool>>? ConfirmFileRequest
    {
        get => _files.ConfirmFileRequest;
        set => _files.ConfirmFileRequest = value;
    }

    public event Action<ChatEntry>? MessageAdded;
    public event Action<UserListChangedArgs>? UserListChanged;
    public event Action<StatusChangedArgs>? StatusChanged;
    public event Action<ProgressArgs>? Progress;
    public event Action<string>? Log;
    public event Action<UpdateInfo>? UpdateChecked;
    public event Action<FileFinishedArgs>? FileFinished;

    public ChatApplication(ProtocolOptions? options = null, AppSettings? settings = null, HttpClient? http = null)
    {
        Settings = settings ?? new AppSettings();
        _options = options ?? new ProtocolOptions();
        if (Settings.PreSharedKey is not null) _options.PreSharedKey = Settings.PreSharedKey;
        _discovery = new DiscoveryService(_options);
        _files = new FileTransferService(_options);
        _updates = new UpdateChecker(_options, http);

        _discovery.Progress += a => Progress?.Invoke(a);
        _discovery.Log += s => Log?.Invoke(s);
        _files.MessageAdded += e => MessageAdded?.Invoke(e);
        _files.Log += s => Log?.Invoke(s);
        _files.FileFinished += a => FileFinished?.Invoke(a);
    }

    private void Emit(ChatEntry e) => MessageAdded?.Invoke(e);
    private void EmitSys(string s) => Emit(ChatEntry.Sys(s));

    private void WireSession(ChatSession session)
    {
        session.MessageAdded += e => MessageAdded?.Invoke(e);
        session.UserListChanged += a => UserListChanged?.Invoke(a);
        session.StatusChanged += a => StatusChanged?.Invoke(a);
        session.Log += s => Log?.Invoke(s);
        session.ProtocolWarning += s => Log?.Invoke("[协议] " + s);
        session.UsernameConflict += s => Emit(ChatEntry.Error(s));
        session.MigrationRequested = HandleMigrationAsync;
    }

    /// <summary>login() in main.py:1715 -> find_existing_chatroom -> join / create.</summary>
    public async Task<bool> LoginAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            Emit(ChatEntry.Error(UiText.Warning + ": " + UiText.MustEnterUsername));
            return false;
        }
        _username = username.Trim();
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Searching, UiText.Searching));
        EmitSys(UiText.WelcomePrefix + _username + UiText.Welcome);

        var serverIp = await _discovery.FindServerAsync(ct);
        if (serverIp is not null)
        {
            var framed = await _discovery.ProbeCapabilityAsync(serverIp, ct);
            if (SecureFrameCodec.IsSecure(_options.PreSharedKey) && !framed)
            {
                Emit(ChatEntry.Error("已启用预共享密钥，拒绝加入不支持 FCCH 的 5.1 明文房间"));
                return false;
            }
            return await JoinAsync(serverIp, framed, ct);
        }

        EmitSys(UiText.CreatingChatroom);
        if (!await CreateRoomAsync(ct))
        {
            Emit(ChatEntry.Error("无法创建聊天室"));
            return false;
        }
        return true;
    }

    public async Task<bool> CreateRoomAsync(CancellationToken ct = default)
    {
        await StopResponderAsync();
        var session = NewSession();
        if (!await session.StartServerAsync(_username, Linked(ct))) return false;
        await _discovery.StartResponderAsync();
        _responderStarted = true;
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Serving, session.ServerIp));
        if (!_fileServerStarted) { await _files.StartAsync(); _fileServerStarted = true; }
        MaybeStartAutoUpdate();
        return true;
    }

    /// <summary>Direct join for embeddings/tests: sets the username without LAN discovery.</summary>
    public async Task<bool> JoinDirectAsync(string username, string serverIp, bool framed, CancellationToken ct = default)
    {
        _username = username.Trim();
        return await JoinAsync(serverIp, framed, ct);
    }

    public async Task<bool> JoinAsync(string serverIp, bool framed, CancellationToken ct = default)
    {
        await StopResponderAsync();
        var session = NewSession();
        if (!await session.JoinAsync(_username, serverIp, framed, Linked(ct)))
        {
            Emit(ChatEntry.Error("加入聊天室失败: " + serverIp));
            return false;
        }
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Connected, serverIp));
        if (!_fileServerStarted) { await _files.StartAsync(); _fileServerStarted = true; }
        MaybeStartAutoUpdate();
        return true;
    }

    private ChatSession NewSession()
    {
        _session = new ChatSession(_options);
        WireSession(_session);
        return _session;
    }

    private CancellationToken Linked(CancellationToken ct) =>
        CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token).Token;

    private async Task StopResponderAsync()
    {
        if (!_responderStarted) return;
        await _discovery.StopResponderAsync();
        _responderStarted = false;
    }

    // ---- user actions ----------------------------------------------------
    public Task<bool> SendMessageAsync(string text, CancellationToken ct = default) =>
        _session?.SendChatAsync(text, ct) ?? Task.FromResult(false);

    public Task RequestUserListAsync(CancellationToken ct = default) =>
        _session?.RequestUserListAsync(ct) ?? Task.CompletedTask;

    public Task SendCommandAsync(string target, string command, CancellationToken ct = default) =>
        _session?.SendCommandAsync(target, command, ct) ?? Task.CompletedTask;

    public async Task<bool> SendFileToUserAsync(string targetUser, string path, CancellationToken ct = default)
    {
        if (string.Equals(targetUser, _username, StringComparison.Ordinal))
        { Emit(ChatEntry.Error(UiText.CannotSendToSelfFile)); return false; }
        var ips = _session?.UserIps;
        var ip = ips is not null && ips.TryGetValue(targetUser, out var found)
            ? found
            : (_session?.LocalIp ?? "127.0.0.1");
        return await _files.SendFileAsync(ip, path, ct);
    }

    public async Task<UpdateInfo> CheckUpdateAsync(CancellationToken ct = default)
    {
        var info = await _updates.CheckAsync(ct);
        UpdateChecked?.Invoke(info);
        if (info.HasUpdate && Settings.NotificationsEnabled) EmitSys(UiText.UpdateAvailable);
        return info;
    }

    private void MaybeStartAutoUpdate()
    {
        if (!_options.AutoCheckUpdate) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.AutoUpdateDelay, _lifetime.Token);
                await CheckUpdateAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log?.Invoke("自动检查更新失败: " + ex.Message); }
        });
    }

    // ---- migration (D6) --------------------------------------------------
    private async Task HandleMigrationAsync(MigrationArgs args)
    {
        if (_closing) return;
        if (args.Promote) EmitSys(UiText.Promoted);
        else
        {
            EmitSys("收到迁移消息，新服务器IP: " + args.NewServerIp);
            EmitSys("迁移到新的服务器 " + args.NewServerIp);
        }
        try { if (_session is not null) await _session.DisposeAsync(); } catch { }
        await StopResponderAsync();

        if (args.Promote)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (await CreateRoomAsync(_lifetime.Token)) { EmitSys("已成功创建新的聊天室"); return; }
                }
                catch (Exception ex) { Log?.Invoke("创建聊天室失败: " + ex.Message); }
                await Task.Delay(200, _lifetime.Token);
            }
            Emit(ChatEntry.Error("创建聊天室失败"));
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var framed = await _discovery.ProbeCapabilityAsync(args.NewServerIp, _lifetime.Token);
            if (await JoinAsync(args.NewServerIp, framed, _lifetime.Token)) { EmitSys(UiText.MigrationOk); return; }
            EmitSys(UiText.MigrationRetry);
            try { await Task.Delay(_options.ReconnectDelay, _lifetime.Token); } catch (OperationCanceledException) { return; }
        }
        Emit(ChatEntry.Error(UiText.MigrationFailed));
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_closing) return;
        _closing = true;
        if (MigrateOnClose && _session?.IsServer == true)
        {
            var ok = await _session.InitiateMigrationAsync(ct);
            EmitSys(ok ? UiText.ServerMigrated : UiText.MigrationFailed);
        }
        try { _lifetime.Cancel(); } catch { }
        if (_session is not null) await _session.DisposeAsync();
        await _discovery.DisposeAsync();
        await _files.DisposeAsync();
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Closed, ""));
    }

    public async ValueTask DisposeAsync() => await CloseAsync();
}
