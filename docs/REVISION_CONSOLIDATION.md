# Revision consolidation — 2026-09-19

Revision now has two production files: `PHU_DimensionRevision.cs` (engine)
and `PHU_DimensionRevisionForm.cs` (UI).

The engine incorporates the previous CreateDrawing, Presentation, Colors and
Delete files. Each original namespace body is preserved character for character,
including whitespace, comments and string literals. Each block retains its own
namespace-scoped using directives and aliases. Source markers identify the former
files. Production and test compile lists include only the consolidated engine.

Pre-change sources are backed up in
`.codex-artifacts/RevisionConsolidation/before/`. Exact body comparisons passed
for all five engine blocks (26,149 / 16,700 / 22,116 / 29,761 / 6,695 characters).

Check DIM restores the large vector green check for completed results with at
least one dimension and no errors, reviews or warnings. Skipped angle notices
remain visible. The top-right Revision button opens the existing idle panel;
opening it does not analyze or change the drawing. Returning preserves the report.

Validation:

- Before/after offline suite: 90 classifier cases, 506 hardening assertions,
  67 Revision feature assertions, Revision session checks, 60 batch assertions.
- 14 UI previews and 28 rechecks across both themes, including Revision
  navigation and pixel verification of the large green check.
- Revision Create offline geometry/UI checks: both themes and two widths.
- Release x64 portable rebuilt; 39 runtime files verified and SHA256 manifest checked.

No live drawing mutation was run. These checks verify source preservation,
compilation and covered regressions; they do not claim exhaustive live Tekla testing.
