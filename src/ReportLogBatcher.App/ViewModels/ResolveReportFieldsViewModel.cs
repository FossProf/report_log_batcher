using System.IO;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.ViewModels;

/// <summary>
/// Working copy of the eight required fields of a parsed report, shown in the
/// "Resolve Report Fields" dialog.
///
/// The parser's original <see cref="ReportRecord"/> is never mutated: the
/// working copy starts from (and is shown as) the parsed values, but approval
/// builds a brand-new record via the <see cref="IReportRecordResolver"/>, which
/// re-validates the final values before anything can be committed.
/// </summary>
public sealed class ResolveReportFieldsViewModel : ObservableObject
{
    private readonly ReportRecord _original;
    private readonly IReadOnlyList<ParseIssue> _parserIssues;
    private readonly IReportRecordResolver _resolver;

    private string _reportNumberText;
    private string _inspectionDateText;
    private string _inspectorFirstNameText;
    private string _descriptionOfWorkText;
    private string _drawingReferencesText;
    private string _generalObservationsText;
    private string _discrepanciesText;
    private string _previousDiscrepancyCorrectionsText;
    private string? _validationMessage;

    private readonly bool _reportNumberUnresolved;
    private readonly bool _inspectionDateUnresolved;
    private readonly bool _inspectorFirstNameUnresolved;
    private readonly bool _descriptionOfWorkUnresolved;
    private readonly bool _drawingReferencesUnresolved;
    private readonly bool _generalObservationsUnresolved;
    private readonly bool _discrepanciesUnresolved;
    private readonly bool _previousDiscrepancyCorrectionsUnresolved;

    public ResolveReportFieldsViewModel(SpinParseResult parseResult, IReportRecordResolver resolver)
    {
        if (parseResult is null) throw new ArgumentNullException(nameof(parseResult));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        var record = parseResult.Record;
        _original = record;
        _parserIssues = parseResult.Issues;

        SourceFilePath = parseResult.SourcePath;
        SourceFileName = Path.GetFileName(parseResult.SourcePath);

        _reportNumberText = record.ReportNumber ?? string.Empty;
        _inspectionDateText = record.InspectionDate?.ToString("MM/dd/yyyy") ?? string.Empty;
        _inspectorFirstNameText = record.InspectorFirstName ?? string.Empty;
        _descriptionOfWorkText = record.DescriptionOfWork ?? string.Empty;
        _drawingReferencesText = record.DrawingReferences ?? string.Empty;
        _generalObservationsText = record.GeneralObservations ?? string.Empty;
        _discrepanciesText = record.Discrepancies ?? string.Empty;
        _previousDiscrepancyCorrectionsText = record.PreviousDiscrepancyCorrections ?? string.Empty;

        _reportNumberUnresolved = record.ReportNumber is null;
        _inspectionDateUnresolved = record.InspectionDate is null;
        _inspectorFirstNameUnresolved = record.InspectorFirstName is null;
        _descriptionOfWorkUnresolved = record.DescriptionOfWork is null;
        _drawingReferencesUnresolved = record.DrawingReferences is null;
        _generalObservationsUnresolved = record.GeneralObservations is null;
        _discrepanciesUnresolved = record.Discrepancies is null;
        _previousDiscrepancyCorrectionsUnresolved = record.PreviousDiscrepancyCorrections is null;

        DiagnosticsText = parseResult.Issues.Count == 0
            ? "No diagnostics — every required field was extracted deterministically."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                parseResult.Issues.Select(issue =>
                    $"[{issue.Kind}] {(issue.Field?.ToString() ?? "Document")}: {issue.Message}"));
    }

    public string SourceFilePath { get; }

    public string SourceFileName { get; }

    public string ReportNumberText
    {
        get => _reportNumberText;
        set => SetProperty(ref _reportNumberText, value);
    }

    public string InspectionDateText
    {
        get => _inspectionDateText;
        set => SetProperty(ref _inspectionDateText, value);
    }

    public string InspectorFirstNameText
    {
        get => _inspectorFirstNameText;
        set => SetProperty(ref _inspectorFirstNameText, value);
    }

    public string DescriptionOfWorkText
    {
        get => _descriptionOfWorkText;
        set => SetProperty(ref _descriptionOfWorkText, value);
    }

    public string DrawingReferencesText
    {
        get => _drawingReferencesText;
        set => SetProperty(ref _drawingReferencesText, value);
    }

    public string GeneralObservationsText
    {
        get => _generalObservationsText;
        set => SetProperty(ref _generalObservationsText, value);
    }

    public string DiscrepanciesText
    {
        get => _discrepanciesText;
        set => SetProperty(ref _discrepanciesText, value);
    }

    public string PreviousDiscrepancyCorrectionsText
    {
        get => _previousDiscrepancyCorrectionsText;
        set => SetProperty(ref _previousDiscrepancyCorrectionsText, value);
    }

    public bool ReportNumberUnresolved => _reportNumberUnresolved;

    public bool InspectionDateUnresolved => _inspectionDateUnresolved;

    public bool InspectorFirstNameUnresolved => _inspectorFirstNameUnresolved;

    public bool DescriptionOfWorkUnresolved => _descriptionOfWorkUnresolved;

    public bool DrawingReferencesUnresolved => _drawingReferencesUnresolved;

    public bool GeneralObservationsUnresolved => _generalObservationsUnresolved;

    public bool DiscrepanciesUnresolved => _discrepanciesUnresolved;

    public bool PreviousDiscrepancyCorrectionsUnresolved => _previousDiscrepancyCorrectionsUnresolved;

    public string DiagnosticsText { get; }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>Populated when approval succeeds; null while the dialog is open.</summary>
    public ReportResolutionResult? Result { get; private set; }

    public bool CanApprove => Result is null;

    /// <summary>
    /// Runs resolution/validation on the current working copy. On success stores
    /// the <see cref="ReportResolutionResult"/> and returns true; on failure sets
    /// <see cref="ValidationMessage"/> and returns false so the dialog stays open.
    /// </summary>
    public bool Approve()
    {
        if (Result is not null)
            return true;

        var editedValues = new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = ReportNumberText,
            [ReportField.InspectionDate] = InspectionDateText,
            [ReportField.InspectorFirstName] = InspectorFirstNameText,
            [ReportField.DescriptionOfWork] = DescriptionOfWorkText,
            [ReportField.DrawingReferences] = DrawingReferencesText,
            [ReportField.GeneralObservations] = GeneralObservationsText,
            [ReportField.Discrepancies] = DiscrepanciesText,
            [ReportField.PreviousDiscrepancyCorrections] = PreviousDiscrepancyCorrectionsText,
        };

        var result = _resolver.Resolve(_original, editedValues, _parserIssues);
        if (result.IsApproved)
        {
            Result = result;
            ValidationMessage = null;
            return true;
        }

        ValidationMessage =
            "Cannot approve yet — these required fields still need a value (Inspection Date needs a valid date): " +
            string.Join(", ", result.InvalidFields.Select(field => field.ToString()));
        return false;
    }
}