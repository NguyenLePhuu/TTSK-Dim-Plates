# TTSK Auto Dimension for Tekla Structures

TTSK Auto Dimension is a Windows desktop tool that automates plate-drawing dimensioning workflows in **Tekla Structures 2025 SP7**. It helps detailers process an active drawing or a selected batch, create dimensions and section views, normalize drawing layout, and reduce repetitive manual work.

The application was developed by the **TTSK VN BIM Team** as a C# WinForms integration with the Tekla Open API.

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
