using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Deterministic reconstruction of a paragraph's visible text from its Open XML
/// runs, mirroring the SPIN parser's convention:
///
///  - run text         -> its text
///  - w:tab            -> a single space
///  - w:br / w:cr      -> the supplied line-break string
///
/// Non-text elements (proofErr, fields, etc.) contribute nothing. This is the
/// single source the renderer and the writer use before matching placeholder text
/// that Word may have split across multiple runs.
/// </summary>
internal static class ParagraphText
{
    /// <summary>
    /// Reconstructs the visible text, joining line breaks with
    /// <paramref name="lineBreak"/>.
    /// </summary>
    public static string Reconstruct(OpenXmlElement paragraph, string lineBreak)
    {
        if (paragraph is null)
            throw new ArgumentNullException(nameof(paragraph));

        var text = new System.Text.StringBuilder();
        AppendVisibleText(paragraph, lineBreak, text);
        return text.ToString();
    }

    /// <summary>
    /// Per-text-node homogeneous chunks of the reconstructed string. Positions are
    /// indexes into the string produced by <see cref="Reconstruct"/> with the same
    /// <paramref name="lineBreak"/> value. Break/tab characters occupy positions
    /// but belong to no <see cref="TextSlice.Node"/>.
    /// </summary>
    public static IReadOnlyList<TextSlice> Slice(OpenXmlElement paragraph, string lineBreak)
    {
        if (paragraph is null)
            throw new ArgumentNullException(nameof(paragraph));

        var slices = new List<TextSlice>();
        var position = 0;

        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text textNode:
                    var length = (textNode.Text ?? string.Empty).Length;
                    slices.Add(new TextSlice(textNode, position, position + length));
                    position += length;
                    break;
                case TabChar:
                    position += TabCharacterWidth;
                    break;
                case Break:
                case CarriageReturn:
                    position += lineBreak.Length;
                    break;
                default:
                    if (element.LocalName == "br" || element.LocalName == "cr")
                        position += lineBreak.Length;
                    break;
            }
        }

        return slices;
    }

    /// <summary>The " " width a structural w:tab maps to when reconstructing text.</summary>
    internal const int TabCharacterWidth = 1;

    private static void AppendVisibleText(OpenXmlElement parent, string lineBreak, System.Text.StringBuilder text)
    {
        foreach (var element in parent.Descendants())
        {
            switch (element)
            {
                case Text textNode:
                    text.Append(textNode.Text ?? string.Empty);
                    break;
                case TabChar:
                    text.Append(' ');
                    break;
                case Break:
                case CarriageReturn:
                    text.Append(lineBreak);
                    break;
                default:
                    if (element.LocalName == "br" || element.LocalName == "cr")
                        text.Append(lineBreak);
                    break;
            }
        }
    }
}

/// <summary>A homogeneous run of characters within a single <c>w:t</c> element.</summary>
internal readonly record struct TextSlice(Text Node, int Start, int End);