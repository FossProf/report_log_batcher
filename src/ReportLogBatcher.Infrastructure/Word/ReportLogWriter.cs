using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Transactionally appends one rendered entry to the report log.
///
/// The selected report log is never modified in place. A unique working copy is
/// produced, validated, and only then is the report log replaced — after a
/// byte-for-byte backup has been created in the same directory. The report log
/// receives ONLY the entry's template-structured text; no metadata or audit
/// content is ever written into it.
///
/// Success is reported only after the replacement is reopened and validated. If
/// that final validation fails, the original bytes are restored from the backup.
/// </summary>
public sealed class ReportLogWriter : IReportLogWriter
{
    private static readonly string[] RequiredPlaceholderTokens = RequiredPlaceholderTokensFactory();

    public ReportLogWriteResult Append(string reportLogPath, string renderedEntryPath)
    {
        try
        {
            return AppendCore(reportLogPath, renderedEntryPath);
        }
        catch (Exception ex)
        {
            return new ReportLogWriteResult(
                false,
                reportLogPath,
                null,
                ReportLogWriteErrorKind.AppendFailed,
                "The report log could not be updated. " + ex.Message);
        }
    }

    private ReportLogWriteResult AppendCore(string reportLogPath, string renderedEntryPath)
    {
        if (string.IsNullOrWhiteSpace(reportLogPath))
            return Fail(null, null, ReportLogWriteErrorKind.InvalidDestination,
                "A report-log destination path is required.");

        var destinationValidation = PathValidationService.ValidateReportLog(reportLogPath);
        if (!destinationValidation.IsValid)
            return Fail(null, null, ReportLogWriteErrorKind.InvalidDestination,
                destinationValidation.ErrorMessage ?? "The report log is not available.");

        if (string.IsNullOrWhiteSpace(renderedEntryPath))
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidRenderedEntry,
                "A rendered entry path is required.");

        var entryValidation = PathValidationService.ValidateSpinReport(renderedEntryPath);
        if (!entryValidation.IsValid)
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidRenderedEntry,
                entryValidation.ErrorMessage ?? "The rendered entry is not available or is not a .docx file.");

        if (PathsEqual(reportLogPath, renderedEntryPath))
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidRenderedEntry,
"The rendered entry must not be the report log itself.");

        var destinationInspection = WordDocumentInspector.Inspect(reportLogPath);
        if (new FileInfo(reportLogPath).Length == 0)
            return Fail(null, null, ReportLogWriteErrorKind.InvalidDestination,
                $"The report log '{Path.GetFileName(reportLogPath)}' is empty or is not a valid Word document. " +
                "Initialize a new report log from the template, or select a valid report log.");

        if (!destinationInspection.Openable)
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.DestinationInUse,
                "The report log could not be opened for reading; it may be open or locked by another application.");

        var entryInspection = WordDocumentInspector.Inspect(renderedEntryPath);
        if (!entryInspection.Openable || entryInspection.ParagraphTexts.Count == 0)
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidRenderedEntry,
                "The rendered entry is not a valid Word document.");

        var remaining = WordDocumentInspector.RemainingPlaceholders(entryInspection, RequiredPlaceholderTokens);
        if (remaining.Count > 0)
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidRenderedEntry,
                "The rendered entry still contains placeholders: " + string.Join(", ", remaining));

        if (entryInspection.UnsupportedContentDescription is not null)
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.UnsupportedRenderedContent,
                entryInspection.UnsupportedContentDescription);

        var hasPriorContent = destinationInspection.ParagraphTexts.Any(text => !string.IsNullOrWhiteSpace(text));
        var destinationParagraphCount = destinationInspection.ParagraphTexts.Count;

        var directory = Path.GetDirectoryName(reportLogPath);
        if (string.IsNullOrEmpty(directory))
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.InvalidDestination,
                "The report log directory could not be determined.");
        directory = Path.GetFullPath(directory);

        var stem = Path.GetFileNameWithoutExtension(reportLogPath);
        var workingPath = Path.Combine(directory, $"{stem}.{Guid.NewGuid():N}.working.docx");

int entryParagraphCount;
        try
        {
            File.Copy(reportLogPath, workingPath);
            File.SetAttributes(workingPath, FileAttributes.Normal);
            entryParagraphCount = AppendToWorkingCopy(workingPath, renderedEntryPath, hasPriorContent);
        }
        catch (IOException ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.DestinationInUse,
                "The report log may be open or in use and could not be read. " + ex.Message);
        }
        catch (Exception ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.AppendFailed,
                "The report log could not be prepared for update. " + ex.Message);
        }

        var workingInspection = WordDocumentInspector.Inspect(workingPath);
        if (!workingInspection.Openable
            || workingInspection.ParagraphTexts.Count != destinationParagraphCount + entryParagraphCount)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.AppendFailed,
                "The staged report-log update failed to validate.");
        }

        var remainingInWorking = WordDocumentInspector.RemainingPlaceholders(workingInspection, RequiredPlaceholderTokens);
        if (remainingInWorking.Count > 0)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.AppendFailed,
                "The staged report-log update still contains placeholders: " + string.Join(", ", remainingInWorking));
        }

        var backupPath = UniqueBackupPath(directory, stem);
        string? createdBackup = null;
        try
        {
            File.Copy(reportLogPath, backupPath);
            createdBackup = backupPath;
        }
        catch (IOException ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.DestinationInUse,
                "The report log may be open or in use and could not be read for backup. " + ex.Message);
        }
        catch (Exception ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, null, ReportLogWriteErrorKind.BackupFailed,
                "A backup of the report log could not be created, so the report log was NOT modified. " + ex.Message);
        }

        try
        {
            File.Move(workingPath, reportLogPath, overwrite: true);
        }
        catch (IOException ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, createdBackup, ReportLogWriteErrorKind.DestinationInUse,
                "The report log may be open or in use and could not be replaced. " + ex.Message);
        }
        catch (Exception ex)
        {
            Cleanup(workingPath);
            return Fail(reportLogPath, createdBackup, ReportLogWriteErrorKind.AppendFailed,
                "The report log could not be replaced with the updated version. " + ex.Message);
        }

        var finalInspection = WordDocumentInspector.Inspect(reportLogPath);
        if (!finalInspection.Openable
            || finalInspection.ParagraphTexts.Count != destinationParagraphCount + entryParagraphCount
            || WordDocumentInspector.RemainingPlaceholders(finalInspection, RequiredPlaceholderTokens).Count > 0)
        {
            TryRestoreFromBackup(backupPath, reportLogPath);
            return Fail(reportLogPath, createdBackup, ReportLogWriteErrorKind.FinalValidationFailed,
                "The report log was updated but the final validation failed; the original was restored from the backup.");
        }

        return new ReportLogWriteResult(true, reportLogPath, createdBackup, ReportLogWriteErrorKind.None, null);
    }

    /// <summary>
    /// Appends the rendered entry's body content to the working copy, preserving the
    /// destination's final body-level section properties. Returns the number of
    /// entry paragraphs appended.
    /// </summary>
    private static int AppendToWorkingCopy(string workingPath, string renderedEntryPath, bool hasPriorContent)
    {
        using var entry = WordprocessingDocument.Open(renderedEntryPath, false);
        using var working = WordprocessingDocument.Open(workingPath, true);

        var entryBody = entry.MainDocumentPart!.Document!.Body!;
        var workingBody = working.MainDocumentPart!.Document!.Body!;

        var entryParagraphCount = entryBody.ChildElements.OfType<Paragraph>().Count();
        if (entryParagraphCount == 0)
            throw new InvalidDataException("The rendered entry has no paragraphs.");

        var childrenToAppend = entryBody.ChildElements
            .Where(child => child is not SectionProperties)
            .Select(child => (OpenXmlElement)child.CloneNode(true))
            .ToList();

        foreach (var child in childrenToAppend)
            foreach (var nestedSection in child.Descendants<SectionProperties>().ToList())
                nestedSection.Remove();

if (hasPriorContent)
        {
            var firstTextParagraph = childrenToAppend
                .OfType<Paragraph>()
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(ParagraphText.Reconstruct(p, "\n")));

            var pageBreakTarget = firstTextParagraph
                ?? childrenToAppend.OfType<Paragraph>().FirstOrDefault();

            if (pageBreakTarget is not null)
                EnsurePageBreakBefore(pageBreakTarget);
        }

        var finalSection = workingBody.ChildElements.OfType<SectionProperties>().LastOrDefault();
        foreach (var child in childrenToAppend)
        {
            if (finalSection is null)
                workingBody.Append(child);
            else
                workingBody.InsertBefore(child, finalSection);
        }

        working.Save();
        return entryParagraphCount;
    }

    /// <summary>
    /// Deterministic same-directory backup pattern:
    /// <c>Name.backup-YYYYMMDD-HHmmssfff.docx</c>. Existing names are never
    /// overwritten; a collision suffix is appended until a unique name is found.
    /// </summary>
    private static string UniqueBackupPath(string directory, string stem)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var attempt = 0;
        while (true)
        {
            var candidate = Path.Combine(
                directory,
                $"{stem}.backup-{stamp}{(attempt == 0 ? string.Empty : "-" + attempt)}.docx");
            if (!File.Exists(candidate))
                return candidate;
            attempt++;
        }
    }

    private static void EnsurePageBreakBefore(Paragraph paragraph)
    {
        var paragraphProperties = paragraph.ParagraphProperties
            ?? paragraph.AppendChild(new ParagraphProperties());

        if (paragraphProperties.Descendants<PageBreakBefore>().Any())
            return;

        paragraphProperties.InsertAt(new PageBreakBefore(), 0);
    }

    private static void TryRestoreFromBackup(string backupPath, string reportLogPath)
    {
        try
        {
            File.Move(backupPath, reportLogPath, overwrite: true);
        }
        catch (Exception)
        {
            // Recovery is best effort; the structured failure is reported to the caller.
        }
    }

private static void Cleanup(string workingPath)
    {
        try
        {
            if (File.Exists(workingPath))
            {
                File.SetAttributes(workingPath, FileAttributes.Normal);
                File.Delete(workingPath);
            }
        }
        catch (Exception)
        {
            // Cleanup is best effort; the structured failure is reported to the caller.
        }
    }

    private static ReportLogWriteResult Fail(
        string? destinationPath,
        string? backupPath,
        ReportLogWriteErrorKind errorKind,
        string message) =>
        new(false, destinationPath, backupPath, errorKind, message);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string[] RequiredPlaceholderTokensFactory()
    {
        var tokens = ReportTemplateContract.HeaderPlaceholders.Values.ToList();
        tokens.Add(ReportTemplateContract.BodyPlaceholder);
        return tokens.ToArray();
    }
}
