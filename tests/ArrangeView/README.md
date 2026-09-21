# Arrange View regression and read-only audit

Build/run on Windows with Tekla Structures 2025 installed:

```powershell
dotnet build tests/ArrangeView/ArrangeView.csproj -c Release -o .codex-artifacts/arrange-view-session/tests
& .codex-artifacts/arrange-view-session/tests/ArrangeView.exe
```

No Tekla connection is needed for the default regression suite. It links the three production Arrange sources directly. Coverage includes centering with GAP 0/1/5/15; dynamic keyplan width, height and translation; filled table interiors; a paper frame that must not block its interior; unknown geometry rejection; horizontal/vertical/bottom-up flow; 50-section dense overflow and 200 mixed sections; impossible layouts without partial results; invalid numbers; section names beyond Z; deterministic plans; and 400 seeded randomized layouts.

Adaptive GAP coverage additionally verifies right-cluster priority with no left-flank spill, local compression that preserves the lower row, uniform global compression, a zero-only exact fit, failure only after a zero-gap trial, read-back using per-group spacing, and adjustment notices for MainForm.

`ReadingOrderTests` reproduces the GAP 35/>40 failure using captured production geometry. It checks physical A–E row order and F–G column order across high GAP values, rejects later letters backfilling earlier rows/columns, and permits a new row to start at the left below the previous row. Optional GAP values can be passed after `--audit`, for example `--audit 35 40 45 60`.

While the intended drawing is open in Tekla:

```powershell
& .codex-artifacts/arrange-view-session/tests/ArrangeView.exe --audit
& .codex-artifacts/arrange-view-session/tests/ArrangeView.exe --portable-audit 'portable/TTSK Dim Plates.exe'
& .codex-artifacts/arrange-view-session/tests/ArrangeView.exe --portable-audit 'portable/TTSK Dim Plates.exe' 30
```

Both audit modes are read-only. They do not call Run, Modify, Insert, Delete, CommitChanges, save, change the model work plane or switch drawings. `--audit` checks both orientations at GAP 0/5/15/30; `--portable-audit` checks a horizontal GAP 15 plan (or the optional supplied GAP) using the actual delivered EXE.

After verification, remove the task output directory and `tests/ArrangeView/obj`. Do not delete other tasks' build directories.
