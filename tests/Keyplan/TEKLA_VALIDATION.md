# Tekla validation — 2026-09-27

## Environment and isolation

Tekla 2025 Service Pack 7, build 56843, Japan environment. Source model `D:\27-027)Sinjuku_Slope`; isolated copy `D:\TTSK_KeyplanValidation_20260927\27-027)Sinjuku_Slope`.

User explicitly authorized development changes and required production slot 09 to remain read-only. Pending source work was saved before switching. A separately named property preset was saved to measure UI encoding, without applying it to the source drawing. Geometry/view mutations ran only in the guarded copy. The source model and `[A-H100-3]` were reopened afterwards.

Tekla could not open the first copy under the Unicode workspace path; testing used the short ASCII path above.

## Property encoding confirmed in the UI

Saved Assembly drawing properties with Coordinate system **local**, X/Y/Z=0. Result:

```
dv.coord_sys 1
dv.coord_sys_x_rotate 0
dv.coord_sys_y_rotate 0
dv.coord_sys_z_rotate 0.000000
```

The original model preset used `dv.coord_sys 2` and the UI showed **model**. Saving X=90/Y=180 produced literal 90/180, not combobox indexes. Encoding is Shift-JIS 932. Baseline attribute SHA256: `DFD40B23FE02569F3696FBC966BEF9E8763F1455664384C11A087C417381436A`.

## Independent measurements

Created assembly drawings with `AssemblyDrawing(assemblyId, uniqueSheet, attributeName).Insert()`, one saved attribute file per tuple. Read actual `DisplayCoordinateSystem`, view type and sheet origin. No `RotateViewOnAxis*` substitute was used.

| Batch | Case | Samples |
|---|---|---|
| 1 | Beam +Y, profile rolled approximately 1.23° | 32 tuples / 64 frames |
| 2 | Column +Z | 32 / 64 |
| 3 | Beam +X | 32 / 64 |
| 4 | Beam +Y, upright profile | 32 / 64 |
| 6 | Beam reversed −X | 32 / 64 |
| 7 | Column reversed −Z | 32 / 64 |
| 8 | Beam yaw 37.25° | 32 / 64 |

Batch 5 was an incomplete development attempt: opening the new drawing required the force-open flag after changing the main part. It is excluded from evidence. Subsequent changed-part copies use that flag; the original model never does.

Fixtures contain the main-part normalized basis and measured frames, without the model database. The last field in basis files is the API-reported column flag. For the downward member in batch 7 it is false: Tekla reports it as BEAM but applies vertical placing. The checker therefore uses the geometric axis, not that flag, for vertical members.

`MeasuredTests` independently reads these frozen outputs. All 448 forward frames pass; all 224 inverse searches return exactly the expected complete set of tuples. Maximum measured angular error: **1.190163561803648E−10°**.

## Actual end-to-end tests

Both tests started with Local X=180/Y=180/Z=90:

| Case | Reader suggestion | Regenerated result |
|---|---|---|
| Beam +X | Local 0 / 0 / 0 | `Correct`, no tuple, no search |
| Column +Z, Top left / Front right | Local 270 / 0 / 0 | `Correct`, no tuple, no search |

The same production `Reader.Capture` and `QuickAnalyze` methods checked the wrong and regenerated drawings. All writes remain in the separate test harness.

## Source drawing read-only audit after restore

`[A-H100-3]`: Front right=(0,1,0), up=(0,0,1); Top right=(0,1,0), up=(−1,0,0). Its main-part local up is approximately (0.021508,0,0.999769).

The original drawing is **Model / 0 / 0 / 0**, confirmed by the native properties reader. It MUST NOT get a tick even though Beam Y's visible grid order is correct. The user confirmed the intended Y keyplan: SG-1 → SF-1 left/right and S17A → S17 bottom/top. Final result is **Beam Y: VerifiedSuggestion, Local / 0 / 0 / 0**. Beam X is independent and has no solution in the 32 allowed triples.

The previous exact 3D target comparison incorrectly rejected the member's 1.23-degree profile roll. The corrected acceptance checks the projected grid intervals on paper and the uniquely dominant viewing side; original-property/view consistency still uses full 3D comparison. A 45-degree side ambiguity is rejected. No nearest-angle rounding or sample-specific production branch is used.

## Original property reader regression

All 32 existing test drawings were read through the final native dialog reader while their same-named .ad presets were deliberately changed to Model/90/180/270. Every original Local tuple was read correctly, proving the reader does not trust the preset file. A selected drawing object was restored after every read. The decoy files were restored in finally; changes were confined to the copy.

Tekla's owner-drawn coordinate/angle comboboxes return `???` from WM_GETTEXT. CB_GETLBTEXT contains item data, not display text. The reader uses CB_GETCURSEL with indexes empirically verified for all 32 tuples on build 56843. Z is read from the native edit control. General attribute dialogs and Get do not reliably load active drawing values and are not used.

Final regression also checks Model with matching views, missing properties, invalid original angles, Local settings with stale view geometry, both user-confirmed X/Y axis orders and upward floors, rolled profiles, ambiguous 45-degree views, and skew grids. 78 semantic guards pass. An independent projection oracle checks 84 full searches against the measured frames in addition to the 448-frame/224-search 3D validation. Measured batch 1 independently confirms that the sample has no permitted X tuple, while Y selects Local/0/0/0.

## Release boundary

### Actual generated view roles: Front/Bottom column regression

`[A-C12-9]` originally stored Local/270/0/0 and contained Front on the left (Right=+Y, Up=+Z) and Bottom on the right (Right=−X, Up=+Z). The old incomplete-view path discarded Bottom and solved a synthetic Top-left/Front-right pair. Its 270/0/0 tuple therefore did not describe the actual drawing even though the target label cards were correct.

The new search uses actual view types and sheet positions. Local/0/0/0 was applied through the production native writer. An independent reopen confirmed the stored values; regenerated Front-left is Right=+X/Up=+Z and Bottom-right is Right=+Y/Up=+Z. A Drawing.GridLine audit locates SF-1 in the right Bottom view. S17A/S18 are target model-grid references and may be outside the visible cropped grid range.

`Fixtures/FrontBottomColumn2025SP7` contains all 32 actual saved angle tuples and 64 real frames from the current column. The original 0/0/0 tuple was restored after the sweep. This calibration changed drawing attributes only; no model object writes or work-plane changes. The independent matrix oracle now checks all six unordered pairs of Front/Top/Back/Bottom in both orders: 10,368 searches / 331,776 candidates. Back formulas additionally have oracle coverage; the new live matrix specifically measures Front/Bottom.

Post-apply validation binds the original plan to the resulting drawing: changed types, missing/duplicate views, wrong projected grid direction and wrong Local frame all prevent success. Reordered views produce a layout warning instead of a newly inferred tuple. Tests simulate Tekla's automatic type replacement and actual-view geometry failures even when saved XYZ is correct. UI cards identify their intended view types; an unavailable view is explicitly marked missing. These checks do not predict every Tekla Auto view-creation rule; unexpected output is detected after Modify and reported without automatic retry.

### Auto-apply addition, 2026-09-27

Production now includes a separate drawing-properties writer (`PHU_KeyplanApply.cs`). It has no Model API writes. Native field preflight was exercised against the active Assembly drawing dialog on Tekla 2025 SP7: Coordinate system has six choices (Local index 0, Model index 1), X has four, Y two; Z is an Edit. Local and calculated XYZ were filled, read back and Cancelled, followed by reopening to confirm original Model/90/0/0 was unchanged. Undeformed is preserved. Native Tekla automatically forces and disables Recreate = Yes for some angle changes (observed with Z=90/270). The writer keeps No when editable, but never bypasses a disabled field: forced recreation requires an explicit per-operation confirmation with default No before Modify. Cancelling that confirmation closes the owned properties dialog without committing.

The initial preview test did **not** press Modify and did not prove native binding updates. The subsequent authorized live validation below supersedes that limitation.

Historical preview matrix: all 32 Local tuples appeared correctly in controls and were Cancelled. This was insufficient: combo selection changes also need `CBN_SELENDOK` to commit their backing values and tree/apply flags. After this fix, even a coordinate-system change can force recreation. Do not use the previous Z-only observations as a rule; the writer reads native state each time.

323 semantic checks include zero/single/duplicate views, unknown view types, generic solid principal-axis recognition, column pair inference, stale angle requests, same-number-but-failed-geometry guards and changed drawing identity. Simulated light/dark UI tests now cover saved ON/OFF across form instances, automatic run on opening with saved ON, unchanged-setting idempotence, continued ON on drawing switch, error without preference loss or automatic retries, and no closing/reentry during asynchronous writes. Atomic preference writes and a fresh preferences instance confirm on-disk persistence. Existing 448 empirical view frames / 224 exhaustive searches / 84 projected searches still pass.

### Authorized live Modify / Apply / OK validation

Slot 09 scope is Assembly drawing only. Single-part drawing handling is intentionally excluded because a single-part view does not require this keyplan rotation workflow.

The user explicitly authorized modifying the current drawing. Active identity was `D:\27-000)HULIC_B|35198364|34913299`, mark `[C-csb20W-13]`, initial Model/0/0/0.

The old path reproduced the reported failure: edited controls displayed Local/270/0/0, but an independent reopen still read Model/0/0/0. Sending `CBN_SELENDOK` made Tekla accept the selection; the native recreation confirmation then appeared. The new writer handles that exact PID/title/message prompt while waiting for Modify, after the user's recreation consent, instead of timing out after five seconds.

Final unified production service tested with exactly one properties-dialog session:

| Keyplan reference | Stored values, independently reopened after the run | View result |
|---|---|---|
| 180° test reference | Local/90/0/0 | Correct |
| Restored 0° reference | Local/270/0/0 | Correct |
| Repeat at 0° | Local/270/0/0, Applied=False | Correct |

Each mutating run completed Modify → Apply → authoritative reload into the same dialog handle → OK. The authoritative reload was separately tested by changing X without Modify and confirming that reload discarded the edit and restored the drawing's stored X in the same window. The independent reopen in the live harness is verification only, not part of the production run. Before/after main-part Start/End and Local frame matched; production contains no model mutation or work-plane change. The final current drawing was left at Local/270/0/0 with Correct views.

### Generic Assembly main parts

`[C-BP-4]` is an Assembly whose linked main part is `ContourPlate / BASE_PLATE`, not `Beam` or `Column`. The old gate rejected it before searching any rotation. The expanded reader resolves the linked Part, extracts the 3D solid vertices in global coordinates, finds the unique long principal direction, and then uses the same 32-angle search and the same actual Front/Top display frames. It proposed Local/0/0/0 on the current Assembly; the 32-tuple live drawing roundtrip wrote and reopened every tuple, verified each measured view frame, then restored Local/0/0/0. Main solid geometry stayed unchanged.

For generic parts with equal principal lengths, no automatic direction is invented. For Assembly views without one authoritative main-part link, every visible view is checked and any paper rotation is reported per view; no unsafe shared Local tuple is offered.

Supported confirmation and rotation export are intentionally limited to the measured Tekla build, English native property labels, straight Beam/Column main-part coordinate semantics and Front/Top views. A different build, unreadable properties or unmeasured column placing option cannot pass. Numerical tolerance on paper is 0.0001°, not a claim of infinite precision.
