# Check DIM Batch

Choose BATCH in the main window, select drawings in Document Manager, click Load
Selected, then Check Dim from the Check menu. The dashboard retains this exact
loaded list, removes duplicate drawing identities, and binds the session to its
model. Changing the selection in Document Manager alone does not change the batch.
Reload Selected to create a new batch. ACTIVE keeps the single-drawing report.

Check Dim automatically starts the loaded list when the dashboard opens; no extra
Start click is needed. Reopening a paused batch resumes pending drawings while
preserving finished results. Reopening a completed batch checks the list again.
The dashboard button remains available for Stop, Resume, and retry after a blocked
start. Processing runs one drawing at a time on the UI STA thread. It uses Analyze()
directly, never Run()/ShowReport(), so there are no per-drawing modal reports.
The timer yields between drawings; Stop/Escape takes effect between drawing API
calls, not in the middle of an analysis. Resume only runs pending drawings.
Closing the dashboard pauses it; reopening Check Dim retains results in this app
session. Results do not persist across application restarts; export a text report
to keep a record.

| Drawing state | Meaning | Available action |
|---|---|---|
| DIM cần xem | Geometric evidence identified suspect dimension feet | Xem DIM; recheck after editing |
| Cần xác minh | REVIEW findings, missing data, warnings, or no checked DIMs | Xem DIM / Thông tin |
| Không phát hiện lỗi | Checked DIMs have neither errors nor incomplete coverage | Xem DIM to inspect/recheck |
| Chưa kiểm tra được | Open, analysis, or cleanup failed | Recovery text, details, retry this drawing |
| Chưa hỗ trợ | Not an Assembly or SinglePart drawing | Manual review; remains in Cần xử lý |
| Chờ kiểm tra | Never run, queued for recheck, or pending after Stop | Start/Resume |

The progress counter says **đã xử lý**, not **đã kiểm tra**: failed and unsupported
drawings are processed but never counted as passes. The needs-work filter includes
unsupported drawings; bulk retry excludes them. Filters do not change batch scope.
Search matches name/mark (Ctrl+F). Duplicate names have separate numbered cards and
entry keys. Each drawing has its own counts, timestamp, recovery text and details.
All DIM/warning information and pending/failed/unsupported states appear in export.

Batch opens drawings in the background with public `SetActiveDrawing(drawing,
false)` and closes only its own drawing with `CloseActiveDrawing(false)`. It never
calls Save, Update, CommitChanges, Insert, Modify, or the Revision color workflow.
If a user drawing is already open, Start leaves it untouched and shows one inline
instruction to save/close it. Loss of connection, model change, wrong active
drawing, or cleanup failure pauses the queue. A failure local to one drawing is
recorded there and does not prevent checking the next drawing. When both analysis
and cleanup fail, both technical messages are retained.

Xem DIM opens the exact drawing visibly and analyzes it afresh. The dashboard
stores only compact findings, not geometry graphs or stale DrawingObject handles.
F5 in the detail report updates the same batch entry. A failed recheck invalidates
its previous pass. Selecting a DIM validates both model and active drawing first.
The visible drawing is left to the user after closing details, so their edits are
not automatically saved/discarded. Save/close it in Tekla before resuming batch or
opening another drawing. Opening another already-active drawing produces an inline
instruction and does not silently switch drawings.

The detail screen retains its explicit Revision command; automatic batch checking
does not invoke it. Classification, geometry, and Revision engines are unchanged.

Angle dimensions are intentionally excluded individually, before the straight
dimension count/classifier. The loop continues through all other DIMs and views.
Their IDs/views appear in a separate skipped-angle list, not geometry-coverage
warnings. Batch and detail show the count of skipped angles while preserving all
straight-DIM errors/reviews. A clean mixed drawing says "DIM thẳng: không lỗi";
an angle-only drawing says "Không có DIM thẳng" and never passes with zero checks.
Other unsupported dimension types still produce their existing warnings. A missing
angle ID is reported as `?` and cannot abort the remaining drawing scan.

## Verification

Build tests using the installed Tekla path:

    dotnet build tests/Slot09DimensionCheck/Slot09DimensionCheck.csproj -c Release -p:TeklaBinPath=C:/TeklaStructures/2025.0/bin

The normal no-argument suite includes BatchTests.Run. `--batch-tests` runs only
the deterministic batch tests. `--batch-preview <folder>` renders four synthetic
dashboard scenarios (light/dark, 1000/1180 px), tests filters/search/empty results,
one-drawing progress and Stop. These tests do not connect to or alter Tekla.

Live verification remains manual: selected Assembly/SinglePart drawings, a drawing
that cannot be opened, and an unsupported type; stop/resume, open detail, select
DIM, edit/F5, and confirm the matching dashboard entry updates. Do not describe
mock tests or a successful build as proof of live background drawing behavior.

Public API references:
- [SetActiveDrawing](https://developer.tekla.com/doc/tekla-structures/2025/set-active-drawing-method-drawing-boolean-48581)
- [CloseActiveDrawing(save)](https://developer.tekla.com/doc/tekla-structures/2025/close-active-drawing-method-boolean-48559)
