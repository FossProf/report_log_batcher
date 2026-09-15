using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Models;

/// <summary>
/// A single required-field validation finding. Validation only reports a
/// problem; it never repairs it (in particular it never substitutes a value,
/// so a source <c>"N/A"</c> is untouched and a blank field stays blank).
/// </summary>
public sealed record ReportValidationIssue(ReportField Field, string Message);

/// <summary>
/// Structured outcome of validating an eight-field <see cref="ReportRecord"/>
/// against the authoritative <see cref="ReportTemplateContract.AllFields"/> schema.
/// Records every missing field rather than stopping at the first problem.
/// </summary>
public sealed class ReportValidationResult
{
    public ReportValidationResult(
        bool isValid,
        IReadOnlyList<ReportField> missingFields,
        IReadOnlyList<ReportValidationIssue> issues)
    {
        IsValid = isValid;
        MissingFields = missingFields ?? Array.Empty<ReportField>();
        Issues = issues ?? Array.Empty<ReportValidationIssue>();
    }

    public bool IsValid { get; }

    /// <summary>All required fields that are blank/missing (or date with no value).</summary>
    public IReadOnlyList<ReportField> MissingFields { get; }

    /// <summary>One human-readable finding per missing field, in canonical field order.</summary>
    public IReadOnlyList<ReportValidationIssue> Issues { get; }
}