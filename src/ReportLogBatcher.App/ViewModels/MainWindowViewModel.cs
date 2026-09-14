using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Commands;
using ReportLogBatcher.App.Services;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogService;
    private readonly SettingsService _settings;
    private readonly ILogger _logger;

    private string _reportLogPath = string.Empty;
    private string _reportsDirectoryPath = string.Empty;
    private string? _reportLogError;
    private string? _reportsDirectoryError;
    private PathValidationResult? _reportLogValidation;
    private PathValidationResult? _reportsDirectoryValidation;

    public MainWindowViewModel(
        IFileDialogService dialogService,
        SettingsService settingsService,
        ILoggerFactory loggerFactory)
    {
        _dialogService = dialogService;
        _settings = settingsService;
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
    }

    private void OnBuildBatch()
    {
        // Batch building is a later slice; nothing to do here yet.
        _logger.LogInformation("Build Batch invoked, but no batch operation is implemented yet.");
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