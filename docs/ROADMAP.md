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

## Slice 6 — Validation + Manual Resolution + Approved Record Preview
Core required-field validation (all eight fields, no WPF/Open XML):
- String fields invalid when null, "", or whitespace-only; literal `N/A` valid; validation never rewrites values.
- `InspectionDate` stays a typed `DateOnly?`; missing date is invalid (no invented "today", no filename guess).
- Structured result reports ALL missing fields, not just the first.

Manual resolution:
- "Review Record" command opens an editable modal for the selected staged report (all eight fields; unresolved fields visually flagged).
- Edits live only in an in-memory working copy; the parser's original `ReportRecord` is never mutated and the source SPIN is never written.
- Blank required STRING fields become `N/A` ONLY at approval, after the user has seen the resolution dialog; a blank/unparsable Inspection Date blocks approval.
- Cancel discards everything; nothing is persisted or written to any Word document.

Approved record:
- `ValidatedReportRecord` has a private constructor and is produced only after a passing validation run, so an incomplete record can never reach the renderer/writer.
- `ReportResolutionResult` tracks manual edits vs `N/A` fallbacks separately (source `N/A` is never classified as a fallback) and preserves parser diagnostics.
- Read-only "Approved Record Preview" shows the exact header (`Report #NNN – mm/dd/yy – first name`), the five contract body sections, and resolution metadata.
- No Word rendering, no master-log append, no persistence of approvals. Reviewed records are endpoint-of-slice only.

## Slice 7 — Template Renderer + Transactional Report-Log Append
Both word-processing slices for ONE validated record, end to end:

Template renderer (`ReportLogTemplateRenderer`):
- Renders a `ValidatedReportRecord` through a COPY of the report-log template; original template bytes are never modified.
- Header placeholders are replaced in place even when Word split them across runs (`proofErr`, `w:tab`, `w:br` interleaving handled by paragraph-text reconstruction/slicing).
- The five textually-identical body placeholders are resolved by their section heading context.
- Narrative values become real Word paragraphs preserving the placeholder paragraph's formatting (`\n\n` = new paragraph, single `\n` = line break).
- The rendered output is validated (openable, no relationship-dependent content, no remaining placeholders, all approved values present); nothing is left behind on failure.

Transactional writer (`ReportLogWriter`):
- Never modifies the report log in place; a unique working copy is produced on disk.
- The rendered entry's text (excluding its body-level section properties, stripping any nested ones) is inserted before the destination's final section properties.
- The first entry in an empty log starts at the top; each subsequent entry begins on a new page (`pageBreakBefore` on the entry's first non-empty paragraph).
- A byte-for-byte backup `Name.backup-yyyyMMdd-HHmmssfff.docx` is created in the same directory before the original is atomically replaced; backup names never overwrite existing files.
- Success is reported only after the replacement is reopened and validated; on final-validation failure the original bytes are restored from the backup.
- The report log receives ONLY template-structured entry text — no metadata or audit content is ever written into it.

Initializer (`ReportLogInitializer`):
- Creates a fresh empty report log from the template (styles + final section properties preserved, every paragraph removed) when the user explicitly chooses to initialize an empty/invalid destination.
- Refuses to overwrite an already-valid document, and never modifies the template.

App integration ("Process Selected"):
- The append workflow pauses at resolution, shows the approved preview, then: validates the destination; if it is empty/invalid, asks the user to Exit or Initialize; shows a final confirmation (report number, source, destination, template, backup notice); renders to a temp directory; appends; and reports a structured success/failure.
- A JSONL audit trail records every append attempt, manual substitution, `N/A` fallback, backup, and failure at `%LocalAppData%\ReportLogBatcher\audit\append-audit.jsonl` — always a SEPARATE file, never inside the report log.
- Rows are marked Complete only after a successful append.

## Slice 8 — Batch Processor
Checked batch processing with automatic approval and exception-only manual intervention.

Automatic approval:
- "Process Batch" runs the verified batch sequentially in displayed order; it requires a valid destination report-log, a valid template, and at least one included row.
- `ReportBatchProcessor` advances through reports automatically with no prompting on the happy path: a report is approved automatically when it parses completely (`Parsed`) with no blocking diagnostics and its resolution carries no manual edits and no `N/A` fallbacks. Auto-approved reports flow straight from parse to validation to rendering.
- A clean batch therefore shows exactly ONE confirmation (report count, destination, template, backup notice) before the run and ONE summary afterwards.

Exception-only manual intervention:
- A report needs manual input only when it genuinely requires attention: an unreadable document or parsing failure, any blocking diagnostic, an unresolved field, a human edit, an `N/A` fallback, or a cancelled resolution prompt.
- Each such report is set to NeedsInput and the injectable manual-resolution dialog pauses processing for that report only; later reports never process ahead, and unpaused reports continue in order afterwards.
- No required field can remain blank: validated records can never carry blanks; a blank manual entry becomes `N/A`; the report number is never guessed from the file name, and today's date is never invented for the Inspection Date.
- A cancelled prompt now applies `N/A`, records the substitution with an explicit cancelled marker, and aborts the run cleanly.

Safe stop:
- On user cancellation or a stop-on-error, the current report is marked NeedsInput (or Failed for an error), earlier reports remain Complete, later reports stay Pending, a summary explains the stop, and `BatchStopped` is emitted (also on external token cancellation). Nothing is silently discarded.

Isolation:
- Every item renders through a fresh template copy into a private temp directory that is deleted in `finally`; rendering/writing failures affect only that row (Failed) and the rest of the batch continues.
- `append-audit.jsonl` records run start/stop, per-item outcomes (`manual edit`, `N/A fallback`, `failed`, `rendered + appended`), and the batch summary — always in the separate `%LocalAppData%\ReportLogBatcher\audit` file, never inside the report log.
- Progress: an "Include" checkbox (≠ removal; excluded rows are never opened) and a per-report status column (Pending / NeedsInput / Complete / Failed) alongside an overall "Processed X of Y" progress bar; terminal statuses remain visible after the run.

## Slice 9 — Production Hardening
Automatic pre-write backup (already modeled in the slice-7 writer).
Duplicate detection: re-append detection and page-break rules already laid down.
Failure recovery.
Windows packaging.
