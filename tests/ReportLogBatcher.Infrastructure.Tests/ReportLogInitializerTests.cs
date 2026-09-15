using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class ReportLogInitializerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ReportLogDocumentFactory _factory;
    private readonly ReportLogInitializer _initializer = new();

    public ReportLogInitializerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-InitTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _factory = new ReportLogDocumentFactory(_tempRoot);
    }

    [Fact]
    public void InitializeCreatesEmptyDocument_NothingButOneParagraphAndSectPr()
    {
        var template = _factory.CreateTemplate("template.docx");
        var destination = Path.Combine(_tempRoot, "log.docx");
        var templateHash = Sha256Of(template);

        var result = _initializer.Initialize(template, destination);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReportLogInitializationErrorKind.None, result.ErrorKind);
        Assert.Equal(destination, result.DestinationPath);

        var inspection = WordDocumentInspector.Inspect(destination);
        Assert.True(inspection.Openable);

        var tokens = ReportTemplateContract.HeaderPlaceholders.Values.Concat(
            new[] { ReportTemplateContract.BodyPlaceholder }).ToList();
        Assert.Empty(WordDocumentInspector.RemainingPlaceholders(inspection, tokens));
        Assert.Single(inspection.ParagraphTexts);
        Assert.Equal(string.Empty, inspection.ParagraphTexts[0]);

        using (var document = WordprocessingDocument.Open(destination, false))
        {
            var body = document.MainDocumentPart!.Document!.Body!;
            Assert.Single(body.ChildElements.OfType<SectionProperties>());
            Assert.Empty(body.ChildElements.OfType<Paragraph>().Skip(1));
        }

        Assert.Equal(templateHash, Sha256Of(template));
    }

    [Fact]
    public void ZeroByteDestination_InitializesSuccessfully()
    {
        var template = _factory.CreateTemplate("template.docx");
        var destination = _factory.CreateEmptyFile("log.docx");

        var result = _initializer.Initialize(template, destination);

        Assert.True(result.Success, result.Message);
        Assert.True(WordDocumentInspector.Inspect(destination).Openable);
    }

    [Fact]
    public void NonexistentDestination_InitializesSuccessfully()
    {
        var template = _factory.CreateTemplate("template.docx");
        var destination = Path.Combine(_tempRoot, "brand-new-log.docx");

        var result = _initializer.Initialize(template, destination);

        Assert.True(result.Success, result.Message);
        Assert.True(WordDocumentInspector.Inspect(destination).Openable);
    }

    [Fact]
    public void AlreadyValidDestination_Refused_Unchanged()
    {
        var template = _factory.CreateTemplate("template.docx");
        var destination = _factory.CreateReportLog("log.docx", "existing record");
        var before = File.ReadAllBytes(destination);

        var result = _initializer.Initialize(template, destination);

        Assert.False(result.Success);
        Assert.Equal(ReportLogInitializationErrorKind.AlreadyValidDocument, result.ErrorKind);
        Assert.Equal(before, File.ReadAllBytes(destination));
    }

    [Fact]
    public void TemplateCannotOverwriteItself_Fails()
    {
        var template = _factory.CreateTemplate("template.docx");
        var before = File.ReadAllBytes(template);

        var result = _initializer.Initialize(template, template);

        Assert.False(result.Success);
        Assert.Equal(ReportLogInitializationErrorKind.InvalidTemplate, result.ErrorKind);
        Assert.Equal(before, File.ReadAllBytes(template));
    }

    [Fact]
    public void MissingTemplate_Fails()
    {
        var destination = Path.Combine(_tempRoot, "log.docx");

        var result = _initializer.Initialize(
            Path.Combine(_tempRoot, "does-not-exist.docx"),
            destination);

        Assert.False(result.Success);
        Assert.Equal(ReportLogInitializationErrorKind.InvalidTemplate, result.ErrorKind);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void BlankDestination_Fails()
    {
        var template = _factory.CreateTemplate("template.docx");

        var result = _initializer.Initialize(template, string.Empty);

        Assert.False(result.Success);
        Assert.Equal(ReportLogInitializationErrorKind.DestinationUnavailable, result.ErrorKind);
    }

    [Fact]
    public void NonDocxDestination_Fails()
    {
        var template = _factory.CreateTemplate("template.docx");

        var result = _initializer.Initialize(template, Path.Combine(_tempRoot, "log.txt"));

        Assert.False(result.Success);
        Assert.Equal(ReportLogInitializationErrorKind.DestinationUnavailable, result.ErrorKind);
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