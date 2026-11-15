using FilesaCOChat.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace FilesaCOChat.WinUI;

/// <summary>
/// Attached behaviour for the chat TextBlocks: instead of binding Text directly,
/// bind <c>local:LinkifiedText.Text</c>. The value is tokenised by LinkParser and
/// written into the TextBlock's Inlines as Run + Hyperlink elements, so URLs in
/// a message become real, keyboard-accessible, underlined links.
/// <para>
/// Only NavigateUri is set; the WinUI framework launches it in the default
/// browser automatically. Handling Click as well would open the URL twice
/// (see the Hyperlink docs), so there is deliberately no Click handler and no
/// Launcher call here. Only http/https absolute URIs are produced.
/// </para>
/// Rebuilding Inlines on every value change is safe inside a DataTemplate:
/// ChatLine.Text is immutable, and container recycling re-runs the binding.
/// </summary>
public static class LinkifiedText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(LinkifiedText),
            new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject obj) =>
        (string?)obj.GetValue(TextProperty);

    public static void SetText(DependencyObject obj, string? value) =>
        obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        var inlines = block.Inlines;
        inlines.Clear();
        foreach (var segment in LinkParser.Tokenize(e.NewValue as string))
        {
            if (segment.Kind == LinkParser.SegmentKind.Text)
            {
                inlines.Add(new Run { Text = segment.Text });
                continue;
            }
            var link = new Hyperlink { NavigateUri = new Uri(segment.Uri!) };
            // A Hyperlink may only contain Run / non-Hyperlink Span inlines.
            link.Inlines.Add(new Run { Text = segment.Text });
            inlines.Add(link);
        }
    }
}
