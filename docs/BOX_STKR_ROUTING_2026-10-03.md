# AmC-1: STKR routing, overall placement and Exact

## Follow-up: Unknown left placement only

Per the user's scope correction, the added H/I, C and L Exact guards were
reverted. Their original section-selection and Exact behavior is preserved.
No all-section Exact enforcement was implemented. Initial AmC-1 routing and
Box/Unknown fixes below remain; this follow-up changes only Unknown placement.

Unknown now computes the vertical total distance as
`TopPoint.X - MinX + tierOffset`. The same compensated distance is used for
its direct writer and grid handoff. Feet, dimension values, horizontal
placement and tier spacing remain unchanged. Top's existing left anchor
still yields the original tierOffset. Front/Bottom totals remain outside the
left boundary even when a selected foot lies at the right end.

Follow-up validation: 7,337 reflection assertions,
2,515 flange cases plus fixture and 127 Slot02 cases passed. Read-only AmC-1
audit reproduced the old Unknown line X=2062.59750957536 and verified the
corrected line X=-100 with a 100-unit gap. Current representation readback:
Front/Top Outline, Section Exact (no mutation by this audit).

Portable is rebuilt after reverting the out-of-scope guards; all 39 runtime
files are verified by the portable workflow's SHA-256 checks.
Only the verified portable TTSK process was closed for this update. Tekla
remained open. No live dimension-writing test was performed.

The sections below record the initial investigation/build before this follow-up.

## Verified cause

Read-only Tekla 2025 API inspection identified single-part drawing `[AmC-1]`,
model `D:\27-027)Sinjuku_Slope`, main part 37468462, profile
`STKR250*250*12`. The actual `MainForm.DetectAutoDimPartType` returned
`ShapeUnknown` before the patch. Box recognition lacked STKR.

Unknown creates only overall dimensions. Its Front vertical feet choose the
outer-Y vertex closest to mid-length. On this beveled solid that is
X=2162.59750957536, rather than the left endpoint. With the raw 100 offset,
the left-directed line is still at X=2062.59750957536. This explains the
rightward total without changing the established Box geometry algorithm.

Unknown also explicitly set the main part Exact in every dimensioned view.
This was separate from its section-only classification. At the time of both
audits Front, Top and Section were Outline; historical changes are diagnosed
from that explicit mutation path, not inferred from current representation.

## Narrow change

- `MainForm.cs`: recognize normalized STKR as Box; expose the same existing
  string classifier privately for regression. Existing aliases are preserved.
- `PHU_Shape_Unknown.cs`: remove the main-view Exact loop and its helper.
  Require actual SectionView at the remaining Exact writer boundary.
- `PHU_Shape_Box.cs`: require actual SectionView at the Exact writer boundary.
  No change to Box chamfer, notch, hole, total or arrangement algorithms.

No forced Outline reset: existing user representation is preserved. Other
pre-existing workspace modifications were retained.

## Validation

- New regression: 3,857 assertions passed (routing, true feet, total spans,
  left/upper line levels, square/beveled geometry, mirrors, size, translation,
  reverse order, scales 5/10/15/20/30).
- Existing recessed-flange suite: 2,515 cases plus captured fixture passed.
- Existing Slot02 suite: 127 pure geometry cases passed.
- Read-only active drawing audit after build: route `ShapeBox`; both Front
  and Top have full span 2171 x 250; production Box anchor and offset methods
  passed; Exact candidate list contains only SectionView. Representations
  remained Outline throughout the audit.
- Release x64 portable workflow succeeded and verified all 39 runtime files
  by SHA-256. Executable hash:
  `741F1E311B20D0E489152017447152A95F2ABCC6E64C913F77E898E929285D50`.
- Focused diff/whitespace check passed. No publish, push or commit.

The read-only audit evaluates actual solid vertices and production selectors;
it does not execute dimension creation or verify rendered chamfer dimensions,
arrangement, persistence or second-run behavior on a live drawing. No live
drawing objects were created, deleted, modified or saved during this work.
