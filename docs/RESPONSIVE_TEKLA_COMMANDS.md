# Responsive command orchestration

The old event handlers executed Tekla API calls and DIM loops on the WinForms UI
thread. `Application.DoEvents` only helped between calls, and Slot 07 / reference
arrangement also disabled the entire form. A slow API call therefore froze window
painting, dragging, animation and sidebar input.

## Execution model

`TeklaOperationDispatcher` owns one persistent background STA and its message loop.
The UI asynchronously awaits a complete synchronous operation. Tekla calls,
manual-scale scopes, Inzai selection and other ThreadStatic DIM contexts all run
and unwind on that same worker thread. No dimension engine was rewritten.

One queue callback is posted at a time. Even a legacy engine's nested message
pump cannot start another queued operation. MainForm rejects a second operation
while one is active instead of accumulating stale clicks against a different
drawing. Updater handoff and the periodic project observation also respect this
busy state.

The common boundary covers external DIM slots, Slot 07 orchestration, Active and
Batch Create, Load Selected, Check Scale, Open Grid, Fit plus neighbor cleanup,
neighbor/grid marks, spacing, line/pick commands, Arrange View, reference batch
arrangement and Snapshot. Load→Create and Load→Check shortcuts execute in order
inside one admitted operation; they do not depend on an asynchronous button
click finishing immediately.

UI controls remain owned by the UI thread. Worker orchestration marshals short
updates through `Ui` and reads control inputs through `ReadUi`. Input filtering
holds command/settings changes while allowing moving/resizing the window,
sidebar navigation, tool tabs/pages, dictionary browsing, theme/pin and scrolling
results. The whole MainForm is no longer disabled during DIM or reference batch
work. PDF printing keeps its existing asynchronous implementation but no longer
disables every top-level control.

Batch STOP is handled directly on the UI and stored in a volatile flag. It is
observed after the current drawing finishes; it never aborts a DIM write midway.
Normal user closure is refused until the operation finishes. Existing selection,
save, skip, rollback, resume and dimension algorithms retain their behavior.

## Evidence and limits

The regression harness keeps WinForms cross-thread checking enabled. During a
960 ms simulated command, the UI timer delivered over 30 ticks, and moving the
window, opening the sidebar and changing pages succeeded. A second command was
rejected; STOP was accepted before the current simulated drawing ended. An
injected failure released the busy state and the next command succeeded.

Read-only integration connected both Tekla APIs from the actual worker STA and
enumerated `[C2-e-12]`. The existing 23 Inzai routing assertions also pass.

The harness has not executed a mutating DIM or a production drawing batch. This
change frees the TTSK UI; it does not make Tekla itself render or dimension faster,
nor permit simultaneous writes to one active drawing. Interactive Tekla picks
and explicit modal dialogs still require their normal user interaction.

The UI marshaling follows the .NET Framework WinForms ownership rule documented
by [Microsoft](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls).
