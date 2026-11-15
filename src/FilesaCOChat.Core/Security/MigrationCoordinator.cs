using System.Net;

namespace FilesaCOChat.Core.Security;

public sealed record MigrationCandidate(string Username, string Ip);

/// <summary>
/// DEFECT D6 (report §7, severity 中): 5.1 picked a new server with random.choice,
/// then slept a fixed 1 second. No term, no ack, no retry -> double/no server.
/// Rewrite: deterministic election, monotonic term, explicit acknowledgement with
/// timeout + retry, and idempotent application of PROMOTE/MIGRATE frames.
/// </summary>
public sealed class MigrationCoordinator
{
    public const string FixId = "D6";
    private readonly object _gate = new();
    private long _term;
    private long _appliedTerm;
    private string? _pendingIp;
    private TaskCompletionSource<bool>? _ack;

    public long CurrentTerm { get { lock (_gate) return _term; } }
    public long AppliedTerm { get { lock (_gate) return _appliedTerm; } }

    /// <summary>Deterministic: smallest numeric IPv4, then ordinal username order.</summary>
    public static MigrationCandidate? Elect(IEnumerable<MigrationCandidate> candidates)
    {
        static long IpKey(string ip) =>
            IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? BitConverter.ToUInt32(a.GetAddressBytes().Reverse().ToArray(), 0)
                : long.MaxValue;
        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Ip))
            .OrderBy(c => IpKey(c.Ip))
            .ThenBy(c => c.Username, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public long BeginTerm(string targetIp)
    {
        lock (_gate)
        {
            _term++;
            _pendingIp = targetIp;
            _ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _term;
        }
    }

    public void RecordAck(long term, string ip)
    {
        lock (_gate)
        {
            if (term == _term && ip == _pendingIp) _ack?.TrySetResult(true);
        }
    }

    public async Task<bool> WaitForAckAsync(long term, TimeSpan timeout)
    {
        Task task;
        lock (_gate) task = _ack?.Task ?? Task.CompletedTask;
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task) return false;
        return task.IsCompletedSuccessfully && await (Task<bool>)task;
    }

    /// <summary>Rejects duplicate / stale terms, so a delayed frame cannot create a second server.</summary>
    public bool ShouldApply(long term, string ip)
    {
        lock (_gate)
        {
            if (term <= 0) term = _term + 1; // legacy frames carry no term
            if (term <= _appliedTerm) return false;
            _appliedTerm = term;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate) { _appliedTerm = 0; _pendingIp = null; _ack?.TrySetResult(false); _ack = null; }
    }
}
