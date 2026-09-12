# TTSK Auto Dimension for Tekla Structures

TTSK Auto Dimension is a Windows desktop tool that automates plate-drawing dimensioning workflows in **Tekla Structures 2025 SP7**. It helps detailers process an active drawing or a selected batch, create dimensions and section views, normalize drawing layout, and reduce repetitive manual work.

The application was developed by the **TTSK VN BIM Team** as a C# WinForms integration with the Tekla Open API.

## Quick start: download and run

For Google Drive / OneDrive distribution, use the validated runtime ZIP described in [distribution/README.md](distribution/README.md). It excludes development backups, scripts, debug symbols and personal settings, and includes hashes and local antivirus validation. Cloud acceptance must still be checked separately.

The recommended distribution for end users is the official **GitHub Release package** (`TTSK-Dim-Plates-Portable.zip`), which supports in-app automatic updates:

1. Download `TTSK-Dim-Plates-Portable.zip` from [TTSK Dim Plates Releases on GitHub](https://github.com/NguyenLePhuu/TTSK-Dim-Plates/releases/latest) and extract it to a convenient folder.
2. Make sure **Tekla Structures 2025 SP7** is installed and activated.
3. Start Tekla Structures, open the required model/drawing.
4. Run `TTSK Dim Plates.exe` to start the application.
5. (Optional) Create a Windows shortcut to the extracted EXE. Developers using a repository checkout can also use `Tao_Phim_Tat_Desktop.bat`; this script is not included in the Release ZIP.
6. **Automatic Updates:** When a new official release is published, the version indicator (e.g. `v1.0.0 ↑`) appears on the top bar. Click it to view release notes and update automatically without losing personal configurations (`theme.cfg`, `shortcut.cfg`, `auto_section.cfg`).
7. (For Developers) Double-click `Cap_Nhat_Portable.bat` in the root folder anytime you edit code to automatically compile the latest Release x64 build and update the `portable` package without opening Visual Studio.

## Auto Update and Release Workflow

- **End-User In-App Updates:** Each time the application opens, it checks the latest public GitHub Release in the background after the UI appears. A highlighted download arrow with a green dot appears at the bottom right when a new release is available. One click starts download, verification, update and restart; a progress dialog allows cancellation during download. Clicking the version label performs a manual check. Busy drawing operations block applying an update. A separate worker verifies the package, waits for a clean application exit, backs up managed files, replaces them individually, verifies the result and restarts. A durable journal supports rollback/recovery; the whole directory is not an atomic filesystem transaction.
- **Bootstrap from Legacy Versions:** Existing portable installations that do not have the updater service must be updated manually once by copying the new files over the existing directory (keeping existing `.cfg` files). After this bootstrap, subsequent updates are fully automatic.
- **Developer Release Flow:**
  `Developer edits code → Cap_Nhat_Portable.bat → Rebuilds Release x64 → Verifies SHA-256 → Commits and pushes to current branch`.
  When runtime files are pushed to `main`, GitHub Actions packages the committed runtime bytes, generates hashes, verifies uploaded assets in a draft and publishes a stable Release (`v<prefix>.<run_number>`). The runner does not build the Tekla project or sign the EXE. SHA-256 checks integrity; it is not publisher code signing. Pushes to feature branches and README-only changes do not publish releases.

Edit `release-version-prefix.txt` only for a new major/minor line (for example `1.1`). Patch numbers come from the workflow run number and can have gaps. Close pushes may replace a pending workflow run; this is not a FIFO queue promising a release for every push. Use `workflow_dispatch` on `main` for bootstrap/retry when no runtime path changed. A rerun verifies existing public assets without overwriting them; an older version cannot replace Latest. Draft conflicts fail instead of being deleted.

The updater preserves `theme.cfg`, `shortcut.cfg`, `auto_section.cfg`, logs, unknown local files and .NET user settings including `ManualScaleDenominator`. The existing application may migrate legacy shortcut settings when it starts; the updater does not rewrite them. `TTSK Dim Plates.exe.config`, the dictionary and declared artwork are managed runtime files.

Updater state, logs, journals and backups live under `%LocalAppData%\TTSK Dim Plates\Updater\<installation-hash>`. Downloads use a unique session under `%TEMP%\TTSK-Dim-Plates-Update`. Completed downloads older than seven days are cleaned up, and the latest backup is retained. Pending recovery data is kept. If recovery cannot validate a backup, the app refuses to continue on an inconsistent installation. Keep the journal and backup; if the target EXE cannot start, run the verified staged `TTSK Dim Plates.exe --recovery-update --target "<installation-folder>"` with all target instances closed. If staging is unavailable, restore the recorded backup with the app closed or use a verified Release manually while preserving user files.

Developer validation without publishing: `Cap_Nhat_Portable.bat -BuildOnly -NoPause`. The normal BAT also stages all repository changes and commits/pushes the current branch, so review the complete worktree before using it. Local updater tests: `powershell.exe -NoProfile -File scripts\Test-Updater.ps1`. Test fixtures are isolated in Temp and summaries are written to `.codex-artifacts\auto-update`; mock publish tests do not access GitHub. Live GitHub publication and production Tekla UI testing are separate release checks.

## Update from another computer

`Cap_Nhat_Portable.bat` does not depend on OneDrive or a fixed user folder. It finds the repository from the BAT file's own location, so it can run from any local folder on another Windows computer.

For a development machine that must build and push to GitHub:

1. Install Tekla Structures 2025 SP7, Git for Windows, and Visual Studio or Build Tools with the .NET desktop workload and .NET Framework 4.8 Developer Pack.
2. Clone the repository: `git clone https://github.com/NguyenLePhuu/TTSK-Dim-Plates.git`.
3. Sign in to GitHub in Git Credential Manager when Git asks for it, using an account with write access to this repository.
4. Open the cloned folder and run `Cap_Nhat_Portable.bat`.

The script builds in an isolated temporary folder, refreshes every runtime file in `portable`, verifies SHA-256 for each copied file, then commits, pushes, and compares the local commit with the remote branch (`origin/$branch`). It reports success only after those checks pass. If Tekla is installed outside the standard location, run `Cap_Nhat_Portable.bat -TeklaBinPath "D:\Tekla\2025.0\bin"`.

A downloaded ZIP or a copied `portable` folder can run the application, but cannot push source changes because it has no Git history or GitHub credentials.

The portable package includes this application's executable, NuGet dependencies, dictionary data, and artwork. It intentionally does not redistribute Tekla Structures product binaries; the application loads those assemblies from the local Tekla installation. If Tekla is installed in a non-standard location, set `TeklaBinPath` to its `bin` folder before starting the application.

> The application cannot dimension drawings without a running, licensed Tekla Structures session and a suitable model/drawing.

## Key features

- Process the active Tekla drawing or a batch of selected drawings.
- Check drawing scale before creating dimensions.
- Automatically create plate dimensions with rules specialized for common geometries, including L, C, box, and fallback/unknown shapes.
- Create and dimension section views, with an optional Auto Section workflow.
- Provide six focused Auto Dimension tools for selected main parts, neighboring/reference plates, sections, pallet/profile targets, plate edges, and diagnostic inspection.
- Normalize dimension spacing and line distances.
- Show or hide drawing grids and arrange main/section views.
- Configure keyboard shortcuts and repeat frequently used actions.
- Search an integrated Japanese-Vietnamese technical dictionary.
- Switch between light and dark themes.

## Technology

- C# 7.3
- .NET Framework 4.8
- Windows Forms
- Tekla Structures Open API
- Tekla Structures 2025 SP7
- NuGet packages for Trimble remoting and supporting .NET libraries

## Repository structure

```text
TTSK_Dim_Plates/
|-- README.md
`-- TTSK Dim Plates/
    |-- TTSK Dim Plates.slnx
    `-- TTSK Dim Plates/
        |-- MainForm.cs                 # Main UI and workflow orchestration
        |-- PHU_AutoDim_OK-V3.cs        # Core automatic dimension workflow
        |-- PHU_Slot*.cs                # Specialized dimension tools
        |-- PHU_Shape*.cs               # Plate-shape classification and rules
        |-- PHU_Section*.cs             # Section creation and attributes
        |-- PHU_ArrangeView.cs          # Drawing-view arrangement
        |-- PHU_DimSpacing.cs           # Dimension-spacing normalization
        |-- JapaneseDictionary.cs       # Dictionary UI and search
        |-- Data/                        # Dictionary data
        `-- Resources/                   # Application and tool artwork
```

## Prerequisites

1. Windows 10 or Windows 11.
2. Tekla Structures 2025 SP7 with a valid license.
3. Visual Studio with the **.NET desktop development** workload.
4. .NET Framework 4.8 Developer Pack.
5. Access to the Tekla Open API assemblies installed with Tekla Structures.

> By default, the project loads Tekla assemblies from `C:\Program Files\Tekla Structures\2025.0\bin`. If Tekla is installed elsewhere, set the `TeklaBinPath` environment variable or pass `/p:TeklaBinPath="D:\path\to\Tekla\bin"` to MSBuild.

## Build

1. Clone this repository.
2. Open `TTSK Dim Plates/TTSK Dim Plates.slnx` in Visual Studio.
3. Restore the NuGet packages listed in `packages.config`.
4. Select the `x64` platform.
5. Build the `Release` configuration.

The generated executable and its dependencies are placed under:

```text
TTSK Dim Plates/TTSK Dim Plates/bin/x64/Release/
```

## Run

1. Start Tekla Structures 2025 SP7 and open a model with drawings.
2. Build the application as described above.
3. Run `TTSK Dim Plates.exe` from the build output directory.
4. In the application, choose **Active** or **Batch**, load the desired drawing(s), check the scale, and run the dimension workflow.

Because the application communicates with an active Tekla Structures session, its dimensioning features cannot be exercised without Tekla running and a suitable model/drawing open.

## How Codex and GPT-5.6 were used

Codex powered by GPT-5.6 was used as an engineering partner during this project. The collaboration focused on concrete development work rather than one-shot code generation:

- **Codebase analysis:** inspected the WinForms application, Tekla Open API integration, shape-specific dimension logic, section workflow, and supporting drawing tools to understand dependencies and execution paths.
- **Implementation support:** helped develop and refine C# components for automated plate dimensioning, shape handling, section creation, drawing-tool actions, UI behavior, and keyboard shortcuts.
- **Debugging and refactoring:** traced behavior across large, interdependent Tekla drawing routines; identified edge cases; proposed targeted fixes; and reorganized code while preserving the established workflow.
- **Repository readiness:** reviewed the project for accidentally committed credentials, separated source from generated build artifacts, and prepared the repository for reproducible review.
- **Documentation:** produced and verified this README from the actual source tree, including the architecture overview, prerequisites, build steps, and known runtime constraints.

The developer remained responsible for the product requirements, Tekla-domain decisions, review of generated changes, and validation inside real Tekla models. Codex accelerated iteration, but suggested changes were inspected and tested before acceptance.

## Validation notes

- No API key or cloud credential is required by this application.
- A full end-to-end test requires Tekla Structures 2025 SP7, a licensed session, and representative production drawings.
- Build outputs, Visual Studio state, restored packages, local settings, and backup snapshots are intentionally excluded from version control.

## License and third-party software

This repository does not redistribute Tekla Structures binaries. Tekla Structures and the Tekla Open API are products of Trimble and remain subject to their respective licenses. Unless a separate license file is added, the source code in this repository should be treated as all rights reserved by its author(s).
