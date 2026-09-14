# Development Roadmap

## Slice 0/1 — Foundation + File Selection
Runnable desktop shell.
Select and validate master `.docx`.
Select and validate finalized-report directory.

## Slice 2 — Batch Discovery
Discover `.docx` reports.
Natural alphanumeric ascending sort.
Display staging list.

## Slice 3 — Batch Manipulation
Reorder entries.
Rename source files safely.
Remove entries from batch without deleting files.
Reload/reset batch.

## Slice 4 — Template Contract
Analyze supplied Word template.
Define required field schema.
Represent schema independently of GUI.

## Slice 5 — SPIN Parser
Parse one finalized report into ReportRecord.
Deterministic extraction rules based on actual SPIN structure.

## Slice 6 — Validation + Manual Resolution
Validate every required field.
Pause for unresolved values.
Manual-entry modal.
Default unresolved blank input to `N/A`.

## Slice 7 — Batch Processor
Sequential processing in staged order.
Per-report status/progress.
Overall progress.
Pause/resume for manual resolution.
Error isolation.

## Slice 8 — Template Renderer
Populate a fresh template instance from a validated ReportRecord.
Preserve required Word formatting.

## Slice 9 — Master Log Writer
Append completed forms to selected master log in exact batch order.

## Slice 10 — Production Hardening
Automatic pre-write backup.
Duplicate detection.
Failure recovery.
Audit logging.
Final validation.
Windows packaging.
