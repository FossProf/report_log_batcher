using System.Windows;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Dialogs;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.Services;

public interface IParsePreviewService
{
    void Show(string spinReportPath);
}

/// <summary>
/// GUI orchestration for the parse preview: runs the deterministic parser,
/// logs only the outcome (never narrative bodies), and presents the result in a
/// read-only window. Actual parsing lives in <see cref="ISpinReportParser"/> so
/// it stays callable and testable independently of this GUI service.
/// </summary>
public sealed class ParsePreviewService : IParsePreviewService
{
    private readonly ISpinReportParser _parser;
    private readonly ILogger _logger;

    public ParsePreviewService(ISpinReportParser parser, ILoggerFactory loggerFactory)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = loggerFactory.CreateLogger<ParsePreviewService>();
    }

    public void Show(string spinReportPath)
    {
        try
        {
            var result = _parser.Parse(spinReportPath);

            _logger.LogInformation(
                "Parse preview for {Path}: status = {Status}, unresolved = {Unresolved}, issues = {IssueCount}",
                spinReportPath,
                result.Status,
                result.UnresolvedFieldCount,
                result.Issues.Count);

            if (result.Issues.Count > 0)
            {
                _logger.LogWarning(
                    "Parse preview issues for {Path}: {Issues}",
                    spinReportPath,
                    string.Join("; ", result.Issues.Select(issue =>
                        $"[{issue.Kind}] {(issue.Field?.ToString() ?? "Document")}: {issue.Message}")));
            }

            var dialog = new ParsePreviewWindow(result)
            {
                Owner = Application.Current?.MainWindow,
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parse preview failed for {Path}.", spinReportPath);
            MessageBox.Show(
                "The selected report could not be parsed for preview. See the application logs for details.",
                "Parse Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}