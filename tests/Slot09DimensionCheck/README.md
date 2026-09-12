# Slot 09 dimension-foot checks

This test executable compiles the actual Slot 09 source, Slot 08 connection reader and main resolver independently of the application/updater. It does not build or replace the portable application.

From the repository root in PowerShell:

```powershell
dotnet build tests/Slot09DimensionCheck/Slot09DimensionCheck.csproj -c Release
& '.codex-artifacts/Slot09Check/tests/Slot09DimensionCheck.exe'
& '.codex-artifacts/Slot09Check/tests/Slot09DimensionCheck.exe' --live '.codex-artifacts/Slot09Check/current-live.txt'
```

Override `TeklaBinPath` with `-p:TeklaBinPath=...` if needed. The config patcher binds the test helper to the specified Tekla installation. Open the approved drawing before running the live fixture.

The pure tests cover both view bottom-edge cases, 1 mm wrong picks, free points, holes, shifted extension origins, connected reference versus connected solid edge, unconnected reference, rotation/translation, ordering, invalid coordinates and graph traversal boundaries. The graph visibility fallback must never authorize dimension feet.

The live fixture audits all current straight dimension sets and feet, replays the previous wrong lower point in memory for four corrected sets across Front/Top, and repeats the audit to verify identical results. Fixture IDs and sample coordinates appear only in tests. It never inserts, modifies, deletes, selects, saves or commits drawing objects or changes the work plane.

## Current limits

- Geometric support is not proof of the saved Tekla associativity rule. Overlapping features can share the same projected coordinate.
- An exact allowed feature yields OK. Only measurement-axis projection yields REVIEW, not automatic acceptance. Unknown free points and disallowed features without a supported projection yield ERROR (suspected wrong foot).
- Primary means the fabrication assembly, including its plates/subassemblies; in a single-part drawing it means the represented part. External members are permitted only for reference features through direct connection evidence from Slot 08. Dummy/proximity retention is not permission.
- Finite reference segments are checked. Grid dimensions, angles and curved sets are not certified; unsupported dimension types are reported as incomplete. Construction/drawing-only geometry is not treated as model evidence.
- Geometry-read failure downgrades errors in the affected view to REVIEW. No guarantee is made that a drawing is technically correct merely because its feet have geometric support.
- The current approved fixture still contains five unsupported feet and nine projection reviews beyond its corrected lower anchors. These are reported for manual review, not silently accepted or repaired.

Official application build and portable deployment are deferred at the user's request.
