using System.Windows;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Dialogs;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.Services;

public interface IReportReviewService
{
    /// <summary>
    /// Runs the Review Record workflow for one staged report: parse, validate,
    /// show the editable resolution dialog, and on approval show the read-only
    /// approved-record preview. No Word document is written and nothing is
    /// persisted; this stops at the validated record for this slice.
    /// </summary>
    void Review(string spinReportPath);
}

/// <summary>
/// GUI orchestration of the review workflow. Parsing stays in
/// <see cref="ISpinReportParser"/>, validation/resolution in Core, and this
/// service only drives the two dialogs and logs the concise workflow outcome
/// (never narrative bodies).
/// </summary>
public sealed class ReportReviewService : IReportReviewService
{
    private readonly ISpinReportParser _parser;
    private readonly IReportRecordResolver _resolver;
    private readonly ILogger _logger;

    public ReportReviewService(ISpinReportParser parser, IReportRecordResolver resolver, ILoggerFactory loggerFactory)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = loggerFactory.CreateLogger<ReportReviewService>();
    }

    public void Review(string spinReportPath)
    {
        try
        {
            var parse = _parser.Parse(spinReportPath);

            _logger.LogInformation(
                "Review for {Path}: status = {Status}, unresolved = {Unresolved}, issues = {IssueCount}",
                spinReportPath,
                parse.Status,
                parse.UnresolvedFieldCount,
                parse.Issues.Count);

            if (parse.Status == SpinParseStatus.Failed)
            {
                _logger.LogWarning(
                    "Review blocked for {Path}: document-level failure. Diagnostics: {Issues}",
                    spinReportPath,
                    string.Join("; ", parse.Issues.Select(issue =>
                        $"[{issue.Kind}] {(issue.Field?.ToString() ?? "Document")}: {issue.Message}")));

                MessageBox.Show(
                    "This report cannot be reviewed because it could not be opened or read as a valid Word document.\n\n" +
                    "Diagnostics:\n" + string.Join(Environment.NewLine, parse.Issues.Select(issue => issue.Message)),
                    "Review Record",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new ResolveReportFieldsWindow(parse, _resolver)
            {
                Owner = Application.Current?.MainWindow,
            };

            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                _logger.LogInformation("Review cancelled for {Path}.", spinReportPath);
                return;
            }

            var resolution = dialog.Result;
            _logger.LogInformation(
                "Review approved for {Path}: manual = [{Manual}], naFallback = [{NaFallback}], invalid = {InvalidCount}",
                spinReportPath,
                string.Join(",", resolution.ManuallyEditedFields.Select(field => field.ToString())),
                string.Join(",", resolution.NaFallbackFields.Select(field => field.ToString())),
                resolution.InvalidFields.Count);

            var preview = new ApprovedRecordPreviewWindow(resolution)
            {
                Owner = Application.Current?.MainWindow,
            };
            preview.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review failed for {Path}.", spinReportPath);
            MessageBox.Show(
                "The selected report could not be reviewed. See the application logs for details.",
                "Review Record",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}