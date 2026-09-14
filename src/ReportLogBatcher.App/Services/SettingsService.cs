using System.IO;
using System.Text.Json;

namespace ReportLogBatcher.App.Services;

public sealed class SettingsService
{
    private readonly string _settingsFilePath;
    private SettingsData _data = new();

    public SettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReportLogBatcher",
                "settings.json");
    }

    public string? StoredReportLogPath => _data.ReportLogPath;

    public string? StoredReportsDirectory => _data.ReportsDirectoryPath;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return;

            var json = File.ReadAllText(_settingsFilePath);
            _data = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
        }
        catch
        {
            _data = new SettingsData();
        }
    }

    public void SaveReportLogPath(string path)
    {
        _data.ReportLogPath = path;
        Save();
    }

    public void SaveReportsDirectory(string path)
    {
        _data.ReportsDirectoryPath = path;
        Save();
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private sealed class SettingsData
    {
        public string? ReportLogPath { get; set; }
        public string? ReportsDirectoryPath { get; set; }
    }
}