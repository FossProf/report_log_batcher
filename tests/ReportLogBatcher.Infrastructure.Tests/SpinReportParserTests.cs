using System.Security.Cryptography;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;
using Xunit;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class SpinReportParserTests : IDisposable
{
    private static readonly string ParagraphSeparator = Environment.NewLine + Environment.NewLine;

    private readonly string _tempRoot;
    private readonly SpinDocFactory _factory;
    private readonly SpinReportParser _parser = new();

    public SpinReportParserTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_SpinParserTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _factory = new SpinDocFactory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private const string DescriptionBody =
        "Cornerstone was on site to observe ongoing masonry rehabilitation activities.";

    private const string DrawingBody = "S6";

    private static readonly string ObservationsBody =
        "Demolition at Column Line 6 reached 25%." + Environment.NewLine + Environment.NewLine +
        "Grout temperatures were periodically monitored and remained below 140 degrees F.";

    private const string DiscrepanciesBody = "N/A";

    private static readonly string CorrectionsBody =
        "Repair area was cleaned; the appearance remained distrubuted." + Environment.NewLine +
        "Remixed grout cured defecient and was re-worked the following day.";

    /// <summary>Sections H3/H4/H5 body builders for a complete report.</summary>
    /// <summary>
    /// The production-shaped baseline: split titled paragraph, header table,
    /// five canonical sections, then the certification, signatures, and photo
    /// documentation trailer. Optional parameters let a single case vary one
    /// part of the report while keeping the rest complete.
    /// </summary>
    private static void BuildStandard(
        SpinDocBuilder doc,
        string? generalObservationsMarkers = null,
        string? correctionsBody = null,
        string? finalBoundary = null)
    {
        doc.Paragraph("Special Inspection Report ", "#", "319");
        doc.Table(
            ["Project Name:", "CMF Structural Repairs SPIN", "Inspection Date:", "2026-09-11"],
            ["Cornerstone Project No.:", "24-X-05342"],
            ["Owner:", "MSD"],
            ["Contract Manager:", "Cornerstone Engineering, Inc.", "Temperature (deg F):", "88"],
            ["General Contractor:", "MAC Construction", "Weather:", "Rainy"],
            ["Location(s):", "CMF Structural Repairs Phase 1."],
            ["Cornerstone Inspector(s):", "Anthony Wintergerst"],
            ["Personnel On Site:", "Bohannon Masonry, MAC"]);
        doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
        doc.Paragraph(DescriptionBody);
        doc.Paragraph(ReportTemplateContract.DrawingReferencesHeading);
        doc.Paragraph(DrawingBody);
        doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
        doc.Paragraph(generalObservationsMarkers ?? "Demolition at Column Line 6 reached 25%.[BR][BR]Grout temperatures were periodically monitored and remained below 140 degrees F.");
        doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
        doc.Paragraph(DiscrepanciesBody);
        doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
        doc.Paragraph(correctionsBody ?? "Repair area was cleaned; the appearance remained distrubuted.[BR]Remixed grout cured defecient and was re-worked the following day.");
        if (finalBoundary is null)
        {
            doc.Paragraph("To the best of my knowledge, work inspected was in accordance with approved drawings.");
            doc.Paragraph("Submitted By:[TAB]Reviewed By:");
            doc.Paragraph("PHOTO DOCUMENTATION");
        }
        else
        {
            doc.Paragraph(finalBoundary);
            doc.Paragraph("This trailing content must be excluded.");
        }
    }

    /// <summary>Completes a report shell: title, header table, then the given
    /// H3 section followed by Discrepancies and PreviousDiscrepancyCorrections.</summary>
    private static void BuildWithCustomObservations(
        SpinDocBuilder doc,
        Action<SpinDocBuilder> generalObservationsSection)
    {
        doc.Paragraph("Special Inspection Report #319");
        doc.Table(
            ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
            ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
        doc.Paragraph(DescriptionBody);
        doc.Paragraph(ReportTemplateContract.DrawingReferencesHeading);
        doc.Paragraph(DrawingBody);
        generalObservationsSection(doc);
        doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
        doc.Paragraph(DiscrepanciesBody);
        doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
        doc.Paragraph("Final narrative paragraph.");
    }

    private static void AssertParsedAllFields(SpinParseResult result)
    {
        Assert.True(result.Status == SpinParseStatus.Parsed, "Expected Parsed. Issues: " + string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.True(result.ParsedAllFields);
        Assert.Equal(0, result.UnresolvedFieldCount);
        Assert.Empty(result.Issues);
    }

    private static void AssertIssue(
        SpinParseResult result,
        ParseIssueKind kind,
        ReportField? field = null)
    {
        var issue = result.Issues.FirstOrDefault(i =>
            i.Kind == kind && (field is null || i.Field == field));
        Assert.True(issue is not null, $"Expected issue {kind}{(field is null ? string.Empty : " for " + field)} but none found. Actual: {string.Join("; ", result.Issues.Select(i => i.Message))}");
    }

    // ---- Complete valid report ------------------------------------------------

    [Fact]
    public void CompleteValidReport_ParsesAllFields()
    {
        var path = _factory.Create("spin.docx", doc => BuildStandard(doc));

        var result = _parser.Parse(path);

        AssertParsedAllFields(result);
        Assert.Equal("319", result.Record.ReportNumber);
        Assert.Equal(new DateOnly(2026, 9, 11), result.Record.InspectionDate);
        Assert.Equal("Anthony", result.Record.InspectorFirstName);
        Assert.Equal(DescriptionBody, result.Record.DescriptionOfWork);
        Assert.Equal(DrawingBody, result.Record.DrawingReferences);
        Assert.Equal(ObservationsBody, result.Record.GeneralObservations);
        Assert.Equal(DiscrepanciesBody, result.Record.Discrepancies);
        Assert.Equal(CorrectionsBody, result.Record.PreviousDiscrepancyCorrections);
    }

    [Fact]
    public void TitleSplitAcrossRuns_IsReconstructed()
    {
        var path = _factory.Create("split-title.docx", doc =>
        {
            BuildStandard(doc);
        });

        var result = _parser.Parse(path);

        Assert.Equal("319", result.Record.ReportNumber);
    }

    // ---- Report number --------------------------------------------------------

    [Fact]
    public void MissingTitle_ReportNumberNeverTakenFromFileName()
    {
        var path = _factory.Create("SPIN Report 319.docx", doc =>
        {
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph(DescriptionBody);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.ReportNumber);
        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.ReportNumber);
    }

    [Fact]
    public void ConflictingReportNumberTitles_AreAmbiguous()
    {
        var path = _factory.Create("conflicting.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Paragraph("Special Inspection Report #317");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.ReportNumber);
        AssertIssue(result, ParseIssueKind.Ambiguous, ReportField.ReportNumber);
    }

    [Fact]
    public void NonNumericReportNumber_IsInvalidFormat()
    {
        var path = _factory.Create("non-numeric.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319-A");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.ReportNumber);
        AssertIssue(result, ParseIssueKind.InvalidFormat, ReportField.ReportNumber);
    }

    // ---- Inspection date --------------------------------------------------------

    [Theory]
    [InlineData("2026-09-11", 2026, 9, 11)]
    [InlineData("09/11/2026", 2026, 9, 11)]
    [InlineData("9/11/2026", 2026, 9, 11)]
    [InlineData("9/5/2026", 2026, 9, 5)]
    public void DateFormats_Parse(string value, int year, int month, int day)
    {
        var path = _factory.Create($"date-{value.Replace('/', '-')}.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", value],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph(DescriptionBody);
        });

        var result = _parser.Parse(path);

        Assert.Equal(new DateOnly(year, month, day), result.Record.InspectionDate);
    }

    [Fact]
    public void UnparsableDate_IsInvalidFormat()
    {
        var path = _factory.Create("bad-date.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "Sept 11, 2026"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.InspectionDate);
        AssertIssue(result, ParseIssueKind.InvalidFormat, ReportField.InspectionDate);
    }

    // ---- Inspector -------------------------------------------------------------

    [Fact]
    public void InspectorFirstName_IsFirstToken()
    {
        var path = _factory.Create("inspector.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst, P.E. (Cornerstone)"]);
        });

        var result = _parser.Parse(path);

        Assert.Equal("Anthony", result.Record.InspectorFirstName);
        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
    }

    [Fact]
    public void EmptyInspectorValue_IsMissing()
    {
        var path = _factory.Create("empty-inspector.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", ""]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.InspectorFirstName);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.InspectorFirstName);
    }

    // ---- Header label conflicts/missing ----------------------------------------

    [Fact]
    public void RepeatedDateLabel_IsAmbiguous()
    {
        var path = _factory.Create("dup-date.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Inspection Date:", "2026-09-11"],
                ["Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.InspectionDate);
        AssertIssue(result, ParseIssueKind.Ambiguous, ReportField.InspectionDate);
    }

    [Fact]
    public void MissingInspectionDateLabel_IsMissing()
    {
        var path = _factory.Create("no-date.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.InspectionDate);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.InspectionDate);
    }

    // ---- Body sections -----------------------------------------------------------

    [Fact]
    public void MissingSectionHeading_YieldsMissingAndUnresolved()
    {
        var path = _factory.Create("missing-section.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph(DescriptionBody);
            doc.Paragraph(ReportTemplateContract.DrawingReferencesHeading);
            doc.Paragraph(DrawingBody);
            // General observations section omitted entirely.
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph(DiscrepanciesBody);
            doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
            doc.Paragraph("Final narrative paragraph.");
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.GeneralObservations);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.GeneralObservations);
        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
        Assert.Equal(1, result.UnresolvedFieldCount);
    }

    [Fact]
    public void DuplicateSectionHeading_IsAmbiguous()
    {
        var path = _factory.Create("dup-section.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("First observations paragraph.");
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("Second observations paragraph.");
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.GeneralObservations);
        AssertIssue(result, ParseIssueKind.Ambiguous, ReportField.GeneralObservations);
    }

    [Fact]
    public void SectionHeadingWithNoContent_IsMissing()
    {
        var path = _factory.Create("empty-section.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph(DiscrepanciesBody);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.GeneralObservations);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.GeneralObservations);
    }

    [Fact]
    public void SectionHeadingOnlyBlankParagraphs_IsMissing()
    {
        var path = _factory.Create("blank-section.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("   ");
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph(DiscrepanciesBody);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.GeneralObservations);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.GeneralObservations);
    }

    [Fact]
    public void MultiParagraphSection_JoinsWithParagraphSeparator()
    {
        var path = _factory.Create("multi-paragraph.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph("First paragraph of description.");
            doc.Paragraph("Second paragraph of description.");
        });

        var result = _parser.Parse(path);

        Assert.Equal(
            "First paragraph of description." + ParagraphSeparator + "Second paragraph of description.",
            result.Record.DescriptionOfWork);
    }

    // ---- Final section boundaries ------------------------------------------------

    [Theory]
    [InlineData("To the best of my knowledge, the inspected work is as described.")]
    [InlineData("Submitted By:")]
    [InlineData("Reviewed By:")]
    [InlineData("PHOTO DOCUMENTATION")]
    public void FinalSection_StopsAtBoundaryMarker(string boundary)
    {
        var path = _factory.Create($"boundary-{boundary.GetHashCode():x}.docx", doc =>
        {
            BuildStandard(
                doc,
                correctionsBody: "Only the narrative before the marker belongs to this section.",
                finalBoundary: boundary);
        });

        var result = _parser.Parse(path);

        Assert.Equal("Only the narrative before the marker belongs to this section.", result.Record.PreviousDiscrepancyCorrections);
        AssertParsedAllFields(result);
    }

    // ---- Page breaks / narrative continuation --------------------------------------

    [Fact]
    public void NarrativeAcrossExplicitPageBreak_IsPreserved()
    {
        var path = _factory.Create("page-break.docx", doc =>
        {
            BuildWithCustomObservations(doc, spinDoc =>
                spinDoc.Paragraph(ReportTemplateContract.GeneralObservationsHeading)
                    .Paragraph("Paragraph before the page break.[PAGEBREAK]Continuation after the page break."));
        });

        var result = _parser.Parse(path);

        Assert.Equal(
            "Paragraph before the page break." + Environment.NewLine + "Continuation after the page break.",
            result.Record.GeneralObservations);
        AssertParsedAllFields(result);
    }

    // ---- N/A and typo preservation -------------------------------------------------

    [Fact]
    public void ExplicitNaValue_IsPreservedAsLiteralNa()
    {
        // Part of the standard report: DiscrepanciesBody == "N/A".
        var path = _factory.Create("na.docx", doc => BuildStandard(doc));

        var result = _parser.Parse(path);

        Assert.Equal("N/A", result.Record.Discrepancies);
        AssertParsedAllFields(result);
    }

    [Fact]
    public void MissingNarrativeValue_IsNotAutoSubstitutedWithNa()
    {
        var path = _factory.Create("no-na-substitute.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
        });

        var result = _parser.Parse(path);

        Assert.Null(result.Record.Discrepancies);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.Discrepancies);
        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
    }

    [Fact]
    public void NarrativeTypos_ArePreservedVerbatim()
    {
        var path = _factory.Create("typos.docx", doc => BuildStandard(doc));

        var result = _parser.Parse(path);

        Assert.Contains("distrubuted", result.Record.PreviousDiscrepancyCorrections);
        Assert.Contains("defecient", result.Record.PreviousDiscrepancyCorrections);
    }

    // ---- Heading matching flexibility ----------------------------------------------

    [Fact]
    public void HeadingWithOrWithoutTrailingColon_Matches()
    {
        var path = _factory.Create("no-colon.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading.TrimEnd(':'));
            doc.Paragraph(DescriptionBody);
        });

        var result = _parser.Parse(path);

        Assert.Equal(DescriptionBody, result.Record.DescriptionOfWork);
    }

    [Fact]
    public void HeadingMatching_IsCaseInsensitiveAndRunSplitTolerant()
    {
        var path = _factory.Create("case-heading.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph("GENERAL", " OBSERVATIONS", "/REMARKS:");
            doc.Paragraph("Observation text.");
        });

        var result = _parser.Parse(path);

        Assert.Equal("Observation text.", result.Record.GeneralObservations);
    }

    // ---- Table/flow isolation ---------------------------------------------------------

    [Fact]
    public void TableParagraphs_NeverLeakIntoBodySections()
    {
        var path = _factory.Create("table-isolation.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["General observations/remarks:", "General observations/remarks:", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph(DescriptionBody);
            doc.Paragraph(ReportTemplateContract.DrawingReferencesHeading);
            doc.Paragraph(DrawingBody);
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("Observation text.");
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph(DiscrepanciesBody);
            doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
            doc.Paragraph("Final narrative paragraph.");
        });

        var result = _parser.Parse(path);

        // The identical label text inside a table cell must NOT be treated as a
        // duplicate body-section heading, and must not contribute content.
        Assert.True(result.Status == SpinParseStatus.Parsed, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.Equal("Observation text.", result.Record.GeneralObservations);
    }

    // ---- Failure modes ---------------------------------------------------------------

    [Fact]
    public void NonexistentFile_FailsWithDocumentError()
    {
        var path = Path.Combine(_tempRoot, "missing.docx");

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.Failed, result.Status);
        AssertIssue(result, ParseIssueKind.DocumentError);
    }

    [Fact]
    public void WrongExtension_FailsWithDocumentError()
    {
        var path = _factory.CreateFile("not-a-spin.txt", "plain text"u8.ToArray());

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.Failed, result.Status);
        AssertIssue(result, ParseIssueKind.DocumentError);
    }

    [Fact]
    public void CorruptFakeDocx_FailsWithDocumentError()
    {
        var path = _factory.CreateFile("corrupt.docx", RandomNumberGenerator.GetBytes(2048));

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.Failed, result.Status);
        AssertIssue(result, ParseIssueKind.DocumentError);
    }

    [Fact]
    public void NullPath_FailsWithDocumentError()
    {
        var result = _parser.Parse(null!);

        Assert.Equal(SpinParseStatus.Failed, result.Status);
        AssertIssue(result, ParseIssueKind.DocumentError);
    }

    // ---- Read-only guarantee -----------------------------------------------------------

    [Fact]
    public void Parse_DoesNotModifySourceReport()
    {
        var path = _factory.Create("read-only.docx", doc => BuildStandard(doc));
        var before = SHA256.HashData(File.ReadAllBytes(path));

        _parser.Parse(path);
        _parser.Parse(path);

        var after = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Equal(before, after);
    }

    // ---- Unresolved counting --------------------------------------------------------------

    [Fact]
    public void UnresolvedFieldCount_MatchesNumberOfMissingFields()
    {
        var path = _factory.Create("count.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("Observation text.");
        });

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
        Assert.Equal(5, result.UnresolvedFieldCount);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.InspectionDate);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.DescriptionOfWork);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.DrawingReferences);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.Discrepancies);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.PreviousDiscrepancyCorrections);
    }

    [Fact]
    public void FullyUnresolvedRecord_StillLeavesFieldsNullForResolver()
    {
        var path = _factory.Create("empty-doc.docx", doc =>
        {
            doc.Paragraph("A report with no recognizable structure.");
        });

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
        Assert.Equal(8, result.UnresolvedFieldCount);
        Assert.NotNull(result.Record);
        Assert.True(result.Record.HasAnyUnresolved);
    }

    // ---- Manual resolution path (generated missing-section fixture) --------------

    [Fact]
    public void MissingDrawingSection_ResolutionBlankBecomesNaFallback()
    {
        var path = _factory.Create("missing-drawing.docx", doc =>
        {
            doc.Paragraph("Special Inspection Report #319");
            doc.Table(
                ["Project Name:", "X", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph("Description body text.");
            // Drawing references section omitted entirely.
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("Observation text.");
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph("N/A");
            doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
            doc.Paragraph("Corrections body text.");
        });

        var result = _parser.Parse(path);

        Assert.Equal(SpinParseStatus.ParsedWithUnresolvedFields, result.Status);
        Assert.Equal(1, result.UnresolvedFieldCount);
        Assert.Null(result.Record.DrawingReferences);
        AssertIssue(result, ParseIssueKind.Missing, ReportField.DrawingReferences);

        var edited = new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = result.Record.ReportNumber,
            [ReportField.InspectionDate] = result.Record.InspectionDate?.ToString("MM/dd/yyyy"),
            [ReportField.InspectorFirstName] = result.Record.InspectorFirstName,
            [ReportField.DescriptionOfWork] = result.Record.DescriptionOfWork,
            [ReportField.DrawingReferences] = string.Empty,
            [ReportField.GeneralObservations] = result.Record.GeneralObservations,
            [ReportField.Discrepancies] = result.Record.Discrepancies,
            [ReportField.PreviousDiscrepancyCorrections] = result.Record.PreviousDiscrepancyCorrections,
        };

        var resolver = new ReportRecordResolver(new ReportRecordValidator());
        var resolution = resolver.Resolve(result.Record, edited, result.Issues);

        Assert.True(resolution.IsApproved);
        Assert.Equal("N/A", resolution.ValidatedRecord!.DrawingReferences);
        Assert.Equal(new[] { ReportField.DrawingReferences }, resolution.NaFallbackFields);
        Assert.Empty(resolution.ManuallyEditedFields);
        Assert.Same(result.Issues, resolution.ParserIssues);
    }
}