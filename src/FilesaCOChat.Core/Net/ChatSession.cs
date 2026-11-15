using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using FilesaCOChat.Core.Protocol;
using FilesaCOChat.Core.Security;

namespace FilesaCOChat.Core.Net;

/// <summary>
/// Replacement for create_chatroom / join_chatroom / accept_connections /
/// receive_messages / send_message / initiate_migration / handle_migration /
/// broadcast_user_list in main.py. Two roles (server, client) and two wire modes
/// (legacy raw 5.1, FCCH framed) share one dispatcher.
///
/// Threading contract (D9): every event is raised on a worker thread; the UI layer
/// must marshal it. All shared state is guarded by _stateGate or ConcurrentDictionary.
/// </summary>
public sealed class ChatSession : IAsyncDisposable
{
    private sealed class PeerLink : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public required SocketTransport Transport { get; init; }
        public required WireMode Mode { get; set; }
        public bool IsServerSide { get; init; }
        public bool Registered;
        public bool Rejected;
        public string? Username;
        public string RemoteIp = "";
        public string Sid = "";
        public IMessageReader? Reader;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            WriteLock.Dispose(); Transport.Dispose();
        }
    }

    private readonly ProtocolOptions _options;
    private readonly MigrationCoordinator _migration = new();
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<string, PeerLink> _peers = new();
    private readonly byte[] _key;
    private readonly bool _requireMac;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<string> _userList = new();
    private readonly Dictionary<string, string> _userIps = new();
    private PeerLink? _serverLink;
    private long _seq;
    private bool _closing;
    private Task? _acceptTask;

    public ChatSession(ProtocolOptions options)
    {
        _options = options;
        _key = SecureFrameCodec.DeriveKey(options.PreSharedKey);
        _requireMac = SecureFrameCodec.IsSecure(options.PreSharedKey);
        LocalIp = DiscoveryService.GetLocalIPv4();
    }

    // ---- public state ----------------------------------------------------
    public string Username { get; private set; } = "";
    public string LocalIp { get; }
    public string ServerIp { get; private set; } = "";
    public string SessionId { get; private set; } = "";
    public bool IsServer { get; private set; }
    public WireMode ClientWireMode { get; private set; } = WireMode.Legacy;
    public long ActiveTerm => _migration.CurrentTerm;
    public int PeerCount => _peers.Count;
    public bool RequireMac => _requireMac;

    public IReadOnlyList<string> Users { get { lock (_stateGate) return _userList.ToArray(); } }
    public IReadOnlyDictionary<string, string> UserIps { get { lock (_stateGate) return new Dictionary<string, string>(_userIps); } }

    // ---- events (worker threads) ----------------------------------------
    public event Action<ChatEntry>? MessageAdded;
    public event Action<UserListChangedArgs>? UserListChanged;
    public event Action<StatusChangedArgs>? StatusChanged;
    public event Action<string>? Log;
    public event Action<string>? ProtocolWarning;
    public event Action<string>? CommandRefused;
    public event Action<string>? UsernameConflict;
    public Func<MigrationArgs, Task>? MigrationRequested;

    private long NextSeq() => Interlocked.Increment(ref _seq);
    private void Emit(ChatEntry e) => MessageAdded?.Invoke(e);
    private void EmitSys(string text) => Emit(ChatEntry.Sys(text));

    // ---- server ----------------------------------------------------------
    public Task<bool> StartServerAsync(string username, CancellationToken ct = default)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _options.ChatPort);
            _listener.Start(5); // original listen(5)
        }
        catch (Exception ex)
        {
            Log?.Invoke("创建聊天室失败: " + ex.Message);
            return Task.FromResult(false);
        }
        Username = username;
        IsServer = true;
        SessionId = Frame.NewSessionId();
        ServerIp = LocalIp;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_stateGate)
        {
            _userList.Clear(); _userIps.Clear();
            _userList.Add(username); _userIps[username] = LocalIp;
        }
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Serving, LocalIp));
        EmitSys(username + UiText.Joined);
        PublishUserList();
        Log?.Invoke($"聊天室已创建: {LocalIp}:{_options.ChatPort}, 会话 {SessionId}");
        return Task.FromResult(true);
    }

    /// <summary>D6: free the listening socket before the promoted peer binds it.</summary>
    public Task ReleaseListenerAsync()
    {
        try { _listener?.Stop(); } catch { }
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var socket = await _listener!.AcceptSocketAsync(ct);
                _ = Task.Run(() => ServeLinkAsync(socket, ct));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log?.Invoke("接受连接异常: " + ex.Message); }
    }

    private async Task ServeLinkAsync(Socket socket, CancellationToken ct)
    {
        var transport = new SocketTransport(socket, _options);
        var link = new PeerLink
        {
            Transport = transport, IsServerSide = true,
            RemoteIp = transport.RemoteIp, Mode = WireMode.Legacy
        };
        try
        {
            var first = new byte[4096];
            var n = await transport.ReadAsync(first, ct);
            if (n == 0) { link.Dispose(); return; }
            var head = first.AsSpan(0, n).ToArray();
            link.Mode = n >= 4 && head.AsSpan(0, 4).SequenceEqual(SecureFrameCodec.Magic)
                ? WireMode.Framed : WireMode.Legacy;
            transport.PushBack(head);

            if (_requireMac && link.Mode == WireMode.Legacy)
            {
                Log?.Invoke("PSK 模式拒绝旧版明文客户端: " + link.RemoteIp);
                link.Dispose();
                return;
            }

            var guard = new ReplayGuard();
            link.Reader = link.Mode == WireMode.Framed
                ? new FramedMessageReader(transport, _options, link.Id, guard)
                : new LegacyMessageReader(transport, _options);
            _peers[link.Id] = link;

            if (link.Mode == WireMode.Framed)
                await SendFrameAsync(link, new Frame { Type = FrameTypes.Hello, Sid = SessionId, Seq = NextSeq(), Nonce = Frame.NewNonce() });

            EmitSys(link.RemoteIp + " 正在连接...");
            await ReceiveLoopAsync(link, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke("接收消息异常: " + ex.Message); }
        finally
        {
            _peers.TryRemove(link.Id, out _);
            var name = link.Username;
            if (name is not null && !_closing)
            {
                lock (_stateGate) { _userList.Remove(name); _userIps.Remove(name); }
                EmitSys(name + UiText.Left);
                PublishUserList();
            }
            link.Dispose();
        }
    }

    // ---- client ----------------------------------------------------------
    public async Task<bool> JoinAsync(string username, string serverIp, bool framed, CancellationToken ct = default)
    {
        Username = username;
        IsServer = false;
        ServerIp = serverIp;
        ClientWireMode = framed ? WireMode.Framed : WireMode.Legacy;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var client = new TcpClient(); // ownership transfers to SocketTransport; do not dispose here
            await client.ConnectAsync(serverIp, _options.ChatPort, ct);
            var transport = new SocketTransport(client.Client, _options);
            var link = new PeerLink
            {
                Transport = transport, Mode = ClientWireMode, IsServerSide = false,
                RemoteIp = serverIp, Registered = true
            };
            _serverLink = link;
            _peers[link.Id] = link;

            var guard = new ReplayGuard();
            link.Reader = framed
                ? new FramedMessageReader(transport, _options, link.Id, guard)
                : new LegacyMessageReader(transport, _options);

            if (framed)
            {
                await SendFrameAsync(link, new Frame { Type = FrameTypes.Hello, Sid = "", Seq = NextSeq(), Nonce = Frame.NewNonce() });
                // Wait for server hello (session id) before JOIN.
                var first = await link.Reader.ReadAsync(ct);
                if (first?.Frame is { Type: FrameTypes.Hello } hello)
                    SessionId = hello.Sid ?? "";
                else
                    ProtocolWarning?.Invoke("服务器未返回 FCCH hello，降级失败");
                await SendFrameAsync(link, Frame.Make(FrameTypes.Join, SessionId, NextSeq()) with { User = username });
                await SendFrameAsync(link, Frame.Make(FrameTypes.UserListRequest, SessionId, NextSeq()));
            }
            else
            {
                await SendLegacyAsync(link, LegacyCodec.Join(username));
                await Task.Delay(_options.LegacySendGap, ct);
                await SendLegacyAsync(link, LegacyCodec.UserListRequestFrame());
            }

            StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Connected, serverIp));
            EmitSys(UiText.JoinedRoomPrefix + serverIp + UiText.JoinedRoomSuffix);
            _ = Task.Run(() => ReceiveLoopAsync(link, _cts!.Token));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke("加入聊天室失败: " + ex.Message);
            Emit(ChatEntry.Error("无法连接到 " + serverIp));
            return false;
        }
    }

    // ---- send ------------------------------------------------------------
    public async Task<bool> SendChatAsync(string text, CancellationToken ct = default)
    {
        if (LegacyCodec.IsReservedChatText(text))
        {
            Emit(ChatEntry.Error(UiText.ReservedPrefixBlocked));
            return false;
        }
        var wire = LegacyCodec.Chat(Username, text);
        if (wire.Length > _options.MaxChatFrameBytes)
        {
            Emit(ChatEntry.Error(UiText.MessageTooLong));
            return false;
        }
        Emit(ChatEntry.Chat(Username, text)); // original: sender displays locally first
        if (IsServer) await BroadcastChatAsync(Username, text, null, ct);
        else if (_serverLink is not null) await SendChatToAsync(_serverLink, Username, text, ct);
        return true;
    }

    /// <summary>D2 fix: the server forwards every chat frame to all other clients.</summary>
    private async Task BroadcastChatAsync(string username, string text, PeerLink? exclude, CancellationToken ct = default)
    {
        foreach (var link in _peers.Values)
        {
            if (ReferenceEquals(link, exclude) || !link.Registered) continue;
            await SendChatToAsync(link, username, text, ct);
        }
    }

    private Task SendChatToAsync(PeerLink link, string username, string text, CancellationToken ct) =>
        link.Mode == WireMode.Framed
            ? SendFrameAsync(link, Frame.Make(FrameTypes.Chat, SessionId, NextSeq()) with { User = username, Text = text }, ct)
            : SendLegacyAsync(link, LegacyCodec.Chat(username, text), ct);

    public async Task RequestUserListAsync(CancellationToken ct = default)
    {
        if (_serverLink is null) return;
        if (_serverLink.Mode == WireMode.Framed)
            await SendFrameAsync(_serverLink, Frame.Make(FrameTypes.UserListRequest, SessionId, NextSeq()), ct);
        else
            await SendLegacyAsync(_serverLink, LegacyCodec.UserListRequestFrame(), ct);
    }

    /// <summary>D1 fix: a COMMAND frame is still emitted (wire compatibility) but is never executed.</summary>
    public async Task SendCommandAsync(string target, string command, CancellationToken ct = default)
    {
        Emit(ChatEntry.Chat(Username, "[命令] 发送给 " + target + ": " + command));
        Emit(ChatEntry.Error(UiText.RemoteCommandDisabled));
        CommandRefused?.Invoke(CommandPolicy.Evaluate(target, command, Username, fromNetwork: false).Reason);
        if (_serverLink is null) return;
        if (_serverLink.Mode == WireMode.Framed)
            await SendFrameAsync(_serverLink, Frame.Make(FrameTypes.Command, SessionId, NextSeq()) with { Target = target, Command = command, User = Username }, ct);
        else
            await SendLegacyAsync(_serverLink, LegacyCodec.Command(target, command), ct);
    }

    private async Task SendLegacyAsync(PeerLink link, byte[] data, CancellationToken ct = default)
    {
        try
        {
            await link.WriteLock.WaitAsync(ct);
            try
            {
                await link.Transport.WriteAsync(data, ct);
                if (_options.LegacySendGap > TimeSpan.Zero) await Task.Delay(_options.LegacySendGap, ct);
            }
            finally { link.WriteLock.Release(); }
        }
        catch (ObjectDisposedException) { /* link closed during shutdown */ }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke("发送失败: " + ex.Message); }
    }

    private async Task SendFrameAsync(PeerLink link, Frame frame, CancellationToken ct = default)
    {
        frame.Sid ??= SessionId;
        if (frame.Seq == 0) frame.Seq = NextSeq();
        frame.Nonce ??= Frame.NewNonce();
        var data = SecureFrameCodec.Encode(frame, _key, _requireMac);
        try
        {
            await link.WriteLock.WaitAsync(ct);
            try { await link.Transport.WriteAsync(data, ct); }
            finally { link.WriteLock.Release(); }
        }
        catch (ObjectDisposedException) { /* link closed during shutdown */ }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke("发送帧失败: " + ex.Message); }
    }

    // ---- receive / dispatch ---------------------------------------------
    private async Task ReceiveLoopAsync(PeerLink link, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && link.Reader is not null)
            {
                var incoming = await link.Reader.ReadAsync(ct);
                if (incoming is null) break;
                if (incoming.Error is not null)
                {
                    ProtocolWarning?.Invoke("协议帧被拒绝: " + incoming.Error);
                    continue;
                }
                if (incoming.Mode == WireMode.Framed && incoming.Frame is not null)
                    await HandleFrameAsync(link, incoming.Frame, ct);
                else if (incoming.Text is not null)
                    await HandleLegacyAsync(link, incoming.Text, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke("接收消息异常: " + ex.Message); }
    }

    private ParseContext ContextFor(PeerLink link) => new()
    {
        IsServer = link.IsServerSide,
        Registered = link.Registered,
        BoundUsername = link.Username,
        FromServerLink = !link.IsServerSide && ReferenceEquals(link, _serverLink),
        MaxFrameBytes = _options.MaxChatFrameBytes
    };

    private async Task HandleLegacyAsync(PeerLink link, string raw, CancellationToken ct)
    {
        var parsed = LegacyParser.Parse(raw, ContextFor(link));
        if (!parsed.Accepted)
        {
            ProtocolWarning?.Invoke(parsed.RejectReason + " | " + raw);
            return;
        }
        switch (parsed.Kind)
        {
            case MessageKind.Join:
                await RegisterAsync(link, parsed.Username, raw, ct);
                break;
            case MessageKind.UserListRequest:
                await SendUserListAsync(link, ct);
                break;
            case MessageKind.UserList:
                ApplyUserList(parsed.Users);
                break;
            case MessageKind.UsernameExists:
                UsernameConflict?.Invoke(UiText.UsernameExists);
                Emit(ChatEntry.Error(UiText.UsernameExists));
                break;
            case MessageKind.Chat:
                Emit(ChatEntry.Chat(parsed.Username, parsed.Text));
                if (link.IsServerSide) await BroadcastChatAsync(parsed.Username, parsed.Text, link, ct); // D2
                break;
            case MessageKind.Command:
                HandleCommand(parsed.Target, parsed.Command, ct);
                if (link.IsServerSide) await ForwardCommandAsync(link, parsed.Target, parsed.Command, ct);
                break;
            case MessageKind.Migrate:
            case MessageKind.Promote:
                await HandleMigrationFrameAsync(link, parsed.Ip, parsed.Kind == MessageKind.Promote,
                    long.TryParse(parsed.Text, out var t) ? t : 0, ct);
                break;
        }
    }

    private async Task HandleFrameAsync(PeerLink link, Frame f, CancellationToken ct)
    {
        var fromServerLink = !link.IsServerSide && ReferenceEquals(link, _serverLink);
        switch (f.Type)
        {
            case FrameTypes.Hello:
                if (!link.IsServerSide && !string.IsNullOrEmpty(f.Sid)) SessionId = f.Sid!;
                break;

            case FrameTypes.Join:
                if (!link.IsServerSide || link.Registered) { ProtocolWarning?.Invoke("非法 JOIN 帧"); return; }
                await RegisterAsync(link, f.User ?? "", f.Type, ct);
                break;

            case FrameTypes.UserListRequest:
                if (!link.IsServerSide || !link.Registered) { ProtocolWarning?.Invoke("非法 USERLIST_REQUEST 帧"); return; }
                await SendUserListAsync(link, ct);
                break;

            case FrameTypes.UserList:
                if (link.IsServerSide || !fromServerLink) { ProtocolWarning?.Invoke("非法 USERLIST 帧"); return; }
                ApplyUserList(f.Users ?? Array.Empty<string>());
                break;

            case FrameTypes.UsernameExists:
                if (!fromServerLink) return;
                UsernameConflict?.Invoke(UiText.UsernameExists);
                Emit(ChatEntry.Error(UiText.UsernameExists));
                break;

            case FrameTypes.Chat:
                var user = f.User ?? "";
                var text = f.Text ?? "";
                if (LegacyCodec.IsReservedUsername(user)) { ProtocolWarning?.Invoke("保留用户名聊天帧被拒绝"); return; }
                if (link.IsServerSide)
                {
                    if (!link.Registered || user != link.Username)
                    { ProtocolWarning?.Invoke("聊天用户名与会话不匹配（伪造被拒绝）"); return; }
                }
                Emit(ChatEntry.Chat(user, text));
                if (link.IsServerSide) await BroadcastChatAsync(user, text, link, ct); // D2
                break;

            case FrameTypes.Command:
            case FrameTypes.CommandRefused:
                HandleCommand(f.Target ?? "", f.Command ?? "", ct);
                if (link.IsServerSide) await ForwardCommandAsync(link, f.Target ?? "", f.Command ?? "", ct);
                break;

            case FrameTypes.Migrate:
            case FrameTypes.Promote:
                if (link.IsServerSide || !fromServerLink) { ProtocolWarning?.Invoke("非法迁移帧"); return; }
                await HandleMigrationFrameAsync(link, f.Ip ?? "", f.Type == FrameTypes.Promote, f.Term ?? 0, ct);
                break;

            case FrameTypes.Ack:
                if (!link.IsServerSide) return;
                _migration.RecordAck(f.Term ?? 0, link.RemoteIp);
                break;

            case FrameTypes.System:
                EmitSys(f.Text ?? "");
                break;

            case FrameTypes.Bye:
                break;

            default:
                ProtocolWarning?.Invoke("未知帧类型: " + f.Type);
                break;
        }
    }

    /// <summary>
    /// Keeps the 5.1 behaviour of relaying COMMAND frames to other clients so the
    /// target sees them, while every recipient applies CommandPolicy (never executes).
    /// </summary>
    private async Task ForwardCommandAsync(PeerLink origin, string target, string command, CancellationToken ct)
    {
        foreach (var link in _peers.Values.Where(p => p.Registered && !ReferenceEquals(p, origin)))
        {
            if (link.Mode == WireMode.Framed)
                await SendFrameAsync(link, Frame.Make(FrameTypes.Command, SessionId, NextSeq()) with { Target = target, Command = command }, ct);
            else
                await SendLegacyAsync(link, LegacyCodec.Command(target, command), ct);
        }
    }

    private void HandleCommand(string target, string command, CancellationToken ct)
    {
        var evaluation = CommandPolicy.EvaluateRemote(target, command, Username);
        CommandRefused?.Invoke(evaluation.Reason);
        if (!string.Equals(target, Username, StringComparison.Ordinal)) return;
        Emit(ChatEntry.Chat(UiText.System, evaluation.DisplayText));
        Emit(ChatEntry.Error(UiText.RemoteCommandDisabled));
        // No process is started, ever. See Security/CommandPolicy.cs (D1).
    }

    private async Task HandleMigrationFrameAsync(PeerLink link, string newServerIp, bool promote, long term, CancellationToken ct)
    {
        if (!_migration.ShouldApply(term, newServerIp))
        {
            ProtocolWarning?.Invoke("重复/过期迁移帧被忽略，避免双服务器");
            return;
        }
        if (link.Mode == WireMode.Framed)
            await SendFrameAsync(link, Frame.Make(FrameTypes.Ack, SessionId, NextSeq()) with { Term = term, Ip = LocalIp }, ct);
        if (MigrationRequested is not null)
            await MigrationRequested(new MigrationArgs(promote ? LocalIp : newServerIp, term, promote));
    }

    private async Task RegisterAsync(PeerLink link, string username, string raw, CancellationToken ct)
    {
        if (LegacyCodec.IsReservedUsername(username))
        {
            ProtocolWarning?.Invoke("非法用户名被拒绝: " + username);
            return;
        }
        bool duplicate;
        lock (_stateGate) duplicate = _userList.Contains(username, StringComparer.Ordinal);
        if (duplicate)
        {
            Log?.Invoke("用户名冲突: " + username);
            link.Rejected = true;
            if (link.Mode == WireMode.Framed)
                await SendFrameAsync(link, Frame.Make(FrameTypes.UsernameExists, SessionId, NextSeq()), ct);
            else
                await SendLegacyAsync(link, LegacyCodec.UsernameExistsFrame(), ct);
            return;
        }
        link.Registered = true;
        link.Username = username;
        link.Sid = SessionId;
        lock (_stateGate)
        {
            _userList.Add(username);
            _userIps[username] = link.RemoteIp;
        }
        EmitSys(username + UiText.Joined);
        await BroadcastUserListAsync(ct);
    }

    private void ApplyUserList(string[] users)
    {
        lock (_stateGate)
        {
            _userList.Clear();
            _userList.AddRange(users.Where(u => !LegacyCodec.IsReservedUsername(u)));
        }
        PublishUserList();
    }

    private void PublishUserList() => UserListChanged?.Invoke(new UserListChangedArgs(Users));

    private async Task BroadcastUserListAsync(CancellationToken ct = default)
    {
        PublishUserList();
        foreach (var link in _peers.Values.Where(p => p.Registered))
        {
            if (link.Mode == WireMode.Framed)
                await SendFrameAsync(link, Frame.Make(FrameTypes.UserList, SessionId, NextSeq()) with { Users = Users.ToArray() }, ct);
            else
                await SendLegacyAsync(link, LegacyCodec.UserList(Users), ct);
        }
    }

    private Task SendUserListAsync(PeerLink link, CancellationToken ct) =>
        link.Mode == WireMode.Framed
            ? SendFrameAsync(link, Frame.Make(FrameTypes.UserList, SessionId, NextSeq()) with { Users = Users.ToArray() }, ct)
            : SendLegacyAsync(link, LegacyCodec.UserList(Users), ct);

    // ---- migration (D6) --------------------------------------------------
    public async Task<bool> InitiateMigrationAsync(CancellationToken ct = default)
    {
        if (!IsServer) return false;
        var candidates = _peers.Values
            .Where(p => p.Registered && p.Username is not null)
            .Select(p => new MigrationCandidate(p.Username!, p.RemoteIp))
            .ToList();
        if (candidates.Count == 0) { EmitSys(UiText.NoOneToMigrate); return false; }

        foreach (var candidate in candidates.OrderBy(c => c.Ip).ThenBy(c => c.Username, StringComparer.Ordinal))
        {
            var chosen = MigrationCoordinator.Elect(new[] { candidate })!;
            var target = _peers.Values.FirstOrDefault(p => p.Username == chosen.Username);
            if (target is null) continue;
            var term = _migration.BeginTerm(chosen.Ip);
            EmitSys(UiText.MigratingTo + chosen.Ip);

            if (target.Mode == WireMode.Framed)
                await SendFrameAsync(target, Frame.Make(FrameTypes.Promote, SessionId, NextSeq()) with { Ip = LocalIp, Term = term }, ct);
            else
                await SendLegacyAsync(target, LegacyCodec.Promote(LocalIp), ct);

            foreach (var other in _peers.Values.Where(p => !ReferenceEquals(p, target) && p.Registered))
            {
                if (other.Mode == WireMode.Framed)
                    await SendFrameAsync(other, Frame.Make(FrameTypes.Migrate, SessionId, NextSeq()) with { Ip = chosen.Ip, Term = term }, ct);
                else
                    await SendLegacyAsync(other, LegacyCodec.Migrate(chosen.Ip), ct);
            }

            // Let the frames flush, then give up the listening socket so the promoted
            // peer (often on the same LAN host in tests) can bind the same port.
            await Task.Delay(TimeSpan.FromMilliseconds(80), ct);
            await ReleaseListenerAsync();
            var acked = await _migration.WaitForAckAsync(term, _options.MigrationAckTimeout);
            if (acked || await ProbeNewServerAsync(chosen.Ip, ct))
            {
                EmitSys(UiText.MigrationOk);
                Log?.Invoke($"迁移成功，任期 {term}，新服务器 {chosen.Ip}");
                return true;
            }
            Log?.Invoke($"任期 {term} 未获确认，尝试下一位候选");
        }
        Emit(ChatEntry.Error(UiText.MigrationFailed));
        return false;
    }

    private async Task<bool> ProbeNewServerAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(ip, _options.ChatPort, ct).AsTask();
            return await Task.WhenAny(connect, Task.Delay(_options.MigrationAckTimeout, ct)) == connect
                   && connect.IsCompletedSuccessfully;
        }
        catch { return false; }
    }

    // ---- shutdown --------------------------------------------------------
    public async ValueTask DisposeAsync()
    {
        if (_closing) return;
        _closing = true;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        foreach (var link in _peers.Values)
        {
            try { link.Transport.Close(); } catch { }
            link.Dispose();
        }
        _peers.Clear();
        if (_acceptTask is not null) try { await _acceptTask; } catch { }
        _cts?.Dispose();
        StatusChanged?.Invoke(new StatusChangedArgs(AppStatus.Closed, ""));
    }
}
