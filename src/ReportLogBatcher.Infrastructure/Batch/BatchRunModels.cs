using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// The immutable workload snapshot for one batch run: the exact staged order of
/// the included report source paths plus the report log and template the caller
/// has already validated. Captured once at batch start; the run never queries a
/// mutable collection afterwards.
/// </summary>
public sealed class BatchRunSettings
{
    /// <summary>Exactly one entry per included row, in the current staged order.</summary>
    public IReadOnlyList<string> SourcePaths { get; }

    public string ReportLogPath { get; }

    public string TemplatePath { get; }

    public BatchRunSettings(
        IReadOnlyList<string> sourcePaths,
        string reportLogPath,
        string templatePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (string.IsNullOrWhiteSpace(reportLogPath)) throw new ArgumentException("A report log path is required.", nameof(reportLogPath));
        if (string.IsNullOrWhiteSpace(templatePath)) throw new ArgumentException("A template path is required.", nameof(templatePath));

        SourcePaths = sourcePaths;
        ReportLogPath = reportLogPath;
        TemplatePath = templatePath;
    }
}

/// <summary>
/// Why a batch run ended. <see cref="Completed"/> means every included item was
/// appended. <see cref="StoppedByUser"/> means manual resolution was cancelled
/// (or the caller requested cancellation). <see cref="StoppedOnError"/> means a
/// blocking failure stopped the run.
/// </summary>
public enum BatchStopReason
{
    Completed,
    StoppedByUser,
    StoppedOnError,
}

/// <summary>Final outcome of one snapshot item within a batch run.</summary>
public sealed class BatchRunItemResult
{
    public BatchRunItemResult(int orderIndex, string sourcePath)
    {
        OrderIndex = orderIndex;
        SourcePath = sourcePath;
    }

    /// <summary>1-based position of the item in the staged snapshot.</summary>
    public int OrderIndex { get; }

    public string SourcePath { get; }

    public BatchEntryStatus Status { get; private set; } = BatchEntryStatus.Pending;

    public string? ReportNumber { get; private set; }

    public string? BackupPath { get; private set; }

    public string? Message { get; private set; }

    internal void MarkCompleted(string reportNumber, string backupPath)
    {
        Status = BatchEntryStatus.Complete;
        ReportNumber = reportNumber;
        BackupPath = backupPath;
    }

    internal void MarkNeedsInput() => Status = BatchEntryStatus.NeedsInput;

    internal void MarkFailed(string message)
    {
        Status = BatchEntryStatus.Failed;
        Message = message;
    }
}

/// <summary>
/// The concise outcome of a batch run: per-item results plus the counts the UI
/// presents in its single end-of-batch summary.
/// </summary>
public sealed class BatchRunSummary
{
    public BatchRunSummary(
        IReadOnlyList<BatchRunItemResult> items,
        int manualResolutionCount,
        BatchStopReason stopReason,
        string? errorMessage = null)
    {
        Items = items;
        ManualResolutionCount = manualResolutionCount;
        StopReason = stopReason;
        ErrorMessage = errorMessage;

        IncludedCount = items.Count;
        CompletedCount = items.Count(item => item.Status == BatchEntryStatus.Complete);
        FailedCount = items.Count(item => item.Status == BatchEntryStatus.Failed);
        NeedsInputCount = items.Count(item => item.Status == BatchEntryStatus.NeedsInput);
        RemainingCount = items.Count(item => item.Status == BatchEntryStatus.Pending);
    }

    public IReadOnlyList<BatchRunItemResult> Items { get; }

    public int IncludedCount { get; }

    public int CompletedCount { get; }

    public int NeedsInputCount { get; }

    public int FailedCount { get; }

    public int RemainingCount { get; }

    /// <summary>How many records required a manual-resolution interaction (informational diagnostics never count).</summary>
    public int ManualResolutionCount { get; }

    public BatchStopReason StopReason { get; }

    public string? ErrorMessage { get; }
}