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

The live fixture requires zero errors/reviews/incomplete items on the approved drawing. It audits every straight set and foot, finds bottom anchors from main geometry in both views, replays 1 mm wrong picks in memory, then repeats the audit. No fixture IDs are needed. It never inserts, modifies, deletes, selects, saves or commits drawing objects or changes the work plane.

## Current limits

### Reliability hardening

The original feature/REF/grid permissions and 0.04 mm model tolerance are retained. New checks are conservative:

- Validate the complete straight chain: missing/non-finite points, unusable or out-of-plane measurement direction, unreadable placement, zero span and coincident measured stations. Coincident stations can be intentional and produce REVIEW, not an automatic correction or definite error. Signed/zero placement distance and shuffled point order remain valid.
- Keep missing geometry scoped to its view. An unreadable part in another view, or an unsupported unrelated dimension, no longer disables a provable end-notch relationship. Incomplete local geometry still prevents a semantic error conclusion. ERROR/REVIEW companion chains cannot authorize a notch accusation.
- Missing/invalid feature data yields REVIEW when it cannot support a verdict. Empty solid extraction is recorded as incomplete. A failed dimension read does not abort the other chains, and a partial chain cannot become companion evidence.
- Record exact versus projected support and all matching candidate owners in the audit. This records geometric candidates, not saved Tekla associativity. Error hints use the nearest measurement-axis residual and explain the amount of drift; no nearest point is automatically selected as the intended foot.
- Keep review reasons visible in the existing report, including the specific coincident-chain issue. All checks remain read-only.

The standard executable now runs the original 90 cases plus 488 hardening assertions, covering arbitrary rotations, mirrors, changed sizes, large translated coordinates, ordering, normal sign/magnitude, tolerance boundaries, broken geometry and view-scoped semantic evidence. Both feet drifting together are detected even when the numeric dimension value stays unchanged. Run live stress separately:

```powershell
& '.codex-artifacts/Slot09Check/tests/Slot09DimensionCheck.exe' --hardening-live '.codex-artifacts/DimCheckHardening/after-live.txt'
```

This reads the active drawing twice, transforms accepted feet/features in memory, and injects 1 mm wrong extreme picks in memory. It never modifies the drawing. On the tested drawing: 56 sets / 183 feet in 8 views retained all original verdicts, 732 rotated/mirrored/shuffled replays passed, 16 injected errors were detected, and the repeated audit was stable. These counts describe this fixture, not production requirements.

A foot accidentally moved onto a different valid hole/edge can still be geometrically supported. Only a proven semantic relation (currently the supported end-notch rule) can disambiguate that intent. Arbitrary expected dimensions, saved associativity, missing dimensions, overridden labels, and general line/text collision or placement intent are not certified by this check. The test replay is not a substitute for checking more real drawing types.

- REF policy follows Slot02: only the main structural part's reference is accepted directly. A connected structural neighbor contributes a single reference-axis intersection with main, including extension and a local geometry bound check. Parallel/coincident axes, remote intersections, unrelated primary-part REF and plate contour REF do not authorize feet. End-on projected references are accepted only when the point lies on main REF and passes the local bound check. Grid processing remains independent. The intersection rule is evaluated once per view, before classifying individual feet.

- Beam connection regression: external connector-plate reference contours cannot authorize structural neighbor REF dimensions. Only the directly connected structural member's REF is allowed; primary fabrication plates retain their normal geometry policy. Both contact-side feet on the current beam are detected, corrected REF projections pass, and overall 125 mm across the main envelope is protected from notch-depth inference. Pure tests cover rotated contact geometry and mirrored/rotated overall-versus-notch pairs. Live test: `--beam-live <report path>`; official executable check: `--product-beam <exe path> <report path>`.
- Report Recheck rebuilds the results from a fresh Analyze call. UI preview tests render 14 scenarios across both themes (errors, clean, single finding, review, warnings, compact with a long dimension chain, coincident-chain review), exercise 28 rechecks, custom scrollbar track input, single-click selection and previous/next boundaries using an injected selection callback. The native white scrollbar is disabled. These UI tests do not edit the Tekla drawing.

- End-notch semantic check: perpendicular two-foot dimensions sharing a shoulder are cross-checked against a continuous main-part contour path from the outer shoulder to the internal wall at the member end. The path excludes bounding-edge/end-cap shortcuts. Only a unique measured wall coordinate is enforced; an unrelated valid REF cannot replace it. Rotated/mirrored configurations and displaced extension origins are tested. Without this pair/topology evidence the ordinary geometry check remains in force; arbitrary drafting intent cannot be guaranteed from point membership alone. Tolerance is 0.04 mm. Production contains no fixture IDs or sample lengths.

- Geometric support is not proof of the saved Tekla associativity rule. Overlapping features can share the same projected coordinate.
- Exact allowed features and supported measurement-axis projections yield OK. Projection support requires a discrete anchor or an edge with constant measured coordinate; arbitrary intervals along longitudinal edges do not qualify. Unknown free points and disallowed features without supported projection yield ERROR.
- Primary geometry means the fabrication assembly, including its plates/subassemblies; in a single-part drawing it means the represented part. This geometry permission does not authorize every part's REF. REF requires the main/Slot02 intersection policy above, plus direct connection evidence from Slot08 for neighbor candidates. Dummy/proximity retention is not permission.
- Finite part reference segments and visible drawing grids mapped to model GridPlane are checked. Grid planes must project edge-on to a unique line, and only their constant measured coordinate is allowed. Angles and curved sets remain unsupported and are reported as incomplete.
- Geometry-read failure downgrades errors in the affected view to REVIEW. No guarantee is made that a drawing is technically correct merely because its feet have geometric support.
- September 13 correction: the previous algorithm omitted grids and incorrectly flagged supported projection feet. Live grid audit proved the missing coordinates belong to 5FL, BX1 and BY2. The approved C1mHz-13 fixture has 24 sets / 57 feet, zero errors or reviews; five wrong-bottom replays across both views are detected.

The user dialog lists suspected errors and clearly marked REVIEW findings, with view name, dimension chain values, chain side, foot location and a plain-language reason. A single row click highlights that set in the same active drawing; previous/next and keyboard navigation also select the corresponding set. Opening or refreshing the report does not select a drawing object automatically. The neutral raised cards have no search/filter controls, redundant selection button or list footer hint. F5 refreshes the report; Enter reselects the current row; Alt+Up/Down moves between findings. Selection still checks that the source drawing is active and the dimension exists. Preview tests simulate the selection callback and do not replace a live Tekla interaction check. Detailed IDs/topology/residuals remain in Audit(), separate from the user report. The user's September 13 instruction authorizes the official build and local portable update.
