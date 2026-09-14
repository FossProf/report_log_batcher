# OpenCode Implementation Prompt — Slice 0/1

Read `AGENTS.md`, `README.md`, and `docs/ROADMAP.md` before making changes.

Implement ONLY vertical Slice 0/1: project foundation and file selection.

## Target

- C#
- .NET 10
- Windows desktop application
- WPF
- MVVM
- xUnit

Do not implement later slices.

## Solution Structure

Create:

    ReportLogBatcher.sln

    src/
        ReportLogBatcher.App/
            ReportLogBatcher.App.csproj
            App.xaml
            App.xaml.cs
            MainWindow.xaml
            MainWindow.xaml.cs
            ViewModels/
                MainWindowViewModel.cs
            Services/
                FileDialogService.cs
                SettingsService.cs

        ReportLogBatcher.Core/
            ReportLogBatcher.Core.csproj
            Services/
                PathValidationService.cs

    tests/
        ReportLogBatcher.Core.Tests/
            ReportLogBatcher.Core.Tests.csproj
            PathValidationServiceTests.cs

Project responsibilities:

- `ReportLogBatcher.App` — WPF UI, MVVM presentation logic, Windows dialogs, local UI settings.
- `ReportLogBatcher.Core` — application/domain logic independent of WPF.
- `ReportLogBatcher.Core.Tests` — unit tests for Core.

Do NOT create Infrastructure/OpenXML implementation yet. That belongs to later slices.

## GUI Requirements

Create a clean main window titled:

`Report Log Batcher`

Provide two selection sections.

### Project Report Log

Provide:

- read-only path TextBox
- `Browse...` button
- native Windows file-selection dialog

Only existing `.docx` files are valid.

Dialog filter should target Word `.docx` documents.

### Finalized Reports Directory

Provide:

- read-only path TextBox
- `Browse...` button
- native Windows folder-selection dialog

Only existing directories are valid.

### Batch Area

Below the selection controls, create an empty bordered/grouped area labeled:

`Batch`

Do NOT discover or display reports yet.

Provide a:

`Build Batch`

button.

It MUST remain disabled until both:

- report-log path is valid
- finalized-reports directory is valid

The button performs no batch-building operation in this slice.

## MVVM Requirements

Use MVVM.

`MainWindowViewModel` owns the UI state, including:

- selected report-log path
- selected reports-directory path
- whether Build Batch is enabled

Do not place filesystem validation logic in `MainWindow.xaml.cs`.

Keep code-behind minimal and limited to WPF concerns that cannot reasonably be expressed through binding/commands.

Use `ICommand` implementations for Browse actions.

Do not introduce a third-party MVVM framework in this slice.

Implement a small reusable command implementation if needed.

## File Dialogs

Encapsulate Windows file/folder dialogs behind `FileDialogService`.

The ViewModel must not directly instantiate dialogs.

Canceling either dialog must:

- leave the previous valid selection unchanged
- produce no error
- not crash

## Path Validation

Implement validation in:

`ReportLogBatcher.Core/Services/PathValidationService.cs`

Use `System.IO`.

Provide independently testable validation for:

### Report Log

Valid only when:

- path is nonempty
- path exists
- path represents a file
- extension is `.docx`, case-insensitive

### Reports Directory

Valid only when:

- path is nonempty
- path exists
- path represents a directory

Do not attempt to open or modify Word documents in this slice.

Return enough information for the GUI to present a concise validation message rather than only returning `true/false`.

## Error Handling

Invalid selections must produce a concise user-visible error.

Examples:

- `The selected report log does not exist.`
- `The report log must be a .docx file.`
- `The selected reports directory does not exist.`

Expected validation failures are not application crashes.

Unexpected exceptions should be logged and presented using a generic user-facing error message.

## Settings Persistence

Remember the last successfully selected:

- report-log path
- finalized-reports directory

between launches.

Use a simple application-local settings implementation appropriate for .NET/WPF.

Do not add a third-party settings package.

On startup:

- restore a stored report-log path only if still valid
- restore a stored directory only if still valid
- otherwise leave the corresponding selection empty

Never restore an invalid path into the active application state.

## Logging

Use `Microsoft.Extensions.Logging`.

Keep configuration minimal.

Log:

- application startup
- unexpected exceptions
- important path-selection failures where useful

Do not build a complex logging subsystem.

Do not add telemetry.

## Tests

Use xUnit.

Create unit tests for `PathValidationService`.

Use temporary files/directories created by the tests.

Required cases:

1. valid `.docx`
2. valid uppercase `.DOCX`
3. wrong file extension
4. nonexistent report-log file
5. directory supplied as report log
6. valid reports directory
7. nonexistent reports directory
8. file supplied as reports directory
9. null/empty/whitespace paths where applicable

Tests must:

- not require Microsoft Word
- not modify production documents
- clean up temporary resources

## Dependencies

Keep NuGet dependencies minimal.

Do NOT add Open XML SDK yet unless required solely for project compilation, which it should not be.

Do NOT add:

- Office Interop
- database packages
- AI/LLM packages
- third-party MVVM frameworks
- third-party settings frameworks

## Out of Scope

Do NOT implement:

- scanning finalized reports
- natural sorting
- staging-list entries
- report reordering
- drag/drop
- file renaming
- removal from batch
- ReportRecord
- Word parsing
- Open XML processing
- template parsing
- required template fields
- manual field-entry dialogs
- batch processing
- progress bars
- report-log modification
- backups
- duplicate detection
- audit records
- production packaging

These belong to later vertical slices.

## Acceptance Criteria

Slice 0/1 is complete when:

1. `ReportLogBatcher.sln` builds successfully.
2. WPF application launches successfully.
3. Main window displays both required selectors and empty Batch area.
4. Native Explorer dialogs select the report log and reports directory.
5. Invalid selections are rejected with concise messages.
6. Canceling dialogs causes no state change or error.
7. `Build Batch` enables only when both paths are valid.
8. Valid selections persist across application restart.
9. Invalid persisted paths are ignored on startup.
10. Core path validation is independent of WPF.
11. All xUnit tests pass.
12. No functionality from Slice 2 or later has been implemented.

## Verification

Before completion run:

    dotnet restore
    dotnet build
    dotnet test

Fix all build errors and failing tests.

Do not suppress warnings merely to obtain a clean build unless the warning is understood and the suppression is justified.

## Completion Report

At completion, report concisely:

1. files created or changed
2. NuGet packages added
3. `dotnet build` result
4. `dotnet test` result and test count
5. exact command to launch the application
6. assumptions or deviations from this specification

Do not begin Slice 2.
