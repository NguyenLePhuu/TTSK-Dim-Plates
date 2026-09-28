# Fit neighbor envelope

Read-only audit and geometry regressions. No test command changes the drawing,
model, view attributes, selection or work plane.

```powershell
dotnet build tests/FitNeighbors/FitNeighbors.csproj -c Release
& '.codex-artifacts/fit-neighbors/bin/FitNeighbors.exe' --tests 'portable/TTSK Dim Plates.exe'
& '.codex-artifacts/fit-neighbors/bin/FitNeighbors.exe' --audit 'portable/TTSK Dim Plates.exe'
```

The helper targets the running Tekla 2025 x64 API and uses TSAppConfigPatcherTask.
Override `TeklaBinPath` for a different installation.

`grid-baseline.txt` was captured from the pre-change portable. It freezes 36 cases
for the single-axis candidate resolver and the two/three-axis selector (both sides,
missing axes, different distances, reversed enumeration). Do not regenerate it
from the new implementation to make a failing test pass.

Other regressions cover all 16 side lock masks, preserved depth, repeated fitting,
invalid inputs, longitudinal clipping of a long support while retaining its width,
mirrored/translated/rotated members, actual face contact, holes and false contact
through empty bounding-box space.

The live audit reads the current pink box, identifies main/assembly/bolt/weld
relationships, and invokes only the production read-only envelope planner. A
second plan uses an in-memory tight box derived from the main solid, simulating
a newly created drawing. It verifies complete core/plate containment with padding
and a stable repeated fit. It does not assign the simulated box to the view.

After validation, remove task-created `.codex-artifacts/fit-neighbors` and this
project's `obj` directory. Preserve source and the frozen baseline.
