using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Renders a <see cref="ValidatedReportRecord"/> through a copy of the report-log
/// template. The template copy is the formatting source: header placeholders are
/// replaced in place even when Word split them across runs, each of the five body
/// placeholders is resolved by its section heading context, and narrative values
/// become real Word paragraphs that keep the placeholder paragraph's formatting.
/// The original template bytes are never modified. The rendered output is
/// validated before success is reported.
/// </summary>
public sealed class ReportLogTemplateRenderer : IReportLogTemplateRenderer
{
    private const string NormalizedLineBreak = "\n";
    private const string ParagraphSeparator = "\n\n";

    public ReportTemplateRenderResult Render(string templatePath, ValidatedReportRecord record, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.OutputPathInvalid,
                "A rendered output path is required.");

        if (!string.Equals(Path.GetExtension(outputPath), ".docx", StringComparison.OrdinalIgnoreCase))
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.OutputPathInvalid,
                "The rendered output must be a .docx file.");

        var templateValidation = PathValidationService.ValidateReportLogTemplate(templatePath);
        if (!templateValidation.IsValid)
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.TemplateNotFound,
                templateValidation.ErrorMessage ?? "The report-log template is not available.");

        var templateInspection = WordDocumentInspector.Inspect(templatePath);
        if (!templateInspection.Openable)
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.InvalidTemplate,
                "The report-log template is not a valid Word document.");

        if (templateInspection.UnsupportedContentDescription is not null)
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.UnsupportedContent,
                templateInspection.UnsupportedContentDescription);

        if (PathsEqual(templatePath, outputPath))
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.OutputPathInvalid,
                "The rendered output must not be the template itself.");

        string? outputCleaned = null;
        try
        {
            byte[] templateBytes;
            try
            {
                templateBytes = File.ReadAllBytes(templatePath);
            }
            catch (Exception ex)
            {
                return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.InvalidTemplate,
                    "The template could not be read. " + ex.Message);
            }

            try
            {
                File.WriteAllBytes(outputPath, templateBytes);
                outputCleaned = outputPath;
            }
            catch (Exception ex)
            {
                return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.OutputPathInvalid,
                    "The rendered output could not be written. " + ex.Message);
            }

            string? stepErrorMessage = null;
            ReportTemplateRenderErrorKind? stepErrorKind = null;
            using (var document = WordprocessingDocument.Open(outputPath, true))
            {
                var body = document.MainDocumentPart?.Document?.Body;
                if (body is null)
                {
                    stepErrorKind = ReportTemplateRenderErrorKind.InvalidTemplate;
                    stepErrorMessage = "The template has no document body.";
                }
                else
                {
                    var unsupported = WordDocumentInspector.FindUnsupportedContent(body);
                    if (unsupported is not null)
                    {
                        stepErrorKind = ReportTemplateRenderErrorKind.UnsupportedContent;
                        stepErrorMessage = unsupported;
                    }
                    else
                    {
                        RenderHeader(body, record);
                        RenderBodySections(body, record);
                        document.Save();
                    }
                }
            }

            if (stepErrorKind is not null)
                return Fail(outputPath, outputCleaned, stepErrorKind.Value, stepErrorMessage!);

            return ValidateRenderedOutput(outputPath, record);
        }
        catch (Exception ex)
        {
            Cleanup(outputPath);
            return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.Unexpected,
                "Rendering failed unexpectedly. " + ex.Message);
        }
    }

    private void RenderHeader(Body body, ValidatedReportRecord record)
    {
        var paragraphs = body.Descendants<Paragraph>().ToList();

        foreach (var (field, placeholder) in ReportTemplateContract.HeaderPlaceholders)
        {
            var paragraph = paragraphs.FirstOrDefault(p =>
                ParagraphText.Reconstruct(p, NormalizedLineBreak).Contains(placeholder, StringComparison.Ordinal));
            if (paragraph is null)
                continue;

            ReplaceSpanAcrossRuns(paragraph, placeholder, ValueOf(record, field));
        }
    }

    private void RenderBodySections(Body body, ValidatedReportRecord record)
    {
        var paragraphs = body.Descendants<Paragraph>().ToList();

        foreach (var section in ReportTemplateContract.BodySections)
        {
            var headingIndex = IndexOfHeading(paragraphs, section.Heading);
            if (headingIndex < 0)
                continue;

            var placeholderParagraph = paragraphs
                .Skip(headingIndex + 1)
                .FirstOrDefault(p => ParagraphText.Reconstruct(p, NormalizedLineBreak) == section.Placeholder);
            if (placeholderParagraph is null)
                continue;

            ReplaceParagraphWithNarrative(placeholderParagraph, ValueOf(record, section.Field));
        }
    }

    private static int IndexOfHeading(IReadOnlyList<Paragraph> paragraphs, string heading)
    {
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (ParagraphText.Reconstruct(paragraphs[i], NormalizedLineBreak) == heading)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Replaces <paramref name="placeholder"/> text across the runs that contain
    /// it. Word may split a single placeholder across many runs (with proofErr and
    /// other non-text elements interleaved); this maps the reconstructed paragraph
    /// text back to the contributing w:t nodes and splices the replacement into
    /// the first of them.
    /// </summary>
    private static void ReplaceSpanAcrossRuns(Paragraph paragraph, string placeholder, string replacement)
    {
        var text = ParagraphText.Reconstruct(paragraph, NormalizedLineBreak);
        var start = text.IndexOf(placeholder, StringComparison.Ordinal);
        if (start < 0)
            return;

        var end = start + placeholder.Length;
        var slices = ParagraphText.Slice(paragraph, NormalizedLineBreak);
        var overlaps = new List<(Text Below, int indexInText, int length)>();

        foreach (var slice in slices)
        {
            var overlapStart = Math.Max(slice.Start, start);
            var overlapEnd = Math.Min(slice.End, end);
            if (overlapStart >= overlapEnd)
                continue;

            overlaps.Add((
                slice.Node,
                overlapStart - slice.Start,
                overlapEnd - overlapStart));
        }

        if (overlaps.Count == 0)
            return;

        foreach (var (node, indexInText, length) in overlaps)
            node.Text = node.Text!.Remove(indexInText, length);

        var (firstNode, firstIndex, _) = overlaps[0];
        firstNode.Text = firstNode.Text!.Insert(firstIndex, replacement);
    }

    /// <summary>
    /// Replaces a body placeholder paragraph with the narrative value as real Word
    /// paragraphs. "\n\n" separates paragraphs; "\n" becomes a line break within a
    /// paragraph. Every generated paragraph keeps the placeholder paragraph's
    /// paragraph properties (and the first run's run properties, if present), so
    /// formatting is preserved from the template.
    /// </summary>
    private static void ReplaceParagraphWithNarrative(Paragraph placeholderParagraph, string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");
        var paragraphStrings = normalized.Split(
            new[] { ParagraphSeparator },
            StringSplitOptions.None);

        var original = (Paragraph)placeholderParagraph.CloneNode(true);

        Paragraph? previous = null;
        for (var i = 0; i < paragraphStrings.Length; i++)
        {
            var target = i == 0 ? placeholderParagraph : (Paragraph)original.Clone();
            if (i > 0)
            {
                if (previous is null)
                {
                    placeholderParagraph.InsertAfterSelf(target);
                }
                else
                {
                    previous.InsertAfterSelf(target);
                }
            }

            SetParagraphNarrativeContent(target, paragraphStrings[i]);
            previous = target;
        }
    }

    private static void SetParagraphNarrativeContent(Paragraph paragraph, string text)
    {
        var runProperties = paragraph.Descendants<Run>().FirstOrDefault()?.RunProperties;
        var clonedRunProperties = runProperties?.CloneNode(false);

        var pPr = paragraph.ParagraphProperties;
        foreach (var child in paragraph.ChildElements.ToList())
        {
            if (child != pPr && child is not SectionProperties)
                child.Remove();
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var run = new Run();
            if (clonedRunProperties is not null)
                run.AppendChild((DocumentFormat.OpenXml.OpenXmlElement)clonedRunProperties.CloneNode(true));

            if (i > 0)
                run.AppendChild(new Break());

            run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.AppendChild(run);
        }
    }

    private static ReportTemplateRenderResult ValidateRenderedOutput(string outputPath, ValidatedReportRecord record)
    {
        var inspection = WordDocumentInspector.Inspect(outputPath);
        if (!inspection.Openable)
            return Fail(outputPath, outputPath, ReportTemplateRenderErrorKind.Unexpected,
                "The rendered entry is not a valid Word document.");

        if (inspection.UnsupportedContentDescription is not null)
            return Fail(outputPath, outputPath, ReportTemplateRenderErrorKind.UnsupportedContent,
                inspection.UnsupportedContentDescription);

        var remaining = WordDocumentInspector.RemainingPlaceholders(inspection, AllPlaceholderTokens());
        if (remaining.Count > 0)
            return Fail(outputPath, outputPath, ReportTemplateRenderErrorKind.PlaceholdersRemain,
                "Rendered entry still contains placeholders: " + string.Join(", ", remaining));

        var expectedValues = ReportTemplateContract.AllFields.Select(field => ValueOf(record, field)).ToList();
        var missing = WordDocumentInspector.MissingExpectedValues(inspection, expectedValues);
        if (missing.Count > 0)
            return Fail(outputPath, outputPath, ReportTemplateRenderErrorKind.ExpectedValuesMissing,
                "Rendered entry is missing expected approved values: " + string.Join(", ", missing));

        return new ReportTemplateRenderResult(true, outputPath, ReportTemplateRenderErrorKind.None, null);
    }

    private static IReadOnlyCollection<string> AllPlaceholderTokens()
    {
        var tokens = ReportTemplateContract.HeaderPlaceholders.Values.ToList();
        tokens.Add(ReportTemplateContract.BodyPlaceholder);
        return tokens;
    }

    private static ReportTemplateRenderResult Fail(
        string outputPath,
        string? outputToClean,
        ReportTemplateRenderErrorKind errorKind,
        string message)
    {
        if (outputToClean is not null)
            Cleanup(outputToClean);
        return new ReportTemplateRenderResult(false, null, errorKind, message);
    }

    private static void Cleanup(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch (Exception)
        {
            // Cleanup is best effort; the caller sees the structured failure.
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string ValueOf(ValidatedReportRecord record, ReportField field) => field switch
    {
        ReportField.ReportNumber => record.ReportNumber,
        ReportField.InspectionDate => record.InspectionDate.ToString("MM/dd/yy"),
        ReportField.InspectorFirstName => record.InspectorFirstName,
        ReportField.DescriptionOfWork => record.DescriptionOfWork,
        ReportField.DrawingReferences => record.DrawingReferences,
        ReportField.GeneralObservations => record.GeneralObservations,
        ReportField.Discrepancies => record.Discrepancies,
        ReportField.PreviousDiscrepancyCorrections => record.PreviousDiscrepancyCorrections,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unexpected report field."),
    };
}