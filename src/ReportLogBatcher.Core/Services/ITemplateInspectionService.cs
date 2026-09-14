using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Opens a report-log template read-only and validates its structure against
/// <see cref="ReportTemplateContract"/>. Implementations MUST NOT modify the template.
/// </summary>
public interface ITemplateInspectionService
{
    TemplateValidationResult Inspect(string templatePath);
}