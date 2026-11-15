using System.Text.RegularExpressions;

namespace FilesaCOChat.WinUI.ViewModels;

/// <summary>
/// Pure (WinUI-free) URL tokeniser shared by the chat bubble renderer and the
/// head-less test suite. It splits a chat message into plain-text segments and
/// clickable http(s) links without ever changing the original text: every
/// character of the input is present in exactly one segment, so copy / tooltip
/// / audit behaviour is unchanged.
/// <para>
/// Deliberately conservative: only http://, https:// and www. forms are
/// recognised. Bare domains (example.com) are not, because in Chinese chat text
/// they collide with ordinary punctuation-heavy sentences. CJK ideographs and
/// full-width punctuation terminate a URL, so "看 https://a.com 的内容" does not
/// swallow the trailing Chinese words.
/// </para>
/// </summary>
public static class LinkParser
{
    public enum SegmentKind { Text, Link }

    public readonly record struct Segment(SegmentKind Kind, string Text, string? Uri);

    private static readonly Regex Url = new(
        @"(?:https?://|www\.)[^\s<>""'\u0000-\u001F\u3000-\u303F\u3400-\u9FFF\uF900-\uFAFF\uFF00-\uFFEF]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Trailing punctuation that is almost never part of a pasted URL.</summary>
    private const string TrailingPunctuation =
        ".,;:!?)]}'\"，。！？；：、）】》」』…~～";

    /// <summary>Returns an empty list for null / empty input.</summary>
    public static IReadOnlyList<Segment> Tokenize(string? text)
    {
        var segments = new List<Segment>();
        if (string.IsNullOrEmpty(text)) return segments;

        var cursor = 0;
        foreach (Match match in Url.Matches(text))
        {
            if (match.Index < cursor) continue;              // overlapping match
            var display = match.Value.TrimEnd(TrailingPunctuation.ToCharArray());
            if (display.Length == 0) continue;
            var candidate = display.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? "https://" + display
                : display;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;                                    // leave it as plain text

            if (match.Index > cursor)
                segments.Add(new Segment(SegmentKind.Text, text[cursor..match.Index], null));
            segments.Add(new Segment(SegmentKind.Link, display, uri.ToString()));
            cursor = match.Index + display.Length;           // trimmed punctuation stays in text
        }
        if (cursor < text.Length)
            segments.Add(new Segment(SegmentKind.Text, text[cursor..], null));
        return segments;
    }
}
