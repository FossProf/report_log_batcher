namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Outcome of one manual resolution/review interaction for a staged report.
///
/// When <see cref="IsApproved"/> is true, <see cref="ValidatedRecord"/> contains
/// the exact record that the future renderer/writer will receive. When false,
/// <see cref="InvalidFields"/> lists the fields that still need input.
///
/// Also carries developmental metadata for the future audit trail: which fields
/// the user manually changed (<see cref="ManuallyEditedFields"/>), which were
/// blanked into the "N/A" fallback (<see cref="NaFallbackFields"/>), and the
/// parser's original diagnostics (<see cref="ParserIssues"/>) — diagnostics are
/// never discarded merely because the user resolved a field.
/// </summary>
public sealed class ReportResolutionResult
{
    public ReportResolutionResult(
        ValidatedReportRecord? validatedRecord,
        IReadOnlyList<ReportField> manuallyEditedFields,
        IReadOnlyList<ReportField> naFallbackFields,
        IReadOnlyList<ReportField> invalidFields,
        IReadOnlyList<ParseIssue> parserIssues)
    {
        ValidatedRecord = validatedRecord;
        ManuallyEditedFields = manuallyEditedFields ?? Array.Empty<ReportField>();
        NaFallbackFields = naFallbackFields ?? Array.Empty<ReportField>();
        InvalidFields = invalidFields ?? Array.Empty<ReportField>();
        ParserIssues = parserIssues ?? Array.Empty<ParseIssue>();
    }

    public ValidatedReportRecord? ValidatedRecord { get; }

    public bool IsApproved => ValidatedRecord is not null;

    /// <summary>Fields whose final value differs from the original parser output (or were a manually supplied replacement for an unresolved field).</summary>
    public IReadOnlyList<ReportField> ManuallyEditedFields { get; }

    /// <summary>Required string fields that were blank at approval and became the "N/A" fallback. Distinct from a source "N/A".</summary>
    public IReadOnlyList<ReportField> NaFallbackFields { get; }

    /// <summary>Fields that still block approval (blank at approval or an unparsable/blank Inspection Date).</summary>
    public IReadOnlyList<ReportField> InvalidFields { get; }

    /// <summary>The parser diagnostics that originally accompanied the record; preserved for context.</summary>
    public IReadOnlyList<ParseIssue> ParserIssues { get; }
}