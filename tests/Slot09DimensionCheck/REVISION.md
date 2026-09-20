# Revision — topology and native per-segment colour update, 2026-09-18

Latest performance update: see [REVISION_PERFORMANCE.md](REVISION_PERFORMANCE.md)
for measured before/after timings, snapshot cache invalidation tests, preservation
checks, and the remaining limit in reproducing the reported Delete REV delay.

## Colour writer follow-up on phu17 workstation — live verified

The actual failed job reported that `PresentationFramework` could not load.
Using its full framework identity resolved loading but `Application.Current`
was null in Tekla's macro context. The writer now uses a temporary, same-process
WH_CALLWNDPROC hook to dispatch only native property access to the verified
main-window OS thread. Public Drawing/Model API preflight, verification and commit
stay on the macro worker. Factory creation, property access and disposal all occur
on the main thread; no parent Modify, recreation or splitting is introduced.
Windows thread-hook behavior: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw

An intermediate live test incorrectly dispatched the entire operation, including
Drawing API, to the main thread and waited in Tekla PID 14864. Its job
`5758d8c50a394588b1eeacd5c0affb11` was cancelled and external helpers stopped.
The user restarted Tekla (new PID 2552). The final implementation prevents this
main-thread API call and recognizes cancelled jobs whose Tekla process has exited.

Before-test audit confirmed BmC-117: child 5078 (690.2) in parent 5075 with nine
children; all three changed values were black. Live writing then exposed a separate
verification bug: Open API `DimensionSetAttributes.Text.Font.Color` returned the
parent's Black while the child's native `proFontColor` correctly returned Red.
The old check therefore rolled back successful writes. Analyze now reads native
colours in a batch; writes refresh those values before the no-op check and verify
them natively after commit. Other attributes/marks and full chain geometry remain
checked through Open API. Read-only colour jobs never set properties or commit.

Live result: 5078/690.2 is Red in the original nine-child set 5075; both 2555 values
are Red; 14 unchanged measurements remain Black. All child/parent IDs, 3D feet,
dimension-line positions and values match the pre-write audit. A second pass writes
nothing. Save/close/reopen verified the colours and full chains persist on disk.
Selecting only 5078 in Tekla confirmed Properties → Font color: Red, with one object
selected. Tekla did not crash during this resumed run.

Build/offline suite passed, including 65 Revision assertions. Portable was rebuilt
locally with 39 runtime files checked by SHA256; no publish/push. Artifacts are in
`.codex-artifacts/RevisionColorFix`, including `live-color.txt`, `persistence.txt`,
`offline.txt`, and `portable-build.txt`. Live commands:
`--revision-color-live <report>`, `--revision-color-persistence <report>` (saves and
reopens the drawing), and `--product-revision-audit <exe> <report>` (read-only).
This native integration is verified on the installed Tekla 2025; other versions
remain unverified. The comparison algorithm and unrelated DIM slots were not changed.

The WPF-dispatcher description below documents the superseded implementation.

Open Check DIM → Check Revision. New and changed measurements become red
automatically. Unchanged siblings stay black. Mark and property-schedule findings
are read-only and never enter the colour-writing path.

## Historical WPF implementation and its validation boundary (superseded)

The colour writer no longer deletes or recreates dimension sets. It dispatches a
bounded request through the Tekla macro runtime, then uses the same native
DrawingPropertiesObjectFactory / proFontColor property as the Properties pane on
each existing child. This is an internal Tekla integration, not a public Open API
per-child font setter. It does not select objects, click the UI or modify the parent.
The macro request itself is launched on the caller's Tekla API context; it does not
wrap Operation.RunMacro in Task.Run. The native property operation is dispatched to
Tekla's WPF main dispatcher and checks the OS thread before touching the native proxy.
This guard was added after Tekla logged `DebugBreakIfNotInMainThread` for the earlier
probe path.
The public API explains why StraightDimension.Modify was insufficient:
https://developer.tekla.com/doc/tekla-structures/2025/straight-dimension-straight-dimension-attributes-class-50132

Before writing, the request verifies model path, drawing GUID, frozen baseline hash,
complete set membership, 3D feet, direction, dimension-line position, values and
original colours. It verifies all siblings and child attributes afterward,
including tags/marks, and restores original colours on failure. A versioned copy
of the writer prevents Tekla from reusing an older loaded assembly after rebuild.
Expired or cancelled requests cannot start writes. An uncertain pending write
blocks a second write until completion; logs are outside the model in LocalAppData.
Unchanged dimensions retain user colours. Arrange DIM code is untouched.

Matching first checks axis, range and witness feet. IDs only break ties between
geometrically valid candidates. When two chains share the same feet, the nearest
dimension-line level separates them; genuinely coincident candidates remain review
findings. All chains are assigned together; competing recreated chains remain review
findings instead of depending on enumeration order.
Within a chain, unique unchanged intervals anchor local edit regions. Split and
merged intervals are new measurements even if Tekla reuses an old child ID.
Monotonic alignment preserves later changed/translated intervals when point count
changes; equal overall counts do not conceal simultaneous add/remove operations.
A partially unreadable native set never contributes a partial chain.

This revision passed Release x64 compilation and offline regressions, including
60 Revision assertions, generated-macro compilation, native-property adapter
failure tests and cancelled/expired request tests. The broader suite also passed
90 classifier cases, 506 hardening assertions, session checks and 60 batch checks.
The final read-only audit on Tekla drawing BmC-117 compared 17 DIMs: it recognized
688.2 → 690.2 inside the nine-segment hole chain, 2553 → 2555 in both relevant
views, and left 14 measurements unchanged. Native colour persistence, unchanged
marks and native set membership still require verification in Tekla; no mutating
live colour test was run. An earlier native probe crashed on Tekla's main-thread
assertion, so the final audit intentionally remained read-only.
Already split sets from older versions are not automatically reconstructed.

Unsupported value formats/units and genuinely ambiguous geometry remain review
findings. The snapshot contains presentation data, not the full historical drawing
database; the implementation cannot guarantee perfect identity in every case.

## Historical split-writer implementation (superseded)

StraightDimension.Modify with internal DimensionSetAttributes did not persist the
font colour of a child within a native multi-segment chain. The previous write
failed at 690.2 and rolled back the whole operation; the UI incorrectly labelled
these known changes as requiring review.

The previous writer used the supported parent-set font attributes. Uniform-colour sets
are modified in place. A mixed-colour chain is partitioned into contiguous native
colour groups on the same dimension line. Feet, values, placement, child tags and
rule references are checked/copied; all replacement groups are created and read
back before removing the old set. Failure attempts restoration. The next analysis
assembles visual chains across native set boundaries, so these colour groups are
still compared as one measurement chain. Their new child IDs/signatures are
remapped in session choices, keeping toggle and original-colour restoration usable.

A recreated DIM is matched by measurement axis, range and witness-foot attachment
levels. A changed ID or dragged dimension line alone is not a new DIM. Historical
chains are reserved once; genuinely absent feature/ranges are labelled DIM mới.
Ambiguous/unreadable evidence remains explicit. Failed writes now say CHƯA GHI
ĐƯỢC MÀU and show the actual error in TTSK, instead of implying the value is uncertain.

## Read-only contents

The native DPM mark text is compared to the preserved baseline. For the example's
property schedule, only the columns preceding 図面番号 are compared. The boundary
is derived from table geometry and its header; changes or newly filled values in
図面番号 are ignored. Preceding cells and row count still participate in equality.
An example notification is: Ô số lượng khác giá trị [数量]. The UI uses the short
notification, not a full before/after dump of the table.

## Delete REV

Select one section view (or an object inside that view), then press Delete REV.
The command reads the existing selection; it sends no picker instructions, prompt,
macro, dialog or explanatory text to Tekla. All guidance/errors stay in TTSK.

It creates native revcloud geometry around Origin + FrameOrigin / Width / Height
in paper millimetres, including the view label, with a 1 mm margin. It loads the
revcloud attribute preset when available, applies the verified revision line style,
and uses a 0.5 bulge with clockwise corners. The two straight diagonals use the
same attribute builder as PHU_AllPartsDeleted (XKITLINE04, Red, DashDot, no arrows),
including the post-insert attribute application. Each endpoint is checked against
an actual returned cloud corner. Only after all three objects verify is the selected
section removed and committed. PHU_AllPartsDeleted's original workflow is unchanged;
only its line-attribute builder is exposed for reuse.

## Historical live validation of the superseded writer

- Release x64 and portable build, with local checksum verification.
- Offline: 90 classifier cases, 506 hardening assertions, 31 Revision feature
  assertions, session choices and 60 batch assertions.
- Native BmC-117: five actual revised values red (2555 in two views, 690.2, 20, 105),
  fourteen unchanged measurements black; only 690.2 red within the nine-value hole
  chain; no mark/schedule notifications after ignoring 図面番号.
- Recreated the unchanged 75 with a fresh ID and moved its dimension line 5 paper
  mm; it remained unchanged. Original line position restored. Second auto pass
  made zero writes.
- Created a disposable native section, ran Delete REV, verified one Red/DashDot
  cloud, two Red/DashDot diagonal lines exactly on its corners, and deletion of
  only the disposable view. All test markers removed; original section preserved.
- Real TTSK report UI in both themes: automatic colour state, black/red toggle,
  exclusion across F5, reset on reopening, selection and scrolling passed.

Commands (test executable, explicit live authorization required for live commands):

- No arguments: offline regression suite.
- --revision-audit <report>: read-only comparison.
- --revision-data-audit <folder>: read-only geometry/style audit.
- --revision-apply <report>: apply new/changed DIM colours on active drawing.
- --revision-recreate-live <report>: BmC-117 recreation/moved-line regression.
- --revision-delete-live <report>: BmC-117 disposable-section Delete REV regression.
- --revision-preview <folder>: mutating live UI colour/toggle regression.
- --revision-offline-preview <folder>: synthetic UI preview in both themes/widths.

Baseline freeze, model/drawing identity guards, no drawing save, session-only
exclusions and persistent original colours remain in place. The frozen snapshot
is the one available at first use; it cannot recover previously overwritten data.
