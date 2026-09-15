using System.IO;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// The batch execution engine. Responsibilities are limited to orchestration:
///
/// - Processes the workload snapshot strictly sequentially (no parallel writes).
/// - For each report: pre-flights the source, parses it, and decides whether the
///   record can be approved automatically. Automatic approval NEVER bypasses
///   validation — the approved object travels through the resolver/validator,
///   and identity resolution is used only to detect "no edits, no N/A fallback".
///   Any unresolved field, manual edit, N/A fallback, or blocking parser
///   diagnostic routes the report through the injected manual-resolution
///   callback.
/// - A cancelled manual resolution pauses/stops safely (nothing is rolled back
///   and no value is silently turned into "N/A"); a read/render/write failure
///   marks the item Failed and stops the run.
/// - Emits <see cref="BatchRunEvent"/>s for per-row progress, the audit trail,
///   and the end-of-batch summary. No GUI and no audit filesystem code lives
///   here.
/// </summary>
public sealed class ReportBatchProcessor : IBatchReportProcessor
{
    private readonly ISpinReportParser _parser;
    private readonly IReportRecordResolver _resolver;
    private readonly IReportLogTemplateRenderer _renderer;
    private readonly IReportLogWriter _writer;

    public ReportBatchProcessor(
        ISpinReportParser parser,
        IReportRecordResolver resolver,
        IReportLogTemplateRenderer renderer,
        IReportLogWriter writer)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<BatchRunSummary> RunAsync(
        BatchRunSettings settings,
        Func<SpinParseResult, ReportResolutionResult?> manualResolution,
        Action<BatchRunEvent> onEvent,
        CancellationToken cancellationToken = default)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (manualResolution is null) throw new ArgumentNullException(nameof(manualResolution));
        if (onEvent is null) throw new ArgumentNullException(nameof(onEvent));

        var items = settings.SourcePaths
            .Select((path, index) => new BatchRunItemResult(index + 1, path))
            .ToList();

        var renderDirectory = Path.Combine(Path.GetTempPath(), "RLB-Batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(renderDirectory);

        var manualResolutionCount = 0;

        try
        {
            onEvent(new BatchRunEvent(
                BatchRunEventKind.BatchStarted,
                stagedOrder: settings.SourcePaths.Select(path => Path.GetFileName(path) ?? path).ToList()));

            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[index];
                var (stopReason, manualResolutions) = await ProcessItemAsync(
                    settings,
                    item,
                    manualResolution,
                    onEvent,
                    renderDirectory,
                    cancellationToken);
                manualResolutionCount += manualResolutions;

                if (stopReason is BatchStopReason.Completed)
                    continue;

                onEvent(new BatchRunEvent(BatchRunEventKind.BatchStopped, message: item.Message));
                return BuildSummary(items, manualResolutionCount, stopReason, item.Message);
            }

            onEvent(new BatchRunEvent(BatchRunEventKind.BatchCompleted));
            return BuildSummary(items, manualResolutionCount, BatchStopReason.Completed);
        }
        catch (OperationCanceledException)
        {
            onEvent(new BatchRunEvent(BatchRunEventKind.BatchStopped, message: "Batch cancelled."));
            return BuildSummary(items, manualResolutionCount, BatchStopReason.StoppedByUser, "Batch cancelled.");
        }
        finally
        {
            TryDeleteDirectory(renderDirectory);
        }
    }

    /// <summary>
    /// Processes one snapshot item. Returns the stop reason for the run plus how
    /// many manual-resolution interactions this item required.
    /// </summary>
    private async Task<(BatchStopReason StopReason, int ManualResolutionCount)> ProcessItemAsync(
        BatchRunSettings settings,
        BatchRunItemResult item,
        Func<SpinParseResult, ReportResolutionResult?> manualResolution,
        Action<BatchRunEvent> onEvent,
        string renderDirectory,
        CancellationToken cancellationToken)
    {
        onEvent(new BatchRunEvent(BatchRunEventKind.ReportStarted, item.OrderIndex, item.SourcePath));

        if (!File.Exists(item.SourcePath))
        {
            item.MarkFailed("The source report file no longer exists and cannot be read.");
            onEvent(new BatchRunEvent(BatchRunEventKind.ItemFailed, item.OrderIndex, item.SourcePath, message: item.Message));
            return (BatchStopReason.StoppedOnError, 0);
        }

        var parse = await Task.Run(() => _parser.Parse(item.SourcePath), cancellationToken);

        if (parse.Status == SpinParseStatus.Failed)
        {
            item.MarkFailed("The source report could not be read as a valid Word document.");
            onEvent(new BatchRunEvent(BatchRunEventKind.ItemFailed, item.OrderIndex, item.SourcePath, message: item.Message));
            return (BatchStopReason.StoppedOnError, 0);
        }

        var resolution = _resolver.Resolve(parse.Record, IdentityValues(parse.Record), parse.Issues);

        var autoApproved = parse.Status == SpinParseStatus.Parsed
            && !parse.Issues.Any(issue => issue.IsBlocking)
            && resolution.IsApproved
            && resolution.ManuallyEditedFields.Count == 0
            && resolution.NaFallbackFields.Count == 0;

        if (autoApproved)
        {
            onEvent(new BatchRunEvent(
                BatchRunEventKind.AutoApproved,
                item.OrderIndex,
                item.SourcePath,
                resolution.ValidatedRecord!.ReportNumber));
            return (await RenderAndAppendAsync(settings, item, resolution, onEvent, renderDirectory, cancellationToken), 0);
        }

        onEvent(new BatchRunEvent(BatchRunEventKind.NeedsInput, item.OrderIndex, item.SourcePath));

        var manual = manualResolution(parse);
        if (manual is null || manual.ValidatedRecord is null)
        {
            item.MarkNeedsInput();
            onEvent(new BatchRunEvent(
                BatchRunEventKind.ManualResolutionCancelled,
                item.OrderIndex,
                item.SourcePath));
            return (BatchStopReason.StoppedByUser, 1);
        }

        resolution = manual;
        onEvent(new BatchRunEvent(
            BatchRunEventKind.ManualResolutionCompleted,
            item.OrderIndex,
            item.SourcePath,
            resolution.ValidatedRecord!.ReportNumber,
            manualFields: resolution.ManuallyEditedFields,
            naFallbackFields: resolution.NaFallbackFields));

        return (await RenderAndAppendAsync(settings, item, resolution, onEvent, renderDirectory, cancellationToken), 1);
    }

    private async Task<BatchStopReason> RenderAndAppendAsync(
        BatchRunSettings settings,
        BatchRunItemResult item,
        ReportResolutionResult resolution,
        Action<BatchRunEvent> onEvent,
        string renderDirectory,
        CancellationToken cancellationToken)
    {
        var record = resolution.ValidatedRecord!;
        onEvent(new BatchRunEvent(
            BatchRunEventKind.Validated,
            item.OrderIndex,
            item.SourcePath,
            record.ReportNumber));

        var renderedPath = Path.Combine(renderDirectory, $"entry-{item.OrderIndex:D3}.docx");
        onEvent(new BatchRunEvent(BatchRunEventKind.Writing, item.OrderIndex, item.SourcePath, record.ReportNumber));

        var render = await Task.Run(
            () => _renderer.Render(settings.TemplatePath, record, renderedPath),
            cancellationToken);

        if (!render.Success)
        {
            item.MarkFailed($"The report-log entry could not be rendered: {render.Message}");
            onEvent(new BatchRunEvent(BatchRunEventKind.ItemFailed, item.OrderIndex, item.SourcePath, record.ReportNumber, message: item.Message));
            return BatchStopReason.StoppedOnError;
        }

        var write = await Task.Run(
            () => _writer.Append(settings.ReportLogPath, renderedPath),
            cancellationToken);

        if (!write.Success)
        {
            item.MarkFailed($"The entry could not be appended to the report log: {write.Message}");
            onEvent(new BatchRunEvent(BatchRunEventKind.ItemFailed, item.OrderIndex, item.SourcePath, record.ReportNumber, message: item.Message));
            return BatchStopReason.StoppedOnError;
        }

        item.MarkCompleted(record.ReportNumber, write.BackupPath!);
        onEvent(new BatchRunEvent(
            BatchRunEventKind.WriteSucceeded,
            item.OrderIndex,
            item.SourcePath,
            record.ReportNumber,
            write.BackupPath));

        return BatchStopReason.Completed;
    }

    /// <summary>
    /// The identity value map used to detect automatic approval: the parsed
    /// values re-submitted unchanged. Only a resolution that is approved with NO
    /// manual edits and NO "N/A" fallback is eligible for automatic approval.
    /// </summary>
    private static IReadOnlyDictionary<ReportField, string?> IdentityValues(ReportRecord record) =>
        new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = record.ReportNumber,
            [ReportField.InspectionDate] = record.InspectionDate?.ToString("MM/dd/yyyy"),
            [ReportField.InspectorFirstName] = record.InspectorFirstName,
            [ReportField.DescriptionOfWork] = record.DescriptionOfWork,
            [ReportField.DrawingReferences] = record.DrawingReferences,
            [ReportField.GeneralObservations] = record.GeneralObservations,
            [ReportField.Discrepancies] = record.Discrepancies,
            [ReportField.PreviousDiscrepancyCorrections] = record.PreviousDiscrepancyCorrections,
        };

    private static BatchRunSummary BuildSummary(
        IReadOnlyList<BatchRunItemResult> items,
        int manualResolutionCount,
        BatchStopReason stopReason,
        string? errorMessage = null) =>
        new(items, manualResolutionCount, stopReason, errorMessage);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the batch render directory.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}