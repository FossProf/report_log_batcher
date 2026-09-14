using System.Windows;
using System.Windows.Input;

namespace ReportLogBatcher.App.Dialogs;

public partial class RenameFileDialog : Window
{
    public string ProposedFileName => NewFileNameBox.Text;

    public RenameFileDialog(string currentFileName, string? message)
    {
        InitializeComponent();

        CurrentFileNameBox.Text = currentFileName;
        NewFileNameBox.Text = currentFileName;
        MessageText.Text = message;

        Loaded += (_, _) =>
        {
            NewFileNameBox.SelectAll();
            NewFileNameBox.Focus();
        };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}