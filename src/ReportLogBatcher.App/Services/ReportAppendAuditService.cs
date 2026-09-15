using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ReportLogBatcher.App.Services;

/// <summary>
/// One append-operation audit record. Metadata only — no narrative report bodies
/// are ever recorded. Batch runs record one entry per workflow event
/// (<see cref="BatchEvent"/>), each tagged with <see cref="BatchId"/> and its
/// <see cref="OrderIndex"/>. Written to a file separate from the report log.
/// </summary>
public sealed class ReportAppendAuditEntry
{
    public string Utc { get; set; } = string.Empty;
    public string ReportNumber { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public bool InitializedReportLog { get; set; }
    public string[] ManualFields { get; set; } = Array.Empty<string>();
    public string[] NaFallbackFields { get; set; } = Array.Empty<string>();
    public string? Backup { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? Message { get; set; }

    /// <summary>Batch-run identifier shared by every entry recorded during one batch.</summary>
    public string? BatchId { get; set; }

    /// <summary>The batch workflow event this entry records (see BatchRunEventKind).</summary>
    public string? BatchEvent { get; set; }

    /// <summary>1-based staged position of the report within the batch.</summary>
    public int? OrderIndex { get; set; }

    /// <summary>On the BatchStarted event: the full staged processing order.</summary>
    public string[]? StagedOrder { get; set; }
}

/// <summary>
/// Append-only audit trail for report-log operations, stored in a dedicated file
/// so the report log itself never carries metadata. Failures to write the audit
/// are logged but never block or discard the operation outcome.
/// </summary>
public sealed class ReportAppendAuditService
{
    private readonly ILogger _logger;
    private readonly string _auditFilePath;
    private readonly object _sync = new();

    public ReportAppendAuditService(ILoggerFactory loggerFactory, string? auditDirectory = null)
    {
        _logger = loggerFactory.CreateLogger<ReportAppendAuditService>();
        var directory = auditDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReportLogBatcher",
                "audit");
        _auditFilePath = Path.Combine(directory, "append-audit.jsonl");
    }

    public void Record(ReportAppendAuditEntry entry)
    {
        entry.Utc = DateTime.UtcNow.ToString("O");
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_auditFilePath)!);
                File.AppendAllText(_auditFilePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write report-log append audit record for {Number}.", entry.ReportNumber);
        }
    }
}