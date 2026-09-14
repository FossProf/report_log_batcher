using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Services;
using ReportLogBatcher.App.ViewModels;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.App;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger("ReportLogBatcher.App");
        _logger.LogInformation("Report Log Batcher starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var settings = new SettingsService();
            settings.Load();

            var viewModel = new MainWindowViewModel(
                new FileDialogService(),
                settings,
                new BatchDiscoveryService(),
                new BatchFileService(),
                new RenameFileDialogService(),
                new TemplateInspectionService(),
                _loggerFactory);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();

            _logger.LogInformation("Main window shown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during application startup.");
            MessageBox.Show(
                "An unexpected error occurred. See the application logs for details.",
                "Report Log Batcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Report Log Batcher exiting.");
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception.");
        MessageBox.Show(
            "An unexpected error occurred. See the application logs for details.",
            "Report Log Batcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}