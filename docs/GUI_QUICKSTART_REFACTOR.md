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
| Main shell | Main window, action manager, panel manager | Main window and action manager; panel creation hard-coded in `MainWindow` | `MainWindow`, `ActionManager`, and `PanelManager` have separate ownership |
| New system | Create from scratch | Always opened the Cooke-style demo | Starts blank; blank and Cooke demo are separate commands |
| Lens editor | Editable sequential surface table | Present, including component editors | Retained |
| System properties | Aperture, fields, wavelengths | Present, plus backend selection | Retained |
| 2D viewer | Lens and rays with pan/zoom | Static rendering | Wheel zoom, drag pan, ray visibility, reset |
| 3D viewer | Interactive VTK rotation/pan/zoom | Static orthographic wireframe | Drag rotation, Shift-drag pan, wheel zoom, reset; still not VTK |
| Analysis | Configurable analyses with graphical plots | One analysis page with metric table and text report | Structured plots, table/report views, generated parameter editors, persisted settings, numbered multi-analysis pages, clone/close |
| Analysis refresh | Connector signals update consumers | Connector events already used | Retained for every open analysis page |
| Command palette | `Ctrl+K`, searchable commands | `Cmd+P` only | `Ctrl+K` and `Cmd+K`; actions include panels and layouts |
| Layout | Dockable panels and saved layout slots | Fixed split tabs; one persisted layout | Stable panel IDs, persisted split/tabs, save/load slots 1 and 2 |
| Theme | Light and dark | Present | Retained |
| Help | Help menu and About dialog | Missing | Added |
| Native JSON | Python Optiland nested JSON | Workbench-specific schema-versioned JSON | Architecture retained; Python JSON adapter is still required |
| Python terminal | Embedded IPython with connector access | Missing by design | Still missing; see Remaining Gaps |

## Architecture Mapping

### Application Layer

The reference GUI uses a main window for application commands, a panel manager for dock widgets, and an `OptilandConnector` for all model access and change signals.

The refactor introduces the same ownership boundaries:

```text
MainWindow
  ActionManager
  PanelManager
    LensEditorPanel
    SystemPropertiesPanel
    ViewerPanel
    AnalysisPanel
    OptimizationPanel
    TolerancingPanel
    MultiConfigurationPanel
  OptilandConnector
    Optic
```

Panels receive `OptilandConnector`, plus `AppSettings` only when they own persisted UI state. They do not replace the active `Optic` and refresh through `OpticLoaded`, `OpticChanged`, and `SurfaceDataChanged`.

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

Workbench: use **File > New Cooke Triplet Sample** or **File > New Tessar F/4.5 Sample** for prescriptions and ray models validated against Optiland 0.5.8, or **File > Open** for Workbench JSON and the supported ZMX/SEQ/LEN subset. All panels refresh through connector events.

### Inspect 2D And 3D Rays

Reference: use Matplotlib and VTK navigation controls.

Workbench: the renderer remains Avalonia-native. The refactor adds the missing interaction contract while avoiding a VTK dependency:

- 2D: drag to pan, wheel to zoom.
- 3D: drag to rotate, Shift-drag to pan, wheel to zoom.
- Both: toggle rays and reset the camera.

### Change Surface Radius

Reference: edit Surface 1 radius and immediately update viewers.

Workbench: `LensEditorPanel` captures undo state at edit start, commits through the connector, applies pickups and solves, then emits surface and optic change events. Geometry and viewers refresh from the same event path.

### Run RMS Spot Size Vs Field

Reference: select the analysis, press Run, and inspect a plot.

Workbench: select **RMS-Field**, press **Run**, and inspect the Graph tab. Additional analyses can be opened as numbered pages and cloned or closed independently.

## Persistence And Interoperability

The Workbench native JSON remains the lossless format for Workbench-specific components. Python Optiland 0.5.8 recursive JSON dictionaries are now detected on open and can be exported explicitly through **File > Export Python Optiland JSON**. The validated interoperability subset covers angle fields, EPD/image F-number/object NA, wavelengths, centered Plane, StandardGeometry, BiconicGeometry, representable ToroidalGeometry, pure PolynomialGeometry/ChebyshevPolynomialGeometry/fringe ZernikePolynomialGeometry, and representable high-order EvenAsphere/OddAsphere surfaces, catalog/ideal/Abbe materials, centered radial/rectangular apertures, refractive/reflective and non-reflective thin-lens interactions, and simple Python coating dictionaries on the Workbench adapter path. Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, so external Python retention of `SimpleCoating` is not claimed yet.

Commercial format support is intentionally a common sequential subset. ZMX, SEQ, and LEN files can lose unsupported freeform, coating, solve, polarization, or multi-configuration data. The UI should not claim lossless commercial compatibility.

## Remaining Gaps

### Priority 0: Compatibility And Numerical Trust

- Extend Python Optiland JSON interoperability beyond the current validated sequential subset to the remaining freeforms, Python-preserved coating models, BSDFs, phase models, pickups, solves, telecentric systems, polarization, and apodization.
- Continue with vectorial diffraction and broader analysis defaults; FFT/MMDFT/Huygens PSF, FFT/Huygens/geometric/sampled MTF, best-fit ray fan, chief-ray and centroid/best-fit wavefronts, sampled through-focus MTF, Zernike, distortion, field curvature, irradiance/radiant intensity, Jones pupil, and image simulation now have validated numerical implementations.
- Integrate GRIN propagation with curved-ray intersection instead of applying a bend after straight-line distance calculation.

### Priority 1: GUI Parity

- Add true detachable/dockable panels. The current `PanelManager` provides stable ownership and layout IDs, but the visual layout is still a two-pane tab workspace.
- Broaden UI automation around generated analysis parameter editors, settings persistence, file dialogs, edits, layout slots, themes, and command-palette keyboard navigation.
- Decide whether advanced scripting should use embedded Python interoperability or a native C# scripting host. A terminal must not be labeled Python-compatible without an actual Optiland Python object.

### Priority 2: Platform And Performance

- Add optional GPU/autograd backends behind `INumericBackend`.
- Add a higher-performance 3D renderer if large systems outgrow the Avalonia wireframe view.
- Add application-level UI automation tests for file dialogs, edits, layout slots, themes, and command-palette keyboard navigation.

## Validation

The refactor is expected to satisfy:

```bash
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

As of 2026-07-13, the solution builds with zero warnings and all `170/170` tests pass. Coverage includes finite structured plots for every catalog entry, generated analysis parameter settings, Python golden comparisons, tracing, serialization, optimization, tolerancing, plugins, visualization, and file formats.
