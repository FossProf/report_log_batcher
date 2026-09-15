# Report Log Batcher

Windows desktop utility for converting finalized SPIN Word reports into entries in a project-specific master Word report log.

## Intended workflow

Select:

1. Master report-log `.docx`
2. Directory containing finalized SPIN `.docx` reports

The application builds a staging list of reports using natural alphanumeric ascending order.

Before processing, the user can reorder the batch, rename files, or remove files from the batch without deleting the source documents.

After verification, reports are processed sequentially.

Each report is parsed into a structured record containing all fields required by the report-log template. A deterministic parser extracts the certified fields from the SPIN document's title, header table, and five canonical narrative sections, preserving source wording verbatim (including typos). Structural ambiguity, missing structure, and unparsable values are surfaced as diagnostics; unresolved fields are reported in a read-only preview.

Missing/unreliable fields require manual resolution before processing continues. Blank required fields are prohibited; `N/A` is the fallback value.

The "Review Record" action (after verifying the staging batch) opens every selected report in an editable resolution dialog: all eight parsed fields can be corrected, unresolved fields are flagged, and the Inspection Date demands a valid date. Blank required fields become `N/A` only at approval. The approved record is validated into an immutable `ValidatedReportRecord` and shown in a read-only Approved Record Preview; reviewing never modifies the source SPIN and never writes to any Word document.

Validated records are rendered through the Word template and appended to the selected master report log in staged order.

## Processing pipeline

SPIN .docx -> Parser -> ReportRecord -> Validator -> Template Renderer -> Master Log

## Technology

- C# (.NET 8+)
- WPF
- Open XML SDK
- NUnit / xUnit
- Windows

## Development

Development is organized into vertical slices so every milestone produces a runnable and testable application.

See `AGENTS.md` and `docs/ROADMAP.md`.
