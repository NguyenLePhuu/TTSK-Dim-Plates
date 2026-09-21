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
