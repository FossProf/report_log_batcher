using System.Windows;
using ReportLogBatcher.App.Dialogs;

namespace ReportLogBatcher.App.Services;

public interface IRenameFileDialogService
{
    string? Prompt(string currentFileName, string? message);
}

public sealed class RenameFileDialogService : IRenameFileDialogService
{
    public string? Prompt(string currentFileName, string? message)
    {
        var dialog = new RenameFileDialog(currentFileName, message)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.ProposedFileName : null;
    }
}