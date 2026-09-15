namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Why initializing a fresh report log failed. <see cref="None"/> means success.
/// </summary>
public enum ReportLogInitializationErrorKind
{
    None,
    InvalidTemplate,
    AlreadyValidDocument,
    DestinationUnavailable,
    Unexpected,
}

/// <summary>
/// Outcome of creating a fresh (empty) report log from the report-log template.
/// The initialized document keeps the template's styling and final section
/// properties but contains NOTHING else: no placeholders, no entry content.
/// </summary>
public sealed class ReportLogInitializationResult
{
    public ReportLogInitializationResult(
        bool success,
        string? destinationPath,
        ReportLogInitializationErrorKind errorKind,
        string? message)
    {
        Success = success;
        DestinationPath = destinationPath;
        ErrorKind = errorKind;
        Message = message;
    }

    public bool Success { get; }

    public string? DestinationPath { get; }

    public ReportLogInitializationErrorKind ErrorKind { get; }

    public string? Message { get; }
}