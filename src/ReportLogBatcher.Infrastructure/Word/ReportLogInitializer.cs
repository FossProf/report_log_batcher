using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Creates a fresh, empty report log from the report-log template when the user
/// explicitly chooses to initialize one. The copied package keeps the template's
/// styles/section properties but every paragraph is removed, leaving an empty
/// document. The template file itself is never modified.
/// </summary>
public sealed class ReportLogInitializer : IReportLogInitializer
{
    public ReportLogInitializationResult Initialize(string templatePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Fail(ReportLogInitializationErrorKind.DestinationUnavailable,
                "A report-log destination path is required.");

        var templateValidation = PathValidationService.ValidateReportLogTemplate(templatePath);
        if (!templateValidation.IsValid)
            return Fail(ReportLogInitializationErrorKind.InvalidTemplate,
                templateValidation.ErrorMessage ?? "The report-log template is not available.");

        if (!string.Equals(Path.GetExtension(destinationPath), ".docx", StringComparison.OrdinalIgnoreCase))
            return Fail(ReportLogInitializationErrorKind.DestinationUnavailable,
                "The report log must be a .docx file.");

        if (PathsEqual(templatePath, destinationPath))
            return Fail(ReportLogInitializationErrorKind.InvalidTemplate,
                "A new report log cannot be created in place of the template.");

        if (File.Exists(destinationPath) && WordDocumentInspector.Inspect(destinationPath).Openable)
            return Fail(ReportLogInitializationErrorKind.AlreadyValidDocument,
                "The report log is already a valid Word document and will not be overwritten.");

        try
        {
            byte[] templateBytes;
            try
            {
                templateBytes = File.ReadAllBytes(templatePath);
            }
            catch (Exception ex)
            {
                return Fail(ReportLogInitializationErrorKind.InvalidTemplate,
                    "The template could not be read. " + ex.Message);
            }

            try
            {
                File.WriteAllBytes(destinationPath, templateBytes);
            }
            catch (Exception ex)
            {
                return Fail(ReportLogInitializationErrorKind.DestinationUnavailable,
                    "The new report log could not be written. " + ex.Message);
            }

            using (var document = WordprocessingDocument.Open(destinationPath, true))
            {
                var body = document.MainDocumentPart!.Document!.Body!;
                foreach (var paragraph in body.Descendants<Paragraph>().ToList())
                    paragraph.Remove();

                var section = body.ChildElements.OfType<SectionProperties>().FirstOrDefault();
                var emptyParagraph = new Paragraph();
                if (section is null)
                    body.AppendChild(emptyParagraph);
                else
                    body.InsertBefore(emptyParagraph, section);

                document.Save();
            }

            var inspection = WordDocumentInspector.Inspect(destinationPath);
            if (!inspection.Openable)
                return Fail(ReportLogInitializationErrorKind.Unexpected,
                    "The initialized report log could not be opened after creation.");

            return new ReportLogInitializationResult(true, destinationPath, ReportLogInitializationErrorKind.None, null);
        }
        catch (Exception ex)
        {
            return Fail(ReportLogInitializationErrorKind.Unexpected,
                "Report-log initialization failed unexpectedly. " + ex.Message);
        }
    }

    private static ReportLogInitializationResult Fail(
        ReportLogInitializationErrorKind errorKind,
        string message) =>
        new(false, null, errorKind, message);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
