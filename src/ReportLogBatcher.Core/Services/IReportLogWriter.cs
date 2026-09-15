using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Transactionally appends one rendered entry to the project report log.
///
/// The selected report log is never used as the primary working file. The writer
/// works on a temporary working copy, validates it, creates a byte-for-byte
/// backup of the current report log in the same directory, and only then replaces
/// the report log. The report log receives ONLY template-structured entry text —
/// no metadata, timestamps, or audit content is ever written into it.
/// </summary>
public interface IReportLogWriter
{
    /// <summary>
    /// Appends <paramref name="renderedEntryPath"/> (a rendered report-log entry,
    /// see <see cref="IReportLogTemplateRenderer"/>) to
    /// <paramref name="reportLogPath"/>. See <see cref="ReportLogWriteResult"/>.
    /// </summary>
    ReportLogWriteResult Append(string reportLogPath, string renderedEntryPath);
}