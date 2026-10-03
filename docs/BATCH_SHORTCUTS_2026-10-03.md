# Batch shortcuts — 2026-10-03

## Updated flexible targeting

MainForm now displays drawing rows only for more than one resolved drawing.
Single/active targets remain in the processing snapshot but leave the grid empty;
old visible rows are cleared when switching to a single target. The count label
says "Xử lý 1 bản vẽ". Explicit Load Selected follows the same rule. Check Scale
also avoids rebuilding a lone error row; its status still reports the result.
Tests assert active/one-row/Document Manager single targets keep their drawing
identity with zero UI rows, while two selected targets produce two rows.

The earlier forced-Batch behavior below is superseded. Check DIM (C and its
button), Check Scale, Merge/Print PDF and Snapshot share one target resolver:

1. In Active mode, an open drawing takes precedence over list/Document Manager
   selection. If none is open, selected rows and Document Manager are available.
2. In Batch mode, explicitly selected rows take precedence, followed by the
   current Document Manager selection. With neither, use the active drawing.
3. One drawing is valid in every source; no minimum batch count is imposed.
4. Empty selections never mean "run the entire old loaded list". No available
   target reports an error. A stale model list or API failure aborts rather than
   silently choosing another drawing. Duplicate identities are removed.

The resolved selection is shown in the processing list with its source/count.
An active source uses the existing single Check DIM report; one/many selected
drawings use the existing batch report. Ctrl+H and saved custom bindings remain
unchanged. No dimension, PDF or drawing-generation algorithm is modified.

Validation: 36 pure target combinations, duplicates and failure propagation;
existing orchestration/migration tests; a read-only live adapter using actual
production target-loading code, actual Tekla drawing identities and isolated
WinForms controls. Live cases cover active drawing, one/two selected rows,
stale-model rejection and the existing Document Manager selection (or active
fallback if empty). The active Tekla drawing is unchanged. Full PDF export and
the modal batch-check UI were not run in these tests.

Run live adapter: `dotnet build tests/DrawingTargets/DrawingTargets.csproj -c Release`,
then `tests/DrawingTargets/bin/Release/net48/DrawingTargets.exe` with Tekla open.

## Earlier implementation record (forced-Batch behavior superseded)

| Default key | Action |
|---|---|
| C | Batch -> fresh Document Manager selection -> Check DIM batch report (auto-start) |
| Ctrl+P | Batch -> fresh selection -> existing Merge PDF command |
| Ctrl+H | Work journal |
| Ctrl+Shift+P | Batch -> fresh selection -> Snapshot, including one drawing |
| Ctrl+L | Batch -> Load Selected (existing) |

Print PDF per drawing has a configurable shortcut action, unassigned by
default. There is no shortcut action for opening the PDF menu; mouse access
is unchanged. Existing Batch Create, Check Scale and Arrange Reference
shortcuts remain available. The Check DIM button still follows Active/Batch
mode; C specifically chooses Batch as requested.

The existing PrintMenu configuration ID is retained for Merge so saved custom
bindings survive. WorkJournal's former default Ctrl+Shift+J migrates once to
Ctrl+H if that chord is free. Custom/disabled keys and occupied Ctrl+H bindings
are preserved. The migration marker prevents overriding later user choices.

MainForm.BatchShortcuts separates worker-thread Load from the UI report.
It awaits Load completion and releases the operation lock before invoking the
consumer. Empty/error loads cannot fall back to old rows. Busy, modal, updater,
disposal and repeated-invocation guards prevent overlapping runs. Check Scale
uses the same loader. Ctrl+P reuses RunPdfCommand(true), which already implements
Batch/Load, progress, merge and its existing child-PDF cleanup policy.

Validation: tests/BatchShortcuts compiles the actual production orchestration
and ShortcutManager with an in-memory host. Tests cover success, stale selection,
empty/error Load, six busy/lifetime conditions, pending Load, duplicate calls,
disposal during await, recovery after exception, defaults, migration, conflicts
and persisted custom choices. Source route checks verify C/Merge/Print/Snapshot.
Release x64 build and portable SHA-256 verification passed for 39 runtime files.

No live Check DIM batch or PDF export/merge was executed in this change.
Validation establishes routing and state handling; it is not a claim that
every drawing or printer configuration has been exercised. No geometry, DIM
check, PDF merge or drawing-generation algorithm was modified.

Run regression: `dotnet run --project tests/BatchShortcuts/BatchShortcuts.csproj -c Release`.
