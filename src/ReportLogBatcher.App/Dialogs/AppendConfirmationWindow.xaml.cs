using System.Windows;

namespace ReportLogBatcher.App.Dialogs;

/// <summary>
/// Final destination confirmation before a report entry is rendered and appended:
/// shows the source report, the target report log, the template, and the automatic
/// backup notice. No report-log modification happens unless the user clicks Append.
/// </summary>
public partial class AppendConfirmationWindow : Window
{
    public AppendConfirmationWindow(
        string reportNumber,
        string sourcePath,
        string reportLogPath,
        string templatePath)
    {
        InitializeComponent();

        TitleText.Text = $"Append Report #{reportNumber} to the report log?";
        SourceText.Text = $"Source report:     {sourcePath}";
        DestinationText.Text = $"Destination:       {reportLogPath}";
        TemplateText.Text = $"Report-log template: {templatePath}";
    }

    private void OnAppendClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}