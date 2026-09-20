# Plate Auto Section: horizontal and oblique partial bevels

## Root causes and final contract

1. The old straight-axis family included both horizontal and vertical Front
   edges. The final classifier explicitly excludes Front-vertical edges;
   horizontal and oblique remain separate families.
2. A 55% whole-plate span requirement and mandatory whole-plate midpoint
   excluded partial bevels. Candidate stations now start with the plate midpoint
   for compatibility, then midpoints of actual witness intervals in deterministic
   order. Each station must pass an exact solid-slice proof on the same exterior
   boundary. Long stepped outline edges with rectangular slices are rejected.
3. Direct endpoint-to-boundary adjacency rejected rounded terminations.
   Acceptance instead requires the exact bevel diagonal endpoints to match the
   exterior boundary and the Front witness level.
4. The Section creation switch in drawing properties was confused with whether
   its View property could be loaded. Explicit Auto Section can load that property
   even when automatic drawing-generation Section views are disabled.
5. Tekla can return a section origin offset from the requested insertion point.
   The plate worker explicitly places its new view before validating, preserving
   Front/Top. Geometry and rollback validation remain mandatory.
6. Plate worker aggregation and MainForm converted failures into NotApplicable
   and continued DIM. Preflight, creation, rollback and unsafe failures now retain
   their status and stop completion/save instead of reporting success.

Section depth is limited to the witness neighborhood. No drawing IDs, marks,
coordinates or sample thickness thresholds are encoded in production. A manual
section at another station is reused only if it crosses the same continuous
witness interval, has the same exact cross-section polygon, direction and scale.
All comparison geometry uses the analyzer's existing Front-coordinate Solid.

## Tests

```powershell
dotnet build tests/PlateAutoSection/PlateAutoSection.csproj
& ./.codex-artifacts/PlatePartial/tests/PlateAutoSection.exe 'portable/TTSK Dim Plates.exe'
```

525 checks cover directions (vertical, reversed, noise, horizontal, oblique),
rounded/direct ends, resizing, mirrored boundaries, reversed edge enumeration,
partial bevel candidates, exterior diagonal correlation, interior diagonals,
unrelated levels, tiny end features, and propagation of all creation failure
statuses through MainForm. Geometry utility rotation tests do not imply that
Front-vertical bevels are eligible; the direction gate is tested separately.

## Authorized live validation — 2026-09-19

The user corrected the initial interpretation and supplied a manual A-A on
B-GP-83 / PL6. That original drawing was saved and preserved. A separate single
part drawing B-GP-83 - 1 (name: TTSK Auto Section test) was created for live runs.

- Correct target: two horizontal bevels on the left projecting plate region.
- Their Front spans are about 23.2% of the whole width; offsets are about 4 mm.
- Planned cut is vertical at Front X approximately 44.491 (derived from witness
  endpoints); the manual sample is approximately 59.487 in the same constant
  region. Exact cross-section has six vertices and two bevel diagonals.
- Generated view has the same orientation as the sample, local X = thickness.
- All unique exact bevel dimension feet match the manual sample's geometry.
- View property new_Geo_single_part_PL_5_R1_2 and GEO_SECTION loaded successfully.
- Complete MainForm.RunCurrentAutoDimScript created A-A and dimensioned it.
- Second complete run reused A-A. Counts remained one SectionView, six section
  straight dimension sets, two Front sets and two Top sets.
- All six section plans matched exactly one actual dimension set by geometry
  feet and direction (allowing Tekla's reversal of point enumeration).
- The earlier horizontal cut through a vertical bevel was an incorrect test;
  it is superseded by the vertical cut through horizontal bevels above.
- The original manual sample is recognized read-only as ExistingEquivalent;
  its manual dimensions are not used as the expected automatic DIM count.

Evidence under .codex-artifacts/PlatePartial: user-sample.txt,
horizontal-full.txt, horizontal-second.txt, sample-equivalence.txt,
build-live.txt. Full runs after final error-propagation changes are recorded
separately as final-full.txt and final-second.txt.

H/C geometry and creation worker code are unchanged. Shared attribute resolution
now accepts configured Section properties with automatic creation disabled.
No live H/C or oblique drawing regression was performed in this session.

## Mixed horizontal + oblique bevel regression — C-RPz-8

Only PHU_Section.cs geometry correlation tolerance and diagnostic text changed
in this follow-up. Front matching already used max(0.001 mm, adaptive tolerance),
but center diagonal correlation used the smaller adaptive tolerance alone.

Read-only exact data: projected oblique exterior boundary 4.00022437413939;
correlated slice bevel endpoint 3.9999742735498742. The 0.0002501 mm difference
exceeded the old double adaptive tolerance (approximately 0.0002201 mm).
The true oblique bevel was rejected and its failure blocked the combined A/B
precheck. The status displayed the successful A branch first, misleadingly
showing 'Plate can 1 center Section A-A' as the apparent error.

The correlation now uses the same 0.001 mm matching floor as the Front witness.
This does not change candidate directions, station selection, part geometry,
creation/rollback rules, or H/C logic. Summary errors now identify rejected
families first; per-family text uses the actual A/B name.

Added 360 rounding/mismatch checks; total 885 pass. Positive perturbations of
0.0003/0.0008 mm pass, while a 0.02 mm exterior mismatch remains rejected.
The previous portable executable fails the new rounding regression.

Preserved original C-RPz-8 with its manual A/B. Created separate test drawing
C-RPz-8 - 1 from the same drawing attributes, initially without sections. Ran
MainForm.RunCurrentAutoDimScript twice:
- First: created A for horizontal and B for oblique bevel, both at scale 1:5.
- Second: ExistingLayout, exactly the same two sections, no duplicates.
- Both runs: six verified automatic straight-DIM sets per section, two Front
  sets and two Top sets. Each planned section dimension matches exactly one
  actual set by exact feet and direction. These counts validate the automatic
  DIM engine; they are not claimed to copy the manual sample's DIM count.
- Front/Top remain present. Visual inspection confirms A and B remain on sheet.
- Section View new_Geo_single_part_PL_5_R1_2 and GEO_SECTION loaded successfully.

Evidence: dual-raw2.txt, dual-full.txt, dual-second.txt, dual-build.txt and
 dual-old-regression.txt under .codex-artifacts/PlatePartial.
Original manual A's stored cut plane is outside the plate interval, so the
read-only existing-section classifier rejects that sample A; it was not altered.
The requested creation-from-no-sections case is the live regression above.

## Small Top bevels and existing-section DIM — C-RPz-13

User contract update: if any existing SectionView belongs to the current plate,
keep existing sections and run DIM; do not plan/create additional sections or
reject a manual section for differing from the automatic station/profile.
Both the public precheck and plate worker now enforce this rule before geometry
creation preflight. Sections of unrelated model parts do not trigger the bypass.
The previous geometry-equivalence discussion applies to automatic creation
validation, not this user-authorized existing-layout routing rule.

Top's legacy detail recognizer has a 5 mm minimum, which missed the actual
approximately 4 mm thickness bevels. A narrow fallback reuses the exact-profile
section bevel planner when the legacy recognizer finds no bevel. It consumes
the projected solid outer contour, not a center slice, so the overall 93.6 mm
length and bevel feet remain at real projected geometry. Run, land/depth chain,
thickness total, angles and overall length are created once. Established large
bevel and rectangular paths are unchanged; no T > 16 restriction is added.

Authorized live tests on C-RPz-13:
- Existing A view ID 1797 preserved on both full AutoDim runs.
- Top ID 1285 and Front ID 1264 preserved.
- Top: 7 straight sets and 2 angles; all 6 detailed bevel plans matched exactly
  one actual set, plus the total length set. Second-run counts are unchanged.
- Existing A: 6 straight sets and 2 angles; Front: 2 straight sets.
- No new section for the vertical bevel; the existing-section flow no longer
  reports a station/profile precheck error.
- Recreated A/B from zero on the owned C-RPz-8 - 1 test drawing after the final
  changes. Both sections created and dimensioned, six straight sets/two angles
  each; Front/Top remained present.
- 1,015 pure checks passed, including mirrored/resized/reversed small bevels,
  exact foot membership, rectangle rejection and large-bevel path preservation.

Evidence: vertical-baseline.txt, vertical-full.txt, vertical-second.txt,
vertical-dual-regression.txt and vertical-build.txt under .codex-artifacts/PlatePartial.

## C-RP-4: false oblique candidate and ordinary Top totals

The oblique outside contour lay inside global projected bounds and was treated
as a bevel witness. Its rectangular slice rejected family B and blocked the real
partial horizontal A. Front candidates now require a corresponding parallel
outer-boundary segment overlapping the witness region. The exact slice proof
remains mandatory. C-RP-4 - 1 reproduced the original B failure with the previous
portable, then created one A with the corrected source, retaining Front/Top.

The user correctly identified a regression in the recently added small-bevel
fallback: the projected rounded end left a lateral sliver of about 0.119 mm
across the 12 mm thickness. The fallback classified it as a bevel, attempted an
invalid short dimension, and the existing view catch stopped the subsequent
ordinary totals. The fallback now requires the bevel width run to exceed the
legacy THIN_CHAMFER_EDGE_TOL (1 mm). Slivers return to the unchanged ordinary
thin-view total path. Diagnostic instrumentation was removed after diagnosis.

Verified full-flow results:
- C-RP-4 - 1 Top: two straight total sets, 236 mm length and 12 mm thickness,
  zero angles, same counts after rerun. Actual thickness feet (0,-6) and (0,6).
- Section A retained: two straight sets and one angle for its full-depth bevel.
- C-RPz-8 - 1 reset to zero owned sections: A and B recreated successfully;
  each has six straight sets and two angles; ordinary Top has two totals.
- C-RPz-13: existing section retained and small Top bevel dimensions checked
  again after the sliver guard.
- 1,131 tests pass: ordinary thicknesses 6/12/16/28, widths 94/236/950,
  rectangular/sliver outlines, mirrored noise, small genuine bevels, legacy
  large-bevel path, false sloping outline rejection, axis rules and error states.

The ordinary CreateThicknessProjectionExactPlateDims and its total-foot
resolvers were not changed. This establishes no regression for the tested
cases; no claim of exhaustive validation over all possible plate shapes.
Evidence: outline-before.txt, outline-full.txt, top-debug.txt,
top-fixed-full.txt, top-fixed-second.txt, top-dual-regression.txt,
top-small-regression.txt. Top-debug records the regression before its fix.

## Conditional part-name mark placement

Restored the supplied reference file's upper placement formula and 15 mm
box-edge clearance, without its old width < 180 rule. The new selector reads
actual straight dimension line direction and signed distance in the mark's
view: a bottom-directed line below the part moves the name above; otherwise
the current centered-below placement is kept. Left/right vertical dimensions
and negative-distance lines are classified by their visual side. The mark
content filters, baseline preparation, box measurement and move method remain
unchanged; no DIM feet, section creation, hole marks or H/C code is modified.

C-BPz-1 live full run and read-back: bottom DIM detected, mark centered above,
actual lower mark-box edge is 15 mm above plate maximum Y. Unit tests cover
above/below, left-side DIM, signed distances and both box center formulas.
Total regression count: 1,138. Reference file was read only.
Evidence: mark-current.txt, mark-full.txt, mark-audit.txt, mark-below-full.txt,
mark-build.txt in .codex-artifacts/PlatePartial.
