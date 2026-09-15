using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class ReportLogWriterTests : IDisposable
{
    private const string EnDash = "\u2013";
    private const string EntryHeader = "Report #319 " + EnDash + " 09/11/26" + EnDash + " Anthony";

    private readonly string _tempRoot;
    private readonly ReportLogDocumentFactory _factory;
    private readonly ReportLogWriter _writer = new();

    public ReportLogWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-WriterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _factory = new ReportLogDocumentFactory(_tempRoot);
    }

    [Fact]
    public void AppendToEmptyLog_Succeeds_BackupCreated_NoPageBreak()
    {
        var log = _factory.CreateReportLog("log.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReportLogWriteErrorKind.None, result.ErrorKind);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        var inspection = WordDocumentInspector.Inspect(log);
        Assert.True(inspection.Openable);
        Assert.Contains(EntryHeader, inspection.ParagraphTexts);

        Assert.False(HasPageBreakBefore(appendedParagraphIndex(log, EntryHeader)),
            "The first entry in an empty log must not begin with a page break.");
    }

    [Fact]
    public void AppendSecondEntry_StartsOnNewPage()
    {
        var log = _factory.CreateReportLog("log.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        Assert.True(_writer.Append(log, entry).Success);

        var second = _writer.Append(log, entry);
        Assert.True(second.Success, second.Message);

        var indexes = HeaderIndexes(log, EntryHeader);
        Assert.Equal(2, indexes.Count);
        Assert.False(HasPageBreakBefore(indexes[0]));
        Assert.True(HasPageBreakBefore(indexes[1]));
    }

    [Fact]
    public void AfterAppend_ExistingContentAndNewEntryBothPresent_InOrder()
    {
        var log = _factory.CreateReportLog("log.docx", "Existing record A", "Existing record B");
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryDocument("entry.docx");
        var entryCount = WordDocumentInspector.Inspect(entry).ParagraphTexts.Count;

        var result = _writer.Append(log, entry);

        Assert.True(result.Success, result.Message);
        var paragraphs = WordDocumentInspector.Inspect(log).ParagraphTexts;

        Assert.Equal("Existing record A", paragraphs[0]);
        Assert.Equal("Existing record B", paragraphs[1]);
        Assert.Equal(EntryHeader, paragraphs[2]);
        Assert.Equal(2 + entryCount, paragraphs.Count);

        Assert.NotEqual(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void Backup_IsByteForByteIdenticalToPreWriteDestination()
    {
        var log = _factory.CreateReportLog("log.docx", "Existing record");
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.True(result.Success, result.Message);
        Assert.Equal(before, File.ReadAllBytes(result.BackupPath!));
        Assert.NotEqual(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void BackupName_FollowsDeterministicPattern()
    {
        var log = _factory.CreateReportLog("log.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.True(result.Success, result.Message);
        Assert.Matches(new Regex(@"^log\.backup-\d{8}-\d{9}\.docx$"), Path.GetFileName(result.BackupPath));
    }

    [Fact]
    public void Success_LeavesNoWorkingOrTempFiles()
    {
        var log = _factory.CreateReportLog("log.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        Assert.True(_writer.Append(log, entry).Success);

        var leftovers = Directory.GetFiles(_tempRoot)
            .Where(path => path.Contains(".working.docx"))
            .ToList();
        Assert.DoesNotContain(leftovers, path => path.Contains(".working.docx"));
    }

    [Fact]
    public void DestinationFinalSectionProperties_ExactlyOneAfterAppend()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var entry = _factory.CreateEntryDocument("entry.docx");

        Assert.True(_writer.Append(log, entry).Success);

        using var document = WordprocessingDocument.Open(log, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        Assert.Single(body.ChildElements.OfType<SectionProperties>());
    }

    [Fact]
    public void EntryNestedSectionProperties_AreStripped()
    {
        var log = _factory.CreateReportLog("log.docx");
        var entry = _factory.CreateEntryWithNestedSection("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.True(result.Success, result.Message);
        using var document = WordprocessingDocument.Open(log, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        Assert.Single(body.ChildElements.OfType<SectionProperties>());
        Assert.DoesNotContain(body.Descendants<SectionProperties>(),
            section => section.Parent is Paragraph);
    }

    [Fact]
    public void EntryWithPlaceholders_Rejected_DestinationUnchanged()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryWithPlaceholder("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.InvalidRenderedEntry, result.ErrorKind);
        Assert.Contains("placeholder", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void EntryWithRelationshipContent_Rejected_Safely()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryWithRelationshipReference("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.UnsupportedRenderedContent, result.ErrorKind);
        Assert.Equal(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void EntrySameAsDestination_Rejected()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var before = File.ReadAllBytes(log);

        var result = _writer.Append(log, log);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.InvalidRenderedEntry, result.ErrorKind);
        Assert.Equal(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void NonexistentDestination_InvalidDestination()
    {
        var log = Path.Combine(_tempRoot, "missing.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.InvalidDestination, result.ErrorKind);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void ZeroByteDestination_InvalidDestination_WithInitializationGuidance()
    {
        var log = _factory.CreateEmptyFile("log.docx");
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.InvalidDestination, result.ErrorKind);
        Assert.Contains("Initialize", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, new FileInfo(log).Length);
    }

    [Fact]
    public void NonWordTextFileDestination_Fails_Unchanged()
    {
        var log = _factory.CreateFile("log.docx", System.Text.Encoding.UTF8.GetBytes("definitely not a docx"));
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryDocument("entry.docx");

        var result = _writer.Append(log, entry);

        Assert.False(result.Success);
        Assert.Equal(ReportLogWriteErrorKind.DestinationInUse, result.ErrorKind);
        Assert.Equal(before, File.ReadAllBytes(log));
    }

    [Fact]
    public void LockedDestination_DestinationInUse_Unchanged()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var entry = _factory.CreateEntryDocument("entry.docx");

        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = _writer.Append(log, entry);

            Assert.False(result.Success);
            Assert.Equal(ReportLogWriteErrorKind.DestinationInUse, result.ErrorKind);
        }

        Assert.Contains("existing", WordDocumentInspector.Inspect(log).ParagraphTexts);
        Assert.Empty(WorkingFiles());
    }

    [Fact]
    public void ReadOnlyDestination_AppendFailed_Unchanged_NoWorkingFiles()
    {
        var log = _factory.CreateReportLog("log.docx", "existing");
        var before = File.ReadAllBytes(log);
        var entry = _factory.CreateEntryDocument("entry.docx");
        File.SetAttributes(log, FileAttributes.ReadOnly);

        try
        {
            var result = _writer.Append(log, entry);

            Assert.False(result.Success);
            Assert.Equal(ReportLogWriteErrorKind.AppendFailed, result.ErrorKind);
            Assert.Equal(before, File.ReadAllBytes(log));
            Assert.Empty(WorkingFiles());
        }
        finally
        {
            File.SetAttributes(log, FileAttributes.Normal);
        }
    }

    private bool HasPageBreakBefore(string logPath, int paragraphIndex)
    {
        using var document = WordprocessingDocument.Open(logPath, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraph = body.ChildElements.OfType<Paragraph>().ElementAt(paragraphIndex);
        return paragraph.ParagraphProperties?.Descendants<PageBreakBefore>().Any() == true;
    }

    private bool HasPageBreakBefore(int paragraphIndex) =>
        HasPageBreakBefore(Path.Combine(_tempRoot, "log.docx"), paragraphIndex);

    private static List<int> HeaderIndexes(string logPath, string headerText)
    {
        var paragraphs = WordDocumentInspector.Inspect(logPath).ParagraphTexts;
        return paragraphs
            .Select((text, index) => (text, index))
            .Where(item => item.text == headerText)
            .Select(item => item.index)
            .ToList();
    }

    private static int appendedParagraphIndex(string logPath, string headerText)
    {
        var indexes = HeaderIndexes(logPath, headerText);
        Assert.True(indexes.Count >= 1, "Expected at least one entry header in the report log.");
        return indexes[^1];
    }

    private string[] WorkingFiles() =>
        Directory.GetFiles(_tempRoot, "*.working.docx");

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_tempRoot))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort test cleanup.
        }
    }
}