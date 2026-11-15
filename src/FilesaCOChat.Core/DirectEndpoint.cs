using System.Globalization;

namespace FilesaCOChat.Core;

/// <summary>
/// Parsed value of the "地址:端口" field on the login page's 连接指定频道 tab.
/// The wire protocol itself is untouched: the port only selects which TCP chat
/// port this client dials (and, after a migration, listens on).
/// </summary>
/// <param name="Host">IPv4 / IPv6 / DNS name, without brackets.</param>
/// <param name="Port">TCP chat port; never 0 (omitted ports fall back to the default).</param>
public readonly record struct DirectEndpoint(string Host, int Port)
{
    /// <summary>Used whenever the user leaves the port out, including a trailing ":".</summary>
    public const int DefaultPort = ProtocolOptions.DefaultChatPort;

    /// <summary>
    /// Accepts "host", "host:" (default port), "host:port", "[ipv6]" and
    /// "[ipv6]:port". A bare IPv6 address (two or more colons) is treated as a
    /// host with no port so it cannot be mistaken for a host:port pair.
    /// </summary>
    public static bool TryParse(string? input, out DirectEndpoint endpoint, out string error,
                                int defaultPort = DefaultPort)
    {
        endpoint = default;
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "请输入地址";
            return false;
        }

        string host;
        var portText = "";
        if (text[0] == '[')
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                error = "IPv6 地址缺少右方括号";
                return false;
            }
            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                {
                    error = "地址格式不正确";
                    return false;
                }
                portText = rest[1..].Trim();
            }
        }
        else
        {
            var firstColon = text.IndexOf(':');
            // Exactly one colon => host:port (or host: with the port omitted).
            // Two or more => unbracketed IPv6 address, port stays at default.
            if (firstColon >= 0 && text.IndexOf(':', firstColon + 1) < 0)
            {
                host = text[..firstColon];
                portText = text[(firstColon + 1)..].Trim();
            }
            else
            {
                host = text;
            }
        }

        if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            error = "地址格式不正确";
            return false;
        }

        var port = defaultPort is >= 1 and <= 65535 ? defaultPort : DefaultPort;
        if (portText.Length > 0 &&
            (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
             port is < 1 or > 65535))
        {
            error = "端口需为 1-65535 的数字";
            return false;
        }

        endpoint = new DirectEndpoint(host, port);
        return true;
    }

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
