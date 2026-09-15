using System.Windows;
using System.Windows.Input;
using ReportLogBatcher.App.ViewModels;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.Dialogs;

/// <summary>
/// Modal manual-resolution dialog for a parsed stage report.
///
/// Receives the parse result and a resolver; edits live only in the working
/// <see cref="ResolveReportFieldsViewModel"/> and are turned into an approved
/// record only through the resolver's validation gate. Cancelling simply
/// abandons the working copy: the original parse result and the staged batch
/// are untouched, and no filesystem write ever occurs from this dialog.
/// </summary>
public partial class ResolveReportFieldsWindow : Window
{
    private readonly ResolveReportFieldsViewModel _viewModel;

    public ResolveReportFieldsWindow(SpinParseResult parseResult, IReportRecordResolver resolver)
    {
        InitializeComponent();
        _viewModel = new ResolveReportFieldsViewModel(parseResult, resolver);
        DataContext = _viewModel;
    }

    /// <summary>NonNull and valid only when the dialog was approved.</summary>
    public ReportResolutionResult? Result => _viewModel.Result;

    private void OnApproveClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Approve())
            DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}