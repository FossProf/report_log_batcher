using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

public sealed class BatchDiscoveryService
{
    public IReadOnlyList<BatchEntry> Discover(string reportsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(reportsDirectoryPath))
            throw new ArgumentException("A reports directory path is required.", nameof(reportsDirectoryPath));

        if (!Directory.Exists(reportsDirectoryPath))
            throw new DirectoryNotFoundException(
                $"The reports directory does not exist: {reportsDirectoryPath}");

        var discovered = new List<BatchEntry>();
        var originalIndex = 0;

        foreach (var filePath in Directory.EnumerateFiles(reportsDirectoryPath))
        {
            var fileName = Path.GetFileName(filePath);
            if (IsEligible(fileName))
                discovered.Add(new BatchEntry(filePath, fileName, originalIndex));

            originalIndex++;
        }

        return discovered
            .OrderBy(entry => entry.FileName, NaturalStringComparer.Instance)
            .ToList();
    }

    private static bool IsEligible(string fileName)
    {
        if (fileName.StartsWith("~$", StringComparison.Ordinal))
            return false;

        return string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase);
    }
}