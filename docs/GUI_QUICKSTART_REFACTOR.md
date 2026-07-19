# Optiland GUI Quickstart Comparison And Refactor

## Scope

This audit uses the Optiland 0.5.8 documentation as the behavioral reference:

- [GUI Quickstart](https://optiland.readthedocs.io/en/latest/gui_quickstart.html)
- [Developer Guide: Optiland GUI](https://optiland.readthedocs.io/en/latest/developers_guide/gui.html)
- [Developer Guide: Architecture](https://optiland.readthedocs.io/en/latest/developers_guide/architecture.html)
- [Developer Guide: File Format](https://optiland.readthedocs.io/en/latest/developers_guide/optiland_file_format.html)

The goal is behavioral and architectural alignment. This project remains a clean-room .NET/Avalonia implementation; it does not embed or wrap the Python Optiland backend.

## Executive Comparison

| Area | Optiland 0.5.8 reference | Workbench before refactor | Workbench after refactor |
| --- | --- | --- | --- |
| Runtime | Python, PySide6, Optiland backend | .NET 10, Avalonia, managed C# core | Intentionally unchanged |
| Main shell | Main window, action manager, panel manager | Main window and action manager; panel creation hard-coded in `MainWindow` | `MainWindow`, `ActionManager`, Application services, and the Dock workspace have separate ownership; the top category selection drives one linked large-command Ribbon |
| New system | Create from scratch | Always opened the Cooke-style demo | Starts blank; blank and Cooke demo are separate commands |
| Lens editor | Editable sequential surface table | Present, including component editors | Retained |
| System properties | Aperture, fields, wavelengths | Present, plus backend selection | Retained |
| 2D viewer | Lens and rays with pan/zoom | Static rendering | Equal-scale YZ view, outlined elements, split aperture-stop blades, pointer-centered wheel zoom, shared optical-axis pan, ray visibility, reset |
| 3D viewer | Interactive VTK rotation/pan/zoom | Static orthographic wireframe | Light-background translucent lens solids, colored ray bundles, drag rotation, Shift-drag pan, pointer-centered wheel zoom, solid/wireframe rendering, reset; still not VTK |
| Analysis | Configurable analyses with graphical plots | One analysis page with metric table and text report | All 32 analyses are grouped in the top Analysis Ribbon; selecting one runs it into a closable page with bottom Plot/Data/Text tabs, collapsed graph settings, persisted parameters, and interactive plots |
| Analysis refresh | Connector signals update consumers | Connector events already used | Revision events mark heavy results stale; only the synchronization icon reruns them, with cancellation and generation checks |
| Command palette | `Ctrl+K`, searchable commands | `Cmd+P` only | `Ctrl+K` and `Cmd+K`; actions include panels and layouts |
| Layout | Dockable panels and saved layout slots | Fixed split tabs; one persisted layout | Dock.Avalonia tabs support drag splitting, merging, floating, redocking, tiling/cascading, global defaults, slots, and per-file sessions |
| Theme | Light and dark | Present | Consistent light theme; the incomplete dark-theme command is not exposed |
| Help | Help menu and About dialog | Missing | Added |
| Native JSON | Python Optiland nested JSON | Workbench-specific schema-versioned JSON | Architecture retained; Python JSON adapter is still required |
| Python terminal | Embedded IPython with connector access | Missing by design | Still missing; see Remaining Gaps |

## Architecture Mapping

### Application Layer

The reference GUI uses a main window for application commands, a panel manager for dock widgets, and a backend connector. The refactor keeps those responsibilities but makes the GUI/backend boundary explicit:

```text
MainWindow
  ActionManager
  PanelManager
    WorkspaceDockFactory
      ToolDock
      DocumentDock
        LensEditorPanel
        ViewerPanel
        AnalysisPanel
        OptimizationPanel
        TolerancingPanel
        MultiConfigurationPanel
  IWorkbenchApplication
    document / prescription / analysis / visualization services
    Core Optic context
```

Panels receive UI-free service interfaces and immutable DTOs, plus `AppSettings` only when they own presentation defaults. The App project has no Core reference, and Core/Application have no Avalonia or Dock reference. The application context owns mutation locking, undo/redo, Core snapshots, revision increments, and categorized workspace events.

### Core Object Model

Both systems center their public surface on an `Optic` object. The Workbench implementation owns aperture, fields, wavelengths, a surface group, material registry, ray tracers, analyses, optimization, tolerancing, solves, pickups, and backend selection.

Important differences remain:

| Core area | Python Optiland | Workbench |
| --- | --- | --- |
| Numeric backend | NumPy and optional PyTorch/autograd/GPU | Managed CPU abstraction only |
| Surface model | Geometry, interaction, material, coating, aperture, propagation | Equivalent composition shape plus GUI-compatible legacy columns |
| Sequential trace | Production Optiland trace stack | Deterministic C# implementation with recorded per-surface histories |
| GRIN propagation | Dedicated propagation framework | Material-owned propagation model; current integration is simplified |
| Polarization | Rich polarization/Jones behavior | Source-validated Jones pupil analysis on the sequential sample path; broader polarization state/coating behavior remains limited |
| Non-sequential tracing | Not the Quickstart focus; broader roadmap item | Not implemented |

### Analysis Contract

Before the refactor, `AnalysisData` exposed only a dictionary. The GUI converted every value to text, so plots could not be implemented without parsing display strings.

`AnalysisData` now separates report data from one or more graphical layouts:

```text
Values -> metric table, text export, automation
Series / SeriesList -> ordered typed curves, points, heatmaps, and rasters
PlotPanes -> field/wavelength/focus/component/cross-section layouts
PlotOptions -> limits, aspect, legends, grids, zero lines, and axis visibility
```

Native series are currently produced for:

- Spot Diagram: image-plane scatter points.
- Ray Fan: ordered line samples.
- Best Fit Ray Fan: paired fans referenced to a fitted wavefront sphere.
- Encircled Energy: radius versus energy.
- RMS vs Field: field angle versus RMS spot radius.
- Through Focus: focus shift versus RMS spot radius.
- Y-Ybar: surface number versus mean ray height.
- Zernike: coefficient bars.
- MTF: spatial frequency samples.
- RMS wavefront versus field: one curve per wavelength.
- Through-focus MTF: tangential/sagittal field pairs.
- Incident angle versus image height: pupil and field scan modes with value-colored lines.
- Incoherent irradiance: field-by-wavelength inferno detector heatmaps.
- Radiant intensity: field-by-wavelength jet angle maps paired with central cross-sections.
- Geometric MTF: field-colored tangential/sagittal curves from geometric spot data.
- Sampled MTF: field-colored tangential/sagittal curves from shifted-pupil wavefront overlap.

Analyses without a dedicated series receive a numeric metric bar chart in the connector. This keeps old analyses visible while allowing rigorous series to replace the fallback incrementally.

## Quickstart Workflow Mapping

### Open A Cooke Triplet

Reference: open `Cooke_triplet.json` and update the editor and viewer.

Workbench: use **File > New Cooke Triplet Sample** or **File > New Tessar F/4.5 Sample** for prescriptions and ray models validated against Optiland 0.5.8, or **File > Open** for Workbench JSON, Optiland-compatible sequential ZMX files, and the supported SEQ/LEN subsets. All panels refresh through connector events.

### Inspect 2D And 3D Rays

Reference: use Matplotlib and VTK navigation controls.

Workbench: the renderer remains Avalonia-native. The refactor adds the missing interaction contract while avoiding a VTK dependency:

- 2D: drag to pan the full physical scene, including the optical axis; wheel to zoom around the pointer.
- 3D: choose solid or wireframe rendering, drag to rotate, Shift-drag to pan, and wheel to zoom around the pointer.
- Both: toggle rays and reset the camera.

The two viewer modes are opened from separate commands in the top **View** Ribbon. Each mode gets its own document tab and can be floated into an independently resizable native window, so the central viewer does not repeat a second 2D/3D selector.

### Change Surface Radius

Reference: edit Surface 1 radius and immediately update viewers.

Workbench: `LensEditorPanel` submits a `SurfaceRowDto` command. Application captures undo state, updates Core, applies pickups and solves, increments the revision once, and emits one structured surface event. Lightweight viewers debounce that event; heavy analyses become stale without blocking the editor.

### Run RMS Spot Size Vs Field

Reference: select the analysis, press Run, and inspect a plot.

Workbench: open the top **Analysis** category and choose **RMS-Field**. Choosing the icon runs the analysis and opens its result as a first-class closable document beside **Lens Data**, 2D/3D views, optimization, tolerancing, and multi-configuration pages. Expand the page-level **Settings** panel only when parameters need adjustment, then use the adjacent synchronization icon to rerun with the current values.

Every result page provides bottom-aligned **Plot**, **Data**, and **Text** tabs. Plots are rendered from numerical series rather than static images and support pointer-centered wheel zoom, drag pan, double-click reset, and nearest-sample hover readout. Every document has a compact tab with a small close button. Tabs can be dragged to split, merge, float, and redock; the **Window** Ribbon also supplies bulk docking, floating, tiling, cascading, locking, closing, and default-layout actions.

Ordinary prescription edits only mark a heavy result stale. Synchronization captures the current Core revision, cancels an older run for the same page, and accepts the result only while instance ID, task generation, and source revision still match. Closing the page or switching files cancels its work.

## Persistence And Interoperability

The Workbench native JSON remains the lossless format for Workbench-specific components. Python Optiland 0.5.8 recursive JSON dictionaries are now detected on open and can be exported explicitly through **File > Export Python Optiland JSON**. The validated interoperability subset covers angle/object-height/paraxial-image-height fields, field vignetting and telecentric flags, finite/infinite object conjugates, EPD/image F-number/object NA/float-by-stop-size system apertures, wavelengths, centered Plane, StandardGeometry, PlaneGrating, StandardGratingGeometry, BiconicGeometry, representable ToroidalGeometry, pure PolynomialGeometry/ChebyshevPolynomialGeometry/fringe ZernikePolynomialGeometry, and representable high-order EvenAsphere/OddAsphere surfaces, homogeneous catalog/ideal/Abbe materials, radial/rectangular/elliptical/polygon/file-backed/recursive boolean physical apertures, all seven Optiland 0.5.8 apodization profiles, refractive/reflective, transmissive/reflective thin-lens, plane-surface phase interactions with all four phase profiles, transmissive/reflective diffractive interactions, and simple Python coating dictionaries on the Workbench adapter path. Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, and its grating dictionaries cannot currently reconstruct themselves, so those external Python retention paths are not claimed yet.

ZMX import follows the Python Optiland 0.5.8 supported sequential boundary; SEQ and LEN remain common sequential subsets. These formats can still lose unsupported freeform, coating, solve, polarization, or multi-configuration data, so the UI should not claim lossless commercial compatibility.

## Remaining Gaps

### Priority 0: Compatibility And Numerical Trust

- Extend Python Optiland JSON interoperability beyond the current validated sequential subset to the remaining freeforms, Python-preserved coating models, BSDFs, pickups, solves, and polarization.
- Continue with vectorial diffraction and broader analysis defaults; FFT/MMDFT/Huygens PSF, FFT/Huygens/geometric/sampled MTF, best-fit ray fan, chief-ray and centroid/best-fit wavefronts, sampled through-focus MTF, Zernike, distortion, field curvature, irradiance/radiant intensity, Jones pupil, and image simulation now have validated numerical implementations.
- Integrate GRIN propagation with curved-ray intersection instead of applying a bend after straight-line distance calculation.

### Priority 1: GUI Parity

- Broaden automated UI acceptance for four-direction tab drops and native floating-window redocking. The Dock model and session round-trip are covered by unit tests; pointer-driven desktop automation remains future work.
- Broaden UI automation around generated analysis parameter editors, settings persistence, file dialogs, edits, layout slots, themes, and command-palette keyboard navigation.
- Decide whether advanced scripting should use embedded Python interoperability or a native C# scripting host. A terminal must not be labeled Python-compatible without an actual Optiland Python object.

### Priority 2: Platform And Performance

- Add optional GPU/autograd backends behind `INumericBackend`.
- Add a higher-performance 3D renderer if large systems outgrow the Avalonia solid/wireframe view.
- Add application-level UI automation tests for file dialogs, edits, layout slots, themes, and command-palette keyboard navigation.

## Validation

The refactor is expected to satisfy:

```bash
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

As of 2026-07-19, the solution builds with zero warnings and all `273/273` tests pass. Coverage includes layering constraints, application revisions and cancellation, Dock model/session round-trips, finite structured plots for every catalog entry, generated analysis parameter settings, Python golden comparisons, manufacturer glass data, bundled Zemax AGF conversion and ZMX import including real-image-height fields, tracing, serialization, optimization, tolerancing, plugins, visualization, editor transactions, and file formats.
