# Beam interior Grid regression

Read-only API audit and reflection planner tests. Neither command creates, deletes,
modifies, commits drawing objects, or changes the work plane.

```powershell
dotnet build tests/BeamGrid/BeamGrid.csproj -c Release
& '.codex-artifacts/beam-grid/bin/BeamGrid.exe' --tests 'portable/TTSK Dim Plates.exe'
& '.codex-artifacts/beam-grid/bin/BeamGrid.exe' --audit 'portable/TTSK Dim Plates.exe'
```

Pass `-p:TeklaBinPath=<running Tekla bin>` when the installation differs. The
TSAppConfigPatcherTask prepares the helper's executable configuration.

`legacy-signatures.txt` captures 72 pre-change exterior/coincident planner cases:
modes 1/2/3, TOP/FRONT, left/right grids, and cross grids above/below/on REF.
Tests compare complete plan names, feet and distances against this frozen baseline.
Additional cases cover the interior grid, missing exterior grid, ambiguity, cut-end
tolerance, scale 5/15/30, changed lengths, translated/rotated coordinates, reversed
enumeration, view-role isolation, and supplementary view handoff/no-handoff.

Interior order from inside out is G1 REF–EDGE–EDGE–REF, G2 REF–GRID–REF,
G3 REF–REF. All line levels come from the Shape handoff, not fixture coordinates.
Grid=1 keeps FRONT/BACK's existing behavior; Grid=2 enables interior horizontal
plans in TOP/FRONT and standard BOTTOM/BACK views. Arbitrary SectionViews are
not inferred to be BOTTOM/BACK; the existing primary Shape view mapping remains
authoritative.

Active drawing inspected on 2026-09-21: assembly `[C1-SB15-6]`, H150*150*7*10,
TOP and FRONT scale 15. In both views BX1 is at station 300 from REF-left within
the cut span 110..1740, while another grid coincides with REF-right at 1900.
The original mode-2 resolver omitted BX1. The updated resolver selects the
interior grid without changing the exterior axis used for existing alignment.
Existing TOP dimensions establish grid-chain-middle / overall-outer ordering.

Validation uses the verified portable executable. No mutating AutoDim run was
performed on the user's drawing. Supplementary BOTTOM/BACK variants are covered
by planner tests; they were not present in the inspected drawing.

After validation, remove only this test's `obj` and `.codex-artifacts/beam-grid`
directories; preserve the checked-in source and baseline fixture.
