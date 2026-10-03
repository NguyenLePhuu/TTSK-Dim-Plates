# Recessed flange bevel recognition — 2026-10-02

**Superseded semantics:** See [the final full-closure correction](RECESSED_FLANGE_FULL_CLOSURE_2026-10-02.md).
The user clarified that lines enclose the complete cut and the Top chain must
be 643–32. The 643/7/25 intermediate-chain implementation described below is
historical and has been removed.

## Confirmed failure

Read-only Tekla 2025 audit of single-part drawing `[AmH-1]`, profile
`H300*150*6.5*9`, reproduced zero recognized features in the old portable.
Both Model and Drawing APIs connected. The geometry was normalized from the
unchanged current work plane through global into the view coordinate system.

The front solid contains these distinct stations:

| Feature | X | Y, upper/lower |
|---|---:|---:|
| Outer flange bevel tip | 636.698 | ±150 |
| Bevel shoulder | 643 | ±141 |
| Shelf end / first inward web step | 650 | ±141 |
| Further web step | 665 | ±135 |
| Furthest web end | 675 | ±111.598 |

The old resolver assumed that every shelf ended at the solid's extreme X and
that a single vertical edge joined the upper and lower shelves. Neither is
true here. It also required exactly two bevels at one end and both opposite
outer corners to be intact.

## Change and technical meaning

`PHU_Shape_RecessedFlangeBevel.cs` resolves each corner using actual edges:
outer flange edge -> full-thickness diagonal -> horizontal shelf -> connected
inward web boundary. A bounded, visited graph search follows the web toward
its centre through steps and chamfers. Collinear interval coverage supports
split diagonal/shelf edges without bridging gaps beyond the 0.05 mm tolerance.
Competing candidates are rejected per corner. Square copes and ordinary
chamfers do not satisfy this topology.

The horizontal dimension means bevel shoulder -> real shelf end. On AmH-1 it
is 7 mm. The vertical dimension measures the 9 mm flange thickness; the bevel
angle is approximately 35 degrees. Face-view chain feet use the same real
stations, selecting exact vertices in the consumer view. No sample coordinates
or drawing identifiers were added to production selection logic.

Flange thickness uses the Tekla report property first, with the existing
profile-string parser as fallback. Audit output now explains rejection.

`PHU_Shape.cs` excludes only recognized corners from legacy front notch/chamfer
writers. Other corners continue through the existing rules. Face chains merge
the new feet only on the matching end, preserving the other end's legacy feet.
Other slots, hole grouping and UI routing are unchanged by
this patch.

## Follow-up: overall-length and repeat-run corrections

The first fix was incomplete: it put shelf endpoint 650 into the face chain's
`RightOuter`. The existing `ApplyTopBottomFrontNotchTotalAnchors` uses that field
as the entire member's end. Consequently Top measured 650 although Front and
the solid measured 675. Earlier pure tests incorrectly accepted that endpoint
without testing its downstream total consumer. Those expectations are fixed.

Outer face-chain points now mean the complete projected solid extremes.
The shelf is retained in a separate optional intermediate-feet list. The
legacy writer receives 0/643/650/675 (643 + 7 + 25), and its overall anchors
receive 0/675. PHU_Shape adds one optional field and one helper call; the helper
is a no-op for unrelated legacy chains. The existing total and hole algorithms
were not rewritten. The new 193-case consumer matrix calls the actual total
anchor function with resized/mirrored/translated and mixed-end geometry.

Same-process multi-scale tests found another defect: unrestricted GetAllObjects
in the recessed auxiliary cleanup attempted to deserialize unrelated leader-line
marks after the full DIM flow enabled AutoFetch, aborting Front DIM creation.
Cleanup now fetches only Line/AngleDimension, inside the explicit exception
boundary. This was verified with repeated runs in one process, not just fresh
helper processes.

Reference search included Git's original recessed rule and PHU_Shape files on
Desktop and Desktop/A. The compiled recessed file contains the notch-closing
line writer; those standalone Shape files contain a hole-centre-line helper,
not another complete recessed-bevel implementation. No wholesale replacement
of PHU_Shape was made.

## Validation and delivery — final follow-up

- 2,515 regression checks plus the captured live solid fixture passed against
  the delivered portable executable.
- Read-only live audit on `[AmH-1]` changed from 0 to 2 front features. Both
  shoulders/shelf endpoints were 643/650. Top and
  section views correctly did not independently classify front-web bevels.
- Release x64 portable update succeeded; the repository workflow verified
  SHA-256 for all 39 runtime files. No release publication was requested.
- Portable EXE SHA-256:
  `CE86170A17880CFC0D3F24C9D88F705E8465E6A784553654D623E850DB830F19`.
- Focused `git diff --check` passed. Existing unrelated workspace changes and
  deletions were preserved.

The user authorized mutating tests in the follow-up. Production ShapeScript.Run
was executed against the final portable on the current drawing. All five
supported scales (5/10/15/20/30) passed: 13 straight DIM sets, Top and Front
totals 675, Top chain 0/643/650/675, Front shelf widths 7 and depths 9, four
styled auxiliary lines and two owned ~35-degree angles. Two preexisting
unowned web angles were preserved. Repeated runs in one process and restoration
to 1:5 yielded identical geometry and placement signatures. Four temporarily
translated/rotated model work planes yielded identical recognized coordinates;
the original work plane was restored.

Visual inspection in Tekla confirmed both displayed 675 totals and chain
643/7/25. Final API readback versus the initial baseline differed only in the
two corrected Top sets; other DIM/line/angle signatures matched. The full DIM
command applied its existing view arrangement. Save/close/reopen testing also
passed, including line-style verification. AmH-1 is left open and saved at 1:5.
No model geometry was changed.

Local evidence under `.codex-artifacts/recessed-flange/`: `baseline-dims.txt`,
`final-drawing.txt`, `portable-live-suite.txt`, `persist-verify.txt`,
`portable-total-update.log`, and `before-live-drawings/drawings/` (disk backup).
The baseline records pre-test in-memory dimension geometry; the disk backup
does not claim to contain unsaved edits. Failure injection during Tekla writes
was not performed; disconnected and ambiguous geometric inputs are tested in
the pure suite.

## Supported boundary

The adapter still deliberately targets horizontal, orthogonal web views of
single-part H/I drawings with a full-thickness bevel and a distinct shelf.
Mirrored directions and transformed model work planes are normalized; arbitrary
in-plane rotated/skew web views, tapered/unequal flange thickness and bevels
without this topology remain on the legacy route. Ambiguous or disconnected
geometry is rejected instead of inventing dimension feet. The regression matrix
is evidence for these variants, not a claim that every possible steel cut is
recognized.
