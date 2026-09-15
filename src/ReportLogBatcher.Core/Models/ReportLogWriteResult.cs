namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Why a transactional report-log append failed. <see cref="None"/> means success.
/// </summary>
public enum ReportLogWriteErrorKind
{
    None,
    InvalidDestination,
    DestinationInUse,
    InvalidRenderedEntry,
    UnsupportedRenderedContent,
    BackupFailed,
    AppendFailed,
    FinalValidationFailed,
}

/// <summary>
/// Structured result of a transactional report-log append.
///
/// On success the destination was atomically replaced with the original bytes +
/// the rendered entry, and <see cref="BackupPath"/> holds a byte-for-byte copy of
/// the pre-write report log. On failure the destination is unchanged for every
/// error kind, including <see cref="ReportLogWriteErrorKind.FinalValidationFailed"/>
/// (where the pre-write bytes are restored from the backup and re-validation is
/// left to the caller).
/// </summary>
public sealed class ReportLogWriteResult
{
    public ReportLogWriteResult(
        bool success,
        string? destinationPath,
        string? backupPath,
        ReportLogWriteErrorKind errorKind,
        string? message)
    {
        Success = success;
        DestinationPath = destinationPath;
        BackupPath = backupPath;
        ErrorKind = errorKind;
        Message = message;
    }

    public bool Success { get; }

    public string? DestinationPath { get; }

    public string? BackupPath { get; }

    public ReportLogWriteErrorKind ErrorKind { get; }

    public string? Message { get; }
}