# OpenCode Implementation Prompt — Slice 0/1

Read `AGENTS.md`, `README.md`, and `docs/ROADMAP.md` before making changes.

Implement ONLY vertical Slice 0/1: project foundation and file selection.

## Target

Python 3.12+ Windows desktop application using PySide6.

## Create

Use a src layout with package:

    src/report_log_batcher/
        __init__.py
        __main__.py
        app.py
        gui/
            __init__.py
            main_window.py
        services/
            __init__.py
            path_validation.py

    tests/
        test_path_validation.py

Also create appropriate `pyproject.toml` and `.gitignore`.

## GUI Requirements

Create a clean main window titled:

`Report Log Batcher`

Provide two selection sections.

### 1. Project Report Log

- read-only path field
- Browse button
- use native Windows file dialog
- only accept existing `.docx` files

### 2. Finalized Reports Directory

- read-only path field
- Browse button
- use native directory-selection dialog
- only accept existing directories

Below these controls reserve an empty area/group labeled:

`Batch`

Do NOT implement report discovery or populate the batch yet.

Add a disabled button:

`Build Batch`

Enable Build Batch only when BOTH selections are valid.

## Validation

Implement validation outside GUI code in `services/path_validation.py`.

Use `pathlib.Path`.

Provide independently testable functions for:

- validating report-log path
- validating finalized-report directory

A valid report log:

- exists
- is a file
- has case-insensitive `.docx` extension

A valid report directory:

- exists
- is a directory

Display concise GUI validation errors when invalid input is encountered.

Do not crash because a user cancels a file dialog.

## Persistence

For this slice, remember the last successfully selected report-log path and reports-directory path between application launches using `QSettings`.

On startup:

- restore each stored path only if it is still valid
- otherwise leave that field empty

## Logging

Configure standard Python logging at application startup.

Log application startup and unexpected errors.

Do not create a complex logging framework.

## Tests

Use pytest.

Test path validation with temporary files/directories, including:

- valid `.docx`
- uppercase `.DOCX`
- wrong extension
- nonexistent file
- directory supplied as report log
- valid reports directory
- nonexistent directory
- file supplied as reports directory

Tests must not require Microsoft Word.

## Out of Scope

Do NOT implement:

- scanning reports
- sorting
- staging list entries
- drag/drop
- rename
- removal
- Word parsing
- python-docx processing
- ReportRecord
- template fields
- batch processing
- progress bars
- report-log modification
- backups
- duplicate detection

`python-docx` may be declared as a future project dependency, but it must not be used in this slice.

## Quality

Keep GUI and validation logic separated.

Use type hints.

Avoid unnecessary abstractions.

Do not add dependencies beyond what this slice requires.

The application must launch with:

    python -m report_log_batcher

Run the test suite after implementation and fix failures.

## Completion Report

At completion report:

1. files created/changed
2. tests executed and results
3. exact command to install dependencies
4. exact command to launch the app
5. any assumptions made

Do not begin Slice 2.
