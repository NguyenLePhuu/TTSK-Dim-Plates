# Revision performance verification — 2026-09-18

The change preserves the dimension matching rules. It reduces repeated work in
snapshot reading, chain extraction, colour validation and Delete REV attributes.

## Measured results

Same open BmC-117 drawing, Tekla 2025, 19 compared measurements / 6 revision targets.
These are local measurements, not guaranteed timings for other drawings.

| Operation | Before | After |
| --- | ---: | ---: |
| Analyze, first call in fresh helper | 5663 ms | 2902 ms |
| Analyze, repeated calls | 3456–3465 ms | 1741–2012 ms |
| Read unchanged snapshot again | 196–211 ms | 1 ms |
| No-op colour request | 1761–2086 ms | 1257–1281 ms |
| Write six actual child colours | 3583 ms | 1742 ms |
| Delete REV, public selection route on disposable section | 309 ms | 385 ms |

No Delete REV speedup is claimed from these runs. The isolated original deletion
core measured 59 ms. The long delay reported by the user was not reproduced in
this fixture. The implementation now loads its line preset once per operation,
reuses the sheet, and clones the preset when reapplying after insertion. It keeps
all geometry/style read-back and pre-delete checks. The UI shows a wait cursor.

## Changes and safeguards

- Keep one isolated DPM reader AppDomain per process. Fetch live presentation data
  on every Analyze; recreate the reader after a failure.
- Cache only one parsed file snapshot, keyed by full path and SHA256 content.
  Verify the file again after parsing and return deep copies to callers. File
  length/timestamp alone never authorize reuse. No drawing handles or colours
  are cached between analyses.
- Enumerate each native chain once per extraction/validation pass. Reuse those
  children for segment and full-chain signatures. Index presentation segments
  without changing duplicate/ambiguous handling.
- One macro request reads current native colours and writes differences, replacing
  the previous recursive read-job followed by a write-job. The fresh native read
  and writes occur in the same main-thread callback; cached UI colours do not
  decide whether a write is needed. Return the actual changed-child count.
- Read-only and no-op paths never set properties or commit. They still recheck
  complete chain geometry after reading. Actual writes retain native colour,
  mark/non-colour attributes, sibling checks, commit verification and rollback.
- No change to main-thread native dispatch, per-child `proFontColor`, matching
  tolerances, feet, spacing, parent/child IDs, or other DIM algorithms.

## Verification

All baseline and optimized analysis reports, verdicts, colours and full chain
signatures matched byte-for-byte on the fixture. The colour-cycle test verified
fresh native results and restored the user's original colours. Tests also prove:

- caller mutation cannot corrupt the snapshot cache;
- replacing a temporary snapshot with different contents at the same length and
  timestamp invalidates the cache;
- the reader recovers after invalid snapshot input;
- native colour changes after Analyze are seen even when the old result would
  have classified the requested colour as a no-op;
- a stale chain signature is rejected before native mutation;
- Delete REV creates the same Red/DashDot cloud and two exact diagonals, removes
  only a disposable section, and preserves the user's original section.

Offline: 90 classifier cases, 506 hardening assertions, 65 Revision assertions,
Revision session checks and 60 batch assertions passed. Native DPM loading emits
the pre-existing protobuf UTF-8 warning; the comparisons/tests still pass.

Artifacts: `.codex-artifacts/RevisionPerformance/`. Source backup: `before/`.
`before.txt`, `after.txt`, `write-before.txt`, `write-after.txt`,
`delete-selection-before.txt`, `delete-selection-after.txt`, `cache-live.txt`, and
`offline-final.txt` contain the measurements/checks. Test switches:

```
--revision-performance <report>
--revision-color-cycle <report>
--revision-cache-live <report>
--revision-delete-selection-live <report>
```

The colour-cycle/cache tests temporarily change colours and restore them; the
Delete test creates/deletes a disposable section and removes its test markers.
The isolated baseline build uses `RevisionSourceRoot` and a separate OutputPath;
the normal test/product builds default to the actual current production source.

Final source build and offline run passed after the last optimization. The final
public-selection Delete REV fixture measured 352 ms and preserved the original
section. The final read-only BmC-117 audit compared 19 measurements, kept 6 red,
13 unchanged and 0 review findings. Portable was rebuilt again at
`20260918-224421-1cf27dc2`; its EXE SHA256 remains
`155acda6a08996ede1eb985fdeea6120c1ea42d5be855d063b37087defb7c8d8`, matching
`portable/SHA256.csv`.

## Portable handoff

`Update-Portable.ps1 -BuildOnly -NoPause` rebuilt Release x64 and checked 39 runtime
files in run `20260918-223500-082097ce`. Portable EXE SHA256:
`155acda6a08996ede1eb985fdeea6120c1ea42d5be855d063b37087defb7c8d8`.
`portable/SHA256.csv` was updated and every listed file rechecked.

The actual portable was launched and Check DIM → Revision exercised through the
UI. It displayed 6 red / 13 unchanged / 0 review / 0 notifications; 690.2 remains
among the red measurements. The portable's own native job returned DONE, wrote
the six revised child colours, and its loaded writer hash matched the portable
EXE. `portable-request.txt`, `portable-colors.txt` and `portable-result.txt` record
that job. The final native chain audit is `portable-final-chain.txt`.
