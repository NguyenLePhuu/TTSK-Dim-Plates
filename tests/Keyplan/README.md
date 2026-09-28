# Keyplan regression

Build: `dotnet build tests/Keyplan/Keyplan.csproj -c Release`

Executable: `.codex-artifacts/keyplan/tests/Keyplan.exe`

- `--tests`: pure geometry, independent matrix oracle, keyplan and error guards. No Tekla required.
- `--measured tests/Keyplan/Fixtures/Tekla2025SP7`: verify production forward/inverse model against 448 externally measured Tekla view frames. No Tekla required.
- `--ui <absolute-png-path>`: light/dark screenshots and UI state assertions using fixtures. No Tekla required.
- `--ui-live <absolute-png-path>`: read the active drawing once, render the resulting read-only UI and close the test form.
- `--live`: read-only audit of active assembly drawing with the production Reader.
- `--properties`: read original Coordinate system and X/Y/Z via a newly opened native properties dialog; close with Cancel.
- No arguments: extended read-only API inventory.
- `Verify-ReadOnly.ps1`: checks the slot 09 read path for forbidden mutation calls and excludes the development harness from production.

**`--experiment` is a development-only mutating harness, not a user command.** Its write actions require the exact isolated model path in `TeklaExperiment.cs`. Never ship this executable in portable. Production links Geometry, Reader, Properties, Form and MainForm integration. `read-settings` tests all 32 native tuples against deliberately incorrect same-named preset files and restores those files afterwards.

See [recorded validation](TEKLA_VALIDATION.md). The initial unverified rotation hypothesis and manual angle-baseline UI have been removed. Production now computes absolute Local angles from main-part geometry.

2026-09-27 automatic-mode review: 216 semantic checks plus the existing 5,185 independent matrix assertions and 448 measured frames pass. Added 3D Dầm/Cột recognition independent of BEAM/COLUMN names, rotated/reversed/sloped members, calibrated direction, endpoint/Local-basis consistency, grid coverage and compatible-superset selection, invalid auxiliary grids, conflicting-grid ambiguity, manual overrides, Y labels B→C with the single lower grid 1 and fixed column conventions. UI tests cover manual mode/direction persistence, return to full automatic, reset on drawing change and recovery from ambiguous direction.

Read-only live check on `[C-Hb15W-4]`: four grids, three without usable plan labels; automatically selected grid 27408149 and direction Y. Reference is B→C / 1 (below); original Model/90/0/0 gives Local/270/0/0. No drawing regeneration or mutation was performed in this revision. Grid ranking is a geometric heuristic when several complete grids compete; manual selection remains available.

Read-only live check on `[C-csb20W-13]`: API name BEAM, vertical reference axis of length 866, Top left of Front, original Model/0/0/0. Full automatic returns COLUMN and Local/270/0/0, exactly matching manual column mode; manual beam X and Y return no solution. Captured geometry is a regression fixture. Forward verification using predicted regenerated views is a simulation, not a live regeneration. `--portable <exe>` verifies the full automatic production path and compares it to the corresponding explicit mode on the same captured drawing.
