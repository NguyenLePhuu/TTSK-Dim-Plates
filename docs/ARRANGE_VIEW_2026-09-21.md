# Arrange View: dynamic occupied regions and centered section packing

The command keeps the existing UI overloads and moves regular section views only. Main/special views remain fixed. No view scale, paper size, drawing attribute or DIM geometry is changed.

## Data and geometry

- Read the actual sheet Width/Height and complete view frames in paper millimetres. Missing/invalid bounds reject the command; restriction-box or guessed A3 fallback is removed. Tekla documents the units of [ViewBase.GetAxisAlignedBoundingBox](https://developer.tekla.com/doc/tekla-structures/2025/get-axis-aligned-bounding-box-method-50559).
- Read live Tekla presentation data every run through a separate assembly-binding domain with a 15-second RPC deadline. Layout tables and linked keyplan views omitted from normal Drawing API view enumeration are included. Match native view IDs and origins before accepting the presentation.
- Occupied areas follow current geometry, including changed positions, widths and heights. There are no fixed keyplan coordinates/sizes or percentage-based top/bottom reserved bands.
- A normal table/keyplan occupies its entire bounding rectangle, including empty cells. A template containing the paper outline is decomposed into frame strokes, connected content and closed cells so its enclosing frame does not reserve the whole sheet. A large ordinary keyplan does not trigger that decomposition.
- Text uses a conservative glyph envelope; arcs use their enclosing circle. Unsupported or nonfinite geometry stops before movement. A small 2 mm obstacle allowance protects strokes; the sheet margin is 15 mm. These allowances do not determine table/keyplan size.

## Planning and application

Horizontal mode first centers a row below the main cluster, using the available interval and the requested GAP. Overflow uses the right-hand space and then further rows. Vertical mode honors its direction and finds additional space when columns fill. Five deterministic packing strategies are tried when the preferred layout cannot fit; the dense fallback may prioritize capacity over symmetry. There is no fixed section/row/column count limit.

The 2026-09-22 update follows the user's clarified preference to always prioritize the right cluster. Overflow is no longer placed in the left flank beside the main views. If the preferred layout does not fit at the requested GAP, it first reduces GAP only for the right cluster, keeping the lower row and its spacing unchanged. If needed, it then tries the alternate layouts and uniformly reduces GAP across the drawing. Descending trials explicitly include zero; a bounded refinement retains the largest feasible GAP found in the successful interval. The search is heuristic and does not claim the mathematical maximum.

Every planned placement carries its spacing group. Preflight and live read-back validate the actual group GAP, including the unchanged separation between the right cluster and other rows. MainForm's visible status reports whether the right cluster or the whole layout was compressed, with requested and effective millimetres. The user's GAP setting is retained for future drawings.

The whole plan must pass page containment, fixed-region collision, pairwise collision, item-size and GAP checks before any view moves. Read-back checks precede and follow commit; refreshed layout geometry is checked after commit. A failure attempts to restore section and linked main-view origins and verifies restoration. Failures are reported instead of being swallowed as success.

No packing strategy can put arbitrarily many views on a finite sheet at unchanged scale. Spatial failure is returned only after trying global GAP 0. Failure means no complete valid plan was found by these strategies, not proof that no mathematical packing exists. The command retains the drawing and asks for a smaller display scale or larger sheet rather than returning a partial layout. Missing or invalid Tekla geometry remains a separate preflight error.

## Validation on 2026-09-21

- 792 regression assertions passed, including 400 randomized trials. Of those trials, 333 found complete valid layouts and 67 returned no partial plan. Reversing obstacle enumeration preserved successful plans.
- Dense 50-section overflow and 200 mixed-size section cases passed.
- Read-only live audit: `[2-h-47]`, AssemblyDrawing, 841 × 594 mm, two main views and five sections. Both horizontal and vertical plans passed at GAP 0, 5, 15 and 30.
- The current keyplan frame was measured at approximately X 702.819–818.655 / Y 57.000–263.030 mm, with linked rendered content also protected. Those coordinates are evidence from this drawing, never constants in the planner.
- At GAP 15, the horizontal section cluster changes from X 25.000–495.072 to X 185.464–655.536 in the planned result, centered at paper X 420.5 mm.
- No mutating integration run was performed on the user's active drawing. Actual movement/rollback and visual approval on additional production templates remain integration checks.

Files: `PHU_ArrangeView.cs` owns the adapter and guarded write, `PHU_SectionLayoutPlanner.cs` owns pure packing, and `PHU_ArrangePresentation.cs` owns live occupied-region extraction. Other DIM algorithms are outside this change.

## Selected sections and special Bottom protection, 2026-09-29

Select the section **view frames** in Tekla and press the existing Arrange View button. Only selected, unprotected views move. A selected view can participate even if the automatic naming/size heuristic did not recognize it. Every unselected view remains a fixed obstacle and its origin is verified before and after commit. Unselected ordinary sections do not enlarge the main cluster used to anchor the preferred layout.

With no drawing objects selected, automatic recognition remains available. A selection containing only protected views or non-view objects does not fall back to arranging everything. Clear the selection to explicitly return to automatic mode. Selecting section cut marks or parts inside a view is not treated as selecting the section view frame. The result message reports the selected/automatic mode, arranged count, protected selected views, and any GAP reduction.

The previous named-section shortcut classified A as ordinary before evaluating its special role. The protection now checks model-coordinate view-plane orientation and longitudinal restriction-box coverage before the letter/name shortcut. A Top/Bottom-like plane contains the main longitudinal direction, is perpendicular to the Front plane, and covers at least 55% of its longitudinal model span (the existing special-view width threshold, now applied to model geometry). It does not depend on the letter A, current sheet placement, annotation extents or display scale. Transverse sections remain movable even when their paper frames are wide. This is a geometric classification; short local longitudinal details remain ordinary sections.

Validation:

- 1,420 assertions passed, including the previous packing suite, 400 seeded randomized layouts, selection scope, special-only/non-view selections, renamed/unknown section selection, and translated/rotated/mirrored longitudinal versus transverse geometry.
- Read-only API inspection identified `[A-G12-5]`, Front/Top and A-K. A has a longitudinal Top/Bottom-type plane; B-K are transverse. No letter or fixture identifier is encoded in production classification.
- Mutating integration against the rebuilt portable on that drawing: A-only is a no-op; selecting A+B+C moves only B/C and preserves every other origin within 0.001 mm; automatic read-only planning protects A; selecting all sections arranges ten views B-K while A/Front/Top remain fixed. A second run reproduces the same positions within 0.1 mm. View scales are unchanged.
- The original origins were saved under `.codex-artifacts/arrange-selection-tests/original-view-origins.txt`. The subset experiment was restored before the full-selection test. The final B-K arrangement remains on the open drawing. Requested GAP 15 mm was reduced by the existing planner to approximately 9.94 mm. A remains at its pre-test position; the tool does not guess a replacement Bottom location.
- The first integration attempt exposed a test-harness issue: selecting an empty list does not clear Tekla selection. The harness now explicitly calls `UnselectAllObjects`; the failed attempt restored all original positions before retrying.
- Release x64 portable update verified all 39 runtime files. Pure tests: `dotnet build tests/ArrangeView/ArrangeView.csproj -c Release`, then run `ArrangeView.exe`. `--inspect` and `--audit` are read-only; `--live-selection <portable-exe> <expected-drawing-mark> <snapshot-file>` is explicitly mutating and requires a matching A/B/C fixture.

The packing algorithm, presentation extraction, DIM creation, and existing Slot02 fix are unchanged by this Arrange View extension.

## Vertical mode fills rows, 2026-09-29

Historical implementation below: superseded by the selectable legacy/aligned-grid modes described next. Adaptive independent rows are retained only as an internal regression fixture, not the user-facing grid mode.

The existing vertical-mode button now fills each row left-to-right before proceeding to the next row. Its existing direction switch controls whether rows advance upward or downward. No new UI controls or fixed row/column settings are introduced. Horizontal mode retains its existing planner, including its right-column overflow behavior.

Each candidate row uses the actual sum of section widths, maximum row height, requested GAP, and obstacle-free horizontal intervals. The right-hand region is preferred when it can hold the complete ordered layout; otherwise the same-GAP search uses the available sheet regions. Subsequent rows cannot return across earlier rows. Column count may differ between rows where tables or fixed views narrow the space. Unequal-height rows align their bottoms when descending and their tops when ascending. Ascending plans use reflected planning coordinates, then return real paper coordinates before validation/writing. Actual view sizes remain attached to their letters; this does not relabel an old column layout.

The existing whole-plan validation and GAP reduction remain in use. Capacity-first and compact alternatives supplement the preferred row placement. This is a deterministic heuristic, not a proof of optimal packing; impossible or unfound layouts still reject without applying a partial plan. No section scale, protected-view classification or selection scope changes.

- 1,482 assertions passed: adaptive 1/2/3/6-column layouts, both directions, mixed sizes, varying capacities around a table, column-first permutation rejection, and all horizontal-mode regression fixtures. In 400 seeded mixed-mode random cases, 306 complete layouts passed and 94 safely rejected after GAP 0; the new row-order constraint differs from the former column-order constraint.
- Live tests against the verified portable on `[A-G12-5]` passed in both directions at GAP 15 mm without compression. Front/Top/special A origins and all view scales remained unchanged. The final upward layout is retained: B-C, D-E-F, G-H-I-J, then K. Row counts follow current free space rather than hard-coded fixture names.
- Before-run origins and live read-back evidence are in `.codex-artifacts/arrange-selection-tests/vertical-original-origins.txt` and `vertical-live-result.txt`. The earlier user adjustment placing special A below Front was preserved.
- Release x64 portable update verified all 39 runtime files; an initial transient file lock cleared before the successful retry. No application or Tekla process was terminated for this update.

## Legacy choice and aligned grid, 2026-09-29 (current behavior)

UI refinement: the initial long dropdown has been replaced by a compact, centered two-segment icon switch below GAP. Left shows column-first numbering (1/2, then 3/4); right shows row-first numbering (1/2 across, then 3/4). One click selects a mode; the active segment has an accent outline. Hover tooltips contain the full explanation. Tab focus, Left/Right and Space provide keyboard operation, with a dynamic accessible name. The control is disabled in horizontal mode. Default, algorithms and the separate up/down switch are unchanged. The earlier dropdown description below records the initial integration only.

Icon-switch validation: UI startup, left/right mouse selection, keyboard selection, disabled-state protection, non-overlapping bounds, light/dark rendering and selection retention across themes passed against the rebuilt portable. Both previews were visually inspected. The runtime update verified all 39 files. No drawing operations were performed for this UI change.

The vertical panel now has a two-choice selector in its existing free space between GAP and Apply. `Theo cột · thuật toán cũ` is the startup default and uses the original column-first planner; the existing four-argument API also defaults to legacy. `Theo hàng · căn hàng và cột` explicitly selects the new grid. Horizontal mode disables this selector and retains the old horizontal algorithm. The up/down switch controls progression in either vertical mode. Existing selection and special-view protection remain in force. The panel retains its dimensions and both action buttons, with verified light/dark rendering.

The user's approved manual sample is not an exact rectangular grid: it has B-C-D / E-F-G-H / I-J-K. It provides evidence for view-origin X alignment (B/E/I have identical origins) and common lower frame baselines (E/F/G/H and I/J/K). In the absence of an answer to the optional 3-4-3-versus-grid question, the implementation prioritizes the explicit request for both aligned rows and aligned columns. It does not promise to reproduce the manual 3-4-3 grouping.

For each feasible column count, build a complete grid from actual section frames and origin-to-frame offsets. All rows share the same column axes and equal column pitch. Pitch is the maximum adjacent-column envelope separation plus GAP. Row baselines use the maximum frame height in that row plus GAP, preserving equal clearances between row envelopes rather than forcing equal-height cells. An incomplete final row keeps the same axes; it is not independently centered. Bounds and annotation extents determine collisions, but column alignment follows the view origin. All units are paper millimetres.

Search the right corridor first, then the remaining permitted sheet region. Try column counts from the widest possible downward. Candidate vertical translations come from actual obstacle edges; forbidden horizontal-translation intervals are computed for every occupied cell, so empty final-row cells need not reserve space. Accept only a complete plan passing paper, obstacles, gap, reading order, common-axis and equal-pitch checks. Existing GAP reduction applies only after no complete grid is found at the requested GAP. This remains a geometric heuristic, not a guarantee of optimal layout or universal suitability of view origins.

Validation of the current version:

- 1,626 assertions passed, including legacy column-first behavior, all previous horizontal fixtures, 1/2/3/6-column grids, both directions, mixed frame sizes and origin offsets, shifted-column readback rejection, obstacle permutation, the manual-sample geometry and 80 additional random grid cases. The original mixed-mode random suite returns 318 complete layouts out of 400 again; the others safely reject.
- Active-drawing read-only planning produces three columns on `[A-G12-5]`, with a complete 3-3-3-1 layout. At requested GAP 15, upward fits at 15 and downward at about 14.92. The sample was not rearranged. Full inspection before/after is identical, including every origin, scale and selection.
- UI startup smoke found and fixed the old theme's last-control assumption. Both selector choices render without clipping or overlap in light/dark themes. Legacy is the default and mode switching survives theme changes. Images are in `.codex-artifacts/arrange-selection-tests/layout-ui.png`, `grid-ui-light.png`, and `grid-ui-dark.png`.
- Release x64 portable rebuilt; all 39 runtime files verified and executable SHA-256 matched its staging build. No new-grid mutating test was run on the approved manual sample, preserving it for comparison.

Read-only commands: `ArrangeView.exe --inspect`, `--grid-audit`. UI smoke: `--ui-layout <portable-exe> <output-png>`. No arguments runs pure regressions. Historical `--live-vertical-rows` targets the default four-argument API and therefore now exercises legacy mode; do not interpret its old label as a grid integration result.

## Validation of adaptive GAP on 2026-09-22

- 1,200 assertions passed, including right-only compression, unchanged lower-row geometry, both vertical orders, uniform global reduction, exact fit at GAP 0, and a spatial failure that reaches GAP 0 before rejecting.
- 400 seeded randomized cases: 358 complete valid plans, 42 safe failures after the zero-gap attempt; successful plans remained deterministic when obstacle enumeration was reversed.
- Read-only audit of the now seven-section `[2-h-47]`: at horizontal GAP 30, A–G retain their previous positions and 30 mm GAP; H and I both fit on the right using approximately 28.59 mm. I no longer appears on the left flank.
- The same drawing still fits at GAP 0/5/15 without any GAP reduction. Both horizontal and vertical audit modes passed at all four requested values.
- This follow-up changes the planner, result/read-back spacing contract and one MainForm status expression. Presentation extraction, scale, DIM, model objects and the drawing mutation scope remain unchanged. Validation on the active drawing remains read-only.

## Reading-order correction on 2026-09-22

The GAP 35/>40 failures came from spatial backfilling, not unsorted view names: after placing an alphabetical prefix, the planner could return later letters to a free pocket to its left or into a previously used column. Collision checks alone allowed that scatter.

Each plan now records consecutive row/column bands. Rows read left-to-right, with subsequent rows entirely below earlier rows; columns follow the selected up/down direction and progress to the right without overlapping the previous column's horizontal band. Once the planner returns to overflow rows, it cannot re-enter earlier columns. This contract is checked both before movement and on actual read-back. Preferred-layout global GAP reduction is tried before changing to alternate layout topologies.

- 1,323 assertions passed, including a production-geometry fixture at GAP 0/5/15/30/35/40/41/45/50/60/100 in horizontal/vertical and both column directions.
- In the current seven-section `[2-h-47]` drawing, horizontal GAP 35/40/45/60 produces one lower A–B–C–D–E row followed by F–G in the right column, with uniform effective GAP approximately 29.54 mm. No later letter returns to an earlier row.
- Of the 400 seeded randomized cases, 318 found complete plans satisfying both geometry and reading order; 82 returned safe failure after GAP 0. Some geometrically possible scatter layouts are intentionally no longer accepted.
- The active-drawing tests are read-only. This correction changes packing and its validation; it does not change presentation extraction or model objects.

Latest UI polish: removed the layout-switch tooltip entirely at the user's request. Increased the switch to 220 x 46, enlarged numbering from 7.5 to 10 pt, replaced the accent selection outline with a neutral filled segment, and improved selected/unselected contrast. Main orientation illustrations now use rounded, lightly filled shapes and clearer labels. Mouse/keyboard/disabled-state/theme smoke and visual review passed. Algorithms and drawing contents are unchanged. Portable runtime rebuilt and SHA-256 verified.

## Rectangle target region, 2026-09-30

Select one drawing Rectangle to override the arrangement region. Rectangle alone uses automatic ordinary-section recognition; Rectangle plus selected view frames uses only those views. Existing semantic special-view protection is applied in both cases. Without a selected Rectangle, the existing selection and mode behavior is unchanged. More than one Rectangle rejects before movement. Non-view drawing objects are not section selections.

The Rectangle is read in its owning drawing coordinate system and converted to sheet coordinates. Horizontal/vertical edges are supported, including native angles -90/90/180; skewed rectangles reject. The usable region is inset 2 mm from its edges. An explicitly selected region may lie outside the paper (as in the supplied live case); it replaces the paper limit rather than being clipped to it. Main-cluster placement preference is disabled inside this region, but all fixed views, tables and keyplans remain collision obstacles. The selected sheet Rectangle alone is excluded from presentation obstacles by exact object ID; other overlapping objects remain protected. A Rectangle owned by a movable view rejects because its target would move. No Rectangle geometry is changed or deleted. The Rectangle remains selected after success so repeated operations stay in the same scope.

All existing modes, GAP reduction, full-plan validation, readback and rollback operate on this region. A region too small or blocked for a complete plan rejects without moving views. No UI controls were added.

Validation: 1,668 assertions passed, including all previous no-Rectangle tests, external/reversed-corner regions, orthogonal angles, every mode/direction, skew/tiny-region rejection and exact-ID obstacle exclusion. Twelve mutating cases passed on [A-G12-5]: Rectangle alone / Rectangle+B,C crossed with horizontal, legacy vertical and aligned-grid vertical in both directions. Every target frame was inside the region, all unselected views and scales were preserved, and Rectangle start/end points remained unchanged. All original view origins and the original selection were restored and verified after testing. This current fixture has B-K sections; special-view protection remains covered by the earlier geometric/selection regression tests. Release x64 portable was rebuilt and all 39 runtime files verified by SHA-256.
