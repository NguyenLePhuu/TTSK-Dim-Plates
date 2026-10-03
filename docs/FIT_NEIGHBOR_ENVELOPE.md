# Fit View: neighbor and connection envelope

## Direct Fit without View Property (2026-10-01)

The Fit button preflight checks the same per-view Open Grid snapshots used by the
geometry planner. When none of the target views has a completed Open Grid
snapshot, both `None` and `Grid` Fit modes skip the Grid Visibility attribute
macros. Those macros opened View Property to change grid visibility and neighbor
`Collect By`; they were not part of the geometry calculation. Direct Fit now
keeps the existing grid/neighbor attributes without opening that dialog.

Target selection, geometry planning, 20 mm padding, preserved Z depth, final
restriction-box verification and the existing neighbor cleanup still run.
Selections containing an Open Grid target retain the full previous macro flow,
including mixed selections. Open Grid and Data Center commands are unchanged.

## Direct Fit without Open Grid (2026-09-26)

Fit no longer requires an Open Grid snapshot. Routing is per view, so a mixed
selection retains the existing behavior of each already-opened view:

- With a successful Open Grid snapshot, the original envelope floor, grid
  selection and layout rules remain unchanged.
- Without that snapshot, Fit computes tight XY from the same main/connection
  geometry and adds 20 mm model padding on all sides. It preserves the current
  Z depth and does not search for grids or arrange Top/Front. This also works
  when the Grid switch was left on; `None` remains the ordinary direct-fit mode.
- Without connections, the same computation reduces to the main solid bounds
  plus 20 mm, matching shape `ResizeViewBoundaryKeepDepth`.
- Outside neighboring members retain their complete transverse profile around
  the connection. Core members and attached plates remain complete. Membership
  is determined by identifiers/topology/contact, never drawing line color.
- Every target envelope is planned before writing. A failed direct-grid-route
  apply/readback restores captured entry boxes. The existing post-macro
  verification still rechecks the final restriction boxes.

An unopened view never treats its current XY frame as a minimum: oversized boxes
must shrink, cropped boxes must expand, and repeat fitting must not accumulate
padding. Direct Fit does not mark the view as having completed Open Grid.

Read-only audit of the user's active assembly drawing `[A-B12-7]`, main
`H150*75*5*7`, Front/Top 1:10, identified 10 core/connection parts and 7 neighboring
members. Front's protected XY is `(-180,-245)..(2165,350.5)`; Top's is
`(-180,-538.277)..(2165,555.643)`. These are audit outcomes, not production constants.
The Front check proves full transverse-profile containment with 20 mm gap for
both H300 end supports and the upper C100. All core/connection plate outlines
also pass. The model contains unrelated large geometry that is excluded.

The same audit simulates an oversized frame, a repeat run and no connections
entirely in memory. Main-only results are Front `(-20,-95)..(2005,95)` and Top
`(-20,-57.5)..(2005,57.5)`, both preserving Z. Frozen legacy grid selection and
side-lock regression cases pass. No mutating Fit button/macro was run on the
user's drawing; visual regeneration after applying the command remains a live
integration check.

The original no-grid command restored the box captured before Open Grid. If a new
drawing had already cropped a support or an attached plate, Fit reproduced that
crop. The grid command also used the full solids of every drawing part on its
unconstrained sides, which could include long or unrelated members.

## Rule

1. Resolve the drawing main part by its model identifier; include assembly
   secondaries for assembly drawings.
2. Protect all those parts plus plates directly bolted/welded to that assembly.
3. Resolve adjoining members through these connections and the support assembly
   of a directly connected plate. Discover unlinked physical supports with a
   local model query followed by vertex-to-solid-face contact within 2 mm.
   Bounding-box overlap alone is insufficient. There is no recursive traversal
   through a neighboring assembly.
4. Read exact solid edges in the current work plane, transform to global, then
   to `ViewCoordinateSystem`, the coordinate system of the restriction box.
   Cache geometry once per operation. Do not change the work plane.
5. Keep complete core/plate outlines. For a straight neighboring member, clip
   only along its own longitudinal axis to the fixed core connection region
   plus 3 mm on paper (at least the requested model padding). Preserve its
   transverse geometry. This shows the local connection, not the full story
   height or remote end of the member.
6. Union this envelope and 20 mm model padding with the original box. Preserve
   the original Z depth. Explicit section views clip core edges to their cut
   depth first.
7. Grid selection retains the legacy inputs and selection rules. The chosen
   left/right/top/bottom grid coordinates own those sides exactly; only the
   other sides use the protected envelope. With no suitable axis, fit all sides
   geometrically and return a no-grid diagnostic.

The focus region is frozen before processing neighbors. The current expanded
Open Grid box is never used to determine neighbor length, so repeat runs do not
grow the envelope. Saved original bounds remain a floor, preserving manually
approved wider framing.

Both Fit buttons and the Data Center Open/Create/Fit path verify the final box
after the neighbor attribute macro. If Tekla regenerated the boundary during that
macro, they reapply and read back the planned box. This happens before view
arrangement. A combo with a missing grid retains the protected fit and existing
view positions instead of reverting to the cropped original box.

## Evidence from the active drawing

Read-only API audit on 2026-09-22: assembly drawing `[C2-e-12]`, main
`H125*125*6.5*9`, Top/Front at 1:15. The main solid spans X 0..995. Directly
connected plate `112228763` spans Front Y -474.5..473.5. Left support width is
X -220..-20; the right support cross-section spans X 980..1280. Their long
member axes must not make the drawing several meters tall.

| View | Approved/current box XY | Plan from a simulated new tight box XY |
|---|---|---|
| Top | (-241.614, -290.475)..(1333.392, 285.342) | (-240, -262.5)..(1300, 262.5) |
| Front | (-261.054, -508.387)..(1300.236, 682.5) | (-240, -539.5)..(1300, 538.5) |

These numbers and identifiers are audit evidence only; they are not inputs or
constants in production. Applying the planner to the wider current box preserves
Top and extends only the Front lower edge to -539.5. No drawing was modified.

## Validation and limits

- Release x64 compilation and portable runtime hash verification.
- 36 frozen grid selection cases and all 16 combinations of locked sides.
- Mirroring, rotation, translation, repeat fitting, invalid inputs, unchanged
  depth, and contact/hole geometry tests.
- Production planner audit against the live drawing and a tight box computed
  in memory; complete core plate containment and idempotency verified.

The new-drawing combo and Tekla's visual regeneration have not been executed as
a mutating live test. Neighbor attribute filters/visibility still determine
which parts Tekla draws. This feature protects the restriction boundary; it does
not override those filters or move annotation marks. Curved/polyline neighbors
without a straight axis reject the fit with a diagnostic, rather than silently
restoring a clipped box. Vertex-to-face contact is a conservative fallback; a
connection with only edge/face penetration and no topology/near-face vertices
may require an additional geometric resolver.

Changed scope: `PHU_OpenGridView` and its new partial geometry adapter,
post-macro verification in `MainForm`, and the project compile item. Dimension
slot engines and the grid selection algorithms are unchanged by this task.
