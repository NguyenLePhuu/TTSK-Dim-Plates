# Responsive Tekla command dispatcher

```powershell
dotnet build tests/UiDispatcher/UiDispatcher.csproj -c Release
& '.codex-artifacts/ui-dispatch/tests/UiDispatcher.exe' 'portable/TTSK Dim Plates.exe'
& '.codex-artifacts/ui-dispatch/tests/UiDispatcher.exe' 'portable/TTSK Dim Plates.exe' --live-readonly
```

Tests load the supplied production assembly. They do not compile a copy of its
source. UI tests instantiate a hidden MainForm, keep cross-thread control checks
enabled, and run simulated work through the actual command admission path.
No production DIM/Create/Fit operation is executed.

Checks cover:

- a persistent worker STA distinct from the main UI thread;
- ordered execution even when legacy work calls `Application.DoEvents`;
- ThreadStatic lifetime and actual Inzai selection-scope disposal after failure;
- exception propagation, recovery, disposal rejection;
- MainForm timer heartbeat, movement, sidebar/page navigation during work;
- command input blocking while allowing navigation and Batch STOP;
- updater/journal exclusion and refusing closure while work is active;
- thread-safe label/grid updates with illegal cross-thread checks enabled;
- the actual external-slot route, using a test-only engine that waits briefly;
- immediate STOP requests observed at the simulated drawing boundary.

`--live-readonly` additionally constructs fresh Model/Drawing API clients on the
production STA dispatcher, identifies the active drawing, and enumerates its
views/parts. It does not select, modify, save, close, commit or change the work
plane. On 2026-09-23 the probe read `[C2-e-12]`, two views and 40 drawing parts.

Runtime resources and any test settings are local to the test output directory.
The MainForm is never shown, so its Shown hooks do not start update checks or
save work journals. Remove this test's generated `obj` and
`.codex-artifacts/ui-dispatch` after validation.
