using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Renders one <see cref="ValidatedReportRecord"/> through the report-log
/// template, producing a complete .docx entry document.
///
/// The renderer accepts ONLY a <see cref="ValidatedReportRecord"/> (never a raw
/// <see cref="ReportRecord"/>). It treats its own copy of the template as the
/// formatting source: placeholders are replaced in place, split across Open XML
/// runs, and narrative values become real Word paragraphs that preserve the
/// placeholder paragraph's formatting. The original template byte stream is never
/// modified.
/// </summary>
public interface IReportLogTemplateRenderer
{
    /// <summary>
    /// Renders into <paramref name="outputPath"/> a docx that is the template
    /// with every required placeholder replaced.
    /// The output is validated before success is reported: it is openable XML,
    /// contains no leftover required placeholders, and contains the approved
    /// values. On failure no output file is left behind.
    /// </summary>
    ReportTemplateRenderResult Render(string templatePath, ValidatedReportRecord record, string outputPath);
}