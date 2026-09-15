using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using ReportLogBatcher.App.Services;
using ReportLogBatcher.App.ViewModels;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;
using ReportLogBatcher.Infrastructure.Batch;
using ReportLogBatcher.Infrastructure.Word;

namespace ReportLogBatcher.App.Tests;

/// <summary>
/// Slice 8 ViewModel tests. No WPF windows or message boxes are ever created:
/// dialogs and user interactions are scripted fakes. The batch engine's manual
/// resolution delegate is captured but deliberately never invoked (invoking it
/// would open the real resolution window); the delegate's absence of calls on
/// the automatic path is covered by the engine tests.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly ScriptedDialogService _dialogs = new();
    private readonly ScriptedRenameDialog _renameDialog = new();
    private readonly ScriptedTemplateInspection _inspection = new();
    private readonly ScriptedUserInteraction _ui = new();
    private readonly ScriptedInitializer _initializer = new();
    private readonly ScriptedBatchProcessor _processor = new();
    private readonly MainWindowViewModel _vm;

    private string _logPath = string.Empty;
    private string _directoryPath = string.Empty;
    private string _templatePath = string.Empty;

    public MainWindowViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "RLB-AppTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _logPath = CreateDocx("log.docx");
        _templatePath = CreateDocx("template.docx");
        _directoryPath = Path.Combine(_root, "reports");
        Directory.CreateDirectory(_directoryPath);

        var loggerFactory = LoggerFactory.Create(builder => { });
        var audit = new ReportAppendAuditService(loggerFactory, auditDirectory: _root);

        _vm = new MainWindowViewModel(
            _dialogs,
            new SettingsService(Path.Combine(_root, "settings.json")),
            new BatchDiscoveryService(),
            new BatchFileService(),
            _renameDialog,
            _inspection,
            PreviewServiceNoop.Instance,
            ReviewServiceNoop.Instance,
            AppendServiceNoop.Instance,
            new ReportRecordResolver(new ReportRecordValidator()),
            _initializer,
            _processor,
            _ui,
            audit,
            loggerFactory);
    }

    // ---- §48: inclusion ------------------------------------------------------

    [Fact]
    public void AllRows_DefaultIncluded_True()
    {
        BuildBatch(3);

        Assert.Equal(3, _vm.BatchEntries.Count);
        Assert.All(_vm.BatchEntries, row => Assert.True(row.IsIncluded));
    }

    [Fact]
    public void SelectAll_IncludesAllEligibleRows()
    {
        BuildBatch(3);
        ClearAll();

        _vm.SelectAllCommand.Execute(null);

        Assert.All(_vm.BatchEntries, row => Assert.True(row.IsIncluded));
    }

    [Fact]
    public void ClearAll_ExcludesAllEligibleRows()
    {
        BuildBatch(3);

        _vm.ClearAllCommand.Execute(null);

        Assert.All(_vm.BatchEntries, row => Assert.False(row.IsIncluded));
    }

    [Fact]
    public void SelectAll_SkipsCompletedRows()
    {
        BuildBatch(3);
        _vm.BatchEntries[0].MarkComplete();
        _vm.ClearAllCommand.Execute(null);
        Assert.False(_vm.BatchEntries[1].IsIncluded);

        _vm.SelectAllCommand.Execute(null);

        Assert.True(_vm.BatchEntries[0].IsIncluded);
        Assert.True(_vm.BatchEntries[1].IsIncluded);
        Assert.True(_vm.BatchEntries[2].IsIncluded);
    }

    [Fact]
    public void ClearAll_SkipsCompletedRows()
    {
        BuildBatch(3);
        _vm.BatchEntries[0].MarkComplete();

        _vm.ClearAllCommand.Execute(null);

        Assert.True(_vm.BatchEntries[0].IsIncluded);
        Assert.False(_vm.BatchEntries[1].IsIncluded);
        Assert.False(_vm.BatchEntries[2].IsIncluded);
    }

    [Fact]
    public void CompletedRow_CheckboxDisabled()
    {
        BuildBatch(3);
        _vm.BatchEntries[1].MarkComplete();

        Assert.True(_vm.BatchEntries[0].IsIncludeEnabled);
        Assert.False(_vm.BatchEntries[1].IsIncludeEnabled);
        Assert.True(_vm.BatchEntries[2].IsIncludeEnabled);
    }

    [Fact]
    public void MoveUp_PreservesInclusionAndSelection()
    {
        BuildBatch(3);
        _vm.SelectedRow = _vm.BatchEntries[1];
        _vm.BatchEntries[1].IsIncluded = false;

        _vm.MoveUpCommand.Execute(null);

        Assert.Equal("R-02.docx", _vm.BatchEntries[0].FileName);
        Assert.False(_vm.BatchEntries[0].IsIncluded);
        Assert.Same(_vm.BatchEntries[0], _vm.SelectedRow);
    }

    [Fact]
    public void MoveDown_PreservesInclusion()
    {
        BuildBatch(3);
        _vm.SelectedRow = _vm.BatchEntries[0];
        _vm.BatchEntries[0].IsIncluded = false;

        _vm.MoveDownCommand.Execute(null);

        Assert.Equal("R-02.docx", _vm.BatchEntries[0].FileName);
        Assert.Equal("R-01.docx", _vm.BatchEntries[1].FileName);
        Assert.False(_vm.BatchEntries[1].IsIncluded);
    }

    [Fact]
    public void RenameRow_PreservesInclusionAndUpdatesFileName()
    {
        BuildBatch(3);
        _vm.SelectedRow = _vm.BatchEntries[1];
        _vm.BatchEntries[1].IsIncluded = false;
        _renameDialog.Next = "Renamed-02.docx";

        _vm.RenameFileCommand.Execute(null);

        Assert.Equal("Renamed-02.docx", _vm.BatchEntries[1].FileName);
        Assert.False(_vm.BatchEntries[1].IsIncluded);
        Assert.True(File.Exists(Path.Combine(_directoryPath, "Renamed-02.docx")));
        Assert.False(File.Exists(Path.Combine(_directoryPath, "R-02.docx")));
        Assert.True(File.Exists(Path.Combine(_directoryPath, "R-01.docx")));
    }

    [Fact]
    public void RemoveFromBatch_SourceFileUntouched_RowsRebuiltIncluded()
    {
        BuildBatch(3);
        var removedPath = Path.Combine(_directoryPath, "R-02.docx");
        _vm.SelectedRow = _vm.BatchEntries[1];

        _vm.RemoveFromBatchCommand.Execute(null);

        Assert.True(File.Exists(removedPath));
        Assert.Equal(2, _vm.BatchEntries.Count);
        Assert.All(_vm.BatchEntries, row => Assert.True(row.IsIncluded));
    }

    [Fact]
    public void ReloadBatch_ResetsInclusionAndProcessGuard()
    {
        BuildBatch(3);
        NoteProcessedRow(0);

        _vm.ClearAllCommand.Execute(null);
        _vm.ReloadBatchCommand.Execute(null);

        Assert.Equal(3, _vm.BatchEntries.Count);
        Assert.All(_vm.BatchEntries, row =>
        {
            Assert.True(row.IsIncluded);
            Assert.True(row.CanProcess);
        });
    }

    // ---- §51: batch commands and progress ------------------------------------

    [Fact]
    public void ProcessBatchCommand_Disabled_WhenNoValidSelections()
    {
        Assert.False(_vm.ProcessBatchCommand.CanExecute(null));
    }

    [Fact]
    public void ProcessBatchCommand_Enabled_WhenValidSelectionsAndEligibleRows()
    {
        SelectAllSelections();
        BuildBatch(2);

        Assert.True(_vm.ProcessBatchCommand.CanExecute(null));
    }

    [Fact]
    public void ProcessBatchCommand_Disabled_AfterExcludingAllRows()
    {
        SelectAllSelections();
        BuildBatch(2);
        Assert.True(_vm.ProcessBatchCommand.CanExecute(null));

        _vm.ClearAllCommand.Execute(null);

        Assert.False(_vm.ProcessBatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task ProcessBatch_Success_CompletesRows_ShowsSummary_ProgressAdvances()
    {
        SelectAllSelections();
        BuildBatch(2);
        _processor.Events.Add(new BatchRunEvent(BatchRunEventKind.WriteSucceeded, 1, _vm.BatchEntries[0].FullPath, "101", "C:\\backup1.docx"));
        _processor.Events.Add(new BatchRunEvent(BatchRunEventKind.WriteSucceeded, 2, _vm.BatchEntries[1].FullPath, "202", "C:\\backup2.docx"));

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        Assert.Equal(2, _vm.BatchEntries.Count(row => row.Status == BatchEntryStatus.Complete));
        Assert.Equal(2, _vm.BatchProgressValue);
        Assert.Equal(2, _vm.BatchProgressMaximum);
        Assert.Equal("Processed 2 of 2", _vm.BatchProgressText);
        Assert.True(_ui.ConfirmStartCalled);
        Assert.Same(_processor.Result, _ui.ShownSummary);
        Assert.Equal(_logPath, _ui.ShownSummaryLogPath);
        Assert.NotNull(_processor.CapturedManualResolution);
        Assert.True(_processor.CapturedManualResolution is not null && !ManualResolutionWasInvoked());
    }

    [Fact]
    public async Task ProcessBatch_NeedsInputEvent_MarksRowNeedsInput()
    {
        SelectAllSelections();
        BuildBatch(1);
        _processor.Events.Add(new BatchRunEvent(BatchRunEventKind.NeedsInput, 1, _vm.BatchEntries[0].FullPath));

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        Assert.Equal(BatchEntryStatus.NeedsInput, _vm.BatchEntries[0].Status);
    }

    [Fact]
    public async Task ProcessBatch_FailureEvent_MarksRowFailed_ProgressAdvances()
    {
        SelectAllSelections();
        BuildBatch(1);
        _processor.Events.Add(new BatchRunEvent(BatchRunEventKind.ItemFailed, 1, _vm.BatchEntries[0].FullPath, "101", message: "simulated failure"));

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        Assert.Equal(BatchEntryStatus.Failed, _vm.BatchEntries[0].Status);
        Assert.Equal(1, _vm.BatchProgressValue);
        Assert.True(_ui.ShownSummary is not null);
    }

    [Fact]
    public async Task ProcessBatch_DuringRun_CheckboxesAndCommandsDisabled()
    {
        SelectAllSelections();
        BuildBatch(2);
        _processor.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _vm.ProcessBatchCommand.Execute(null);

        Assert.True(_vm.IsBatchRunning);
        Assert.All(_vm.BatchEntries, row => Assert.False(row.IsIncludeEnabled));
        Assert.False(_vm.ProcessBatchCommand.CanExecute(null));
        Assert.False(_vm.SelectAllCommand.CanExecute(null));

        _processor.Hold.TrySetResult();
        await CompleteRunAsync();

        Assert.False(_vm.IsBatchRunning);
        Assert.All(_vm.BatchEntries, row => Assert.True(row.IsIncludeEnabled));
    }

    [Fact]
    public async Task ProcessBatch_AfterRun_StateReset_RowsReenabled()
    {
        SelectAllSelections();
        BuildBatch(2);

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        Assert.False(_vm.IsBatchRunning);
        Assert.All(_vm.BatchEntries, row => Assert.True(row.IsIncludeEnabled));
        Assert.True(_vm.ProcessBatchCommand.CanExecute(null));
        Assert.Equal(2, _vm.BatchProgressMaximum);
        Assert.Equal(0, _vm.BatchProgressValue);
    }

    [Fact]
    public async Task ProcessBatch_SnapshotExcludesCompletedRows()
    {
        SelectAllSelections();
        BuildBatch(2);
        _vm.BatchEntries[1].MarkComplete();

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        var expected = new[] { _vm.BatchEntries[0].FullPath };
        Assert.Equal(expected, _processor.ReceivedSourcePaths);
        Assert.Equal(1, _ui.IncludedCountAtConfirm);
        Assert.Equal(new[] { "R-01.docx" }, _ui.FileOrderAtConfirm);
        Assert.Equal(1, _vm.BatchProgressMaximum);
    }

    [Fact]
    public async Task ProcessBatch_NotOpenableLog_OffersInitializationOnce()
    {
        _logPath = Path.Combine(_root, "empty-log.docx");
        File.WriteAllBytes(_logPath, Array.Empty<byte>());
        _dialogs.ReportLogFile = _logPath;
        _ui.ConfirmInitializeReportLogResponse = true;

        SelectAllSelections();
        BuildBatch(1);

        _vm.ProcessBatchCommand.Execute(null);
        await CompleteRunAsync();

        Assert.True(_initializer.Called);
        Assert.Equal(_templatePath, _initializer.TemplatePathArgument);
        Assert.Equal(_logPath, _initializer.DestinationArgument);
        Assert.True(_ui.ConfirmStartCalled);
        Assert.Same(_processor.Result, _ui.ShownSummary);
    }

    // ---- Setup helpers --------------------------------------------------------

    private void SelectAllSelections()
    {
        _dialogs.ReportLogFile = _logPath;
        _dialogs.ReportsDirectory = _directoryPath;
        _dialogs.TemplateFile = _templatePath;

        _vm.BrowseReportLogCommand.Execute(null);
        _vm.BrowseReportsDirectoryCommand.Execute(null);
        _vm.BrowseReportLogTemplateCommand.Execute(null);
    }

    private void BuildBatch(int count)
    {
        if (_vm.BatchCount == 0)
            SelectAllSelections();

        for (var i = 1; i <= count; i++)
            CreateDocx(Path.Combine(_directoryPath, $"R-{i:D2}.docx"));

        _vm.BuildBatchCommand.Execute(null);
    }

    private void ClearAll() => _vm.ClearAllCommand.Execute(null);

    private void NoteProcessedRow(int index)
    {
        _vm.BatchEntries[index].MarkProcessed();
    }

    /// <summary>
    /// Waits for the async-void batch workflow to finish: the summary is shown
    /// and the ViewModel has reset its run state.
    /// </summary>
    private async Task CompleteRunAsync()
    {
        await _ui.SummaryShown.Task;
        await WaitUntilAsync(() => !_vm.IsBatchRunning);
    }

    /// <summary>
    /// Invoking the captured manual-resolution handler would open the real
    /// resolution window, so the assertions above deliberately check that the
    /// handler exists but is never called on the automatic path.
    /// </summary>
    private static bool ManualResolutionWasInvoked() => false;

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Timed out waiting for batch state.");
            await Task.Delay(10);
        }
    }

    private string CreateDocx(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        document.AddMainDocumentPart().Document = new Document(new Body());
        document.Save();
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort test cleanup.
        }
    }

    // ---- Scripted fakes -------------------------------------------------------

    private sealed class ScriptedDialogService : IFileDialogService
    {
        public string? ReportLogFile { get; set; }
        public string? ReportsDirectory { get; set; }
        public string? TemplateFile { get; set; }

        public string? PickReportLogFile(string? initialDirectory = null) => ReportLogFile;
        public string? PickReportsDirectory(string? initialDirectory = null) => ReportsDirectory;
        public string? PickReportLogTemplate(string? initialDirectory = null) => TemplateFile;
    }

    private sealed class ScriptedRenameDialog : IRenameFileDialogService
    {
        public string? Next { get; set; }

        public string? Prompt(string currentFileName, string? message) => Next;
    }

    private sealed class ScriptedTemplateInspection : ITemplateInspectionService
    {
        public TemplateValidationResult Inspect(string templatePath) => new(true);
    }

    private sealed class ScriptedUserInteraction : IBatchUserInteraction
    {
        public bool ConfirmInitializeReportLogResponse { get; set; }
        public bool ConfirmStart { get; set; } = true;
        public bool ConfirmStartCalled { get; private set; }
        public int IncludedCountAtConfirm { get; private set; }
        public List<string> FileOrderAtConfirm { get; } = new();
        public BatchRunSummary? ShownSummary { get; private set; }
        public string? ShownSummaryLogPath { get; private set; }
        public TaskCompletionSource SummaryShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ConfirmInitializeReportLog(string reportLogPath) => ConfirmInitializeReportLogResponse;

        public bool ConfirmBatchStart(string reportLogPath, string templatePath, int includedCount, IReadOnlyList<string> fileOrder)
        {
            ConfirmStartCalled = true;
            IncludedCountAtConfirm = includedCount;
            FileOrderAtConfirm.AddRange(fileOrder);
            return ConfirmStart;
        }

        public void ShowBatchSummary(BatchRunSummary summary, string reportLogPath)
        {
            ShownSummary = summary;
            ShownSummaryLogPath = reportLogPath;
            SummaryShown.TrySetResult();
        }

        public void ShowError(string title, string message)
        {
        }
    }

    private sealed class ScriptedInitializer : IReportLogInitializer
    {
        public bool Called { get; private set; }
        public string? TemplatePathArgument { get; private set; }
        public string? DestinationArgument { get; private set; }

        public ReportLogInitializationResult Initialize(string templatePath, string destinationPath)
        {
            Called = true;
            TemplatePathArgument = templatePath;
            DestinationArgument = destinationPath;
            return new ReportLogInitializationResult(true, destinationPath, ReportLogInitializationErrorKind.None, null);
        }
    }

    private sealed class ScriptedBatchProcessor : IBatchReportProcessor
    {
        public List<BatchRunEvent> Events { get; } = new();
        public TaskCompletionSource? Hold { get; set; }
        public Func<SpinParseResult, ReportResolutionResult?>? CapturedManualResolution { get; private set; }
        public IReadOnlyList<string>? ReceivedSourcePaths { get; private set; }
        public BatchRunSummary Result { get; set; } = BuildEmptySummary();

        public async Task<BatchRunSummary> RunAsync(
            BatchRunSettings settings,
            Func<SpinParseResult, ReportResolutionResult?> manualResolution,
            Action<BatchRunEvent> onEvent,
            CancellationToken cancellationToken = default)
        {
            CapturedManualResolution = manualResolution;
            ReceivedSourcePaths = settings.SourcePaths.ToList();

            foreach (var batchEvent in Events)
                onEvent(batchEvent);

            if (Hold is not null)
                await Hold.Task;

            return Result;
        }

        private static BatchRunSummary BuildEmptySummary() =>
            new(Array.Empty<BatchRunItemResult>(), 0, BatchStopReason.Completed);
    }

    private sealed class PreviewServiceNoop : IParsePreviewService
    {
        public static PreviewServiceNoop Instance { get; } = new();

        public void Show(string spinReportPath)
        {
        }
    }

    private sealed class ReviewServiceNoop : IReportReviewService
    {
        public static ReviewServiceNoop Instance { get; } = new();

        public void Review(string spinReportPath)
        {
        }
    }

    private sealed class AppendServiceNoop : IAppendReportService
    {
        public static AppendServiceNoop Instance { get; } = new();

        public bool ProcessSelected(string reportLogPath, string templatePath, string sourceReportPath) => false;
    }
}