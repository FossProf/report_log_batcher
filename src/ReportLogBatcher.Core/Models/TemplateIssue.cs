namespace ReportLogBatcher.Core.Models;

public enum TemplateIssueKind
{
    /// <summary>A required placeholder is absent from the template.</summary>
    MissingPlaceholder,

    /// <summary>A placeholder that must occur exactly once occurs more than once.</summary>
    DuplicatePlaceholder,

    /// <summary>A body placeholder is not under its expected section heading (e.g. before any heading).</summary>
    MisplacedPlaceholder,

    /// <summary>The five body section headings do not appear in the expected order.</summary>
    SectionOrderViolation,

    /// <summary>An expected body section heading is missing, so context cannot be established.</summary>
    MissingSectionHeading,
}

/// <summary>
/// A single deterministic template-contract finding. Carries a human-readable
/// message so the UI can surface concise, actionable information.
/// </summary>
public sealed record TemplateIssue(
    ReportField? Field,
    TemplateIssueKind Kind,
    string Message,
    int? ParagraphIndex = null);