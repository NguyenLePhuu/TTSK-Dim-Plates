# Teams English / Vietnamese captions — 2026-10-08

## Root cause and scoped fix

On this PC, Teams Settings → General → Language and regional formats was changed from Follow OS / English (United States) to Vietnamese (Vietnam), then saved and restarted. The meeting toolbar is now Vietnamese. The meeting title remains English because it is user-provided content, so title language is not a reliable locale signal.

The live UI Automation caption ancestor is a Group named `Chú thích trực tiếp`, with empty AutomationId and a generic `fui-Flex` class. The previous reader accepted `Phụ đề trực tiếp` but not this actual label, and therefore raised “Chưa thấy vùng Live Captions” even though three caption bodies were present.

Added the exact region alias `Chú thích trực tiếp`. Existing English names (`Live captions`, `Closed captions`), the previous Vietnamese alias, Japanese name and stable ID/class markers remain supported. Caption body scoping, nested Text extraction, source identity, revision tracking, worker scheduling, Riva payloads and translation quality rules are unchanged. Do not broaden detection to all `fui-ChatMessageCompact__body` nodes: the same Fluent class can appear in ordinary chat.

## Validation

- Before the fix, the production reader failed to find the caption region in the open Vietnamese meeting.
- After the fix, the rebuilt production reader returned all three visible sources exactly: `はじめましてウインレイフォーと申します。`, `私はウィンレーフォ。`, `ベトナム側では。`.
- LocaleTests validates English and Vietnamese region names, exclusion of caption setting/button labels and ordinary chat, the three live sources, and all three sources reaching the coordinator's injected translator unchanged.
- VerbatimTests and RivaSourceContractTests validate source fidelity through serialized NVIDIA HTTP requests, source revisions, retry and backlog handling using a fake HTTP transport. They do not assert live NVIDIA quality or latency.
- English region recognition is covered by contract tests; a live English meeting was not reopened, to avoid interrupting the current meeting.
- Final checks passed: LocaleTests 22, VerbatimTests 18, RivaSourceContractTests 38 (78 assertions). Release x64 compiled successfully; 41 portable manifest entries were verified, and the installed EXE matches the tested build. Code map check passed (57 files).
- Local artifacts are under `.codex-artifacts/meeting-localization/`; they are not runtime dependencies.

Only the region name list changed in product C#. No drawing algorithms or UI styling changed.
