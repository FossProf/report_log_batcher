namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Why template rendering failed. <see cref="None"/> means success.
/// </summary>
public enum ReportTemplateRenderErrorKind
{
    None,
    TemplateNotFound,
    InvalidTemplate,
    OutputPathInvalid,
    UnsupportedContent,
    PlaceholdersRemain,
    ExpectedValuesMissing,
    Unexpected,
}

/// <summary>
/// Structured outcome of rendering one validated record through the report-log
/// template. On success, <see cref="OutputPath"/> is a completed, validated .docx
/// that the report-log writer may append. On failure, nothing at
/// <see cref="OutputPath"/> is persisted (the renderer cleans its temp output).
/// </summary>
public sealed class ReportTemplateRenderResult
{
    public ReportTemplateRenderResult(
        bool success,
        string? outputPath,
        ReportTemplateRenderErrorKind errorKind,
        string? message)
    {
        Success = success;
        OutputPath = outputPath;
        ErrorKind = errorKind;
        Message = message;
    }

    public bool Success { get; }

    public string? OutputPath { get; }

    public ReportTemplateRenderErrorKind ErrorKind { get; }

    public string? Message { get; }
}