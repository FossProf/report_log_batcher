using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

public interface IReportRecordResolver
{
    /// <summary>
    /// Transforms a parsed record plus the user's final edited values (one raw
    /// string per field, including the date) into a resolution result.
    ///
    /// Resolution applies the N/A fallback for blank required STRING fields,
    /// requires a real editable date for <see cref="ReportField.InspectionDate"/>,
    /// tracks which fields the user changed, and produces the approved
    /// <see cref="ReportResolutionResult.ValidatedRecord"/> only when the final
    /// record passes required-field validation. The original parsed record is
    /// never mutated.
    /// </summary>
    ReportResolutionResult Resolve(
        ReportRecord original,
        IReadOnlyDictionary<ReportField, string?> editedValues,
        IReadOnlyList<ParseIssue> parserIssues);
}