# Approved recessed-flange semantics — full closure and 643–32

This supersedes the intermediate-foot and short-closure interpretation in
RECESSED_FLANGE_BEVEL_2026-10-02.md. The user's three reference images define
the intended result: enclose the complete cut and do not split the Top chain
at the internal web shelf.

## Production change

PHU_Shape_RecessedFlangeBevel now distinguishes:

- `End`: first shelf end, for existing Front shelf/flange details only.
- `ClosureEnd`: first point where the connected cut boundary reaches the
  actual member end; found by following real edges, never a fabricated point.
- `Corner`: intersection of the outer flange level with that member-end station.
- Face-chain outer foot: actual complete member extreme, with no internal shelf
  added to the chain.

On AmH-1, H300*150*6.5*9, the upper closure is:

1. Horizontal: (636.698,150) -> (675,150).
2. Vertical: (675,111.598) -> (675,150).

The lower closure is symmetric about Y=0. These lines close the entire cut,
including all steps and the web chamfer. The shelf at X=650 no longer limits
the closure. Front detail DIM remains 7/9/~35 degrees as before; the requested
Top chain contains only X=0,643,675, displaying 643–32 with overall 675.

The extra RecessedIntermediateFeet field and AppendRecessedFaceChainFeet hook
were removed from PHU_Shape. Its existing general chain writer is restored;
no replacement algorithm or new global DIM rule was introduced.

Owned short lines are removed only after new closure objects are created and
verified. Equivalent manual lines are reused without retagging, modifying or
deleting them. In the user's current drawing, two manual upper closure lines
were retained and two obsolete owned lower short lines were replaced.

## Validation

- 2,515 pure regression cases passed against the delivered portable, including
  assertions for complete closure coordinates under both mirrors, translation,
  resizing, changed thickness, one/four corners and split/reversed edges.
- Production full DIM runs at 1:5/10/15/20/30 in one process assert Top/Front
  overall 675, Top chain 643–32, no Top foot at 650, Front details 7/9, 13 DIM
  sets, four complete closure lines and two owned bevel angles.
- Readback checks LineDistance attributes for generated owned lines. Existing
  manual line attributes are preserved. Repeat/scale restoration compares all
  DIM/line/angle signatures and requires no duplicate or stale short lines.
- Four translated/rotated model work planes must yield identical recognized
  features, then restore the original plane.
- Save/close/reopen validation verifies persisted DIM and closure geometry.
- Release x64 update verifies SHA-256 of all 39 runtime files.

Evidence: `.codex-artifacts/recessed-flange/before-full-closure.txt`,
`full-closure-first.txt`, `full-closure-suite.txt`,
`full-closure-persistence.txt`, `full-closure-portable.log`.
The drawings folder was copied to `before-full-closure-drawings` before testing.

Recognition remains scoped to orthogonal horizontal web views of single-part
H/I members with the supported bevel/shelf topology. These results do not claim
support for arbitrary skew views or tapered flanges.
