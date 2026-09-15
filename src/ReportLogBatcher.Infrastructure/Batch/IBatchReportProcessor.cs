using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// Sequentially processes the reports of a checked-in batch. This layer holds
/// the orchestration logic and deliberately knows nothing about the GUI: manual
/// resolution is an injected callback, and all dialogs, message boxes, and
/// audit persistence live in the caller.
/// </summary>
public interface IBatchReportProcessor
{
    /// <summary>
    /// Runs the batch once, strictly in the order of
    /// <see cref="BatchRunSettings.SourcePaths"/>. Clean records are approved
    /// automatically (no manual interaction); a blocking diagnostic or an
    /// unresolved/unusable field pauses at <paramref name="manualResolution"/>
    /// (an expected, recoverable pause). Read/write failures stop the run.
    /// Events are forwarded to <paramref name="onEvent"/> in chronological order
    /// so the caller can drive per-row status, progress, and audit without
    /// re-querying live collections.
    /// </summary>
    Task<BatchRunSummary> RunAsync(
        BatchRunSettings settings,
        Func<SpinParseResult, ReportResolutionResult?> manualResolution,
        Action<BatchRunEvent> onEvent,
        CancellationToken cancellationToken = default);
}