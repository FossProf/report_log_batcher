using System.IO;
using Microsoft.Win32;

namespace ReportLogBatcher.App.Services;

public interface IFileDialogService
{
    string? PickReportLogFile(string? initialDirectory = null);
    string? PickReportsDirectory(string? initialDirectory = null);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickReportLogFile(string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Project Report Log",
            Filter = "Word Documents (*.docx)|*.docx",
            DefaultExt = "docx",
            AddExtension = true,
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickReportsDirectory(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Finalized Reports Directory",
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}