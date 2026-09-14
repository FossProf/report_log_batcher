using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Deterministic parser for the reviewed production SPIN report layout.
///
/// Supported production structure (Special Inspection Report #319 and similar):
///
///   1. A title paragraph in the main flow beginning "Special Inspection Report
///      #NNN" (the phrase may be split across runs).
///   2. A header table whose labeled rows hold "Inspection Date" and
///      "Cornerstone Inspector(s)" next to their value cells in the same row.
///   3. Five canonical body sections introduced by the exact headings defined in
///      <see cref="ReportTemplateContract.BodySections"/>, in order. Only
///      main-flow paragraphs (paragraphs not nested inside a table) participate
///      in section extraction.
///   4. The final narrative section ends at the first boundary marker:
///      certification ("To the best of my knowledge..."), "Submitted By",
///      "Reviewed By", or "PHOTO DOCUMENTATION".
///
/// Rules enforced deterministically:
/// - Logical paragraph text is reconstructed across Open XML runs. Each
///   <c>&lt;w:tab/&gt;</c> contributes a single structural space and each
///   <c>&lt;w:br/&gt;</c>/<c>&lt;w:cr/&gt;</c> (including explicit page breaks)
///   contributes one line feed. The Word paragraph separator is reconstructed as
///   <c>Environment.NewLine + Environment.NewLine</c>.
/// - Headings and header labels match case-insensitively with an optional
///   trailing colon. No fuzzy or substring matching is used.
/// - Absent structure -> Missing diagnostic; repeated structure -> Ambiguous
///   diagnostic; unparsable value -> InvalidFormat diagnostic.
/// - Unresolved values stay null. The parser never writes "N/A"; an explicit
///   source "N/A" is preserved as the literal "N/A".
/// - The file is opened with <c>FileAccess.Read</c> by
///   <c>WordprocessingDocument.Open(stream, isEditable: false)</c> and is never
///   modified.
/// </summary>
public sealed class SpinReportParser : ISpinReportParser
{
    private const string ReportTitlePrefix = "Special Inspection Report #";
    private const string CertificationPrefix = "To the best of my knowledge";
    private const string SubmittedByPrefix = "Submitted By";
    private const string ReviewedByPrefix = "Reviewed By";
    private const string PhotoDocumentationPrefix = "PHOTO DOCUMENTATION";
    private static readonly string[] DateFormats = { "yyyy-M-d", "M/d/yyyy" };

    public SpinParseResult Parse(string spinReportPath)
    {
        var pathValidation = PathValidationService.ValidateSpinReport(spinReportPath);
        if (!pathValidation.IsValid)
        {
            var message = string.IsNullOrWhiteSpace(spinReportPath)
                ? "No SPIN report file was selected."
                : pathValidation.ErrorMessage!;

            return DocumentFailure(spinReportPath, message);
        }

        // Read both the main-flow paragraph stream and the header table cells in
        // one document traversal so the source file is opened a single time.
        IReadOnlyList<string> paragraphs;
        IReadOnlyList<IReadOnlyList<string>> tableRows;
        try
        {
            using var stream = new FileStream(spinReportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = WordprocessingDocument.Open(stream, false);

            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
                return DocumentFailure(spinReportPath, "The document contains no body content.");

            paragraphs = ReadMainFlowParagraphs(body);
            tableRows = ReadHeaderTableRows(body);
        }
        catch (Exception)
        {
            // Failure to open/read the package is always a document-level error.
            return DocumentFailure(spinReportPath, "The document could not be opened as a valid Word document.");
        }

        if (paragraphs.Count == 0 && tableRows.Count == 0)
            return DocumentFailure(spinReportPath, "The document contains no body content.");

        var issues = new List<ParseIssue>();
        var record = new ReportRecord();

        ExtractReportNumber(paragraphs, ref record, issues);
        ExtractHeaderFields(tableRows, ref record, issues);
        ExtractBodySections(paragraphs, ref record, issues);

        var status = record.HasAnyUnresolved
            ? SpinParseStatus.ParsedWithUnresolvedFields
            : SpinParseStatus.Parsed;

        return new SpinParseResult(record, status, spinReportPath, issues);
    }

    /// <summary>Main-flow paragraphs only: paragraphs nested inside tables are excluded.</summary>
    private static IReadOnlyList<string> ReadMainFlowParagraphs(Body body) =>
        body.Descendants<Paragraph>()
            .Where(p => !p.Ancestors<Table>().Any())
            .Select(ReconstructParagraph)
            .ToList();

    /// <summary>All table rows in the body as lists of cell texts.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ReadHeaderTableRows(Body body) =>
        body.Descendants<Table>()
            .SelectMany(table => table.Descendants<TableRow>())
            .Select(row => (IReadOnlyList<string>)row.Descendants<TableCell>()
                .Select(ReconstructCell)
                .ToList())
            .ToList();

    /// <summary>
    /// Cell text is the concatenation of its paragraphs joined by
    /// <c>Environment.NewLine</c> (headers/labels and values are single-paragraph
    /// in production, so this only matters as a safety net).
    /// </summary>
    private static string ReconstructCell(TableCell cell) =>
        string.Join(
            Environment.NewLine,
            cell.Descendants<Paragraph>().Select(ReconstructParagraph));

    /// <summary>
    /// Reconstructs the logical text of one Word paragraph.
    ///
    /// - <c>&lt;w:t&gt;</c> text is concatenated in document order.
    /// - <c>&lt;w:tab/&gt;</c> contributes a single structural space.
    /// - <c>&lt;w:br/&gt;</c>/<c>&lt;w:cr/&gt;</c> (including explicit page
    ///   breaks) contribute one line feed each, so narrative that spans visual
    ///   lines or page boundaries keeps its source line structure without
    ///   prematurely terminating extraction.
    ///
    /// No whitespace is invented between runs; the final text is trimmed.
    /// </summary>
    private static string ReconstructParagraph(Paragraph paragraph)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text text:
                    builder.Append(text.Text ?? string.Empty);
                    break;
                case TabChar:
                    builder.Append(' ');
                    break;
                case Break:
                case CarriageReturn:
                    builder.Append(Environment.NewLine);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    // ---- Report number ------------------------------------------------------

    private static void ExtractReportNumber(
        IReadOnlyList<string> paragraphs,
        ref ReportRecord record,
        List<ParseIssue> issues)
    {
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var malformedValues = new List<string>();
        var foundTitles = 0;

        foreach (var text in paragraphs)
        {
            var trimmed = text.Trim();
            if (!trimmed.StartsWith(ReportTitlePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            foundTitles++;
            var value = trimmed[ReportTitlePrefix.Length..].Trim();

            if (value.Length > 0 && value.All(char.IsAsciiDigit))
            {
                numbers.Add(value);
            }
            else
            {
                malformedValues.Add(value);
            }
        }

        if (numbers.Count == 1)
        {
            record = record with { ReportNumber = numbers.Single() };
            return;
        }

        if (numbers.Count > 1)
        {
            issues.Add(new ParseIssue(
                ParseIssueKind.Ambiguous,
                ReportField.ReportNumber,
                $"Multiple conflicting report numbers found in titles: {string.Join(", ", numbers.Select(n => "#" + n))}."));
            return;
        }

        if (foundTitles > 0)
        {
            var value = malformedValues.Count > 0 ? malformedValues[0] : string.Empty;
            issues.Add(new ParseIssue(
                ParseIssueKind.InvalidFormat,
                ReportField.ReportNumber,
                $"Title \"{ReportTitlePrefix.TrimEnd('#')}{value}\" has no numeric report number after '#'. Expected digits only."));
            return;
        }

        issues.Add(new ParseIssue(
            ParseIssueKind.Missing,
            ReportField.ReportNumber,
            $"Expected title \"{ReportTitlePrefix.TrimEnd('#')}<number>\" not found. The report number is never taken from the file name."));
    }

    // ---- Header metadata (table label rows) -----------------------------------

    private static void ExtractHeaderFields(
        IReadOnlyList<IReadOnlyList<string>> tableRows,
        ref ReportRecord record,
        List<ParseIssue> issues)
    {
        var dateValue = ExtractHeaderLabelValue(tableRows, "Inspection Date", ReportField.InspectionDate, issues);
        var inspectorValue = ExtractHeaderLabelValue(tableRows, "Cornerstone Inspector(s)", ReportField.InspectorFirstName, issues);

        if (dateValue is not null)
        {
            if (DateOnly.TryParseExact(
                    dateValue,
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                record = record with { InspectionDate = parsedDate };
            }
            else
            {
                issues.Add(new ParseIssue(
                    ParseIssueKind.InvalidFormat,
                    ReportField.InspectionDate,
                    $"Inspection Date value '{dateValue}' could not be parsed. Supported formats: yyyy-MM-dd, MM/dd/yyyy, M/d/yyyy."));
            }
        }

        if (inspectorValue is not null)
        {
            var tokens = inspectorValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                record = record with { InspectorFirstName = tokens[0] };
            }
            else
            {
                issues.Add(new ParseIssue(
                    ParseIssueKind.Missing,
                    ReportField.InspectorFirstName,
                    "\"Cornerstone Inspector(s)\" was found but contains no value."));
            }
        }
    }

    /// <summary>
    /// Finds the label in any row's cells and returns the trimmed text of the
    /// cell immediately to its right. A label that appears in more than one row
    /// is treated as conflicting (Ambiguous).
    /// </summary>
    private static string? ExtractHeaderLabelValue(
        IReadOnlyList<IReadOnlyList<string>> tableRows,
        string label,
        ReportField field,
        List<ParseIssue> issues)
    {
        var labelText = NormalizeLabel(label);
        var matchedRows = 0;
        string? value = null;

        foreach (var row in tableRows)
        {
            for (var cellIndex = 0; cellIndex < row.Count; cellIndex++)
            {
                if (!string.Equals(NormalizeLabel(row[cellIndex]), labelText, StringComparison.OrdinalIgnoreCase))
                    continue;

                matchedRows++;
                if (cellIndex + 1 < row.Count)
                {
                    var candidate = row[cellIndex + 1].Trim();
                    if (candidate.Length > 0)
                        value = candidate;
                }
            }
        }

        if (matchedRows == 0)
        {
            issues.Add(new ParseIssue(
                ParseIssueKind.Missing,
                field,
                $"Header field \"{label}\" not found in the report."));
            return null;
        }

        if (matchedRows > 1)
        {
            issues.Add(new ParseIssue(
                ParseIssueKind.Ambiguous,
                field,
                $"Header field \"{label}\" appears {matchedRows} times; extraction is ambiguous."));
            return null;
        }

        if (value is null)
        {
            issues.Add(new ParseIssue(
                ParseIssueKind.Missing,
                field,
                $"Header field \"{label}\" was found but has no value."));
        }

        return value;
    }

    // ---- Body sections ----------------------------------------------------------

    private static void ExtractBodySections(
        IReadOnlyList<string> paragraphs,
        ref ReportRecord record,
        List<ParseIssue> issues)
    {
        var sections = ReportTemplateContract.BodySections;
        var headingPositions = new int[sections.Count];
        var headingCounts = new int[sections.Count];
        Array.Fill(headingPositions, -1);

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
            {
                if (!MatchesHeading(paragraphs[paragraphIndex], sections[sectionIndex].Heading))
                    continue;

                if (headingPositions[sectionIndex] < 0)
                    headingPositions[sectionIndex] = paragraphIndex;
                headingCounts[sectionIndex]++;
            }
        }

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var headingIndex = headingPositions[sectionIndex];

            if (headingIndex < 0)
            {
                issues.Add(new ParseIssue(
                    ParseIssueKind.Missing,
                    section.Field,
                    $"Section heading not found: \"{section.Heading}\""));
                continue;
            }

            if (headingCounts[sectionIndex] > 1)
            {
                issues.Add(new ParseIssue(
                    ParseIssueKind.Ambiguous,
                    section.Field,
                    $"Section heading \"{section.Heading}\" appears {headingCounts[sectionIndex]} times; extraction is ambiguous."));
                continue;
            }

            // Section content spans the main-flow paragraphs strictly between this
            // heading and the next section heading in the canonical order.
            var sectionEnd = paragraphs.Count;
            for (var other = 0; other < sections.Count; other++)
            {
                var otherPosition = headingPositions[other];
                if (otherPosition > headingIndex && otherPosition < sectionEnd)
                    sectionEnd = otherPosition;
            }

            var isFinalSection = sectionIndex == sections.Count - 1;
            var content = ExtractSectionContent(paragraphs, headingIndex + 1, sectionEnd, isFinalSection);

            if (content is null)
            {
                issues.Add(new ParseIssue(
                    ParseIssueKind.Missing,
                    section.Field,
                    $"Section heading \"{section.Heading}\" was found but contains no narrative content."));
                continue;
            }

            record = AssignSection(record, section.Field, content);
        }
    }

    private static ReportRecord AssignSection(ReportRecord record, ReportField field, string content) =>
        field switch
        {
            ReportField.DescriptionOfWork => record with { DescriptionOfWork = content },
            ReportField.DrawingReferences => record with { DrawingReferences = content },
            ReportField.GeneralObservations => record with { GeneralObservations = content },
            ReportField.Discrepancies => record with { Discrepancies = content },
            ReportField.PreviousDiscrepancyCorrections => record with { PreviousDiscrepancyCorrections = content },
            _ => record,
        };

    /// <summary>
    /// Joins the non-blank paragraphs of a section with the paragraph separator.
    /// For the final narrative section, extraction stops at the first boundary
    /// marker (certification, submission/review signature labels, or the photo
    /// documentation heading).
    /// </summary>
    private static string? ExtractSectionContent(
        IReadOnlyList<string> paragraphs,
        int startInclusive,
        int endExclusive,
        bool isFinalSection)
    {
        var end = endExclusive;
        if (isFinalSection)
        {
            for (var index = startInclusive; index < endExclusive; index++)
            {
                if (IsFinalSectionBoundary(paragraphs[index]))
                {
                    end = index;
                    break;
                }
            }
        }

        var segment = new List<string>();
        for (var index = startInclusive; index < end; index++)
        {
            var text = paragraphs[index];
            if (string.IsNullOrWhiteSpace(text))
                continue;

            segment.Add(text);
        }

        return segment.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, segment);
    }

    private static bool IsFinalSectionBoundary(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith(CertificationPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(SubmittedByPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(ReviewedByPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(PhotoDocumentationPrefix, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Matching helpers ---------------------------------------------------------

    private static bool MatchesHeading(string text, string heading)
    {
        var normalized = NormalizeLabel(text);
        return string.Equals(normalized, NormalizeLabel(heading), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLabel(string text) => text.Trim().TrimEnd(':');

    private static SpinParseResult DocumentFailure(string path, string message) =>
        new(
            new ReportRecord(),
            SpinParseStatus.Failed,
            path,
            new[] { new ParseIssue(ParseIssueKind.DocumentError, null, message) });
}