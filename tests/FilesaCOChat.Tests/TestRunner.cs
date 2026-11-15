using System.Diagnostics;
using System.Text.Json;
using FilesaCOChat.Core;

namespace FilesaCOChat.Tests;

public sealed class CheckException : Exception
{
    public CheckException(string message) : base(message) { }
}

public static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new CheckException(message);
    }
    public static void False(bool condition, string message) => True(!condition, message);
    public static void Equal(string expected, string actual, string message) =>
        True(expected == actual, $"{message}: expected <{expected}>, actual <{actual}>");
    public static void Equal(long expected, long actual, string message) =>
        True(expected == actual, $"{message}: expected {expected}, actual {actual}");
    public static void Equal(int expected, int actual, string message) =>
        True(expected == actual, $"{message}: expected {expected}, actual {actual}");
    public static void NotNull(object? value, string message) => True(value is not null, message);
    public static void Eq<T>(T expected, T actual, string message) =>
        True(EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message}: expected <{expected}>, actual <{actual}>");
    public static void Bytes(byte[] expected, byte[] actual, string message) =>
        True(expected.AsSpan().SequenceEqual(actual),
            $"{message}: expected {Convert.ToHexString(expected)}, actual {Convert.ToHexString(actual)}");
}

public sealed record TestResult(string Id, string Name, string Defect, bool Passed, string Detail, long Milliseconds);

public static class TestRunner
{
    private static readonly List<(string Id, string Name, string Defect, Func<Task> Body)> Cases = new();
    public static readonly List<TestResult> Results = new();

    public static void Add(string id, string name, string defect, Func<Task> body) =>
        Cases.Add((id, name, defect, body));

    public static async Task<int> RunAllAsync(string resultPath)
    {
        foreach (var (id, name, defect, body) in Cases)
        {
            var sw = Stopwatch.StartNew();
            var detail = "OK";
            var passed = true;
            try
            {
                await body().WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (Exception ex)
            {
                passed = false;
                detail = ex.GetType().Name + ": " + ex.Message;
                if (ex is not CheckException && ex.StackTrace is not null)
                {
                    var top = string.Join(" | ", ex.StackTrace.Split('\n').Take(4).Select(l => l.Trim()));
                    detail += " @ " + top;
                }
            }
            sw.Stop();
            Results.Add(new TestResult(id, name, defect, passed, detail, sw.ElapsedMilliseconds));
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {id} {name} ({defect}) {sw.ElapsedMilliseconds}ms" +
                              (passed ? "" : "  -> " + detail));
        }
        var json = JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resultPath, json);
        var failed = Results.Count(r => !r.Passed);
        Console.WriteLine($"\n==== {Results.Count - failed}/{Results.Count} passed, {failed} failed ====");
        Console.WriteLine("results -> " + resultPath);
        return failed == 0 ? 0 : 1;
    }
}

public static class TestOptions
{
    public static ProtocolOptions Make(int chat, int? discovery = null, int? cap = null, int? file = null) =>
        new()
        {
            ChatPort = chat,
            DiscoveryPort = discovery ?? chat + 1,
            CapabilityPort = cap ?? chat + 2,
            FilePort = file ?? chat + 3,
            DiscoveryBroadcastAddress = "127.0.0.1",
            EnablePortScan = false,
            AutoCheckUpdate = false,
            DiscoveryWindow = TimeSpan.FromMilliseconds(600),
            MigrationAckTimeout = TimeSpan.FromMilliseconds(400),
            PreSharedKey = null
        };

    public static async Task<ChatApplication> StartServerAsync(ProtocolOptions options, string username)
    {
        var app = new ChatApplication(options);
        if (!await app.CreateRoomAsync()) throw new CheckException("server failed to bind " + options.ChatPort);
        return app;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new CheckException("timeout waiting for " + message);
    }
}
