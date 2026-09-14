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
    private readonly ILogger _logger;

    private string _reportLogPath = string.Empty;
    private string _reportsDirectoryPath = string.Empty;
    private string? _reportLogError;
    private string? _reportsDirectoryError;
    private string? _batchEmptyMessage;
    private string? _batchError;
    private int _batchCount;
    private PathValidationResult? _reportLogValidation;
    private PathValidationResult? _reportsDirectoryValidation;

    public MainWindowViewModel(
        IFileDialogService dialogService,
        SettingsService settingsService,
        BatchDiscoveryService discoveryService,
        ILoggerFactory loggerFactory)
    {
        _dialogService = dialogService;
        _settings = settingsService;
        _discoveryService = discoveryService;
        _logger = loggerFactory.CreateLogger<MainWindowViewModel>();

        BrowseReportLogCommand = new RelayCommand(BrowseReportLog);
        BrowseReportsDirectoryCommand = new RelayCommand(BrowseReportsDirectory);
        BuildBatchCommand = new RelayCommand(OnBuildBatch, () => CanBuildBatch);

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
        _reportLogValidation?.IsValid == true && _reportsDirectoryValidation?.IsValid == true;

    public ObservableCollection<BatchEntryRow> BatchEntries { get; } = new();

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

    public ICommand BuildBatchCommand { get; }

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

    private void OnBuildBatch()
    {
        var reportLogValidation = PathValidationService.ValidateReportLog(_reportLogPath);
        var reportsDirectoryValidation = PathValidationService.ValidateReportsDirectory(_reportsDirectoryPath);

        if (!reportLogValidation.IsValid || !reportsDirectoryValidation.IsValid)
        {
            _logger.LogWarning(
                "Build Batch rejected: report log valid = {ReportLogValid}, reports directory valid = {ReportsDirectoryValid}",
                reportLogValidation.IsValid,
                reportsDirectoryValidation.IsValid);

            ClearBatchUi();
            BatchError = "The selected report log or reports directory is no longer valid. Re-select both and try again.";
            return;
        }

        try
        {
            ClearBatchUi();

            var entries = _discoveryService.Discover(_reportsDirectoryPath);

            var sequence = 1;
            foreach (var entry in entries)
            {
                BatchEntries.Add(new BatchEntryRow(entry, sequence));
                sequence++;
            }

            BatchCount = BatchEntries.Count;
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

    private void ClearBatchUi()
    {
        BatchEntries.Clear();
        BatchCount = 0;
        BatchEmptyMessage = null;
        BatchError = null;
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

        RefreshBuildState();
    }

    private void RefreshBuildState()
    {
        OnPropertyChanged(nameof(CanBuildBatch));
        ((RelayCommand)BuildBatchCommand).RaiseCanExecuteChanged();
    }

    private static string? ToExistingDirectory(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
}