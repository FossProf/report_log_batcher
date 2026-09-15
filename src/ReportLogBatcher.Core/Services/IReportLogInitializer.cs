using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Creates a fresh, empty report log from the report-log template.
///
/// The result document keeps the template's styling parts and final section
/// properties but contains no entry content and no remaining placeholders. It is
/// only used when the user explicitly chooses to initialize a report log that is
/// empty or is not a valid Word document.
/// </summary>
public interface IReportLogInitializer
{
    /// <summary>
    /// Initializes <paramref name="destinationPath"/> from
    /// <paramref name="templatePath"/>. Refuses to overwrite an already-valid
    /// document. The template file itself is never modified.
    /// </summary>
    ReportLogInitializationResult Initialize(string templatePath, string destinationPath);
}