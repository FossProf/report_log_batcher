using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// The workflow events a batch run surfaces to its caller. Each event is emitted
/// in chronological order and carries only metadata — never narrative report
/// bodies — so it can be forwarded straight to the audit trail and progress UI.
/// </summary>
public enum BatchRunEventKind
{
    /// <summary>The batch started. Carries the full staged processing order.</summary>
    BatchStarted,

    /// <summary>A snapshot item began processing.</summary>
    ReportStarted,

    /// <summary>A record passed deterministic parsing and required-field validation and was approved automatically (no manual interaction).</summary>
    AutoApproved,

    /// <summary>A report reached the expected manual-resolution pause.</summary>
    NeedsInput,

    /// <summary>Manual resolution completed with an approved record.</summary>
    ManualResolutionCompleted,

    /// <summary>Manual resolution was cancelled; the batch pauses/stopped safely.</summary>
    ManualResolutionCancelled,

    /// <summary>An approved record is about to be rendered and written.</summary>
    Validated,

    /// <summary>Rendering and the transactional append are in flight.</summary>
    Writing,

    /// <summary>Rendering and the transactional append completed successfully for one record.</summary>
    WriteSucceeded,

    /// <summary>Rendering, appending, or source reading failed; the batch stops.</summary>
    ItemFailed,

    /// <summary>The batch reached its end successfully.</summary>
    BatchCompleted,

    /// <summary>The batch stopped (user cancellation or error).</summary>
    BatchStopped,
}

/// <summary>
/// One batch workflow event. Fields are optional by kind; consumers should treat
/// a property meaningful only for the kinds documented on
/// <see cref="BatchRunEventKind"/>.
/// </summary>
public sealed class BatchRunEvent
{
    public BatchRunEvent(
        BatchRunEventKind kind,
        int orderIndex = 0,
        string? sourcePath = null,
        string? reportNumber = null,
        string? backupPath = null,
        string? message = null,
        IReadOnlyList<string>? stagedOrder = null,
        IReadOnlyList<ReportField>? manualFields = null,
        IReadOnlyList<ReportField>? naFallbackFields = null)
    {
        Kind = kind;
        OrderIndex = orderIndex;
        SourcePath = sourcePath;
        ReportNumber = reportNumber;
        BackupPath = backupPath;
        Message = message;
        StagedOrder = stagedOrder;
        ManualFields = manualFields ?? Array.Empty<ReportField>();
        NaFallbackFields = naFallbackFields ?? Array.Empty<ReportField>();
    }

    public BatchRunEventKind Kind { get; }

    /// <summary>1-based position of the item in the staged snapshot.</summary>
    public int OrderIndex { get; }

    public string? SourcePath { get; }

    public string? ReportNumber { get; }

    /// <summary>Byte-for-byte back-up path created by a successful writer call.</summary>
    public string? BackupPath { get; }

    public string? Message { get; }

    /// <summary>Only on <see cref="BatchRunEventKind.BatchStarted"/>: the file names in exact staged order.</summary>
    public IReadOnlyList<string>? StagedOrder { get; }

    /// <summary>Only on <see cref="BatchRunEventKind.ManualResolutionCompleted"/>.</summary>
    public IReadOnlyList<ReportField> ManualFields { get; }

    /// <summary>Only on <see cref="BatchRunEventKind.ManualResolutionCompleted"/>.</summary>
    public IReadOnlyList<ReportField> NaFallbackFields { get; }
}