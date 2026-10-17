# Teams caption source fidelity — 2026-10-06

## Reproduced defect
An older caption contains `ベトナム側では印象データセンターと。`. A different current caption starts as `ベトナム`, then becomes `ベトナム側では。`. The previous normalizer merged the full current revision into the older caption because the same speaker and prefix matched. The tracker lost the current ID, so its preview and translation remained at `ベトナム`.

Replay against the previous executable: `full_update_delivered=False`, preview `ベトナム`.
Replay against the installed fixed executable: `full_update_delivered=True`, preview `ベトナム側では。`.
A read-only live Teams probe independently enumerated caption body Text nodes: all seven visible captions matched the reader, including two distinct rows containing `ベトナム側では。`. This locates this reproduced loss after reading, in normalization/tracking.

## Fix
- Preserve original caption text, including spaces, newlines, punctuation and full-width characters, through reading, normalization and tracking.
- Merge only known overlapping source IDs, or an explicit Guest/non-Guest mirror with ordinal-identical full text and canonical speaker name.
- Never infer identity from a shared prefix or similar text across different IDs. A repeated utterance is valid.
- Carry source aliases into tracker entries; hand off only through known aliases.
- Honor actual source shortening instead of substituting a previous longer version.
- Remove silent 60/30 sample limits; oversized single captions produce an explicit error instead of disappearing.
- Preserve current startup policy, translation pipeline, revision cancellation, source/result matching and UI.

## Verification
Final Release x64 portable update: `.codex-artifacts/portable-update/20261006-115647-4add306b/`.
`VerbatimTests.cs` passed 18 checks against this final executable. A fake HTTP transport captured the actual serialized NVIDIA request and verified the full original phrase. This test makes no live API accuracy claim.
Other regression groups passed against the preceding fixed build (the final build only removed unused comparison helpers): Startup 17, LatestPreview 37, Performance 77, Integrity 39. The Integrity expectation for a different-ID ASR handoff was updated to require a distinct sequence and unchanged source text. All other assertions remained unchanged.
Evidence and harnesses: `.codex-artifacts/meeting-exact-source/`.

## Limits
Teams can revise its own live captions. The application preserves the exposed source; it cannot reconstruct speech absent from Teams. Distinct IDs without a known alias remain separate even when similar, to avoid losing real messages. Guest mirrors are only deduplicated when full text is identical. Inputs above 4000 characters are explicitly rejected. No live end-to-end meeting validation was performed after installing the final portable; no Tekla drawing behavior was exercised or changed in this fix.
## Follow-up audit: exact Riva input and slow-provider backlog
User explicitly required all visible source text to reach Riva unchanged, including when NVIDIA is slow. Final build for this audit: `.codex-artifacts/portable-update/20261006-121402-41953ae5/build/` (supersedes the build noted above).

Additional defects found and fixed:
- Quality retry used technical/glossary placeholders and collapsed whitespace. Both attempts now pass the untouched original `japanese` directly to Riva; terminology/number guards remain checks on the output.
- Editor shortened current Japanese to 1400 characters and pivot/draft to 1800. Current source and drafts are now supplied whole; bounded older context is separate.
- A full 32-item queue blocked the UIA reader. The session queue now retains pending turns in RAM without that capacity limit; it still replaces superseded revisions of the same sequence. A stalled provider no longer pauses Teams reads. The UI reports the waiting count.
- Temporary network errors, timeouts, HTTP 408/429/5xx exhausted provider retries and then moved on. The coordinator now retains the first sentence, waits 2/4/8/16/30 seconds and retries it until recovery, revision cancellation or explicit user stop. Distinct later turns wait in order.
- Startup previously skipped older visible rows. Production now includes every visible row at start. Latest-only startup remains an explicit internal test option, default false; the preview still displays only the newest sentence.

Final executable checks: RivaSourceContractTests 33, VerbatimTests 18, PerformanceTests 77, IntegrityTests 39, LatestPreviewTests 37 (204 assertions). Contract checks include 12 source cases captured from actual serialized HTTP payloads on initial and quality retries, transient HTTP retry, 300 deterministic text revisions, 71 queued sentences during a stalled worker, and timeout then HTTP 503 recovery preserving exact text and FIFO. Technical tokens, mixed languages, emoji, combining characters, whitespace, multiple Text leaves and near-4000-character input are covered. The old startup tests describing default latest-only behavior are superseded by the 71-caption default-constructor test, not claimed as current-default validation.

Scope of guarantee: character-for-character forwarding of captions exposed and successfully read through Teams UIA, while the session is running. It does not reconstruct vanished captions between UIA reads, correct Teams speech recognition, or guarantee model translation quality. The queue is session RAM, not crash-recovery storage; explicit stop/close ends it. Authentication, invalid responses and quality rejection retain an explicit failed Japanese row instead of pretending translation succeeded. Sources over the existing 4000-character guard fail explicitly rather than being truncated. Live probe results, when available, are in `.codex-artifacts/meeting-exact-source/LiveRivaSource.log`.
## Final live-provider finding and installed build
Final installed Release x64: `.codex-artifacts/portable-update/20261006-122026-80a7f7fc/build/`.
Live requests showed the old `meta/llama-3.1-8b-instruct` editor returning HTTP 410; a candidate `meta/llama-3.3-70b-instruct` also returned 410. The current NVIDIA `/v1/models` list exposed `nvidia/nemotron-3.5-lightning-30b-a3b`; a live candidate probe confirmed HTTP 200. The final product uses that editor with `chat_template_kwargs.enable_thinking=false`, preserving Riva ja-en/en-vi and all quality checks.
Official model reference: https://build.nvidia.com/nvidia/nemotron-3.5-lightning-30b-a3b/modelcard . Official reasoning flag reference: https://huggingface.co/nvidia/NVIDIA-Nemotron-3.5-Lightning-30B-A3B-BF16/blob/main/README.md . Actual service availability was confirmed by requests, not assumed from these catalogue pages.

A second defect surfaced in the live check: Riva returned `Trên phía Việt Nam.` for the exact full source, but the language detector lacked common directional Vietnamese words and rejected it. Added only trên/dưới/phía/bên to the existing dictionary; thresholds and semantic guards stay unchanged. Five new assertions cover both valid phrases and continued rejection of English, nonsense and a translation losing the side relationship.

Final test suite: 38 source-contract assertions, 18 verbatim assertions, 77 performance assertions, 39 integrity assertions and 37 preview assertions (209 total, including a 300-revision test). The live probe additionally captures the serialized Japanese text immediately before the real Riva request. Per-run results: `.codex-artifacts/meeting-exact-source/*-final-delivery.log`. No real API key is printed or included in those logs.