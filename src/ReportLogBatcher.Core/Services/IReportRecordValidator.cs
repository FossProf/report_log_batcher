using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

public interface IReportRecordValidator
{
    /// <summary>
    /// Validates every required field of the record. Strings must be non-null and
    /// non-blank (<c>"N/A"</c> is valid; null, "", and whitespace-only are not).
    /// <see cref="ReportRecord.InspectionDate"/> must be present. Validation never
    /// modifies source values and never repairs a record.
    /// </summary>
    ReportValidationResult Validate(ReportRecord record);
}