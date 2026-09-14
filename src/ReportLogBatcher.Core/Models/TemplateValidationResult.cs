namespace ReportLogBatcher.Core.Models;

public enum TemplateValidationFailure
{
    None,

    /// <summary>The template file does not exist.</summary>
    FileNotFound,

    /// <summary>The template is not a .docx file.</summary>
    InvalidExtension,

    /// <summary>The .docx could not be opened as a valid Word document.</summary>
    CannotOpen,
}

/// <summary>
/// Structured result of report-log template inspection. Never a bare boolean:
/// callers can distinguish path errors, read errors, and each missing/duplicate/
/// misplaced placeholder.
/// </summary>
public sealed record TemplateValidationResult(
    bool IsValid,
    TemplateValidationFailure Failure = TemplateValidationFailure.None,
    string? ErrorMessage = null,
    IReadOnlyList<TemplateIssue>? Issues = null)
{
    public bool HasContractIssues => Issues is { Count: > 0 };
}