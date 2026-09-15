using System.Security.Cryptography;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class ReportLogIntegrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ReportLogDocumentFactory _factory;
    private readonly ReportLogTemplateRenderer _renderer = new();
    private readonly ReportLogInitializer _initializer = new();
    private readonly ReportLogWriter _writer = new();

    public ReportLogIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-IntegrationTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _factory = new ReportLogDocumentFactory(_tempRoot);
    }

    [Fact]
    public void FullPipeline_Render_Append_Reopen_AllValuesPresent()
    {
        var template = _factory.CreateTemplate("template.docx");
        var log = _factory.CreateReportLog("log.docx");
        var record = ReportRecordFixture.Create();
        var rendered = Path.Combine(_tempRoot, "rendered-entry.docx");

        var render = _renderer.Render(template, record, rendered);
        Assert.True(render.Success, render.Message);

        var append = _writer.Append(log, rendered);
        Assert.True(append.Success, append.Message);
        Assert.NotNull(append.BackupPath);
        Assert.True(File.Exists(append.BackupPath));

        var inspection = WordDocumentInspector.Inspect(log);
        Assert.True(inspection.Openable);

        Assert.Equal("Report #319 – 09/11/26– Anthony",
            RenderedDocTestHelper.FindHeader(inspection.ParagraphTexts));

        var sections = RenderedDocTestHelper.ReadSections(inspection.ParagraphTexts);
        Assert.Equal(record.DescriptionOfWork, sections[ReportTemplateContract.DescriptionOfWorkHeading]);
        Assert.Equal(record.DrawingReferences, sections[ReportTemplateContract.DrawingReferencesHeading]);
        Assert.Equal(record.GeneralObservations, sections[ReportTemplateContract.GeneralObservationsHeading]);
        Assert.Equal(record.Discrepancies, sections[ReportTemplateContract.DiscrepanciesHeading]);
        Assert.Equal(
            record.PreviousDiscrepancyCorrections,
            sections[ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading]);

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

    [Fact]
    public void FullPipeline_TwoEntries_BothPresent_SecondStartsNewPage()
    {
        var template = _factory.CreateTemplate("template.docx");
        var log = _factory.CreateReportLog("log.docx");
        var rendered = Path.Combine(_tempRoot, "rendered-entry.docx");

        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), rendered).Success);
        Assert.True(_writer.Append(log, rendered).Success);
        Assert.True(_writer.Append(log, rendered).Success, "Appending a second entry must succeed.");

        var header = "Report #319 – 09/11/26– Anthony";
        var paragraphs = WordDocumentInspector.Inspect(log).ParagraphTexts;
        var headerIndexes = paragraphs
            .Select((text, index) => (text, index))
            .Where(item => item.text == header)
            .Select(item => item.index)
            .ToList();

        Assert.Equal(2, headerIndexes.Count);
        Assert.False(HasPageBreakBefore(log, headerIndexes[0]));
        Assert.True(HasPageBreakBefore(log, headerIndexes[1]));
    }

    [Fact]
    public void Initialize_ThenFirstAppend_StartsAtTop_NoPageBreak()
    {
        var template = _factory.CreateTemplate("template.docx");
        var log = Path.Combine(_tempRoot, "fresh-log.docx");
        var rendered = Path.Combine(_tempRoot, "rendered-entry.docx");

        var init = _initializer.Initialize(template, log);
        Assert.True(init.Success, init.Message);

        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), rendered).Success);
        var append = _writer.Append(log, rendered);
        Assert.True(append.Success, append.Message);

        var headerIndex = SingleHeaderIndex(log);
        Assert.False(HasPageBreakBefore(log, headerIndex),
            "The first entry in an initialized log must not begin with a page break.");

        Assert.Single(SectionPropertiesCount(log));
    }

    [Fact]
    public void EndToEnd_TemplateAndRenderedEntryUnchanged_OnlyLogChanged()
    {
        var template = _factory.CreateTemplate("template.docx");
        var log = _factory.CreateReportLog("log.docx", "existing record");
        var rendered = Path.Combine(_tempRoot, "rendered-entry.docx");

        var templateHash = Sha256Of(template);
        var logHash = Sha256Of(log);

        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), rendered).Success);
        var renderedHash = Sha256Of(rendered);
        Assert.True(_writer.Append(log, rendered).Success);

        Assert.Equal(templateHash, Sha256Of(template));
        Assert.Equal(renderedHash, Sha256Of(rendered));
        Assert.NotEqual(logHash, Sha256Of(log));
        Assert.Contains("existing record", WordDocumentInspector.Inspect(log).ParagraphTexts);
        Assert.Contains("Report #319", WordDocumentInspector.Inspect(log).ParagraphTexts
            .First(text => text.StartsWith("Report #319", StringComparison.Ordinal)));
    }

    [Fact]
    public void AppendToInitializedLog_PreservesTemplateFormatting()
    {
        var template = _factory.CreateTemplate("template.docx");
        var log = Path.Combine(_tempRoot, "fresh-log.docx");
        var rendered = Path.Combine(_tempRoot, "rendered-entry.docx");

        Assert.True(_initializer.Initialize(template, log).Success);
        Assert.True(_renderer.Render(template, ReportRecordFixture.Create(), rendered).Success);
        Assert.True(_writer.Append(log, rendered).Success);

        var inspection = WordDocumentInspector.Inspect(log);
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(log, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.ChildElements
            .OfType<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .ToList();

        foreach (var section in ReportTemplateContract.BodySections)
        {
            var index = inspection.ParagraphTexts.ToList().IndexOf(section.Heading);
            Assert.True(index >= 0, $"Heading '{section.Heading}' must be present after append.");

            var heading = paragraphs[index];
            Assert.True(heading.Descendants<DocumentFormat.OpenXml.Wordprocessing.Bold>().Any(),
                $"Formatting (bold) for '{section.Heading}' must survive render and append.");
        }
    }

    private static int SingleHeaderIndex(string logPath)
    {
        var paragraphs = WordDocumentInspector.Inspect(logPath).ParagraphTexts;
        var indexes = paragraphs
            .Select((text, index) => (text, index))
            .Where(item => item.text.StartsWith("Report #319 ", StringComparison.Ordinal))
            .Select(item => item.index)
            .ToList();
        Assert.Single(indexes);
        return indexes[0];
    }

    private static bool HasPageBreakBefore(string logPath, int paragraphIndex)
    {
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(logPath, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraph = body.ChildElements
            .OfType<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .ElementAt(paragraphIndex);
        return paragraph.ParagraphProperties
            ?.Descendants<DocumentFormat.OpenXml.Wordprocessing.PageBreakBefore>().Any() == true;
    }

    private static IEnumerable<int> SectionPropertiesCount(string logPath)
    {
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(logPath, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        foreach (var section in body.ChildElements.OfType<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>())
            yield return 1;
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