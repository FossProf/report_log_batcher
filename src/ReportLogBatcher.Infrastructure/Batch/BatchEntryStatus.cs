namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// Live state of one staged report during batch processing.
///
/// Status always reflects reality: <see cref="Complete"/> is reached only after
/// the transactional writer reports success, <see cref="NeedsInput"/> is the
/// expected, recoverable pause awaiting manual resolution, and
/// <see cref="Failed"/> is an unexpected, non-recoverable stop. Unchecked rows
/// never become <see cref="Complete"/> or <see cref="Failed"/>.
/// </summary>
public enum BatchEntryStatus
{
    /// <summary>Not yet started in the current run.</summary>
    Pending,

    /// <summary>Source pre-flight and parsing are in flight.</summary>
    Parsing,

    /// <summary>Paused; a manual resolution interaction is required before the record may be committed.</summary>
    NeedsInput,

    /// <summary>An approved (validated) record was obtained, either automatically or after manual resolution.</summary>
    Validated,

    /// <summary>Rendering and the transactional append are in flight.</summary>
    Writing,

    /// <summary>The entry was transactionally appended and the report log re-validated.</summary>
    Complete,

    /// <summary>An unexpected failure stopped the batch; this row cannot proceed without user action.</summary>
    Failed,
}