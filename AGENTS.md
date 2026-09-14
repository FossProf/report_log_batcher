# Report Log Batcher — Development Instructions

## Purpose

Build a Windows desktop application that batches finalized SPIN `.docx` reports into a project-specific Word report log.

## Required workflow

1. User selects the project report-log `.docx`.
2. User selects the directory containing finalized `.docx` reports.
3. App discovers reports and displays a staging list using natural alphanumeric ascending sort.
4. Before processing, user may:
   - reorder reports;
   - rename source files;
   - remove reports from the batch.
5. Removing a report from the batch MUST NOT delete the source file.
6. User explicitly starts processing after verifying the batch.
7. Reports are processed sequentially in displayed order.
8. Required fields are parsed into an internal ReportRecord.
9. Every template field is required.
10. If a field cannot be parsed adequately, processing pauses and a modal requests manual entry.
11. If the user provides no manual value, use `N/A`.
12. No required field may remain blank.
13. Validated records are rendered using the provided Word template.
14. Rendered forms are appended to the selected report log in batch order.
15. Display per-report status/progress and overall batch progress.

## Architectural invariant

Never allow parsing code to write directly to the report log.

Pipeline:

SPIN .docx
-> Parser
-> ReportRecord
-> Validator
-> Template Renderer
-> Master Log

A ReportRecord containing a blank required field MUST NOT reach the renderer/writer.

## Technology

- C# (.NET 8+)
- Windows desktop application
- WPF (MVVM)
- Open XML SDK (DocumentFormat.OpenXml)
- System.IO.Path / System.IO.Directory
- NUnit
- Microsoft.Extensions.Logging (or built-in logging)

Do not introduce another GUI framework or Word-processing library without explicit approval.

## Architecture

Keep these concerns separated:

- GUI
- application/workflow services
- domain models
- report parsing
- validation
- Word template rendering
- report-log writing
- filesystem operations

GUI widgets must not contain Word parsing/writing logic.

Filesystem and Word operations should be callable independently of the GUI so they can be unit tested.

## Development rules

Work in vertical slices.

Do not implement functionality belonging to a later slice unless required by the current slice.

Each slice must:
- leave the application runnable;
- have clear acceptance criteria;
- include appropriate tests;
- avoid breaking previous slices.

Prefer simple deterministic implementations over abstraction for its own sake.

Do not add AI/LLM dependencies.

Do not modify production Word documents while developing tests.

Never silently discard an error affecting report data.

## Safety

The application deals with work records.

Therefore:
- never delete source reports;
- never silently overwrite a report because of a rename collision;
- never commit incomplete records;
- eventually back up the master log before modification;
- preserve an audit trail for processing failures and manual substitutions.

## Current scope

Only implement the slice explicitly requested in the current development prompt.

The exact Word template schema and SPIN parsing rules are intentionally undefined until representative production documents are supplied.
