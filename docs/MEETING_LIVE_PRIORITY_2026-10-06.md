# Direct caption priority and isolated-sentence delay — 2026-10-06

## Evidence
The user's timing tooltip showed settling 600 ms, queue 31,390 ms, translation 889 ms, total 32,879 ms. This is head-of-line blocking, not evidence that the user spoke too quickly. The prior production implementation kept one final worker behind a full provider/retry/quality chain, even with speculative pivots. Old visible Teams captions and failed short phrases could keep this worker occupied across long pauses.

A live single-sentence reproduction found another algorithm defect: `ありがとうございます。` yielded `Cảm ơn bạn.` from Riva, but the Vietnamese language dictionary lacked cảm/ơn/bạn. The quality guard rejected that valid three-word phrase, repeated Riva and waited on the long editor budget, then failed. Baseline evidence: `.codex-artifacts/meeting-live-priority/short-baseline.log`. After adding common Vietnamese response words (without relaxing English/semantic thresholds), the same real API sample completed with one source request and exact source in 5,509 ms including process startup. Evidence: `short-final.log`.

## Current behavior
- Production creates three independent translators. Two background workers keep old captions; one live worker selects the highest Sequence from pending work.
- A newly settled caption causes the live worker to yield an older caption. Cancelled but still-current source is requeued, never deleted. The source's original timing is retained. Obsolete revisions are cancelled in every active worker.
- Provider recovery is per worker. A failed old sentence cannot force the live worker to wait behind it. Concurrency is bounded at three full translation operations; unused speculative preparation is disabled for production.
- Each worker's active caption, draft events, counters and timing are isolated. Same text spoken in different turns is still a different source identity.
- Results are displayed when ready. History cards are inserted by Sequence so late older results remain in chronological position; the live display does not rewind to an old result.
- Editors use available finalized earlier context only, never future turns or drafts. Unlike the old serial design, a slow earlier turn may be absent from a newer turn's context. This is the deliberate real-time tradeoff.
- Exact Riva source, source revision checks, guarded early output, 2-second edit budget for a valid draft, technical/glossary/semantic validation, speaker colors and themes remain.

## Verification
Final build: `.codex-artifacts/portable-update/20261006-143153-1611f3ee/build/`.
236 assertions passed: LivePriorityTests 27; source contract 38; verbatim 18; integrity 39; performance 77; preview 37. Older serial harnesses explicitly exercise the injectable one-worker compatibility path. LivePriorityTests separately exercises the actual production settings constructor with three fake HTTP clients.

Key tests:
- Hold all three workers on cancellable 30-second provider calls, then deliver a new sentence: new result arrived in 721 ms, including settling, before any old result. Release old calls: all four original captions complete exactly once.
- 41-turn burst retains every distinct source; new source queue wait under 600 ms.
- Four separated captions with old captions still visible: recorded queue waits 0, 0, 0, 0 ms (fake provider, not internet latency).
- Real production wiring uses all three clients; concurrent drafts match their own Japanese and counters; final results contain all nine test sources.
- Out-of-order completion sorts history and preserves live display in dark/light modes.
- Valid short Vietnamese phrases pass; English/mixed English remain rejected.

A preliminary live four-turn run before the vocabulary fix showed provider variation (including a 10-second final result) and the short-phrase quality failure. It is retained in `live-provider.log`; it is not represented as a passed latency test. The fixed isolated real request is `short-final.log`. No long real Teams meeting has been validated after this install.

## Limits
No absolute sub-10-second SLA can be guaranteed for NVIDIA/network outages or a model result failing correctness checks. The repair removes the application's shared FIFO dependency for new speech. Older retained sentences may still finish later, and finalization can occur out of order. Pending work remains RAM for the open session, as before. No source tokens are removed to achieve speed.

Backup: `backup/meeting-live-priority-20261006-142201/` (numbered/hash-verified source, form, map, manifest). Scope excludes DIM/Tekla algorithms. Portable updated and hash verified against this build.