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
///
/// <see cref="IsBlocking"/> is the explicit, typed severity. Classification is
/// the parser's decision, made at the emission site — never inferred from
/// message text. Batch auto-approval treats a blocking finding as requiring
/// manual review even when the affected field was somehow resolved, while an
/// informational (non-blocking) finding with all fields present does not force
/// manual review.
/// </summary>
public sealed record ParseIssue(
    ParseIssueKind Kind,
    ReportField? Field,
    string Message)
{
    /// <summary>
    /// True when the finding casts uncertainty onto a report value (missing,
    /// ambiguous, malformed, or unreadable structure) and manual review is
    /// required. False for purely informational structural observations whose
    /// values were still determined deterministically. Defaults to true so every
    /// existing diagnostic stays blocking.
    /// </summary>
    public bool IsBlocking { get; init; } = true;
}