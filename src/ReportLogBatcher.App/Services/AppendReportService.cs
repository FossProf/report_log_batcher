using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Dialogs;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.App.Services;

public interface IAppendReportService
{
    /// <summary>
    /// Runs the Process Selected workflow for one staged report: parse, manual
    /// resolution, approved preview, destination readiness (initialize-or-exit),
    /// final destination confirmation, render, transactional append, and
    /// structured feedback. Returns true only when the append succeeded and the
    /// caller should mark the row Complete.
    /// </summary>
    bool ProcessSelected(string reportLogPath, string templatePath, string sourceReportPath);
}

/// <summary>
/// GUI orchestration of the append workflow. Parsing stays in
/// <see cref="ISpinReportParser"/>, resolution in Core, rendering/writing in
/// Infrastructure, and this service only drives the dialogs, the temp-file
/// lifecycle, and the audit trail (which never touches the report log).
/// </summary>
public sealed class AppendReportService : IAppendReportService
{
    private readonly ISpinReportParser _parser;
    private readonly IReportRecordResolver _resolver;
    private readonly IReportLogTemplateRenderer _renderer;
    private readonly IReportLogWriter _writer;
    private readonly IReportLogInitializer _initializer;
    private readonly ReportAppendAuditService _audit;
    private readonly ILogger _logger;

    private SpinParseResult? _parse;
    private ReportResolutionResult? _resolution;

    public AppendReportService(
        ISpinReportParser parser,
        IReportRecordResolver resolver,
        IReportLogTemplateRenderer renderer,
        IReportLogWriter writer,
        IReportLogInitializer initializer,
        ReportAppendAuditService audit,
        ILoggerFactory loggerFactory)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = loggerFactory.CreateLogger<AppendReportService>();
    }

    public bool ProcessSelected(string reportLogPath, string templatePath, string sourceReportPath)
    {
        try
        {
            return ProcessSelectedCore(reportLogPath, templatePath, sourceReportPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Append workflow failed for {Source}.", sourceReportPath);
            MessageBox.Show(
                "The selected report could not be appended. See the application logs for details.",
                "Process Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private bool ProcessSelectedCore(string reportLogPath, string templatePath, string sourceReportPath)
    {
        _parse = _parser.Parse(sourceReportPath);

        if (_parse.Status == SpinParseStatus.Failed)
        {
            _logger.LogWarning("Append blocked for {Source}: document-level failure.", sourceReportPath);
            ShowDocumentFailure(_parse);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Failed-Parse",
                "The source report could not be read as a valid Word document."));
            return false;
        }

        var resolveDialog = new ResolveReportFieldsWindow(_parse, _resolver)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (resolveDialog.ShowDialog() != true || resolveDialog.Result is null
            || resolveDialog.Result.ValidatedRecord is null)
        {
            _logger.LogInformation("Append cancelled at resolution for {Source}.", sourceReportPath);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Cancelled-Review"));
            return false;
        }

        _resolution = resolveDialog.Result;
        var record = _resolution.ValidatedRecord;

        var preview = new ApprovedRecordPreviewWindow(_resolution, showAppendAction: true)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (preview.ShowDialog() != true || !preview.AppendRequested)
        {
            _logger.LogInformation("Append declined in preview for {Source}.", sourceReportPath);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Cancelled-Preview"));
            return false;
        }

        var destinationValidation = PathValidationService.ValidateReportLog(reportLogPath);
        if (!destinationValidation.IsValid)
        {
            _logger.LogWarning("Append rejected for {Source}: {Destination}: {Message}",
                sourceReportPath, reportLogPath, destinationValidation.ErrorMessage);
            MessageBox.Show(
                destinationValidation.ErrorMessage ?? "The selected report log is not available.",
                "Process Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Failed-Destination"));
            return false;
        }

        var initialized = false;
        if (!WordDocumentInspector.Inspect(reportLogPath).Openable)
        {
            var choice = MessageBox.Show(
                "The selected report log is empty or is not a valid Word document:\n\n" +
                reportLogPath +
                "\n\nInitialize a new report log from the current template and continue?",
                "Report Log Requires Initialization",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
            {
                _logger.LogInformation(
                    "Append cancelled: report log initialization declined ({Destination}).", reportLogPath);
                _audit.Record(BuildAudit(reportLogPath, templatePath, "Cancelled-Initialize"));
                return false;
            }

            var init = _initializer.Initialize(templatePath, reportLogPath);
            if (!init.Success)
            {
                _logger.LogWarning("Report-log initialization failed for {Destination}: {Message}",
                    reportLogPath, init.Message);
                MessageBox.Show(
                    "A new report log could not be created.\n\n" + init.Message,
                    "Report Log Initialization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _audit.Record(BuildAudit(reportLogPath, templatePath, "Failed-Initialize", init.Message));
                return false;
            }

            initialized = true;
            _logger.LogInformation("Report log initialized from template for {Destination}.", reportLogPath);
        }

        var confirm = new AppendConfirmationWindow(record.ReportNumber, sourceReportPath, reportLogPath, templatePath)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (confirm.ShowDialog() != true)
        {
            _logger.LogInformation("Append cancelled at final confirmation for {Source}.", sourceReportPath);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Cancelled-Confirm"));
            return false;
        }

        var renderDirectory = Path.Combine(Path.GetTempPath(), "ReportLogBatcher-Render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(renderDirectory);
        var renderedPath = Path.Combine(renderDirectory, "entry.docx");

        var render = _renderer.Render(templatePath, record, renderedPath);
        if (!render.Success)
        {
            _logger.LogWarning("Render failed for {Source}: {Kind} {Message}",
                sourceReportPath, render.ErrorKind, render.Message);
            CleanupDirectory(renderDirectory);
            MessageBox.Show(
                "The report-log entry could not be rendered from the template.\n\n" + render.Message,
                "Process Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _audit.Record(BuildAudit(reportLogPath, templatePath, "Failed-Render", render.Message));
            return false;
        }

        var write = _writer.Append(reportLogPath, renderedPath);
        CleanupDirectory(renderDirectory);

        if (!write.Success)
        {
            _logger.LogWarning("Append failed for {Source}: {Kind} {Message}",
                sourceReportPath, write.ErrorKind, write.Message);
            MessageBox.Show(
                FailureMessage(write),
                "Process Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            var audit = BuildAudit(reportLogPath, templatePath, "Failed-" + write.ErrorKind, write.Message);
            audit.InitializedReportLog = initialized;
            audit.Backup = write.BackupPath;
            _audit.Record(audit);
            return false;
        }

        _logger.LogInformation("Appended report {Number} to {Destination}; backup at {Backup}.",
            record.ReportNumber, reportLogPath, write.BackupPath);

        MessageBox.Show(
            $"Report #{record.ReportNumber} was appended to the report log.\n\n" +
            "Backup created:\n" + write.BackupPath,
            "Append Successful",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var successAudit = BuildAudit(reportLogPath, templatePath, "Success");
        successAudit.InitializedReportLog = initialized;
        successAudit.Backup = write.BackupPath;
        _audit.Record(successAudit);

        return true;
    }

    private static string FailureMessage(ReportLogWriteResult write) => write.ErrorKind switch
    {
        ReportLogWriteErrorKind.InvalidDestination =>
            "The selected report log is not available as a Word document.",
        ReportLogWriteErrorKind.DestinationInUse =>
            "The report log may be open or in use. Close anything holding it open and try again.",
        ReportLogWriteErrorKind.InvalidRenderedEntry =>
            "The rendered entry did not pass validation, so the report log was NOT modified.",
        ReportLogWriteErrorKind.UnsupportedRenderedContent =>
            "The rendered entry contains content that cannot be safely merged. No report log was modified.",
        ReportLogWriteErrorKind.BackupFailed =>
            "A backup of the report log could not be created, so the report log was NOT modified.",
        ReportLogWriteErrorKind.AppendFailed =>
            "The report log could not be updated. The report log was NOT modified.",
        ReportLogWriteErrorKind.FinalValidationFailed =>
            "The final report log validation failed; the original report log was restored from the backup.",
        _ => "The report entry could not be appended. See the application logs for details.",
    };

    private void ShowDocumentFailure(SpinParseResult parse)
    {
        MessageBox.Show(
            "This report cannot be appended because it could not be opened or read as a valid Word document.\n\n" +
            "Diagnostics:\n" + string.Join(Environment.NewLine, parse.Issues.Select(issue => issue.Message)),
            "Process Selected",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private ReportAppendAuditEntry BuildAudit(
        string reportLogPath,
        string templatePath,
        string result,
        string? message = null) =>
        new()
        {
            Source = _parse?.SourcePath ?? string.Empty,
            ReportNumber = _resolution?.ValidatedRecord?.ReportNumber
                ?? _parse?.Record?.ReportNumber
                ?? string.Empty,
            Destination = reportLogPath,
            Template = templatePath,
            ManualFields = (_resolution?.ManuallyEditedFields
                .Select(field => field.ToString()) ?? Array.Empty<string>()).ToArray(),
            NaFallbackFields = (_resolution?.NaFallbackFields
                .Select(field => field.ToString()) ?? Array.Empty<string>()).ToArray(),
            Result = result,
            Message = message,
        };

    private void CleanupDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Render temp directory cleanup failed: {Directory}", directory);
        }
    }
}