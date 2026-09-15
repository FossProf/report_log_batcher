using System.Security.Cryptography;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Batch;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.Infrastructure.Tests;

/// <summary>
/// Slice 8: batch orchestration. Happy paths use the real parser/renderer/writer
/// against manufactured documents; failure and manual-resolution paths use
/// scripted fakes so no UI is ever required.
/// </summary>
public sealed class ReportBatchProcessorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SpinDocFactory _spinFactory;
    private readonly ReportLogDocumentFactory _logFactory;
    private readonly IReportRecordResolver _resolver = new ReportRecordResolver(new ReportRecordValidator());

    public ReportBatchProcessorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-BatchProcessorTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _spinFactory = new SpinDocFactory(_tempRoot);
        _logFactory = new ReportLogDocumentFactory(_tempRoot);
    }

    [Fact]
    public void CleanStandardSpin_CompletesWithoutManualResolutionDialog()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(
            new[] { spin },
            (template, log),
            parse => { manualCalls++; return ManualComplete(parse); },
            events.Add);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(0, summary.ManualResolutionCount);
        Assert.Equal(0, manualCalls);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.AutoApproved);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.WriteSucceeded);
        AssertSingleEntry(log, "101");
    }

    [Fact]
    public void CleanStandardSpin_AutoApprovalIdentityResolution_HasNoEditsAndNoFallbacks()
    {
        var spin = CreateSpin("clean.docx", "102");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var summary = Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        var approved = events.Single(e => e.Kind == BatchRunEventKind.AutoApproved);
        Assert.Equal("102", approved.ReportNumber);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.NeedsInput);
    }

    [Fact]
    public void NonBlockingInformationalIssue_StillAutoApprovesNoManualPause()
    {
        var record = CompleteRecord("103");
        var spin = StubSource("with-info-issue.docx");
        var parser = StubParser(path => new SpinParseResult(
            record,
            SpinParseStatus.Parsed,
            path,
            new[] { new ParseIssue(ParseIssueKind.Missing, null, "Informational: no photo documentation section found.") { IsBlocking = false } }));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(new[] { spin }, (template, log), parse => { manualCalls++; return ManualComplete(parse); }, events.Add, parser);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(0, manualCalls);
        Assert.Equal(0, summary.ManualResolutionCount);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.AutoApproved);
        AssertSingleEntry(log, "103");
    }

    [Fact]
    public void BlockingIssue_RoutesToManualResolution_AndCompletes()
    {
        var record = CompleteRecord("201");
        var spin = StubSource("with-blocking-issue.docx");
        var parser = StubParser(path => new SpinParseResult(
            record,
            SpinParseStatus.Parsed,
            path,
            new[] { new ParseIssue(ParseIssueKind.InvalidFormat, ReportField.ReportNumber, "Report number format did not match.") }));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(new[] { spin }, (template, log), parse => { manualCalls++; return ManualComplete(parse); }, events.Add, parser);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(1, summary.ManualResolutionCount);
        Assert.Equal(1, manualCalls);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.NeedsInput);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ManualResolutionCompleted);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.AutoApproved);
        AssertSingleEntry(log, "201");
    }

    [Fact]
    public void UnresolvedField_NoSilentNA_AutoApprovalForbidden()
    {
        var record = CompleteRecord("301") with { ReportNumber = null };
        var spin = StubSource("blank-number.docx");
        var parser = StubParser(path => new SpinParseResult(record, SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>()));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(new[] { spin }, (template, log), parse =>
        {
            manualCalls++;
            var values = FillBlanks(parse.Record);
            values[ReportField.ReportNumber] = "301";
            return _resolver.Resolve(parse.Record, values, parse.Issues);
        }, events.Add, parser);

        Assert.Equal(1, manualCalls);
        Assert.Equal(1, summary.ManualResolutionCount);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.AutoApproved);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ManualResolutionCompleted);
        AssertSingleEntry(log, "301");
    }

    [Fact]
    public void UnresolvedField_ManualCancelled_NeedsInput_NoRecordCommitted()
    {
        var record = CompleteRecord() with { ReportNumber = null };
        var spin = StubSource("blank-number.docx");
        var parser = StubParser(path => new SpinParseResult(record, SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>()));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var summary = Run(new[] { spin }, (template, log), parse => null, events.Add, parser);

        Assert.Equal(BatchStopReason.StoppedByUser, summary.StopReason);
        Assert.Equal(1, summary.NeedsInputCount);
        Assert.Equal(0, summary.CompletedCount);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ManualResolutionCancelled);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.BatchStopped);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.WriteSucceeded);
        AssertLogUnchanged(log, Array.Empty<string>());
    }

    [Fact]
    public void MixedRun_AutoAndManual_OnlyManualCountsInSummary()
    {
        var clean = StubSource("clean.docx");
        var blocked = StubSource("blocked.docx");
        var parser = StubParser(path =>
        {
            if (Path.GetFileName(path) == "blocked.docx")
                return new SpinParseResult(
                    CompleteRecord("401"),
                    SpinParseStatus.Parsed,
                    path,
                    new[] { new ParseIssue(ParseIssueKind.Ambiguous, ReportField.ReportNumber, "Number found twice.") });
            return new SpinParseResult(CompleteRecord("101"), SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>());
        });
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(new[] { clean, blocked }, (template, log), parse => { manualCalls++; return ManualComplete(parse); }, events.Add, parser);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(1, summary.ManualResolutionCount);
        Assert.Equal(1, manualCalls);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.AutoApproved);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.NeedsInput);
        AssertContainsEntryNumberOrder(log, "101", "401");
    }

    [Fact]
    public void ThreeReports_AppendedInExactStagedOrder()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var c = CreateSpin("c.docx", "303");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var summary = Run(new[] { a, b, c }, (template, log), parse => ManualComplete(parse), events.Add);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(3, summary.CompletedCount);
        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(0, summary.NeedsInputCount);
        AssertContainsEntryNumberOrder(log, "101", "202", "303");
        Assert.Equal(
            new[] { "a.docx", "b.docx", "c.docx" },
            summary.Items.Select(item => Path.GetFileName(item.SourcePath)));
    }

    [Fact]
    public void AutoApprovedItem_EmitsExpectedEventSequence()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add);

        var itemEvents = events.Where(e => e.OrderIndex == 1 && e.Kind != BatchRunEventKind.BatchStarted && e.Kind != BatchRunEventKind.BatchCompleted).ToList();
        Assert.Equal(
            new[]
            {
                BatchRunEventKind.ReportStarted,
                BatchRunEventKind.AutoApproved,
                BatchRunEventKind.Validated,
                BatchRunEventKind.Writing,
                BatchRunEventKind.WriteSucceeded,
            },
            itemEvents.Select(e => e.Kind));
        Assert.Equal("101", itemEvents.Last().ReportNumber);
        var succeeded = itemEvents.Last();
        Assert.NotNull(succeeded.BackupPath);
        Assert.True(File.Exists(succeeded.BackupPath));
    }

    [Fact]
    public void ManualItem_EmitsExpectedEventSequence()
    {
        var record = CompleteRecord("201");
        var spin = StubSource("blocked.docx");
        var parser = StubParser(path => new SpinParseResult(
            record,
            SpinParseStatus.Parsed,
            path,
            new[] { new ParseIssue(ParseIssueKind.Missing, ReportField.ReportNumber, "Number not found.") }));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add, parser);

        var itemEvents = events.Where(e => e.OrderIndex == 1 && e.Kind != BatchRunEventKind.BatchStarted && e.Kind != BatchRunEventKind.BatchCompleted).ToList();
        Assert.Equal(
            new[]
            {
                BatchRunEventKind.ReportStarted,
                BatchRunEventKind.NeedsInput,
                BatchRunEventKind.ManualResolutionCompleted,
                BatchRunEventKind.Validated,
                BatchRunEventKind.Writing,
                BatchRunEventKind.WriteSucceeded,
            },
            itemEvents.Select(e => e.Kind));
    }

    [Fact]
    public void BatchStartedEvent_CarriesFullStagedOrderFileNames()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        Run(new[] { a, b }, (template, log), parse => ManualComplete(parse), events.Add);

        var started = events.Single(e => e.Kind == BatchRunEventKind.BatchStarted);
        Assert.Equal(new[] { "a.docx", "b.docx" }, started.StagedOrder);
    }

    [Fact]
    public void MissingSourceFile_FailsAndStops_BeforeAnyWrite()
    {
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var missing = Path.Combine(_tempRoot, "does-not-exist.docx");

        var summary = Run(new[] { missing }, (template, log), parse => ManualComplete(parse), events.Add);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(0, summary.CompletedCount);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.WriteSucceeded);
        AssertLogUnchanged(log, Array.Empty<string>());
    }

    [Fact]
    public void ParseFailed_StopsBeforeAnyWrite()
    {
        var spin = StubSource("corrupt.docx");
        var parser = StubParser(path => new SpinParseResult(new ReportRecord(), SpinParseStatus.Failed, path, Array.Empty<ParseIssue>()));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var summary = Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add, parser);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(1, summary.FailedCount);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.WriteSucceeded);
        AssertLogUnchanged(log, Array.Empty<string>());
    }

    [Fact]
    public void RenderFailure_Stops_PriorEntriesIntact_FailureReported()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var inner = new ReportLogTemplateRenderer();
        IReportLogTemplateRenderer renderer = new SequenceFailRenderer(inner, failOnCall: 2);

        var summary = Run(new[] { a, b }, (template, log), parse => ManualComplete(parse), events.Add, renderer: renderer);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.NotNull(summary.ErrorMessage);
        var failed = summary.Items.Single(item => item.Status == BatchEntryStatus.Failed);
        Assert.Contains("render", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.BatchCompleted);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ItemFailed);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public void WriteFailure_FirstEntry_DestinationByteIdentical_NoLeftoverFiles()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var originalBytes = File.ReadAllBytes(log);
        IReportLogWriter writer = new AlwaysFailWriter();

        var summary = Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add, writer: writer);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(0, summary.CompletedCount);
        Assert.Equal(originalBytes, File.ReadAllBytes(log));
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.Writing);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.WriteSucceeded);
        Assert.Empty(Directory.GetFiles(_tempRoot, "*.backup-*"));
        Assert.Empty(Directory.GetFiles(_tempRoot, "*.working.docx"));
    }

    [Fact]
    public void WriteFailure_SecondEntry_PriorEntryIntact()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        IReportLogWriter writer = new SequenceFailWriter(new ReportLogWriter(), failOnCall: 2);

        var summary = Run(new[] { a, b }, (template, log), parse => ManualComplete(parse), events.Add, writer: writer);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.FailedCount);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public void ManualCancelledAtSecond_FirstCommitted_LaterUntouched()
    {
        var clean = StubSource("clean.docx");
        var blocked = StubSource("blocked.docx");
        var later = StubSource("later.docx");
        var parser = StubParser(path =>
            Path.GetFileName(path) == "blocked.docx"
                ? new SpinParseResult(CompleteRecord("501") with { ReportNumber = null }, SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>())
                : new SpinParseResult(CompleteRecord("101"), SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>()));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        var manualCalls = 0;

        var summary = Run(new[] { clean, blocked, later }, (template, log), parse =>
        {
            manualCalls++;
            return Path.GetFileName(parse.SourcePath) == "blocked.docx" ? null : ManualComplete(parse);
        }, events.Add, parser);

        Assert.Equal(BatchStopReason.StoppedByUser, summary.StopReason);
        Assert.Equal(1, manualCalls);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.NeedsInputCount);
        Assert.Equal(1, summary.RemainingCount);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.ItemFailed);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ManualResolutionCancelled);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public void StopOnError_EmitsBatchStopped_AndNoBatchCompleted()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        IReportLogWriter writer = new AlwaysFailWriter();

        var summary = Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add, writer: writer);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.BatchStopped);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.BatchCompleted);
        Assert.Contains("write", summary.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteFailureAtSecondItem_StopsBatch_ThirdItemNeverTouched()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var c = CreateSpin("c.docx", "303");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var countingParser = new CountingParser(new SpinReportParser());
        var countingWriter = new CountingWriter(new SequenceFailWriter(new ReportLogWriter(), failOnCall: 2));

        var summary = Run(
            new[] { a, b, c },
            (template, log),
            parse => ManualComplete(parse),
            events.Add,
            parser: countingParser,
            writer: countingWriter);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(
            new[] { BatchEntryStatus.Complete, BatchEntryStatus.Failed, BatchEntryStatus.Pending },
            summary.Items.Select(item => item.Status));
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(1, summary.RemainingCount);
        Assert.Equal(2, countingParser.Calls);
        Assert.Equal(2, countingWriter.Calls);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.BatchCompleted);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public void RenderFailureAtSecondItem_ThirdItemNeverRenderedOrWritten()
    {
        var a = CreateSpin("a.docx", "101");
        var b = CreateSpin("b.docx", "202");
        var c = CreateSpin("c.docx", "303");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var countingParser = new CountingParser(new SpinReportParser());
        var countingRenderer = new CountingRenderer(new SequenceFailRenderer(new ReportLogTemplateRenderer(), failOnCall: 2));
        var countingWriter = new CountingWriter(new ReportLogWriter());

        var summary = Run(
            new[] { a, b, c },
            (template, log),
            parse => ManualComplete(parse),
            events.Add,
            parser: countingParser,
            renderer: countingRenderer,
            writer: countingWriter);

        Assert.Equal(BatchStopReason.StoppedOnError, summary.StopReason);
        Assert.Equal(
            new[] { BatchEntryStatus.Complete, BatchEntryStatus.Failed, BatchEntryStatus.Pending },
            summary.Items.Select(item => item.Status));
        Assert.Equal(2, countingParser.Calls);
        Assert.Equal(2, countingRenderer.Calls);
        Assert.Equal(1, countingWriter.Calls);
        var failed = summary.Items.Single(item => item.Status == BatchEntryStatus.Failed);
        Assert.Contains("render", failed.Message, StringComparison.OrdinalIgnoreCase);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public void ManualCancelAtSecondItem_ThirdItemNeverTouched_NoNaFabricated()
    {
        var a = StubSource("a.docx");
        var b = StubSource("b.docx");
        var c = StubSource("c.docx");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var parser = StubParser(path =>
            Path.GetFileName(path) == "b.docx"
                ? new SpinParseResult(CompleteRecord("202") with { ReportNumber = null }, SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>())
                : new SpinParseResult(CompleteRecord("101"), SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>()));
        var countingParser = new CountingParser(parser);
        var countingRenderer = new CountingRenderer(new ReportLogTemplateRenderer());
        var countingWriter = new CountingWriter(new ReportLogWriter());
        var manualCalls = 0;

        var summary = Run(
            new[] { a, b, c },
            (template, log),
            parse =>
            {
                manualCalls++;
                return Path.GetFileName(parse.SourcePath) == "b.docx" ? null : ManualComplete(parse);
            },
            events.Add,
            parser: countingParser,
            renderer: countingRenderer,
            writer: countingWriter);

        Assert.Equal(BatchStopReason.StoppedByUser, summary.StopReason);
        Assert.Equal(1, manualCalls);
        Assert.Equal(
            new[] { BatchEntryStatus.Complete, BatchEntryStatus.NeedsInput, BatchEntryStatus.Pending },
            summary.Items.Select(item => item.Status));
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.NeedsInputCount);
        Assert.Equal(1, summary.RemainingCount);
        Assert.Equal(2, countingParser.Calls);
        Assert.Equal(1, countingRenderer.Calls);
        Assert.Equal(1, countingWriter.Calls);
        Assert.Contains(events, e => e.Kind == BatchRunEventKind.ManualResolutionCancelled);
        Assert.DoesNotContain(events, e => e.NaFallbackFields.Count > 0);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.WriteSucceeded && e.OrderIndex > 1);
        Assert.DoesNotContain(events, e => e.Kind == BatchRunEventKind.BatchCompleted);
        AssertContainsEntryNumberOrder(log, "101");
    }

    [Fact]
    public async Task CancellationToken_PreCancelled_StopsWithoutWork()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var summary = await CreateProcessor().RunAsync(
            new BatchRunSettings(new[] { spin }, log, template),
            parse => ManualComplete(parse),
            events.Add,
            cts.Token);

        Assert.Equal(BatchStopReason.StoppedByUser, summary.StopReason);
        Assert.Equal(0, summary.CompletedCount);
        AssertLogUnchanged(log, Array.Empty<string>());
    }

    [Fact]
    public void EmptySnapshot_CompletesImmediately()
    {
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        var summary = Run(Array.Empty<string>(), (template, log), parse => ManualComplete(parse), events.Add);

        Assert.Equal(BatchStopReason.Completed, summary.StopReason);
        Assert.Equal(0, summary.CompletedCount);
        Assert.Equal(new[] { BatchRunEventKind.BatchStarted, BatchRunEventKind.BatchCompleted }, events.Select(e => e.Kind));
    }

    [Fact]
    public void TempRenderDirectory_RemovedAfterRun()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var before = BatchRenderDirs(_tempRoot);

        Run(new[] { spin }, (template, log), parse => ManualComplete(parse), _ => { });

        Assert.Equal(before, BatchRenderDirs(_tempRoot));
    }

    [Fact]
    public void SourcesAndTemplate_ByteUnchanged_OnlyDestinationChanged()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var spinHash = Sha256Of(spin);
        var templateHash = Sha256Of(template);
        var logHash = Sha256Of(log);

        Run(new[] { spin }, (template, log), parse => ManualComplete(parse), _ => { });

        Assert.Equal(spinHash, Sha256Of(spin));
        Assert.Equal(templateHash, Sha256Of(template));
        Assert.NotEqual(logHash, Sha256Of(log));
    }

    [Fact]
    public void ManualEdit_ManualResolutionCompletedEventCarriesEditedField()
    {
        var record = CompleteRecord("201");
        var spin = StubSource("blocked.docx");
        var parser = StubParser(path => new SpinParseResult(
            record,
            SpinParseStatus.Parsed,
            path,
            new[] { new ParseIssue(ParseIssueKind.Missing, ReportField.ReportNumber, "Number not found.") }));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        Run(new[] { spin }, (template, log), parse =>
        {
            var values = FillBlanks(parse.Record);
            values[ReportField.ReportNumber] = "777";
            return _resolver.Resolve(parse.Record, values, parse.Issues);
        }, events.Add, parser);

        var completed = events.Single(e => e.Kind == BatchRunEventKind.ManualResolutionCompleted);
        Assert.Contains(ReportField.ReportNumber, completed.ManualFields);
        Assert.Equal("777", completed.ReportNumber);
    }

    [Fact]
    public void ManualFallback_ManualResolutionCompletedEventCarriesNaField()
    {
        var record = CompleteRecord("201") with { DescriptionOfWork = null };
        var spin = StubSource("blank-dow.docx");
        var parser = StubParser(path => new SpinParseResult(record, SpinParseStatus.Parsed, path, Array.Empty<ParseIssue>()));
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var events = new List<BatchRunEvent>();

        Run(new[] { spin }, (template, log), parse =>
        {
            var values = FillBlanks(parse.Record);
            values[ReportField.DescriptionOfWork] = null;
            return _resolver.Resolve(parse.Record, values, parse.Issues);
        }, events.Add, parser);

        var completed = events.Single(e => e.Kind == BatchRunEventKind.ManualResolutionCompleted);
        Assert.Contains(ReportField.DescriptionOfWork, completed.NaFallbackFields);
        Assert.Contains("N/A", WordDocumentInspector.Inspect(log).ParagraphTexts);
    }

    [Fact]
    public void AutoApprovedEntry_RealBackupCreated_OnDisk()
    {
        var spin = CreateSpin("clean.docx", "101");
        var (template, log) = CreateDestinations("template.docx", "log.docx");
        var backupsBefore = BackupFiles(_tempRoot);
        var events = new List<BatchRunEvent>();

        Run(new[] { spin }, (template, log), parse => ManualComplete(parse), events.Add);

        var backups = BackupFiles(_tempRoot);
        Assert.Equal(backupsBefore.Count + 1, backups.Count);
        var succeeded = events.Single(e => e.Kind == BatchRunEventKind.WriteSucceeded);
        Assert.NotNull(succeeded.BackupPath);
        Assert.True(File.Exists(succeeded.BackupPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort test cleanup.
        }
    }

    // ---- Engines and helpers ------------------------------------------------

    private IBatchReportProcessor CreateProcessor(
        ISpinReportParser? parser = null,
        IReportLogTemplateRenderer? renderer = null,
        IReportLogWriter? writer = null) =>
        new ReportBatchProcessor(
            parser ?? new SpinReportParser(),
            _resolver,
            renderer ?? new ReportLogTemplateRenderer(),
            writer ?? new ReportLogWriter());

    private BatchRunSummary Run(
        IReadOnlyList<string> sources,
        (string Template, string Log) destinations,
        Func<SpinParseResult, ReportResolutionResult?> manualResolution,
        Action<BatchRunEvent> onEvent,
        ISpinReportParser? parser = null,
        IReportLogTemplateRenderer? renderer = null,
        IReportLogWriter? writer = null)
    {
        var summary = CreateProcessor(parser, renderer, writer).RunAsync(
            new BatchRunSettings(sources, destinations.Log, destinations.Template),
            manualResolution,
            onEvent,
            CancellationToken.None);
        return summary.GetAwaiter().GetResult();
    }

    private ReportResolutionResult? ManualComplete(SpinParseResult parse) =>
        _resolver.Resolve(parse.Record, FillBlanks(parse.Record), parse.Issues);

    private static Dictionary<ReportField, string?> FillBlanks(ReportRecord record) =>
        new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = record.ReportNumber ?? "319",
            [ReportField.InspectionDate] = record.InspectionDate?.ToString("MM/dd/yyyy") ?? "09/11/2026",
            [ReportField.InspectorFirstName] = record.InspectorFirstName ?? "Anthony",
            [ReportField.DescriptionOfWork] = record.DescriptionOfWork ?? "Inspection work description.",
            [ReportField.DrawingReferences] = record.DrawingReferences ?? "Sheet S1",
            [ReportField.GeneralObservations] = record.GeneralObservations ?? "General observations note.",
            [ReportField.Discrepancies] = record.Discrepancies ?? "N/A",
            [ReportField.PreviousDiscrepancyCorrections] = record.PreviousDiscrepancyCorrections ?? "No previous discrepancies.",
        };

    private static ReportRecord CompleteRecord(string reportNumber = "319") => new()
    {
        ReportNumber = reportNumber,
        InspectionDate = new DateOnly(2026, 9, 11),
        InspectorFirstName = "Anthony",
        DescriptionOfWork = "Inspect assigned phase of the structural repairs work.",
        DrawingReferences = "Sheet S6",
        GeneralObservations = "Demolition at Column Line 6 reached 25%.",
        Discrepancies = "N/A",
        PreviousDiscrepancyCorrections = "Repair area was cleaned.",
    };

    private string CreateSpin(string fileName, string reportNumber)
    {
        _spinFactory.Create(fileName, doc =>
        {
            doc.Paragraph("Special Inspection Report #" + reportNumber);
            doc.Table(
                ["Project Name:", "CMF Structural Repairs SPIN", "Inspection Date:", "2026-09-11"],
                ["Cornerstone Inspector(s):", "Anthony Wintergerst"]);
            doc.Paragraph(ReportTemplateContract.DescriptionOfWorkHeading);
            doc.Paragraph("Inspect assigned phase of the structural repairs work.");
            doc.Paragraph(ReportTemplateContract.DrawingReferencesHeading);
            doc.Paragraph("Sheet S6");
            doc.Paragraph(ReportTemplateContract.GeneralObservationsHeading);
            doc.Paragraph("Demolition at Column Line 6 reached 25%.[BR]Grout temperatures were periodically monitored.");
            doc.Paragraph(ReportTemplateContract.DiscrepanciesHeading);
            doc.Paragraph("N/A");
            doc.Paragraph(ReportTemplateContract.PreviousDiscrepancyCorrectionsHeading);
            doc.Paragraph("Repair area was cleaned; the appearance remained distributed.");
            doc.Paragraph("To the best of my knowledge, work inspected was in accordance with approved drawings.");
        });
        return Path.Combine(_tempRoot, fileName);
    }

    private (string Template, string Log) CreateDestinations(string templateName, string logName)
    {
        var template = _logFactory.CreateTemplate(templateName);
        var log = _logFactory.CreateReportLog(logName);
        return (template, log);
    }

    /// <summary>Creates a source file whose content is irrelevant (used for scripted parsers).</summary>
    private string StubSource(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    private static ISpinReportParser StubParser(Func<string, SpinParseResult> handler) =>
        new StubParserImpl(handler);

    private void AssertSingleEntry(string log, string reportNumber)
    {
        var paragraphs = WordDocumentInspector.Inspect(log).ParagraphTexts;
        var headers = paragraphs.Where(text => text.StartsWith("Report #", StringComparison.Ordinal)).ToList();
        Assert.Single(headers);
        Assert.Equal($"Report #{reportNumber} \u2013 09/11/26\u2013 Anthony", headers[0]);
    }

    private void AssertContainsEntryNumberOrder(string log, params string[] reportNumbers)
    {
        var paragraphs = WordDocumentInspector.Inspect(log).ParagraphTexts;
        var headers = paragraphs
            .Where(text => text.StartsWith("Report #", StringComparison.Ordinal))
            .Select(text => text.Substring("Report #".Length).Split(' ')[0])
            .ToList();
        Assert.Equal(reportNumbers, headers);
    }

    private void AssertLogUnchanged(string log, IReadOnlyList<string> expectedParagraphs)
    {
        var inspection = WordDocumentInspector.Inspect(log);
        Assert.Equal(expectedParagraphs, inspection.ParagraphTexts.Where(text => !string.IsNullOrEmpty(text)).ToList());
    }

    private static List<string> BatchRenderDirs(string tempRoot) =>
        Directory.GetDirectories(Path.GetTempPath(), "RLB-Batch-*").ToList();

    private static List<string> BackupFiles(string tempRoot) =>
        Directory.GetFiles(tempRoot, "*.backup-*").ToList();

    private static string Sha256Of(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    // ---- Scripted fakes ------------------------------------------------------

    private sealed class StubParserImpl : ISpinReportParser
    {
        private readonly Func<string, SpinParseResult> _handler;

        public StubParserImpl(Func<string, SpinParseResult> handler) => _handler = handler;

        public SpinParseResult Parse(string spinReportPath) => _handler(spinReportPath);
    }

    private sealed class CountingParser : ISpinReportParser
    {
        private readonly ISpinReportParser _inner;

        public CountingParser(ISpinReportParser inner) => _inner = inner;

        public int Calls { get; private set; }

        public SpinParseResult Parse(string spinReportPath)
        {
            Calls++;
            return _inner.Parse(spinReportPath);
        }
    }

    private sealed class CountingRenderer : IReportLogTemplateRenderer
    {
        private readonly IReportLogTemplateRenderer _inner;

        public CountingRenderer(IReportLogTemplateRenderer inner) => _inner = inner;

        public int Calls { get; private set; }

        public ReportTemplateRenderResult Render(string templatePath, ValidatedReportRecord record, string outputPath)
        {
            Calls++;
            return _inner.Render(templatePath, record, outputPath);
        }
    }

    private sealed class CountingWriter : IReportLogWriter
    {
        private readonly IReportLogWriter _inner;

        public CountingWriter(IReportLogWriter inner) => _inner = inner;

        public int Calls { get; private set; }

        public ReportLogWriteResult Append(string reportLogPath, string renderedEntryPath)
        {
            Calls++;
            return _inner.Append(reportLogPath, renderedEntryPath);
        }
    }

    private sealed class AlwaysFailWriter : IReportLogWriter
    {
        public ReportLogWriteResult Append(string reportLogPath, string renderedEntryPath) =>
            new(false, reportLogPath, null, ReportLogWriteErrorKind.AppendFailed, "Simulated write failure.");
    }

    private sealed class SequenceFailWriter : IReportLogWriter
    {
        private readonly IReportLogWriter _inner;
        private readonly int _failOnCall;
        private int _calls;

        public SequenceFailWriter(IReportLogWriter inner, int failOnCall)
        {
            _inner = inner;
            _failOnCall = failOnCall;
        }

        public ReportLogWriteResult Append(string reportLogPath, string renderedEntryPath)
        {
            _calls++;
            if (_calls == _failOnCall)
                return new ReportLogWriteResult(false, reportLogPath, null, ReportLogWriteErrorKind.AppendFailed, "Simulated write failure.");
            return _inner.Append(reportLogPath, renderedEntryPath);
        }
    }

    private sealed class SequenceFailRenderer : IReportLogTemplateRenderer
    {
        private readonly IReportLogTemplateRenderer _inner;
        private readonly int _failOnCall;
        private int _calls;

        public SequenceFailRenderer(IReportLogTemplateRenderer inner, int failOnCall)
        {
            _inner = inner;
            _failOnCall = failOnCall;
        }

        public ReportTemplateRenderResult Render(string templatePath, ValidatedReportRecord record, string outputPath)
        {
            _calls++;
            if (_calls == _failOnCall)
                return new ReportTemplateRenderResult(false, null, ReportTemplateRenderErrorKind.Unexpected, "Simulated render failure.");
            return _inner.Render(templatePath, record, outputPath);
        }
    }
}