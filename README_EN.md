<p align="center">
  <img src="src/OpticalSystemDesign.App/Assets/Brand/AppIcon.png" alt="Optical System Design application icon" width="104" />
</p>

<p align="center">
  <a href="README.md">简体中文</a> · <strong>English</strong>
</p>

<h1 align="center">Optical System Design</h1>

<p align="center">
  <strong>Turn an optical idea into a system you can analyze.</strong><br />
  Modeling, tracing, optimization, initial-structure generation, and coating design in one desktop workbench.
</p>

<p align="center">
  <a href="https://github.com/VendettaMask/opticalsystem/actions/workflows/ci.yml"><img src="https://github.com/VendettaMask/opticalsystem/actions/workflows/ci.yml/badge.svg?branch=main" alt="Live CI status" /></a>
  <a href="global.json"><img src="https://img.shields.io/badge/C%23%20%2F%20.NET-10-512BD4?style=flat-square" alt="C# / .NET 10" /></a>
  <a href="docs/ARCHITECTURE.md"><img src="https://img.shields.io/badge/Avalonia-Desktop-7C3AED?style=flat-square" alt="Avalonia desktop UI" /></a>
  <a href="docs/CURRENT_STATUS.md"><img src="https://img.shields.io/badge/Status-In%20Development-D99A3D?style=flat-square" alt="In development; full release acceptance is not complete" /></a>
</p>

<p align="center">
  <a href="#features"><strong>Features</strong></a> ·
  <a href="#installation-and-startup"><strong>Install</strong></a> ·
  <a href="#developer-quick-start"><strong>Quick Start</strong></a> ·
  <a href="docs/README.md"><strong>Documentation</strong></a> ·
  <a href="https://github.com/VendettaMask/opticalsystem/issues"><strong>Report an Issue</strong></a>
</p>

<br />

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/workbench-dark.png" />
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/readme/workbench-light.png" />
    <img src="docs/assets/readme/workbench-light.png" alt="Optical System Design workbench with the lens data editor, document tabs, and system-options sidebar" width="1120" />
  </picture>
</p>

<p align="center">
  <sub>The desktop workbench in light and dark themes. The preview is an off-screen rendering of the actual Avalonia controls from October 15, 2026. It demonstrates layout, not numerical or cross-platform acceptance.</sub>
</p>

---

## Why Optical System Design?

Optical System Design is a non-commercial optical-design project maintained by S.T.A.R. Labs. It combines a shared optical computation core written entirely in C#/.NET with an Avalonia desktop workflow, keeping lens models, analysis results, and the design process in one place.

- **One workbench:** lens editing, 2D/3D layouts, analysis, optimization, and tolerancing share the same project and workspace.
- **Three UI languages:** Simplified Chinese is the default; English and Japanese can be selected instantly, and the choice is remembered.
- **One computation core:** the main application, initial-structure module, and coating module reuse the shared Core without an external Python optical runtime.
- **One complete product:** both modules are required, in-process features included by the standard installation; they do not need separate launchers or installers.
- **Traceable validation:** implemented capabilities, frozen references, numerical comparisons, and open validation boundaries are kept distinct. UI presentation is not treated as proof of numerical accuracy.

> The project remains under active development and has not completed full release acceptance. See [Current Status](docs/CURRENT_STATUS.md) for the current package identity, validation scope, and open work. Historical test counts do not supersede that status.

## Features

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🔭 Lens modeling and layout</h3>
      <p>Edit surfaces, materials, apertures, fields, and wavelengths, then inspect the system in 2D and 3D layouts. Multi-configuration and a separate non-sequential document workflow are supported.</p>
      <a href="docs/GUI_QUICKSTART_REFACTOR.md">Desktop workflow →</a>
    </td>
    <td width="50%" valign="top">
      <h3>📈 Ray tracing and image analysis</h3>
      <p>Analyze the current optical model with spot diagrams, ray aberrations, wavefronts, PSF, MTF, encircled energy, and image simulation.</p>
      <a href="docs/PARITY_MATRIX.md">Capabilities and support boundaries →</a>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🎯 Optimization and tolerancing</h3>
      <p>Define variables and merit targets, use the implemented optimization algorithms, and evaluate manufacturing variation through sensitivity, compensation, and Monte Carlo workflows.</p>
      <a href="docs/OPTIMIZATION_ALGORITHMS.md">Optimization algorithms</a> · <a href="docs/TOLERANCING.md">Tolerance analysis →</a>
    </td>
    <td width="50%" valign="top">
      <h3>🧩 Initial-structure design</h3>
      <p>Generate and refine candidate structures from specifications using real optical evaluations from the shared Core. Candidate results still require independent verification against the design goals.</p>
      <a href="labs/InitialStructure/README.md">Built-in module guide →</a>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🌈 Optical coating design</h3>
      <p>Edit layers and materials, calculate R/T/A spectra, and generate, optimize, tolerance, and save anti-reflection, high-reflection, and narrowband coating experiments.</p>
      <a href="labs/CoatingDesign/README.md">Built-in module guide →</a>
    </td>
    <td width="50%" valign="top">
      <h3>🛠️ Projects, materials, and manufacturing data</h3>
      <p>Use material and lens libraries, save complete projects, produce manufacturing drawings, and export STEP. Clearly scoped subsets of ZMX, SEQ, and LEN exchange formats are supported.</p>
      <a href="docs/FILE_FORMATS_AND_PLUGINS.md">Files and exchange formats</a> · <a href="docs/MANUFACTURING_DRAWINGS.md">Manufacturing drawings →</a>
    </td>
  </tr>
</table>

## Installation and startup

### Windows installer

Installers are distributed through [GitHub Releases](https://github.com/VendettaMask/opticalsystem/releases). Run `OpticalSystemDesign-*-Setup.exe` from a published release and follow the installer. The standard Windows x64 package is self-contained, so users do not need to install .NET or the initial-structure and coating modules separately.

See **Help → About** or **Help → Check for Updates** for the installed version. Update checks are manual and can open GitHub Releases to obtain the full installer. Online automatic upgrades are reserved, with no automatic downloading or installation. See the [version and release rules](docs/BUILD_AND_RELEASE.md#软件版本与-github-分发); a development version does not imply a publicly available installer.

Open the two built-in modules from **Laboratory → AI Initial Structure / Optical Coating Design** in the main application.

Open **Help → User Guide** for the searchable illustrated manual. It combines an actual workbench screenshot with theme-aware explanatory diagrams, covers every currently registered feature command and analysis, and follows the selected Chinese, English, or Japanese interface language. See the repository [User Guide scope](docs/USER_GUIDE.md) for coverage details.

The current installer is unsigned, so the operating system may display an unknown-publisher warning. Source updates do not automatically replace an existing installation. See the [Windows installation guide](docs/WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md) for installation, repair, and recovery, and the [delivery record](docs/BUILT_IN_MODULES_2026-10-13.md) for the actual package identity.

### Run from source

Install a .NET SDK within the compatible range specified by [global.json](global.json), then run these commands from the repository root:

```powershell
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-build
```

You can also use [Run-OpticalSystemDesign.cmd](Run-OpticalSystemDesign.cmd) on Windows or [Run-OpticalSystemDesign.command](Run-OpticalSystemDesign.command) on macOS. These development launchers restore, clean, build, and start the application in sequence; they do not delete user projects, themes, or workspaces.

Windows ARM64 and macOS publishing entry points, and a successful Linux CI build, do not mean those platforms have completed real-device installation or full desktop acceptance. See [Build and Release](docs/BUILD_AND_RELEASE.md) for the cross-platform delivery scope.

## Developer quick start

Primary stack: **C# / .NET · Avalonia · shared optical Core**. The root solution includes the main application, built-in modules, and their tests. Ordinary builds and regression tests do not require Zemax.

```powershell
# Run tests after the Release build above
dotnet test OpticalSystemDesign.slnx -c Release --no-build --no-restore /m:1 /nr:false

# Build the Windows installer with all required modules
.\Build-Installer.cmd
```

The first packaging run needs network access to prepare dependencies, target runtimes, and the installer compiler. See the [build guide](docs/BUILD_AND_RELEASE.md) for full requirements, frozen-evidence gates, and release boundaries.

<details>
<summary><strong>Show the code structure</strong></summary>

| Path | Responsibility |
| --- | --- |
| `src/OpticalSystemDesign.Core` | Shared optical models, materials, tracing, analysis, optimization, tolerancing, and file core |
| `src/OpticalSystemDesign.Application` | UI-independent application services, transactions, workspace, and DTO mapping |
| `src/OpticalSystemDesign.App` | Desktop workbench, built-in module UI, and recovery entry points |
| `src/OpticalSystemDesign.Modules` | Display adapters for built-in modules, reusing shared computation and canonical DTOs |
| `labs/InitialStructure`, `labs/CoatingDesign` | Internal domain libraries, design workflows, and tests—not separate products |
| `tests/`, `tools/` | Regression, offline validation, and maintenance tools; not product runtime dependencies |
| `docs/` | Current guides, feature contracts, and dated stage reports |
| `validation/`, version-controlled `artifacts/` | Validation evidence retaining file, setting, version, and hash identity |

Continue reading: [System Architecture](docs/ARCHITECTURE.md) · [Code Map](docs/CODE_READING_MAP.md) · [Collaboration Rules](AGENTS.md).

</details>

## Documentation and progress

| I want to… | Start here |
| --- | --- |
| Learn the desktop workflow | [User guide](docs/GUI_QUICKSTART_REFACTOR.md) |
| Browse all documentation | [Documentation index](docs/README.md) |
| Understand the current validation, installer, and remaining work | [Current status](docs/CURRENT_STATUS.md) · [Open issues](docs/OPEN_ISSUES_2026-10-08.md) |
| Review supported models and files | [Capability matrix](docs/PARITY_MATRIX.md) · [File formats](docs/FILE_FORMATS_AND_PLUGINS.md) |
| Review numerical foundations and validation boundaries | [Numerical validation](docs/NUMERICAL_PARITY.md) · [Operand support](docs/ZEMAX_OPERAND_SUPPORT.md) |

The default primary external accuracy reference is the committed Zemax OpticStudio 2026 R1 capture for `123456.ZMX`. Its conclusions cover only the captured file, settings, and software version. Importing or retaining a field does not mean its computation is implemented, and the project does not promise comprehensive equivalence with other software. Unsupported capabilities must be rejected explicitly.

Optiland 0.5.8 material is retained only as frozen historical auxiliary evidence. It is not regenerated and is not a product dependency.

## Contributing

Use [Issues](https://github.com/VendettaMask/opticalsystem/issues) for problems and suggestions, or contribute through [Pull Requests](https://github.com/VendettaMask/opticalsystem/pulls).

When reporting a computation issue, please include a shareable minimal project, software version, analysis settings, reproduction steps, and expected and actual results. Do not upload confidential prescriptions or data you are not authorized to share. Before changing code, read the [collaboration rules](AGENTS.md) and add tests and relevant documentation for behavioral changes.

## Project positioning and acknowledgments

This project is maintained by **S.T.A.R. Labs** as a non-commercial project. Non-commercial use does not automatically grant rights to use or redistribute every third-party resource; code, dependencies, fonts, icons, and material data remain subject to their respective licenses.

Thanks to .NET, Avalonia, and related projects for the underlying infrastructure. Third-party sources and license notices are retained in their resource directories and must not be omitted from distributions.

<p align="center">
  <sub>Optical System Design · S.T.A.R. Labs<br />Make the design process clear. Keep the computational basis traceable.</sub>
</p>
