using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Read-only inspection of a .docx file, shared by the renderer, the writer, and
/// the initializer so their validation rules stay in one place.
/// </summary>
public sealed class WordDocumentInspection
{
    public WordDocumentInspection(
        bool openable,
        string? openErrorMessage,
        IReadOnlyList<string> paragraphTexts,
        string? unsupportedContentDescription)
    {
        Openable = openable;
        OpenErrorMessage = openErrorMessage;
        ParagraphTexts = paragraphTexts;
        UnsupportedContentDescription = unsupportedContentDescription;
    }

    /// <summary>Whether the file opened as valid Office Open XML.</summary>
    public bool Openable { get; }

    public string? OpenErrorMessage { get; }

    /// <summary>Visible text of each top-level body paragraph.</summary>
    public IReadOnlyList<string> ParagraphTexts { get; }

    /// <summary>
    /// Description of unexpected relationship-dependent content (images,
    /// hyperlinks, VML, AlternateContent) in the body, or null when none is found.
    /// </summary>
    public string? UnsupportedContentDescription { get; }
}

public static class WordDocumentInspector
{
    /// <summary>Opens a document read-only and captures its validated shape.</summary>
    public static WordDocumentInspection Inspect(string docxPath)
    {
        try
        {
            using var document = WordprocessingDocument.Open(docxPath, false);
            var body = document.MainDocumentPart?.Document?.Body;
            var texts = body is null
                ? new List<string>()
                : body.Descendants<Paragraph>()
                    .Select(paragraph => ParagraphText.Reconstruct(paragraph, "\n"))
                    .ToList();

            return new WordDocumentInspection(
                openable: true,
                openErrorMessage: null,
                paragraphTexts: texts,
                unsupportedContentDescription: body is null ? null : FindUnsupportedContent(body));
        }
        catch (Exception ex)
        {
            return new WordDocumentInspection(
                openable: false,
                openErrorMessage: ex.Message,
                paragraphTexts: Array.Empty<string>(),
                unsupportedContentDescription: null);
        }
    }

    /// <summary>
    /// The <see cref="ReportTemplateContract"/> token strings still present in the
    /// document's paragraph text. Empty when every required placeholder is gone.
    /// </summary>
    public static IReadOnlyList<string> RemainingPlaceholders(
        WordDocumentInspection inspection,
        IReadOnlyCollection<string> requiredPlaceholderTokens)
    {
        var remaining = new List<string>();
        foreach (var placeholder in requiredPlaceholderTokens)
        {
            if (inspection.ParagraphTexts.Any(text => text.Contains(placeholder, StringComparison.Ordinal)))
                remaining.Add(placeholder);
        }

        return remaining;
    }

    /// <summary>
    /// Expected approved values that are NOT represented in the document's
    /// paragraph text. Paragraph texts are joined with a paragraph separator so a
    /// multi-paragraph value (lines separated by "\n\n") matches the real rendered
    /// paragraphs. Line-break characters are normalized so Windows CRLF values
    /// match the "\n" used by <see cref="Inspect"/>.
    /// </summary>
    public static IReadOnlyList<string> MissingExpectedValues(
        WordDocumentInspection inspection,
        IEnumerable<string> expectedValues)
    {
        var joined = string.Join("\n\n", inspection.ParagraphTexts);
        var missing = new List<string>();
        foreach (var value in expectedValues)
        {
            var normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");
            if (string.IsNullOrEmpty(normalized) || !joined.Contains(normalized, StringComparison.Ordinal))
                missing.Add(value);
        }

        return missing;
    }

    /// <summary>
    /// Whether the body contains content that cannot be safely copied into
    /// another document (any relationship reference or embedded-object namespace).
    /// Returns a description of the first offending element or null.
    /// </summary>
    public static string? FindUnsupportedContent(Body body)
    {
        foreach (var element in body.Descendants())
        {
            if (element is AlternateContent or AlternateContentChoice or AlternateContentFallback
                or Drawing or Picture)
                return $"{element.GetType().Name} is not supported for report-log append.";

            var elementNamespace = element.NamespaceUri;
            if (elementNamespace is { Length: > 0 } && IsEmbeddedObjectNamespace(elementNamespace))
                return $"{element.GetType().Name} (namespace {elementNamespace}) is not supported for report-log append.";

            foreach (var attribute in element.GetAttributes())
            {
                if (attribute.NamespaceUri == RelationshipNamespaceUri ||
                    attribute.NamespaceUri == XLinkNamespaceUri)
                {
                    return $"{element.GetType().Name} references a document relationship ({attribute.LocalName}).";
                }
            }
        }

        return null;
    }

    private const string RelationshipNamespaceUri =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string XLinkNamespaceUri = "http://www.w3.org/1999/xlink";

    private static bool IsEmbeddedObjectNamespace(string namespaceUri) => namespaceUri switch
    {
        "http://schemas.openxmlformats.org/drawingml/2006/main"
            or "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
            or "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing"
            or "urn:schemas-microsoft-com:vml"
            or "http://schemas.openxmlformats.org/markup-compatibility/2006" => true,
        _ => false,
    };
}