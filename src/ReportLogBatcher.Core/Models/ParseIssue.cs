namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Category of a deterministic parser finding. Diagnostics are records data,
/// never silently discarded, so the parser can never hide uncertainty.
/// </summary>
public enum ParseIssueKind
{
    /// <summary>The expected structure (title, header label, or section heading) was not found.</summary>
    Missing,

    /// <summary>The expected structure occurred more than once, so extraction cannot be resolved deterministically.</summary>
    Ambiguous,

    /// <summary>Structure was found but its value did not match an expected format (e.g. unparsable date).</summary>
    InvalidFormat,

    /// <summary>The document itself could not be read (missing file, wrong extension, corrupt .docx).</summary>
    DocumentError,
}

/// <summary>
/// A single deterministic parser finding, tied to the affected
/// <see cref="ReportField"/> when applicable.
/// </summary>
public sealed record ParseIssue(
    ParseIssueKind Kind,
    ReportField? Field,
    string Message);