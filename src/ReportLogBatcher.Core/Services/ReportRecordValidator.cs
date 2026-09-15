using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Required-field validator with no WPF and no Open XML dependency.
///
/// Enforces the application-wide invariant that no blank required field may ever
/// be committed: a record is validated only when every field required by
/// <see cref="ReportTemplateContract.AllFields"/> has a non-blank resolved value,
/// and <see cref="ReportRecord.InspectionDate"/> is present.
///
/// Validation only detects problems; it never silently repairs them, so a source
/// value such as <c>" S6 "</c> is preserved verbatim and a blank field stays blank.
/// </summary>
public sealed class ReportRecordValidator : IReportRecordValidator
{
    public ReportValidationResult Validate(ReportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var issues = new List<ReportValidationIssue>();
        foreach (var field in ReportTemplateContract.AllFields)
        {
            if (!IsPresent(record, field))
                issues.Add(new ReportValidationIssue(field, BuildMessage(field)));
        }

        return new ReportValidationResult(
            issues.Count == 0,
            issues.Select(issue => issue.Field).ToList(),
            issues);
    }

    private static bool IsPresent(ReportRecord record, ReportField field) => field switch
    {
        ReportField.InspectionDate => record.InspectionDate is not null,
        ReportField.ReportNumber => !string.IsNullOrWhiteSpace(record.ReportNumber),
        ReportField.InspectorFirstName => !string.IsNullOrWhiteSpace(record.InspectorFirstName),
        ReportField.DescriptionOfWork => !string.IsNullOrWhiteSpace(record.DescriptionOfWork),
        ReportField.DrawingReferences => !string.IsNullOrWhiteSpace(record.DrawingReferences),
        ReportField.GeneralObservations => !string.IsNullOrWhiteSpace(record.GeneralObservations),
        ReportField.Discrepancies => !string.IsNullOrWhiteSpace(record.Discrepancies),
        ReportField.PreviousDiscrepancyCorrections => !string.IsNullOrWhiteSpace(record.PreviousDiscrepancyCorrections),
        _ => false,
    };

    private static string BuildMessage(ReportField field) => field == ReportField.InspectionDate
        ? "Inspection Date is required; enter a valid date (MM/dd/yyyy, M/d/yyyy, or yyyy-MM-dd)."
        : $"Required field '{field}' is blank or missing.";
}