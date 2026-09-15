using System.Security.Cryptography;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class ReportLogTemplateRendererTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ReportLogDocumentFactory _factory;
    private readonly ReportLogTemplateRenderer _renderer = new();

    public ReportLogTemplateRendererTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-RenderTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _factory = new ReportLogDocumentFactory(_tempRoot);
    }

    [Fact]
    public void SplitRunTemplate_AllFieldsRendered_HeaderAndSectionsMatch()
    {
        var template = _factory.CreateTemplate("template.docx");
        var output = Path.Combine(_tempRoot, "entry-split.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(output));
        Assert.Equal(ReportTemplateRenderErrorKind.None, result.ErrorKind);

        var inspection = WordDocumentInspector.Inspect(output);
        Assert.True(inspection.Openable);

        Assert.Equal(
            "Report #319 – 09/11/26– Anthony",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts));

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);

        Assert.Equal("CMF Structural Repairs inspection", sections[ReportTemplateContract.DescriptionOfWorkHeading]);
        Assert.Equal("Sheet S6", sections[ReportTemplateContract.DrawingReferencesHeading]);
        Assert.Equal(
            "General note: the site was safe and accessible.",
            sections[ReportTemplateContract.GeneralObservationsHeading]);
        Assert.Equal("N/A", sections[ReportTemplateContract.DiscrepanciesHeading]);
        Assert.Equal("No previous discrepancies.", sections[ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading]);
    }

    [Fact]
    public void SingleRunTemplate_AllFieldsRendered()
    {
        var template = _factory.CreateSingleRunTemplate("template-single.docx");
        var output = Path.Combine(_tempRoot, "entry-single.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.True(result.Success, result.Message);
        var inspection = WordDocumentInspector.Inspect(output);
        Assert.Equal("Report #319 – 09/11/26– Anthony",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts));
    }

    [Fact]
    public void IdenticalBodyPlaceholders_ResolvedByHeadingContext()
    {
        var template = _factory.CreateTemplate("template-context.docx");
        var output = Path.Combine(_tempRoot, "entry-context.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.True(result.Success, result.Message);
        var inspection = WordDocumentInspector.Inspect(output);
        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);

        foreach (var section in ReportTemplateContract.BodySections)
        {
            var value = section.Field switch
            {
                ReportField.DescriptionOfWork => "CMF Structural Repairs inspection",
                ReportField.DrawingReferences => "Sheet S6",
                ReportField.GeneralObservations => "General note: the site was safe and accessible.",
                ReportField.Discrepancies => "N/A",
                _ => "No previous discrepancies.",
            };

            Assert.Equal(value, sections[section.Heading]);
        }
    }

    [Fact]
    public void MultilineNarrative_BecomesRealWordParagraphs()
    {
        var template = _factory.CreateTemplate("template-multipara.docx");
        var single = _renderer.Render(template, ReportRecordFixture.Create(), Path.Combine(_tempRoot, "single.docx"));
        Assert.True(single.Success, single.Message);
        var singleParaCount = WordDocumentInspector.Inspect(single.OutputPath!).ParagraphTexts.Count;

        var record = ReportRecordFixture.Create(
            descriptionOfWork: "First paragraph of the description.\n\nSecond paragraph.\n\nThird paragraph.");

        var output = Path.Combine(_tempRoot, "multi.docx");
        var result = _renderer.Render(template, record, output);

        Assert.True(result.Success, result.Message);
        var inspection = WordDocumentInspector.Inspect(output);

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);
        Assert.Equal(
            "First paragraph of the description.\n\nSecond paragraph.\n\nThird paragraph.",
            sections[ReportTemplateContract.DescriptionOfWorkHeading]);
        Assert.Equal(singleParaCount + 2, inspection.ParagraphTexts.Count);
    }

    [Fact]
    public void SingleLineBreaks_StayInsideOneParagraph()
    {
        var template = _factory.CreateTemplate("template-linebreak.docx");
        var record = ReportRecordFixture.Create(drawingReferences: "Line 1 of drawings\nLine 2\nLine 3");

        var output = Path.Combine(_tempRoot, "linebreak.docx");
        Assert.True(_renderer.Render(template, record, output).Success);
        var inspection = WordDocumentInspector.Inspect(output);

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);
        Assert.Equal("Line 1 of drawings\nLine 2\nLine 3", sections[ReportTemplateContract.DrawingReferencesHeading]);
    }

    [Fact]
    public void SourceWording_PreservedVerbatim()
    {
        var template = _factory.CreateTemplate("template-verbatim.docx");
        var record = ReportRecordFixture.Create(
            descriptionOfWork: "Inspected distrubuted joints and noted a defecient connection at the base.");

        var output = Path.Combine(_tempRoot, "verbatim.docx");
        Assert.True(_renderer.Render(template, record, output).Success);
        var inspection = WordDocumentInspector.Inspect(output);

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);
        Assert.Equal(
            "Inspected distrubuted joints and noted a defecient connection at the base.",
            sections[ReportTemplateContract.DescriptionOfWorkHeading]);
    }

    [Fact]
    public void NaFallback_RendersLiteralNAValue()
    {
        var template = _factory.CreateTemplate("template-na.docx");
        var record = ReportRecordFixture.Create(discrepancies: "N/A");

        var output = Path.Combine(_tempRoot, "na.docx");
        Assert.True(_renderer.Render(template, record, output).Success);
        var inspection = WordDocumentInspector.Inspect(output);

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);
        Assert.Equal("N/A", sections[ReportTemplateContract.DiscrepanciesHeading]);
    }

    [Fact]
    public void NoRequiredPlaceholders_RemainAfterRender()
    {
        var template = _factory.CreateTemplate("template-clean.docx");
        var output = Path.Combine(_tempRoot, "clean.docx");

        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), output).Success);

        var remaining = WordDocumentInspector.RemainingPlaceholders(
            WordDocumentInspector.Inspect(output),
            AllPlaceholderTokens());
        Assert.Empty(remaining);
    }

    [Fact]
    public void TemplateFileBytes_AreNeverModified()
    {
        var template = _factory.CreateTemplate("template-sha.docx");
        var before = Sha256Of(template);

        var output = Path.Combine(_tempRoot, "sha.docx");
        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), output).Success);

        Assert.Equal(before, Sha256Of(template));
        Assert.NotEqual(before, Sha256Of(output));
    }

    [Fact]
    public void RenderedOutput_IsAValidOpenableWordDocument()
    {
        var template = _factory.CreateTemplate("template-openable.docx");
        var output = Path.Combine(_tempRoot, "openable.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.True(result.Success, result.Message);
        Assert.True(WordDocumentInspector.Inspect(output).Openable);
    }

    [Fact]
    public void OutputPathEqualTemplate_FailsSafely()
    {
        var template = _factory.CreateTemplate("template-same.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), template);

        Assert.False(result.Success);
        Assert.Equal(ReportTemplateRenderErrorKind.OutputPathInvalid, result.ErrorKind);
        Assert.True(File.Exists(template), "The template must not be disturbed by a rejected render.");
    }

    [Fact]
    public void InvalidOutputExtension_FailsSafely()
    {
        var template = _factory.CreateTemplate("template-ext.docx");
        var output = Path.Combine(_tempRoot, "entry.txt");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.False(result.Success);
        Assert.Equal(ReportTemplateRenderErrorKind.OutputPathInvalid, result.ErrorKind);
    }

    [Fact]
    public void CorruptTemplate_FailsSafely_NoOutputLeftBehind()
    {
        var template = _factory.CreateEmptyFile("template-corrupt.docx");
        var output = Path.Combine(_tempRoot, "corrupt.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.False(result.Success);
        Assert.Equal(ReportTemplateRenderErrorKind.InvalidTemplate, result.ErrorKind);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void MissingHeaderPlaceholders_FailsWithExpectedValuesMissing()
    {
        var template = _factory.CreateTemplateWithoutHeader("template-noheader.docx");
        var output = Path.Combine(_tempRoot, "noheader.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.False(result.Success);
        Assert.Equal(ReportTemplateRenderErrorKind.ExpectedValuesMissing, result.ErrorKind);
        Assert.False(File.Exists(output), "A failed render must not leave output behind.");
    }

    [Fact]
    public void RelationshipDependentTemplateContent_FailsSafely()
    {
        var template = _factory.CreateTemplateWithEmbeddedObject("template-embedded.docx");
        var output = Path.Combine(_tempRoot, "embedded.docx");

        var result = _renderer.Render(template, ReportRecordFixture.Create(), output);

        Assert.False(result.Success);
        Assert.Equal(ReportTemplateRenderErrorKind.UnsupportedContent, result.ErrorKind);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Date_RendersAsMMDDYY()
    {
        var template = _factory.CreateTemplate("template-date.docx");
        var record = ReportRecordFixture.Create(inspectionDate: new DateOnly(2026, 9, 11));

        var output = Path.Combine(_tempRoot, "date.docx");
        Assert.True(_renderer.Render(template, record, output).Success);

        var inspection = WordDocumentInspector.Inspect(output);
        Assert.Contains("09/11/26",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts) ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains("319",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts) ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains("Anthony",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenderedDocument_ContainsAllApprovedValues()
    {
        var template = _factory.CreateTemplate("template-values.docx");
        var record = ReportRecordFixture.Create();
        var output = Path.Combine(_tempRoot, "values.docx");

        Assert.True(_renderer.Render(template, record, output).Success);
        var inspection = WordDocumentInspector.Inspect(output);

        var expected = new[]
        {
            record.ReportNumber,
            record.InspectionDate.ToString("MM/dd/yy"),
            record.InspectorFirstName,
            record.DescriptionOfWork,
            record.DrawingReferences,
            record.GeneralObservations,
            record.Discrepancies,
            record.PreviousDiscrepancyCorrections,
        };

        Assert.Empty(WordDocumentInspector.MissingExpectedValues(inspection, expected));
    }

    private static IReadOnlyCollection<string> AllPlaceholderTokens()
    {
        var tokens = ReportTemplateContract.HeaderPlaceholders.Values.ToList();
        tokens.Add(ReportTemplateContract.BodyPlaceholder);
        return tokens;
    }

    private static string Sha256Of(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort test cleanup.
        }
    }
}