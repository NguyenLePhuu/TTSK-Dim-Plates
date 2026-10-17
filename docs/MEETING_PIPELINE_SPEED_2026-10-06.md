# Meeting translation throughput and first-readable latency — 2026-10-06

## Scope and invariants
User requested a few seconds per readable sentence, without undoing source fidelity or losing queued turns. Only MeetingTranslationService.cs and the draft timing display/event wiring in MeetingTranslatorForm.cs changed. Backup: `backup/meeting-pipeline-speed-20261006-135906/` (numbered originals, hash verified).

Unchanged: exact Japanese source to Riva, Teams reader/normalizer/tracker, settling policy, source identity and revision guards, all visible startup rows, full pending queue, transient recovery, Riva ja-en then en-vi, glossary/technical/semantic guards, final-context order, speaker colors, dark/light layout, Tekla/drawing logic.

## Changes
- Prepare the two context-free Riva steps for at most two pending turns while the current turn completes. Ready preparations retain their exact source, request counts and cache counts; a mismatched source is rejected.
- The final translation worker remains sequential. Every editor receives the previous finalized context; output order remains FIFO. Source revisions cancel obsolete preparations and active work.
- HTTP requests are bounded (controlled test peak: 3 concurrent); the production handler allows 4 connections. Serializers are per call; AsyncLocal isolates each operation's counters. Only the existing context-free pivots are cached.
- Show a draft only after the same TryValidateCandidate routine used by final results accepts it, including technical values, required glossary and semantic/language guards. Mark it `Đang hiệu đính`. A final result replaces the same card and label; drafts do not enter conversation context.
- When a valid draft exists, allow the editor 2 seconds rather than 12. If it is late, use the already validated draft. Invalid drafts are never shown for speed; they retain the existing 12-second correction budget. All normal three model stages remain enabled.

## Measurements
Same endpoint/key and three synthetic sentences, no meeting recordings:

| Sentence | Earlier final latency | New first readable | New final latency |
|---|---:|---:|---:|
| ベトナム側では。 | 14,777 ms | 3,176 ms | 3,922 ms |
| 図面の寸法を確認してください。 | 4,620 ms | 5,738 ms | 6,590 ms |
| 明日の会議で確認します。 | 3,642 ms | 3,626 ms | 5,649 ms |

These are three live samples, not a statistical speed guarantee. They time translation calls, excluding Teams speech recognition, polling and settling. Network/model variation is visible: the second new en-vi request alone took 4,772 ms. The first baseline spent approximately 12 seconds waiting for the editor. The last new sample used its validated draft after the 2-second edit budget.

Controlled 12-turn test (identical fake provider delays and 36 total model calls in both modes): serial 6,065 ms; pipelined 2,552 ms, about 58% less elapsed time. All 12 sources and results retained order, and every editor had the previous finalized turn. Separate slow-editor test: first readable 102 ms, final 755 ms. A 7-second editor was canceled after 2 seconds only when its draft passed all guards.

## Verification and delivery
Final Release x64 build: `.codex-artifacts/portable-update/20261006-140846-dbedab74/build/`.
244 assertions passed: PipelineTests 35, RivaSourceContractTests 38, VerbatimTests 18, PerformanceTests 77, IntegrityTests 39, LatestPreviewTests 37. Checks include exact request payloads, 71 queued turns under a blocked provider, retry recovery, 300 source revisions, isolated parallel metrics, cancellation and stop, complete source replacing a speculative partial, mismatch rejection, invalid-draft suppression, and in-place draft/final updates for both themes.
Evidence: `.codex-artifacts/meeting-pipeline-speed/*-final.log` and `live-baseline.log`. API keys are never printed. Portable was updated and its SHA-256 manifest verified against the tested build.

## Limits
No long real Teams meeting was run. Network/provider delays, quality retries or sustained input above service capacity can still exceed 10 seconds. The application keeps those sentences queued rather than dropping them or presenting unvalidated output. Pending work remains session RAM and is canceled on explicit stop/close, as before. The 2-second editor budget may keep a less polished but guard-approved Riva wording instead of waiting for later stylistic corrections.