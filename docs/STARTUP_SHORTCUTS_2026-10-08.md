# Startup shortcuts — 2026-10-08

The waiting screen previously had KeyPreview but only handled Escape. Shortcut dispatch existed in MainForm, which had not opened yet.

Program.cs now loads the existing ShortcutManager configuration in TeklaStartupForm and handles the configured MeetingTranslation and WorkJournal bindings in ProcessCmdKey. Defaults remain Ctrl+M and Ctrl+H; custom bindings and unassigned actions are respected. Other commands continue through normal keyboard handling and cannot dispatch drawing operations on this screen.

Both existing entry buttons and these shortcuts use RequestStartupEntry. The selected entry is latched once, then the startup form closes using its existing cancellation/child cleanup path. Program dispatches meeting translation through the existing standalone route; the journal uses the existing WorkJournalReportForm and WorkJournal.Read to view saved history independently of MainForm and Tekla. It does not create a working session, access the model, or alter MainForm journal behavior.

Only Program.cs changed in product source. No new .cs files, drawing algorithms, translation scheduling, caption extraction or window styling were added or modified.

Validation: Release x64 build passed. The isolated verification harness passed 20 checks covering defaults, custom F8/F9 bindings loaded from configuration, disabled assignments, unrelated drawing keys, repeated input, existing button routes, cancellation when the shown startup closes, actual standalone journal message-loop startup/close, and absence of Tekla API assemblies. It uses its own configuration in the build staging directory. The installed user shortcut.cfg is unchanged. No live Tekla handoff was exercised.

Verification script/log are local under `.codex-artifacts/startup-shortcuts/`, not product dependencies.
