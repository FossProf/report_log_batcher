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
Read-only "Preview Parse" of a staged report showing every required field and diagnostics.

Supported production contract (narrow on purpose; verified against Special Inspection Report #319):
- Title paragraph `Special Inspection Report #NNN` (runs may be split) supplies the report number — never the file name.
- Header table rows supply `Inspection Date` (yyyy-MM-dd / MM/dd/yyyy / M/d/yyyy) and `Cornerstone Inspector(s)` (first name = first whitespace token).
- Five canonical sections:
  `Description and location(s) of work inspected:`
  `Drawing sheets and sections related to this work:`
  `General observations/remarks:`
  `Discrepancies and direction given:`
  `Observations/Remarks on correction of discrepancies noted in previous inspections:`
  matched case-insensitively with optional trailing colon, broken across runs at will.
- Headings/labels are matched exactly after normalization; no fuzzy/substring matching.
- The final narrative section ends at the certification sentence ("To the best of my knowledge..."),
  "Submitted By", "Reviewed By", or "PHOTO DOCUMENTATION".
- Word paragraph boundary => blank line; each line/page break => one newline; tabs => a single space.
- Pages never terminate a section; page breaks inside narrative continue the section.
- Explicit `N/A` is preserved as `N/A`; the parser never invents values.
- Sections/pages inside tables never leak into body narratives and never count as duplicate headings.
- Missing structure => Missing diagnostic; repeated structure => Ambiguous; unparsable value => InvalidFormat;
  unreadable document => Failed (DocumentError). Unresolved fields stay null for the resolver.
- Parsing opens reports read-only and never modifies them.

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
