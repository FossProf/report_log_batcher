using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Infrastructure.Batch;

namespace ReportLogBatcher.App.ViewModels;

/// <summary>
/// One staged report row. Combines the source file facts (<see cref="FullPath"/>,
/// <see cref="FileName"/>, <see cref="OriginalIndex"/>) with the session-only
/// batch state: inclusion in the batch (checkbox), the live
/// <see cref="Status"/>, and the double-append guard (<see cref="CanProcess"/>).
/// </summary>
public sealed class BatchEntryRow : ObservableObject
{
    private BatchEntryStatus _status = BatchEntryStatus.Pending;
    private bool _isIncluded = true;
    private bool _inclusionEnabled = true;
    private bool _isProcessed;

    public BatchEntryRow(BatchEntry entry, int sequence)
    {
        FullPath = entry.FullPath;
        FileName = entry.FileName;
        OriginalIndex = entry.OriginalIndex;
        Sequence = sequence;
    }

    /// <summary>Raised whenever <see cref="IsIncluded"/> changes so the ViewModel can refresh batch eligibility.</summary>
    public event Action<BatchEntryRow>? InclusionChanged;

    public string FullPath { get; }

    public string FileName { get; }

    /// <summary>Discovery-order identity used to preserve session state across reorder/rename rebinds.</summary>
    public int OriginalIndex { get; }

    /// <summary>Display order in the staged grid, 1-based.</summary>
    public int Sequence { get; }

    public BatchEntryStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public string StatusDisplay => Status switch
    {
        BatchEntryStatus.Pending => "Pending",
        BatchEntryStatus.Parsing => "Parsing",
        BatchEntryStatus.NeedsInput => "Needs Input",
        BatchEntryStatus.Validated => "Validated",
        BatchEntryStatus.Writing => "Writing",
        BatchEntryStatus.Complete => "Complete",
        BatchEntryStatus.Failed => "Failed",
        _ => Status.ToString(),
    };

    /// <summary>Included in the next batch run. Defaults to true on discovery/reload; session-only.</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!SetProperty(ref _isIncluded, value))
                return;

            InclusionChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// The checkbox is editable while the batch is idle and never re-arms a
    /// Completed row (so an already-appended report can never be included again).
    /// </summary>
    public bool IsIncludeEnabled => _inclusionEnabled && Status != BatchEntryStatus.Complete;

    /// <summary>Locks/unlocks all checkboxes (batch running).</summary>
    public void SetInclusionEnabled(bool enabled)
    {
        if (SetProperty(ref _inclusionEnabled, enabled))
            OnPropertyChanged(nameof(IsIncludeEnabled));
    }

    /// <summary>Whether the row was appended in the current session (double-append guard).</summary>
    public bool IsProcessed
    {
        get => _isProcessed;
        private set
        {
            if (SetProperty(ref _isProcessed, value))
                OnPropertyChanged(nameof(CanProcess));
        }
    }

    public bool CanProcess => !IsProcessed;

    public void SetStatus(BatchEntryStatus status) => Status = status;

    /// <summary>Manual single-row completion (Process Selected).</summary>
    public void MarkProcessed()
    {
        Status = BatchEntryStatus.Complete;
        IsProcessed = true;
    }

    /// <summary>Batch completion: the transactional writer succeeded.</summary>
    public void MarkComplete()
    {
        Status = BatchEntryStatus.Complete;
        IsProcessed = true;
    }

    /// <summary>
    /// Carries session-only state (inclusion, status, appended guard) across a
    /// move/rename rebind. Deliberately NOT used by reload/build, which reset the
    /// session.
    /// </summary>
    internal void PreserveStateFrom(BatchEntryRow previous)
    {
        _isProcessed = previous._isProcessed;
        _status = previous._status;
        _isIncluded = previous._isIncluded;
        OnPropertyChanged(nameof(IsIncluded));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(IsIncludeEnabled));
        OnPropertyChanged(nameof(CanProcess));
    }
}