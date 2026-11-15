using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FilesaCOChat.Core.Protocol;

namespace FilesaCOChat.Core.Net;

/// <summary>
/// UDP 11452 discovery + /24 TCP fallback (broadcast_discovery / threaded_port_scan
/// / find_existing_chatroom in main.py). Wire strings are unchanged.
/// The extra UDP capability port is additive: it only tells new-version clients that
/// the server understands FCCH framing; 5.1 clients never use or see frames from it.
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private readonly ProtocolOptions _options;
    private UdpClient? _responder;
    private UdpClient? _capabilityResponder;
    private CancellationTokenSource? _cts;
    private Task? _responderTask;
    private Task? _capabilityTask;
    private int _scanDone;

    public event Action<ProgressArgs>? Progress;
    public event Action<string>? Log;

    public DiscoveryService(ProtocolOptions options) => _options = options;

    public static string GetLocalIPv4()
    {
        try
        {
            var candidates = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString()).ToList();
            var preferred = candidates.FirstOrDefault(IsPrivate) ?? candidates.FirstOrDefault();
            if (preferred is not null) return preferred;
        }
        catch { /* fall through */ }
        return "127.0.0.1";
    }

    private static bool IsPrivate(string ip)
    {
        var p = ip.Split('.');
        if (p.Length != 4 || !int.TryParse(p[0], out var a) || !int.TryParse(p[1], out var b)) return false;
        return a == 10 || (a == 192 && b == 168) || (a == 172 && b >= 16 && b <= 31) || a == 169 && b == 254;
    }

    /// <summary>Original: all hosts in the /24 of the local address (excluding .0/.255/self).</summary>
    public static IEnumerable<string> EnumerateSubnetIpv4(string localIp)
    {
        var parts = localIp.Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[3], out var self)) yield break;
        var prefix = string.Join(".", parts[..3]);
        for (var i = 1; i <= 254; i++)
            if (i != self) yield return $"{prefix}.{i}";
    }

    public async Task<string?> FindServerAsync(CancellationToken ct = default)
    {
        Log?.Invoke(UiText.BroadcastSearching);
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Send(LegacyCodec.DiscoveryRequest(_options.ChatPort),
                     LegacyCodec.DiscoveryRequest(_options.ChatPort).Length,
                     _options.DiscoveryBroadcastAddress, _options.DiscoveryPort);
            var receive = udp.ReceiveAsync(ct).AsTask();
            var window = Task.Delay(_options.DiscoveryWindow, ct);
            var first = await Task.WhenAny(receive, window);
            if (first == receive)
            {
                var result = await receive;
                var text = LegacyCodec.S(result.Buffer, result.Buffer.Length);
                if (LegacyCodec.IsExpectedResponse(text, _options.ChatPort))
                {
                    Log?.Invoke("通过广播发现聊天室: " + result.RemoteEndPoint.Address);
                    return result.RemoteEndPoint.Address.ToString();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log?.Invoke("广播发现失败: " + ex.Message); }

        if (!_options.EnablePortScan) return null;
        Log?.Invoke(UiText.Scanning);
        return await ScanSubnetAsync(ct);
    }

    private async Task<string?> ScanSubnetAsync(CancellationToken ct)
    {
        var ips = EnumerateSubnetIpv4(_options.LocalIpOverride ?? GetLocalIPv4()).ToList();
        if (ips.Count == 0) { Log?.Invoke("未找到可扫描的IP地址"); return null; }
        using var found = new CancellationTokenSource();
        string? result = null;
        var gate = new object();
        _scanDone = 0;
        try
        {
            await Parallel.ForEachAsync(ips,
                new ParallelOptions { MaxDegreeOfParallelism = _options.ScanParallelism, CancellationToken = ct },
                async (ip, token) =>
                {
                    if (found.IsCancellationRequested) return;
                    try
                    {
                        using var client = new TcpClient();
                        var connect = client.ConnectAsync(ip, _options.ChatPort, token).AsTask();
                        if (await Task.WhenAny(connect, Task.Delay(_options.ScanConnectTimeout, token)) == connect && connect.IsCompletedSuccessfully)
                        {
                            lock (gate) result ??= ip;
                            found.Cancel();
                        }
                    }
                    catch { /* host unreachable */ }
                    finally
                    {
                        var done = Interlocked.Increment(ref _scanDone);
                        if (done % 16 == 0 || done == ips.Count)
                            Progress?.Invoke(new ProgressArgs(done, ips.Count, "请稍后"));
                    }
                });
        }
        catch (OperationCanceledException) { /* expected when found */ }
        return result;
    }

    /// <summary>Additive capability probe; false => peer is a 5.1 (legacy) node.</summary>
    public async Task<bool> ProbeCapabilityAsync(string serverIp, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient();
            var probe = LegacyCodec.B(LegacyCodec.CapabilityProbe);
            udp.Send(probe, probe.Length, serverIp, _options.CapabilityPort);
            var receive = udp.ReceiveAsync(ct).AsTask();
            var timeout = Task.Delay(TimeSpan.FromMilliseconds(400), ct);
            if (await Task.WhenAny(receive, timeout) != receive) return false;
            var result = await receive;
            return LegacyCodec.S(result.Buffer, result.Buffer.Length) == LegacyCodec.CapabilityReply;
        }
        catch { return false; }
    }

    public Task StartResponderAsync()
    {
        _cts = new CancellationTokenSource();
        _responder = new UdpClient(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
        _capabilityResponder = new UdpClient(new IPEndPoint(IPAddress.Any, _options.CapabilityPort));
        _responderTask = ResponderLoopAsync(_responder, _cts.Token, LegacyCodec.DiscoveryPrefix,
            _ => LegacyCodec.DiscoveryResponse(_options.ChatPort));
        _capabilityTask = ResponderLoopAsync(_capabilityResponder, _cts.Token, LegacyCodec.CapabilityProbe,
            _ => LegacyCodec.B(LegacyCodec.CapabilityReply));
        return Task.CompletedTask;
    }

    private async Task ResponderLoopAsync(UdpClient udp, CancellationToken ct, string trigger, Func<string, byte[]> reply)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(ct);
                var text = LegacyCodec.S(r.Buffer, r.Buffer.Length);
                if (text.StartsWith(trigger, StringComparison.Ordinal))
                {
                    var data = reply(text);
                    await udp.SendAsync(data, data.Length, r.RemoteEndPoint);
                    Log?.Invoke("响应发现请求: " + r.RemoteEndPoint.Address);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log?.Invoke("发现服务异常: " + ex.Message); }
    }

    /// <summary>Stops only the server-side UDP responders so the object can be reused after migration.</summary>
    public async Task StopResponderAsync()
    {
        var cts = _cts;
        var responder = _responder;
        var capability = _capabilityResponder;
        var t1 = _responderTask;
        var t2 = _capabilityTask;
        _cts = null; _responder = null; _capabilityResponder = null;
        _responderTask = null; _capabilityTask = null;
        try { cts?.Cancel(); } catch { }
        try { responder?.Close(); capability?.Close(); } catch { }
        if (t1 is not null) try { await t1; } catch { }
        if (t2 is not null) try { await t2; } catch { }
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopResponderAsync();
}
