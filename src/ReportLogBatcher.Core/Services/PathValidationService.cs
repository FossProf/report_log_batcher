namespace ReportLogBatcher.Core.Services;

public sealed record PathValidationResult(bool IsValid, string? ErrorMessage);

public static class PathValidationService
{
    public static PathValidationResult ValidateReportLog(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathValidationResult(false, null);

        if (!File.Exists(path))
            return new PathValidationResult(false, "The selected report log does not exist.");

        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            return new PathValidationResult(false, "The report log must be a .docx file.");

        return new PathValidationResult(true, null);
    }

    public static PathValidationResult ValidateReportsDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathValidationResult(false, null);

        if (!Directory.Exists(path))
            return new PathValidationResult(false, "The selected reports directory does not exist.");

        return new PathValidationResult(true, null);
    }

    public static PathValidationResult ValidateReportLogTemplate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathValidationResult(false, null);

        if (!File.Exists(path))
            return new PathValidationResult(false, "The selected report-log template does not exist.");

        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            return new PathValidationResult(false, "The report-log template must be a .docx file.");

        return new PathValidationResult(true, null);
    }
}