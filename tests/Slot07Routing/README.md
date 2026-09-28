# Slot 07 — explicit INJAI selection

- Click Slot 07 to run the existing INJAI/Data Center operation on the active drawing immediately. No project-name/path match is required.
- Shift+click Slot 07 retains the previous mode toggle for CREATE DRAWING / Batch.
- `RunDataCenterCurrentDrawing` owns a thread-local, disposable project-route override before Type-2 preflight and releases it in `finally`. Geometry and connection checks remain intact. This removes a project gate; it does not make unsupported geometry supported.
- Slot 09 retains its separate engine. The general route outside the selected operation keeps its previous behavior.

Offline checks compile the production router with a minimal model stub; this executable cannot connect to Tekla or write a drawing/model:

```powershell
dotnet build tests/Slot07Routing/Slot07Routing.csproj -c Release
& ./.codex-artifacts/slot07-routing/tests/Slot07Routing.exe
```

Tests cover an arbitrary project, no identity read under explicit selection, connection failures, nested/idempotent scopes, thread isolation, exceptional exit and existing automatic project recognition. Live DIM creation has not been run.
