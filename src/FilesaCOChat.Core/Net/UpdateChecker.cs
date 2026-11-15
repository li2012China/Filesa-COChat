using System.Text.Json;
using System.Text.RegularExpressions;

namespace FilesaCOChat.Core.Net;

/// <summary>
/// D8 fix for "更新链路只提示不校验": 5.1 compared GitHub tag_name with 5.1 and
/// opened a browser link. The rewrite keeps that user-visible behaviour (no download,
/// no install, no execution) but validates the HTTPS endpoint, the version tag shape
/// and the release URL before surfacing anything.
/// </summary>
public sealed class UpdateChecker
{
    public const string FixId = "D8";
    private static readonly Regex TagPattern = new(@"^v?\d+(\.\d+){1,3}$", RegexOptions.Compiled);
    private readonly ProtocolOptions _options;
    private readonly HttpClient _http;

    public UpdateChecker(ProtocolOptions options, HttpClient? http = null)
    {
        _options = options;
        _http = http ?? new HttpClient { Timeout = options.UpdateTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FilesaCOChat-WinUI3/" + options.Version);
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var failed = new UpdateInfo(false, _options.Version, "", ProtocolOptions.ReleasesPageUrl, UiText.UpdateCheckFailed);
        if (!_options.UpdateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return failed;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_options.UpdateTimeout);
            var json = await _http.GetStringAsync(_options.UpdateUrl, linked.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TagPattern.IsMatch(tag))
                return failed with { Detail = "忽略格式异常的 tag_name" };
            var latest = tag.TrimStart('v', 'V');
            var hasUpdate = Version.TryParse(latest, out var newer) &&
                            Version.TryParse(_options.Version, out var current) && newer > current;
            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            if (!url.StartsWith("https://github.com/li2012China/Filesa-COChat/", StringComparison.OrdinalIgnoreCase))
                url = ProtocolOptions.ReleasesPageUrl;
            return new UpdateInfo(hasUpdate, _options.Version, latest, url,
                hasUpdate ? UiText.UpdateAvailable : UiText.AlreadyLatest);
        }
        catch (Exception ex)
        {
            return failed with { Detail = UiText.UpdateCheckFailed + ": " + ex.Message };
        }
    }
}
