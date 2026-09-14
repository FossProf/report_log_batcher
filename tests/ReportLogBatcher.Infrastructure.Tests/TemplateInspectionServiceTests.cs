using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class TemplateInspectionServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TemplateInspectionService _service = new();

    public TemplateInspectionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_TemplateInspectionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string CreateTemplate(string[][] paragraphs, string fileName = "template.docx")
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new Body();
        foreach (var runs in paragraphs)
        {
            var paragraph = new Paragraph();
            foreach (var runText in runs)
            {
                paragraph.AppendChild(new Run(
                    new Text(runText) { Space = SpaceProcessingModeValues.Preserve }));
            }

            body.AppendChild(paragraph);
        }

        main.Document = new Document(body);
        main.Document.Save();
        return path;
    }

    private string CreateFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void AssertIssue(
        TemplateValidationResult result,
        TemplateIssueKind kind,
        ReportField? field = null,
        int? paragraphIndex = null)
    {
        var issues = result.Issues ?? [];
        var issue = issues.FirstOrDefault(i =>
            i.Kind == kind &&
            (field is null || i.Field == field));

        Assert.True(issue is not null, $"Expected issue {kind}{(field is null ? string.Empty : " for " + field)} but none found. Actual: {string.Join("; ", issues.Select(i => i.Message))}");
    }

    /// <summary>
    /// Each entry: a paragraph (array of run texts). Placeholders/headings that need
    /// splitting tests pass their own arrays; this is the production-shaped baseline.
    /// </summary>
    private static string[][] BuildBaseline(
        bool splitBodyPlaceholder = false,
        bool splitHeader = true)
    {
        var paragraphs = new List<string[]>
        {
            splitHeader
                ? new[] { "Report #", "{", "report.number", "}", " – ", "{", "report.date", " mm/dd/", "yy", "}", "– ", "{", "report.inspector.first_name", "}" }
                : new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
            new[] { ReportTemplateContract.DescriptionOfWorkHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DrawingReferencesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.GeneralObservationsHeading },
            splitBodyPlaceholder
                ? new[] { "{body of text under the ", "same ", "header", "}" }
                : new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DiscrepanciesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
        };

        return paragraphs.ToArray();
    }

    [Fact]
    public void CompleteValidTemplate_Passes()
    {
        var path = CreateTemplate(BuildBaseline(splitBodyPlaceholder: true));

        var result = _service.Inspect(path);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Issues);
        Assert.Empty(result.Issues);
        Assert.Equal(TemplateValidationFailure.None, result.Failure);
    }

    [Fact]
    public void MissingReportNumberPlaceholder_Fails()
    {
        var baseline = BuildBaseline();
        baseline[0] = new[] { "Report # 005 – {report.date mm/dd/yy}– {report.inspector.first_name}" };
        var path = CreateTemplate(baseline);

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MissingPlaceholder, ReportField.ReportNumber);
    }

    [Fact]
    public void MissingDatePlaceholder_Fails()
    {
        var baseline = BuildBaseline();
        baseline[0] = new[] { "Report #{report.number} – 09/14/26– {report.inspector.first_name}" };
        var path = CreateTemplate(baseline);

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MissingPlaceholder, ReportField.InspectionDate);
    }

    [Fact]
    public void MissingInspectorPlaceholder_Fails()
    {
        var baseline = BuildBaseline();
        baseline[0] = new[] { "Report #{report.number} – {report.date mm/dd/yy}– A. Wintergerst" };
        var path = CreateTemplate(baseline);

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MissingPlaceholder, ReportField.InspectorFirstName);
    }

    [Theory]
    [InlineData(0, "Description and location(s) of work inspected:")]
    [InlineData(1, "Drawing sheets and sections related to this work:")]
    [InlineData(2, "General observations/remarks:")]
    [InlineData(3, "Discrepancies and direction given:")]
    [InlineData(4, "Observations/Remarks on correction of discrepancies noted in previous inspections:")]
    public void MissingBodySectionPlaceholder_Fails(int sectionIndex, string expectedHeading)
    {
        Assert.Equal(expectedHeading, ReportTemplateContract.BodySections[sectionIndex].Heading);
        var paragraphs = new List<string[]>
        {
            new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
        };

        for (var i = 0; i < ReportTemplateContract.BodySections.Count; i++)
        {
            var section = ReportTemplateContract.BodySections[i];
            paragraphs.Add(new[] { section.Heading });
            if (i != sectionIndex)
                paragraphs.Add(new[] { ReportTemplateContract.BodyPlaceholder });
        }

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MissingPlaceholder, ReportTemplateContract.BodySections[sectionIndex].Field);
    }

    [Fact]
    public void DuplicateHeaderPlaceholder_Fails()
    {
        var baseline = BuildBaseline();
        var extra = new List<string[]> { new[] { "Again: {report.number}" } };
        var paragraphs = baseline.Concat(extra).ToArray();
        var path = CreateTemplate(paragraphs);

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.DuplicatePlaceholder, ReportField.ReportNumber);
    }

    [Fact]
    public void DuplicateBodyPlaceholderInSection_Fails()
    {
        var baseline = BuildBaseline();
        var paragraphs = new List<string[]> { baseline[0] };
        var first = true;
        foreach (var paragraph in baseline.Skip(1))
        {
            paragraphs.Add(paragraph);
            if (first)
                paragraphs.Add(new[] { ReportTemplateContract.BodyPlaceholder });
            first = false;
        }

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.DuplicatePlaceholder, ReportField.DescriptionOfWork);
    }

    [Fact]
    public void BodyPlaceholders_AreAssociatedWithCorrectHeadings()
    {
        // Section 2 (drawing) placeholders is moved to sit under section 1's heading:
        // H1 => [PH, PH], H2 => nothing. The duplicate is attributed to DescriptionOfWork
        // and the missing one to DrawingReferences.
        var paragraphs = new List<string[]>
        {
            new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
            new[] { ReportTemplateContract.DescriptionOfWorkHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DrawingReferencesHeading },
            new[] { ReportTemplateContract.GeneralObservationsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DiscrepanciesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
        };

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.DuplicatePlaceholder, ReportField.DescriptionOfWork);
        AssertIssue(result, TemplateIssueKind.MissingPlaceholder, ReportField.DrawingReferences);
    }

    [Fact]
    public void BodyPlaceholderBeforeAnyHeading_IsMisplaced()
    {
        var paragraphs = new List<string[]>
        {
            new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
            new[] { ReportTemplateContract.BodyPlaceholder },
        };
        paragraphs.AddRange(BuildBaseline(splitHeader: false).Skip(1));

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MisplacedPlaceholder);
    }

    [Fact]
    public void IncorrectSectionOrder_Fails()
    {
        // Swap the drawing and general-observation section blocks.
        var paragraphs = new List<string[]>
        {
            new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
            new[] { ReportTemplateContract.DescriptionOfWorkHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.GeneralObservationsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DrawingReferencesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DiscrepanciesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
        };

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.SectionOrderViolation);
    }

    [Fact]
    public void MissingSectionHeading_Fails()
    {
        var paragraphs = new List<string[]>
        {
            new[] { "Report # {report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}" },
            new[] { ReportTemplateContract.DescriptionOfWorkHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DrawingReferencesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.DiscrepanciesHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
            new[] { ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading },
            new[] { ReportTemplateContract.BodyPlaceholder },
        };

        var path = CreateTemplate(paragraphs.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        AssertIssue(result, TemplateIssueKind.MissingSectionHeading, ReportField.GeneralObservations);
    }

    [Fact]
    public void PlaceholderSplitAcrossMultipleRuns_StillValidates()
    {
        // The production template genuinely splits these placeholders across runs.
        var path = CreateTemplate(BuildBaseline(splitBodyPlaceholder: true, splitHeader: true));

        var result = _service.Inspect(path);

        Assert.True(result.IsValid, string.Join("; ", (result.Issues ?? []).Select(i => i.Message)));
    }

    [Fact]
    public void HeadingSplitAcrossMultipleRuns_StillValidates()
    {
        var baseline = BuildBaseline(splitHeader: true);
        baseline[3] = new[] { "Drawing sheets and ", "sections related ", "to this work:" };
        var path = CreateTemplate(baseline);

        var result = _service.Inspect(path);

        Assert.True(result.IsValid, string.Join("; ", (result.Issues ?? []).Select(i => i.Message)));
    }

    [Fact]
    public void NonexistentTemplate_FailsPredictably()
    {
        var path = Path.Combine(_tempRoot, "missing.docx");

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        Assert.Equal(TemplateValidationFailure.FileNotFound, result.Failure);
    }

    [Fact]
    public void WrongExtension_FailsPredictably()
    {
        var path = CreateFile("not-a-template.txt", "plain text"u8.ToArray());

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        Assert.Equal(TemplateValidationFailure.InvalidExtension, result.Failure);
    }

    [Fact]
    public void CorruptFakeDocx_FailsPredictably()
    {
        var path = CreateFile("corrupt.docx", RandomNumberGenerator.GetBytes(2048));

        var result = _service.Inspect(path);

        Assert.False(result.IsValid);
        Assert.Equal(TemplateValidationFailure.CannotOpen, result.Failure);
    }

    [Fact]
    public void Inspection_DoesNotModifySourceTemplate()
    {
        var path = CreateTemplate(BuildBaseline(splitBodyPlaceholder: true));
        var before = SHA256.HashData(File.ReadAllBytes(path));

        _service.Inspect(path);

        var after = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Equal(before, after);
    }
}