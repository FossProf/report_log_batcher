using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Commands;
using ReportLogBatcher.App.Dialogs;
using ReportLogBatcher.App.Services;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Batch;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogService;
    private readonly SettingsService _settings;
    private readonly BatchDiscoveryService _discoveryService;
    private readonly BatchFileService _fileService;
    private readonly IRenameFileDialogService _renameDialogService;
    private readonly ITemplateInspectionService _templateInspectionService;
    private readonly IParsePreviewService _previewParseService;
    private readonly IReportReviewService _reviewService;
    private readonly IAppendReportService _appendService;
    private readonly IReportRecordResolver _resolver;
    private readonly IReportLogInitializer _initializer;
    private readonly IBatchReportProcessor _batchProcessor;
    private readonly IBatchUserInteraction _userInteraction;
    private readonly ReportAppendAuditService _audit;
    private readonly ILogger _logger;
    private readonly StagedBatch _stagedBatch = new();

    private string _reportLogPath = string.Empty;
    private string _reportsDirectoryPath = string.Empty;
    private string _templatePath = string.Empty;
    private string? _reportLogError;
    private string? _reportsDirectoryError;
    private string? _templateStatus;
    private bool _templateStatusIsError;
    private string? _batchEmptyMessage;
    private string? _batchError;
    private int _batchCount;
    private BatchEntryRow? _selectedRow;
    private PathValidationResult? _reportLogValidation;
    private PathValidationResult? _reportsDirectoryValidation;
    private TemplateValidationResult? _templateValidation;

    private bool _isBatchRunning;
    private int _batchProgressValue;
    private int _batchProgressMaximum;
    private IReadOnlyList<BatchEntryRow>? _batchRows;
    private string? _batchId;

    public MainWindowViewModel(
        IFileDialogService dialogService,
        SettingsService settingsService,
        BatchDiscoveryService discoveryService,
        BatchFileService fileService,
        IRenameFileDialogService renameDialogService,
        ITemplateInspectionService templateInspectionService,
        IParsePreviewService previewParseService,
        IReportReviewService reviewService,
        IAppendReportService appendService,
        IReportRecordResolver resolver,
        IReportLogInitializer initializer,
        IBatchReportProcessor batchProcessor,
        IBatchUserInteraction userInteraction,
        ReportAppendAuditService audit,
        ILoggerFactory loggerFactory)
    {
        _dialogService = dialogService;
        _settings = settingsService;
        _discoveryService = discoveryService;
        _fileService = fileService;
        _renameDialogService = renameDialogService;
        _templateInspectionService = templateInspectionService;
        _previewParseService = previewParseService;
        _reviewService = reviewService;
        _appendService = appendService;
        _resolver = resolver;
        _initializer = initializer;
        _batchProcessor = batchProcessor;
        _userInteraction = userInteraction;
        _audit = audit;
        _logger = loggerFactory.CreateLogger<MainWindowViewModel>();

        BrowseReportLogCommand = new RelayCommand(BrowseReportLog, () => !IsBatchRunning);
        BrowseReportsDirectoryCommand = new RelayCommand(BrowseReportsDirectory, () => !IsBatchRunning);
        BrowseReportLogTemplateCommand = new RelayCommand(BrowseReportLogTemplate, () => !IsBatchRunning);
        BuildBatchCommand = new RelayCommand(OnBuildBatch, () => CanBuildBatch);
        MoveUpCommand = new RelayCommand(OnMoveUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(OnMoveDown, () => CanMoveDown);
        RemoveFromBatchCommand = new RelayCommand(OnRemoveFromBatch, () => SelectedRow is not null && !IsBatchRunning);
        RenameFileCommand = new RelayCommand(OnRenameFile, () => SelectedRow is not null && !IsBatchRunning);
        PreviewParseCommand = new RelayCommand(OnPreviewParse, () => SelectedRow is not null && !IsBatchRunning);
        ReviewRecordCommand = new RelayCommand(OnReviewRecord, () => SelectedRow is not null && !IsBatchRunning);
        ProcessSelectedCommand = new RelayCommand(OnProcessSelected, () => CanProcessSelected);
        ReloadBatchCommand = new RelayCommand(OnReloadBatch, () => CanReloadBatch);
        SelectAllCommand = new RelayCommand(OnSelectAll, () => !IsBatchRunning);
        ClearAllCommand = new RelayCommand(OnClearAll, () => !IsBatchRunning);
        ProcessBatchCommand = new RelayCommand(OnProcessBatch, () => CanProcessBatch);

        RestoreStoredSelections();
    }

    public string ReportLogPath
    {
        get => _reportLogPath;
        private set => SetProperty(ref _reportLogPath, value);
    }

    public string ReportsDirectoryPath
    {
        get => _reportsDirectoryPath;
        private set => SetProperty(ref _reportsDirectoryPath, value);
    }

    public string ReportLogTemplatePath
    {
        get => _templatePath;
        private set => SetProperty(ref _templatePath, value);
    }

    public string? TemplateStatus
    {
        get => _templateStatus;
        private set => SetProperty(ref _templateStatus, value);
    }

    public bool TemplateStatusIsError
    {
        get => _templateStatusIsError;
        private set => SetProperty(ref _templateStatusIsError, value);
    }

    public string? ReportLogError
    {
        get => _reportLogError;
        private set => SetProperty(ref _reportLogError, value);
    }

    public string? ReportsDirectoryError
    {
        get => _reportsDirectoryError;
        private set => SetProperty(ref _reportsDirectoryError, value);
    }

    public int BatchCount
    {
        get => _batchCount;
        private set => SetProperty(ref _batchCount, value);
    }

    public string? BatchEmptyMessage
    {
        get => _batchEmptyMessage;
        private set => SetProperty(ref _batchEmptyMessage, value);
    }

    public string? BatchError
    {
        get => _batchError;
        private set => SetProperty(ref _batchError, value);
    }

    public bool IsBatchRunning
    {
        get => _isBatchRunning;
        private set => SetProperty(ref _isBatchRunning, value);
    }

    public int BatchProgressValue
    {
        get => _batchProgressValue;
        private set => SetProperty(ref _batchProgressValue, value);
    }

    public int BatchProgressMaximum
    {
        get => _batchProgressMaximum;
        private set => SetProperty(ref _batchProgressMaximum, value);
    }

    public string BatchProgressText => $"Processed {_batchProgressValue} of {_batchProgressMaximum}";

    public bool CanBuildBatch =>
        !IsBatchRunning
        && _reportLogValidation?.IsValid == true
        && _reportsDirectoryValidation?.IsValid == true
        && _templateValidation?.IsValid == true;

    public bool CanReloadBatch =>
        !IsBatchRunning
        && _reportsDirectoryValidation?.IsValid == true;

    /// <summary>
    /// Process Selected requires a valid report log and template plus a selected row
    /// that has not already been appended in this session.
    /// </summary>
    public bool CanProcessSelected =>
        !IsBatchRunning
        && _reportLogValidation?.IsValid == true
        && _templateValidation?.IsValid == true
        && SelectedRow is { CanProcess: true };

    /// <summary>
    /// Process Batch requires a valid report log and template, at least one included
    /// eligible (non-Complete) report, and no batch already running. It never
    /// requires a row selection.
    /// </summary>
    public bool CanProcessBatch =>
        !IsBatchRunning
        && _reportLogValidation?.IsValid == true
        && _templateValidation?.IsValid == true
        && BatchEntries.Any(row => row.IsIncluded && row.Status != BatchEntryStatus.Complete);

    public ObservableCollection<BatchEntryRow> BatchEntries { get; } = new();

    public BatchEntryRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                RefreshCommands();
        }
    }

    public ICommand BrowseReportLogCommand { get; }

    public ICommand BrowseReportsDirectoryCommand { get; }

    public ICommand BrowseReportLogTemplateCommand { get; }

    public ICommand BuildBatchCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand RemoveFromBatchCommand { get; }

    public ICommand RenameFileCommand { get; }

    public ICommand PreviewParseCommand { get; }

    public ICommand ReviewRecordCommand { get; }

    public ICommand ProcessSelectedCommand { get; }

    public ICommand ReloadBatchCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearAllCommand { get; }

    public ICommand ProcessBatchCommand { get; }

    private bool CanMoveUp => !IsBatchRunning && SelectedIndex > 0;

    private bool CanMoveDown => !IsBatchRunning && SelectedIndex >= 0 && SelectedIndex < BatchEntries.Count - 1;

    private int SelectedIndex => SelectedRow is null ? -1 : BatchEntries.IndexOf(SelectedRow);

    // ---- File selection ------------------------------------------------

    private void BrowseReportLog()
    {
        var selected = _dialogService.PickReportLogFile(ToExistingDirectory(_reportLogPath));
        if (selected is null)
            return;

        ApplyReportLogSelection(selected);
    }

    private void BrowseReportsDirectory()
    {
        var selected = _dialogService.PickReportsDirectory(
            Directory.Exists(_reportsDirectoryPath) ? _reportsDirectoryPath : null);
        if (selected is null)
            return;

        ApplyReportsDirectorySelection(selected);
    }

    private void BrowseReportLogTemplate()
    {
        var selected = _dialogService.PickReportLogTemplate(ToExistingDirectory(_templatePath));
        if (selected is null)
            return;

        ApplyReportLogTemplateSelection(selected);
    }

    private void ApplyReportLogSelection(string path)
    {
        ReportLogPath = path;

        _reportLogValidation = PathValidationService.ValidateReportLog(path);
        if (_reportLogValidation.IsValid)
        {
            ReportLogError = null;
            _logger.LogInformation("Report log selected: {Path}", path);
            _settings.SaveReportLogPath(path);
        }
        else
        {
            ReportLogError = _reportLogValidation.ErrorMessage;
            _logger.LogWarning("Invalid report log selection {Path}: {Message}", path, ReportLogError);
        }

        RefreshBuildState();
        ClearBatchUi();
    }

    private void ApplyReportsDirectorySelection(string path)
    {
        ReportsDirectoryPath = path;

        _reportsDirectoryValidation = PathValidationService.ValidateReportsDirectory(path);
        if (_reportsDirectoryValidation.IsValid)
        {
            ReportsDirectoryError = null;
            _logger.LogInformation("Reports directory selected: {Path}", path);
            _settings.SaveReportsDirectory(path);
        }
        else
        {
            ReportsDirectoryError = _reportsDirectoryValidation.ErrorMessage;
            _logger.LogWarning("Invalid reports directory selection {Path}: {Message}", path, ReportsDirectoryError);
        }

        RefreshBuildState();
        ClearBatchUi();
    }

    private void ApplyReportLogTemplateSelection(string path)
    {
        ReportLogTemplatePath = path;

        var pathValidation = PathValidationService.ValidateReportLogTemplate(path);
        if (!pathValidation.IsValid)
        {
            _templateValidation = null;
            TemplateStatus = pathValidation.ErrorMessage;
            TemplateStatusIsError = true;
            _logger.LogWarning("Invalid report-log template selection {Path}: {Message}", path, TemplateStatus);
        }
        else
        {
            _templateValidation = _templateInspectionService.Inspect(path);
            ApplyTemplateValidationStatus();
            _settings.SaveReportLogTemplatePath(path);

            if (_templateValidation.IsValid)
                _logger.LogInformation("Report-log template accepted: {Path}", path);
            else
                _logger.LogWarning("Report-log template failed inspection {Path}: {Issues}",
                    path, string.Join("; ", (_templateValidation.Issues ?? []).Select(i => i.Message)));
        }

        RefreshBuildState();
        ClearBatchUi();
    }

    private void ApplyTemplateValidationStatus()
    {
        if (_templateValidation is null)
        {
            TemplateStatus = null;
            TemplateStatusIsError = false;
            return;
        }

        if (_templateValidation.IsValid)
        {
            TemplateStatus = "Template valid";
            TemplateStatusIsError = false;
            return;
        }

        if (_templateValidation.Failure != TemplateValidationFailure.None)
        {
            TemplateStatus = _templateValidation.ErrorMessage;
            TemplateStatusIsError = true;
            return;
        }

        var issues = _templateValidation.Issues ?? Array.Empty<TemplateIssue>();
        var summary = issues.Count > 0 ? issues[0].Message : "the template does not match the expected contract.";
        if (issues.Count > 1)
            summary += $" (+{issues.Count - 1} more issue{(issues.Count - 1 == 1 ? string.Empty : "s")})";

        TemplateStatus = $"Template invalid: {summary}";
        TemplateStatusIsError = true;
    }

    // ---- Batch staging --------------------------------------------------

    private void OnBuildBatch()
    {
        var reportLogValidation = PathValidationService.ValidateReportLog(_reportLogPath);
        var reportsDirectoryValidation = PathValidationService.ValidateReportsDirectory(_reportsDirectoryPath);
        var templateValidation = _templateValidation?.IsValid == true;

        if (!reportLogValidation.IsValid || !reportsDirectoryValidation.IsValid || !templateValidation)
        {
            _logger.LogWarning(
                "Build Batch rejected: report log valid = {ReportLogValid}, reports directory valid = {ReportsDirectoryValid}, template valid = {TemplateValid}",
                reportLogValidation.IsValid,
                reportsDirectoryValidation.IsValid,
                templateValidation);

            ClearBatchUi();
            BatchError = "The selected report log, reports directory, or report-log template is no longer valid. Re-select all three and try again.";
            return;
        }

        try
        {
            var entries = _discoveryService.Discover(_reportsDirectoryPath);

            _stagedBatch.Replace(entries);
            RebindRows();

            SetBatchProgress(0, 0);

            if (BatchCount == 0)
                BatchEmptyMessage = "No .docx reports were found in the selected directory.";

            _logger.LogInformation("Batch built with {Count} reports from {Directory}", BatchCount, _reportsDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch discovery failed for {Directory}.", _reportsDirectoryPath);
            ClearBatchUi();
            BatchError = "Could not read the selected reports directory. See the application logs for details.";
        }
    }

    private void OnMoveUp()
    {
        var index = SelectedIndex;
        if (!CanMoveUp)
            return;

        _stagedBatch.MoveUp(index);
        RebindRows(index - 1, preserveState: true);
        _logger.LogInformation("Moved staged entry at index {Index} up.", index);
    }

    private void OnMoveDown()
    {
        var index = SelectedIndex;
        if (!CanMoveDown)
            return;

        _stagedBatch.MoveDown(index);
        RebindRows(index + 1, preserveState: true);
        _logger.LogInformation("Moved staged entry at index {Index} down.", index);
    }

    private void OnRemoveFromBatch()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;

        var removed = _stagedBatch.Entries[index];
        var newSelection = _stagedBatch.RemoveAt(index);
        RebindRows(newSelection, preserveState: false);

        _logger.LogInformation(
            "Removed {File} from the batch (source untouched).", removed.FileName);

        if (_stagedBatch.Count == 0)
            BatchEmptyMessage = "No .docx reports were found in the selected directory.";
    }

    private void OnRenameFile()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;

        var entry = _stagedBatch.Entries[index];

        string? message = null;
        while (true)
        {
            var proposed = _renameDialogService.Prompt(entry.FileName, message);
            if (proposed is null)
                return;

            var validation = _fileService.ValidateFileName(proposed);
            if (!validation.IsValid)
            {
                message = validation.ErrorMessage;
                continue;
            }

            var result = _fileService.RenameFile(entry.FullPath, proposed);
            if (result.IsSuccess)
            {
                var updated = new BatchEntry(result.DestinationPath!, result.DestinationFileName!, entry.OriginalIndex);
                _stagedBatch.UpdateEntry(index, updated);
                RebindRows(index, preserveState: true);

                _logger.LogInformation(
                    "Renamed staged file {Source} to {Destination}.", entry.FullPath, result.DestinationPath);
                return;
            }

            if (result.FailureReason is RenameFailureReason.InvalidFileName or RenameFailureReason.Collision)
            {
                message = result.ErrorMessage;
                continue;
            }

            _logger.LogError(
                "Rename failed for {Source} targeting {Proposed}: {Reason} {Message}",
                entry.FullPath, proposed, result.FailureReason, result.ErrorMessage);

            BatchError = "The file could not be renamed. See the application logs for details.";
            return;
        }
    }

    private void OnPreviewParse()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;

        var entry = _stagedBatch.Entries[index];
        _previewParseService.Show(entry.FullPath);
    }

    private void OnReviewRecord()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;

        var entry = _stagedBatch.Entries[index];
        _reviewService.Review(entry.FullPath);
    }

    private void OnProcessSelected()
    {
        var row = SelectedRow;
        if (row is null || !row.CanProcess)
            return;

        var appended = _appendService.ProcessSelected(_reportLogPath, ReportLogTemplatePath, row.FullPath);
        if (appended)
        {
            row.MarkProcessed();
            _logger.LogInformation("Report {File} marked Complete after successful append.", row.FileName);
            RefreshCommands();
        }
    }

    private void OnReloadBatch()
    {
        try
        {
            var entries = _discoveryService.Discover(_reportsDirectoryPath);

            _stagedBatch.Replace(entries);
            RebindRows();
            SetBatchProgress(0, 0);

            BatchEmptyMessage = BatchCount == 0
                ? "No .docx reports were found in the selected directory."
                : null;
            BatchError = null;

            _logger.LogInformation(
                "Reloaded batch with {Count} reports from {Directory}.", BatchCount, _reportsDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reload failed for {Directory}.", _reportsDirectoryPath);
            ClearBatchUi();
            BatchError = "Could not read the selected reports directory. See the application logs for details.";
        }
    }

    // ---- Inclusion commands ----------------------------------------------

    private void OnSelectAll()
    {
        foreach (var row in BatchEntries)
        {
            if (row.Status != BatchEntryStatus.Complete && row.IsIncludeEnabled)
                row.IsIncluded = true;
        }
    }

    private void OnClearAll()
    {
        foreach (var row in BatchEntries)
        {
            if (row.Status != BatchEntryStatus.Complete && row.IsIncludeEnabled)
                row.IsIncluded = false;
        }
    }

    // ---- Batch processing -------------------------------------------------

    private async void OnProcessBatch()
    {
        if (IsBatchRunning)
            return;

        var snapshot = IncludedEligibleRowsInOrder();
        if (snapshot.Count == 0)
            return;

        if (!EnsureReportLogReady())
            return;

        if (!ConfirmBatchStart(snapshot))
        {
            _logger.LogInformation("Batch cancelled at the final confirmation.");
            return;
        }

        _batchId = Guid.NewGuid().ToString("N");
        _batchRows = snapshot;
        var paths = snapshot.Select(row => row.FullPath).ToList();

        SetBatchRunning(true);
        SetBatchProgress(0, snapshot.Count);

        try
        {
            var settings = new BatchRunSettings(paths, _reportLogPath, ReportLogTemplatePath);
            var summary = await _batchProcessor.RunAsync(
                settings,
                ResolveForBatch,
                OnBatchEvent);

            _userInteraction.ShowBatchSummary(summary, _reportLogPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch run failed unexpectedly.");
            _userInteraction.ShowError("Process Batch", "The batch could not be completed. See the application logs for details.");
        }
        finally
        {
            _batchRows = null;
            _batchId = null;
            SetBatchRunning(false);
        }
    }

    /// <summary>
    /// The workload snapshot, captured once at batch start: included, eligible
    /// (non-Complete) rows in the current staged order. The run never consults
    /// the grid again.
    /// </summary>
    private IReadOnlyList<BatchEntryRow> IncludedEligibleRowsInOrder() =>
        BatchEntries
            .Where(row => row.IsIncluded && row.Status != BatchEntryStatus.Complete)
            .ToList();

    /// <summary>
    /// Destination readiness shown once at batch start: when the report log is
    /// empty/invalid, prompt once and initialize once. Declining stops the batch.
    /// </summary>
    private bool EnsureReportLogReady()
    {
        var validation = PathValidationService.ValidateReportLog(_reportLogPath);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Process Batch rejected: report log invalid: {Message}", validation.ErrorMessage);
            _userInteraction.ShowError("Process Batch", validation.ErrorMessage ?? "The selected report log is not available.");
            return false;
        }

        if (WordDocumentInspector.Inspect(_reportLogPath).Openable)
            return true;

        if (!_userInteraction.ConfirmInitializeReportLog(_reportLogPath))
        {
            _logger.LogInformation("Batch cancelled: report log initialization declined ({Destination}).", _reportLogPath);
            return false;
        }

        var init = _initializer.Initialize(ReportLogTemplatePath, _reportLogPath);
        if (!init.Success)
        {
            _logger.LogWarning("Report-log initialization failed for {Destination}: {Message}",
                _reportLogPath, init.Message);
            _userInteraction.ShowError("Report Log Initialization",
                "A new report log could not be created.\n\n" + init.Message);
            return false;
        }

        _logger.LogInformation("Report log initialized from template for {Destination}.", _reportLogPath);
        return true;
    }

    private bool ConfirmBatchStart(IReadOnlyList<BatchEntryRow> snapshot) =>
        _userInteraction.ConfirmBatchStart(
            _reportLogPath,
            ReportLogTemplatePath,
            snapshot.Count,
            snapshot.Select(row => row.FileName).ToList());

    private ReportResolutionResult? ResolveForBatch(SpinParseResult parse)
    {
        var dialog = new ResolveReportFieldsWindow(parse, _resolver)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void OnBatchEvent(BatchRunEvent e)
    {
        _audit.Record(BuildBatchAuditEntry(e));

        var row = RowFor(e.OrderIndex);
        switch (e.Kind)
        {
            case BatchRunEventKind.ReportStarted:
                row?.SetStatus(BatchEntryStatus.Parsing);
                break;
            case BatchRunEventKind.AutoApproved:
                row?.SetStatus(BatchEntryStatus.Validated);
                break;
            case BatchRunEventKind.NeedsInput:
                row?.SetStatus(BatchEntryStatus.NeedsInput);
                _logger.LogInformation("Batch paused for manual resolution: {File}", e.SourcePath);
                break;
            case BatchRunEventKind.ManualResolutionCompleted:
                row?.SetStatus(BatchEntryStatus.Validated);
                break;
            case BatchRunEventKind.ManualResolutionCancelled:
                row?.SetStatus(BatchEntryStatus.NeedsInput);
                break;
            case BatchRunEventKind.Validated:
                row?.SetStatus(BatchEntryStatus.Validated);
                break;
            case BatchRunEventKind.Writing:
                row?.SetStatus(BatchEntryStatus.Writing);
                break;
            case BatchRunEventKind.WriteSucceeded:
                row?.MarkComplete();
                AdvanceBatchProgress();
                break;
            case BatchRunEventKind.ItemFailed:
                row?.SetStatus(BatchEntryStatus.Failed);
                AdvanceBatchProgress();
                break;
            case BatchRunEventKind.BatchStarted:
                _logger.LogInformation("Batch started for {Count} reports in staged order.", e.StagedOrder?.Count ?? 0);
                break;
        }
    }

    private BatchEntryRow? RowFor(int orderIndex)
    {
        var rows = _batchRows;
        if (rows is null || orderIndex < 1 || orderIndex > rows.Count)
            return null;

        return rows[orderIndex - 1];
    }

    private void AdvanceBatchProgress()
    {
        var value = _batchProgressValue + 1;
        SetBatchProgress(value, _batchProgressMaximum == 0 ? value : _batchProgressMaximum);
    }

    private void SetBatchProgress(int value, int maximum)
    {
        BatchProgressValue = value;
        BatchProgressMaximum = maximum;
        OnPropertyChanged(nameof(BatchProgressText));
    }

    private ReportAppendAuditEntry BuildBatchAuditEntry(BatchRunEvent e)
    {
        var message = e.Kind switch
        {
            BatchRunEventKind.ManualResolutionCompleted =>
                ManualResolutionMessage(e),
            _ => e.Message,
        };

        return new ReportAppendAuditEntry
        {
            BatchId = _batchId,
            BatchEvent = e.Kind.ToString(),
            OrderIndex = e.OrderIndex,
            Source = e.SourcePath ?? string.Empty,
            ReportNumber = e.ReportNumber ?? string.Empty,
            Destination = _reportLogPath,
            Template = ReportLogTemplatePath,
            Backup = e.BackupPath,
            Message = message,
            StagedOrder = e.Kind == BatchRunEventKind.BatchStarted ? e.StagedOrder?.ToArray() : null,
            ManualFields = e.ManualFields.Select(field => field.ToString()).ToArray(),
            NaFallbackFields = e.NaFallbackFields.Select(field => field.ToString()).ToArray(),
            Result = e.Kind.ToString(),
        };
    }

    private static string? ManualResolutionMessage(BatchRunEvent e) =>
        e.ManualFields.Count == 0 && e.NaFallbackFields.Count == 0
            ? null
            : $"manual=[{string.Join(",", e.ManualFields.Select(f => f.ToString()))}] na=[{string.Join(",", e.NaFallbackFields.Select(f => f.ToString()))}]";

    // ---- Session state ---------------------------------------------------

    private void SetBatchRunning(bool running)
    {
        IsBatchRunning = running;
        foreach (var row in BatchEntries)
            row.SetInclusionEnabled(!running);

        RefreshBuildState();
        RefreshCommands();
    }

    private void RebindRows(int? selectIndex = null, bool preserveState = false)
    {
        IReadOnlyDictionary<int, BatchEntryRow>? previous = null;
        if (preserveState)
            previous = BatchEntries.ToDictionary(row => row.OriginalIndex);

        SelectedRow = null;
        BatchEntries.Clear();

        var sequence = 1;
        foreach (var entry in _stagedBatch.Entries)
        {
            var row = new BatchEntryRow(entry, sequence);
            if (previous is not null && previous.TryGetValue(entry.OriginalIndex, out var existingRow))
                row.PreserveStateFrom(existingRow);

            row.InclusionChanged += OnRowInclusionChanged;
            BatchEntries.Add(row);
            sequence++;
        }

        BatchCount = _stagedBatch.Count;

        if (selectIndex is int index && index >= 0 && index < BatchEntries.Count)
            SelectedRow = BatchEntries[index];

        RefreshBuildState();
        RefreshCommands();
    }

    private void OnRowInclusionChanged(BatchEntryRow row) =>
        ((RelayCommand)ProcessBatchCommand).RaiseCanExecuteChanged();

    private void ClearBatchUi()
    {
        _stagedBatch.Clear();
        SelectedRow = null;
        BatchEntries.Clear();
        BatchCount = 0;
        BatchEmptyMessage = null;
        BatchError = null;
        SetBatchProgress(0, 0);
        RefreshBuildState();
        RefreshCommands();
    }

    private void RestoreStoredSelections()
    {
        var storedReportLog = _settings.StoredReportLogPath;
        if (!string.IsNullOrWhiteSpace(storedReportLog))
        {
            var validation = PathValidationService.ValidateReportLog(storedReportLog);
            if (validation.IsValid)
            {
                _reportLogPath = storedReportLog;
                _reportLogValidation = validation;
                OnPropertyChanged(nameof(ReportLogPath));
                _logger.LogInformation("Restored report log path: {Path}", storedReportLog);
            }
            else
            {
                _logger.LogInformation("Ignored stored report log path (no longer valid): {Path}", storedReportLog);
            }
        }

        var storedDirectory = _settings.StoredReportsDirectory;
        if (!string.IsNullOrWhiteSpace(storedDirectory))
        {
            var validation = PathValidationService.ValidateReportsDirectory(storedDirectory);
            if (validation.IsValid)
            {
                _reportsDirectoryPath = storedDirectory;
                _reportsDirectoryValidation = validation;
                OnPropertyChanged(nameof(ReportsDirectoryPath));
                _logger.LogInformation("Restored reports directory path: {Path}", storedDirectory);
            }
            else
            {
                _logger.LogInformation("Ignored stored reports directory (no longer valid): {Path}", storedDirectory);
            }
        }

        var storedTemplate = _settings.StoredReportLogTemplatePath;
        if (!string.IsNullOrWhiteSpace(storedTemplate))
        {
            var pathValidation = PathValidationService.ValidateReportLogTemplate(storedTemplate);
            if (pathValidation.IsValid)
            {
                _templatePath = storedTemplate;
                _templateValidation = _templateInspectionService.Inspect(storedTemplate);
                OnPropertyChanged(nameof(ReportLogTemplatePath));
                ApplyTemplateValidationStatus();
                _logger.LogInformation(
                    "Restored report-log template path: {Path} (valid = {TemplateValid})",
                    storedTemplate,
                    _templateValidation.IsValid);
            }
            else
            {
                _logger.LogInformation("Ignored stored report-log template (no longer valid): {Path}", storedTemplate);
            }
        }

        RefreshBuildState();
    }

    private void RefreshBuildState()
    {
        OnPropertyChanged(nameof(CanBuildBatch));
        OnPropertyChanged(nameof(CanReloadBatch));
        OnPropertyChanged(nameof(CanProcessBatch));
        ((RelayCommand)BuildBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ReloadBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ProcessBatchCommand).RaiseCanExecuteChanged();
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanBuildBatch));
        OnPropertyChanged(nameof(CanReloadBatch));
        OnPropertyChanged(nameof(CanProcessSelected));
        OnPropertyChanged(nameof(CanProcessBatch));

        ((RelayCommand)BrowseReportLogCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BrowseReportsDirectoryCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BrowseReportLogTemplateCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BuildBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveFromBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RenameFileCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewParseCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ReviewRecordCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ProcessSelectedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ReloadBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SelectAllCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearAllCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ProcessBatchCommand).RaiseCanExecuteChanged();
    }

    private static string? ToExistingDirectory(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
}