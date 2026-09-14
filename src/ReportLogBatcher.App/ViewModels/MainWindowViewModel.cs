using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Commands;
using ReportLogBatcher.App.Services;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogService;
    private readonly SettingsService _settings;
    private readonly BatchDiscoveryService _discoveryService;
    private readonly BatchFileService _fileService;
    private readonly IRenameFileDialogService _renameDialogService;
    private readonly ITemplateInspectionService _templateInspectionService;
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

    public MainWindowViewModel(
        IFileDialogService dialogService,
        SettingsService settingsService,
        BatchDiscoveryService discoveryService,
        BatchFileService fileService,
        IRenameFileDialogService renameDialogService,
        ITemplateInspectionService templateInspectionService,
        ILoggerFactory loggerFactory)
    {
        _dialogService = dialogService;
        _settings = settingsService;
        _discoveryService = discoveryService;
        _fileService = fileService;
        _renameDialogService = renameDialogService;
        _templateInspectionService = templateInspectionService;
        _logger = loggerFactory.CreateLogger<MainWindowViewModel>();

        BrowseReportLogCommand = new RelayCommand(BrowseReportLog);
        BrowseReportsDirectoryCommand = new RelayCommand(BrowseReportsDirectory);
        BrowseReportLogTemplateCommand = new RelayCommand(BrowseReportLogTemplate);
        BuildBatchCommand = new RelayCommand(OnBuildBatch, () => CanBuildBatch);
        MoveUpCommand = new RelayCommand(OnMoveUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(OnMoveDown, () => CanMoveDown);
        RemoveFromBatchCommand = new RelayCommand(OnRemoveFromBatch, () => SelectedRow is not null);
        RenameFileCommand = new RelayCommand(OnRenameFile, () => SelectedRow is not null);
        ReloadBatchCommand = new RelayCommand(OnReloadBatch, () => CanReloadBatch);

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

    public bool CanBuildBatch =>
        _reportLogValidation?.IsValid == true &&
        _reportsDirectoryValidation?.IsValid == true &&
        _templateValidation?.IsValid == true;

    public bool CanReloadBatch =>
        _reportsDirectoryValidation?.IsValid == true;

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

    public ICommand BrowseReportLogCommand { get; }

    public ICommand BrowseReportsDirectoryCommand { get; }

    public ICommand BrowseReportLogTemplateCommand { get; }

    public ICommand BuildBatchCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand RemoveFromBatchCommand { get; }

    public ICommand RenameFileCommand { get; }

    public ICommand ReloadBatchCommand { get; }

    private bool CanMoveUp
    {
        get
        {
            var index = SelectedIndex;
            return index is > 0;
        }
    }

    private bool CanMoveDown
    {
        get
        {
            var index = SelectedIndex;
            return index >= 0 && index < BatchEntries.Count - 1;
        }
    }

    private int SelectedIndex => SelectedRow is null ? -1 : BatchEntries.IndexOf(SelectedRow);

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
        RebindRows(index - 1);
        _logger.LogInformation("Moved staged entry at index {Index} up.", index);
    }

    private void OnMoveDown()
    {
        var index = SelectedIndex;
        if (!CanMoveDown)
            return;

        _stagedBatch.MoveDown(index);
        RebindRows(index + 1);
        _logger.LogInformation("Moved staged entry at index {Index} down.", index);
    }

    private void OnRemoveFromBatch()
    {
        var index = SelectedIndex;
        if (index < 0)
            return;

        var removed = _stagedBatch.Entries[index];
        var newSelection = _stagedBatch.RemoveAt(index);
        RebindRows(newSelection);

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
                RebindRows(index);

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

    private void OnReloadBatch()
    {
        try
        {
            var entries = _discoveryService.Discover(_reportsDirectoryPath);

            _stagedBatch.Replace(entries);
            RebindRows();

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

    private void RebindRows(int? selectIndex = null)
    {
        SelectedRow = null;
        BatchEntries.Clear();

        var sequence = 1;
        foreach (var entry in _stagedBatch.Entries)
        {
            BatchEntries.Add(new BatchEntryRow(entry, sequence));
            sequence++;
        }

        BatchCount = _stagedBatch.Count;

        if (selectIndex is int index && index >= 0 && index < BatchEntries.Count)
            SelectedRow = BatchEntries[index];

        RefreshCommands();
    }

    private void ClearBatchUi()
    {
        _stagedBatch.Clear();
        SelectedRow = null;
        BatchEntries.Clear();
        BatchCount = 0;
        BatchEmptyMessage = null;
        BatchError = null;
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
        ((RelayCommand)BuildBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ReloadBatchCommand).RaiseCanExecuteChanged();
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveFromBatchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RenameFileCommand).RaiseCanExecuteChanged();
    }

    private static string? ToExistingDirectory(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
}