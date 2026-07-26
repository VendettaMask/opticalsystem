# Large File Split Plan

## Implementation Status

Completed on 2026-07-25.

- `AnalysisFramework.cs` was removed and replaced by analysis models, catalog, shared
  helpers, and family-specific implementation files.
- Python Optiland JSON now has a public store facade plus independent reader, writer,
  conversion, and model types.
- The former connector implementation is split by responsibility under
  `OpticalWorkspaceModel`; `OptilandConnector` is a thin compatibility facade.
- `WorkbenchApplication` is now a composition root exposing independent services
  coordinated by `WorkspaceCoordinator`.
- Optical drawing rendering now uses a public facade and a responsibility-split
  internal renderer core.
- `MainWindow` and `AnalysisPanel` are split into focused lifecycle, shell, command,
  parameter, result, plot, and export files.

The remainder of this document records the plan and the validation criteria used by
the implementation.

## 1. Purpose

This plan describes how to split the largest source files without changing optical
results, public application contracts, supported file formats, or desktop behavior.

The primary targets are:

1. `src/OptilandWorkbench.Core/Analysis/AnalysisFramework.cs`
2. `src/OptilandWorkbench.Application/Legacy/OptilandConnector.cs`
3. `src/OptilandWorkbench.Application/Services/WorkbenchApplication.cs`
4. `src/OptilandWorkbench.Core/Serialization/PythonOptilandJsonStore.cs`
5. `src/OptilandWorkbench.App/Manufacturing/OpticalDrawingRenderer.cs`
6. `src/OptilandWorkbench.App/MainWindow.cs`
7. `src/OptilandWorkbench.App/Panels/AnalysisPanel.cs`

## 2. Constraints

- Preserve namespaces, public type names, public method signatures, and serialization
  contracts during mechanical moves.
- Do not combine file moves with optical algorithm or numerical-default changes.
- Keep every commit buildable and independently reversible.
- Preserve the existing App -> Application -> Core dependency direction.
- Do not expose Core types through Application contracts.
- Treat `partial` classes as a temporary migration mechanism, not the target design.
- Prefer ordinary files between 150 and 500 lines. Cohesive numerical algorithms may
  remain larger when further splitting would obscure the algorithm.

## 3. Preparation

The current worktree contains substantial uncommitted analysis and Zemax-compatibility
work. Before beginning the split:

1. Review and save the current feature work in a dedicated commit or branch.
2. Record the current solution-build result.
3. Record the full-test result and test count.
4. Avoid regenerating Python fixtures during structural-only changes.
5. Perform moves and responsibility changes in separate commits.

The baseline validation set should include:

- solution build;
- `LayeringArchitectureTests`;
- `CoreArchitectureTests`;
- `WorkbenchApplicationTests`;
- `AnalysisGuiContractTests`;
- `ZemaxImportTests`;
- `PythonAnalysisParityTests`;
- `ManufacturingDrawingTests`.

## 4. Milestone 1: Split AnalysisFramework

### 4.1 Goal

Separate analysis contracts, individual analysis implementations, shared trace
utilities, and catalog registration. This is primarily a mechanical move because the
file already contains many independent top-level types.

### 4.2 Target layout

```text
Core/Analysis/
  AnalysisModels.cs
  BaseAnalysis.cs
  AnalysisCatalog.cs

  Rays/
    SpotDiagramAnalysis.cs
    RayFanAnalysis.cs
    SpotAnalysisEngine.cs
    AnalysisTrace.cs

  Fields/
    DistortionAnalysis.cs
    GridDistortionAnalysis.cs
    FieldCurvatureAnalysis.cs
    RmsVsFieldAnalysis.cs
    RmsWavefrontVsFieldAnalysis.cs
    IncidentAngleVsHeightAnalysis.cs

  Focus/
    ThroughFocusAnalysis.cs
    ThroughFocusMtfAnalysis.cs

  Diffraction/
    PsfAnalysis.cs
    MtfAnalysis.cs
    MmdftPsfAnalysis.cs
    HuygensPsfAnalysis.cs
    HuygensMtfAnalysis.cs
    DiffractionAnalysisPresentation.cs

  Wavefront/
    WavefrontAnalysis.cs
    ZernikeAnalysis.cs

  Radiometry/
    EncircledEnergyAnalysis.cs
    IncoherentIrradianceAnalysis.cs

  Reports/
    FirstOrderAnalysis.cs
    PrescriptionReportAnalysis.cs
```

Existing dedicated engine files such as `DiffractionEngine.cs`,
`MtfScanAnalysis.cs`, and `WavefrontEngine.cs` should remain separate.

### 4.3 Steps

1. Move the data records and enums into `AnalysisModels.cs`.
2. Move `BaseAnalysis` into `BaseAnalysis.cs`.
3. Move one analysis family at a time without editing method bodies.
4. Move shared spot and trace helpers after their callers.
5. Move `AnalysisCatalog` last.
6. Remove `AnalysisFramework.cs` after it becomes empty.

### 4.4 Acceptance criteria

- The analysis catalog contains the same keys in the same order.
- Analysis parameter descriptors and defaults are unchanged.
- All source-derived numerical fixtures remain unchanged.
- No consumer outside Core requires a namespace change.

## 5. Milestone 2: Split Python Optiland JSON

### 5.1 Goal

Separate parsing, writing, validation, and component conversion while preserving
`PythonOptilandJsonStore` as the public compatibility facade.

### 5.2 Target layout

```text
Core/Serialization/PythonOptiland/
  PythonOptilandJsonStore.cs
  PythonOptilandJsonReader.cs
  PythonOptilandJsonWriter.cs
  PythonGeometryConverter.cs
  PythonMaterialConverter.cs
  PythonInteractionConverter.cs
  PythonApertureConverter.cs
  PythonJsonValueReader.cs
  PythonOptilandModels.cs
```

### 5.3 Steps

1. Keep `LooksLike`, `Deserialize`, `Serialize`, and `SaveAsync` on the facade.
2. Move read-only methods into `PythonOptilandJsonReader`.
3. Move write-only methods into `PythonOptilandJsonWriter`.
4. Extract geometry, material, interaction, and aperture conversion by component.
5. Move generic JSON-number and matrix helpers into `PythonJsonValueReader`.
6. Keep unsupported-component failures explicit.

### 5.4 Acceptance criteria

- Existing Python JSON fixtures still load.
- Serialized schemas do not change unexpectedly.
- Cooke, Tessar, aperture, field-definition, phase, and diffractive parity tests pass.
- Unsupported data continues to fail instead of silently degrading.

## 6. Milestone 3: Mechanically Split OptilandConnector

### 6.1 Goal

Reduce navigation and merge-conflict cost before moving behavior into real
Application services.

### 6.2 Temporary layout

```text
Application/Legacy/
  OptilandConnector.cs
  OptilandConnector.Analysis.cs
  OptilandConnector.Documents.cs
  OptilandConnector.Prescription.cs
  OptilandConnector.Components.cs
  OptilandConnector.Optimization.cs
  OptilandConnector.Tolerancing.cs
  OptilandConnector.Configuration.cs
  OptilandConnector.Localization.cs
  LegacyModels.cs
```

### 6.3 Steps

1. Change `OptilandConnector` to `partial`.
2. Leave shared state, constructor, events, and status handling in the root file.
3. Move methods by responsibility without changing their bodies.
4. Move the records and enums declared after the class into `LegacyModels.cs`.
5. Keep all public APIs until the Application-service migration is complete.

### 6.4 Acceptance criteria

- Existing tests that instantiate `OptilandConnector` compile unchanged.
- Undo/redo, document loading, analysis settings, optimization, tolerancing, and
  multi-configuration behavior remain unchanged.
- This milestone contains no new abstractions or behavior changes.

## 7. Milestone 4: Decompose WorkbenchApplication

### 7.1 Goal

Turn `WorkbenchApplication` from a class implementing every Application service into
a composition root that exposes independently testable services.

### 7.2 Target layout

```text
Application/Services/
  WorkbenchApplication.cs
  WorkspaceCoordinator.cs
  OpticalDocumentService.cs
  PrescriptionService.cs
  AnalysisService.cs
  VisualizationService.cs
  OptimizationService.cs
  TolerancingService.cs
  MultiConfigurationService.cs
  MaterialCatalogService.cs

Application/Mapping/
  PrescriptionDtoMapper.cs
  AnalysisDtoMapper.cs
  VisualizationDtoMapper.cs
  MaterialDtoMapper.cs
```

### 7.3 Target composition root

```csharp
public sealed class WorkbenchApplication : IWorkbenchApplication
{
    public IOpticalDocumentService Documents { get; }
    public IPrescriptionService Prescription { get; }
    public IAnalysisService Analyses { get; }
    public IVisualizationService Visualization { get; }
    public IOptimizationService Optimization { get; }
    public ITolerancingService Tolerancing { get; }
    public IMultiConfigurationService MultiConfiguration { get; }
    public IMaterialCatalogService Materials { get; }
    public ILensLibraryService Lenses { get; }
    public IWorkspaceEventStream Events { get; }
}
```

### 7.4 Shared coordination

`WorkspaceCoordinator` should own cross-service state and policies:

- mutation serialization;
- monotonic model revision;
- document generation;
- workspace change events;
- document-switch notification;
- document-level cancellation;
- automatic semi-diameter refresh;
- deferred publication during nested mutations.

Services should depend on `IOpticContext` and `WorkspaceCoordinator`, not directly on
one another.

### 7.5 Migration order

Migrate lower-risk and read-heavy services first:

1. `MaterialCatalogService`
2. `VisualizationService`
3. `AnalysisService`
4. `OptimizationService`
5. `TolerancingService`
6. `MultiConfigurationService`
7. `PrescriptionService`
8. `OpticalDocumentService`

For each service:

1. Add the service class.
2. Move DTO mapping into a mapper where appropriate.
3. Add focused service tests.
4. Change `WorkbenchApplication` to expose the new instance.
5. Remove the corresponding implementation from `WorkbenchApplication`.

### 7.6 Acceptance criteria

- `WorkbenchApplication` is a composition root and lifecycle owner only.
- App still depends exclusively on Application contracts.
- Event and revision semantics remain unchanged.
- Snapshot, cancellation, file-switch, and undo/redo behavior remain unchanged.
- Layering architecture tests pass.

## 8. Milestone 5: Retire OptilandConnector

After the independent services are stable:

1. Move analysis creation and parameter metadata into `AnalysisService` and an
   analysis-definition registry.
2. Move document routing and file-format selection into `OpticalDocumentService`.
3. Move surface and component editing into `PrescriptionService`.
4. Move component-name normalization into dedicated mappers or registries.
5. Migrate production callers away from `OptilandConnector`.
6. Update tests to prefer public Application services.
7. Retain only a thin compatibility adapter if necessary.
8. Delete the connector when no production consumer remains.

The connector should not gain new functionality during this migration.

## 9. Milestone 6: Split OpticalDrawingRenderer

### 9.1 Target layout

```text
App/Manufacturing/Rendering/
  OpticalDrawingRenderer.cs
  ElementDrawingRenderer.cs
  SystemDrawingRenderer.cs
  SpecificationTableRenderer.cs
  TitleBlockRenderer.cs
  DimensionRenderer.cs
  OpticalGlassHatchRenderer.cs
  SkiaTextRenderer.cs
  DrawingPdfExporter.cs
```

### 9.2 Steps

1. Preserve `OpticalDrawingRenderer` as the public facade.
2. Extract system-layout rendering from element rendering.
3. Extract tables and title blocks.
4. Extract dimension and annotation rendering.
5. Centralize typeface selection, text measurement, and fitted-text drawing.
6. Keep preview and PDF output on the same rendering path.

### 9.3 Acceptance criteria

- Page dimensions, scale designations, and standard designations are unchanged.
- Existing manufacturing drawing tests pass.
- Preview and PDF geometry remain visually equivalent.
- Font, line width, title-block, and dimension placement do not change.

## 10. Milestone 7: Split MainWindow and AnalysisPanel

### 10.1 MainWindow target

Keep window lifetime and top-level orchestration in `MainWindow`. Extract:

```text
App/Shell/
  MainWindow.cs
  MainMenuBuilder.cs
  RibbonBuilder.cs
  AnalysisRibbonCatalog.cs
  DocumentCommandHandler.cs
  ThemeController.cs
  StatusBarController.cs
```

Suggested ownership:

- `MainWindow`: open, close, startup completion, service disposal.
- `RibbonBuilder`: Ribbon controls and analysis menus.
- `AnalysisRibbonCatalog`: command grouping and display metadata.
- `DocumentCommandHandler`: open, save, export, and import workflows.
- `ThemeController`: theme and display settings.
- `StatusBarController`: workspace metrics and status refresh.

### 10.2 AnalysisPanel target

```text
App/Panels/Analysis/
  AnalysisPanel.cs
  AnalysisParameterEditor.cs
  AnalysisResultPresenter.cs
  AnalysisSummaryView.cs
  AnalysisTableView.cs
  AnalysisPaneGrid.cs
  AnalysisReportExporter.cs
```

`AnalysisPanel` should retain:

- run cancellation and generation checks;
- stale/locked/running state;
- workspace-event handling;
- composition of parameter and result controls.

### 10.3 Acceptance criteria

- Workspace session restoration still recreates analysis panels.
- Analysis locking, staleness, synchronization, and cancellation remain unchanged.
- Plot, data, and text views preserve their current layout and content.
- Menu, Ribbon, command-palette, and keyboard actions remain available.

## 11. Commit Plan

Use small, reviewable commits:

1. Save the current feature baseline.
2. Move analysis contracts and base class.
3. Move each analysis family.
4. Move the analysis catalog and remove `AnalysisFramework.cs`.
5. Split Python JSON reader and writer.
6. Extract Python component converters.
7. Mechanically split `OptilandConnector`.
8. Extract `WorkspaceCoordinator`.
9. Migrate one Application service per commit.
10. Retire or remove `OptilandConnector`.
11. Split drawing rendering.
12. Split `MainWindow`.
13. Split `AnalysisPanel`.
14. Update README and architecture documentation.

Pure-move commits should not contain formatting or functional changes. Run rename
detection when reviewing diffs so moved code is distinguishable from edited code.

## 12. Validation Gates

### Per commit

```powershell
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests\OptilandWorkbench.Tests\OptilandWorkbench.Tests.csproj `
  --no-build `
  --filter "FullyQualifiedName~LayeringArchitectureTests|FullyQualifiedName~CoreArchitectureTests"
```

Run the focused test class for the component moved in that commit.

### Per milestone

Run:

- `WorkbenchApplicationTests`;
- `AnalysisGuiContractTests`;
- `ZemaxImportTests`;
- `ManufacturingDrawingTests`;
- applicable numerical parity tests.

### Final validation

Run the complete test suite and compare its count and results with the recorded
baseline. No fixture should be regenerated merely to make a structural refactor pass.

## 13. Completion Criteria

The split is complete when:

- the solution builds with zero warnings and zero errors;
- the full test suite passes;
- App does not reference Core;
- Application contracts expose no Core types;
- public analysis keys and file-format behavior are unchanged;
- numerical fixtures are unchanged;
- `WorkbenchApplication` is only a composition and lifecycle root;
- `OptilandConnector` is removed or reduced to a thin compatibility adapter;
- most ordinary files are below 500 lines;
- README and architecture documentation match the implemented analysis catalog and
  service structure.

## 14. Explicit Non-Goals

This refactor should not include:

- new optical analyses;
- changes to Zemax numerical parity;
- new file formats;
- fixture regeneration;
- UI redesign;
- optimization algorithm changes;
- GPU, non-sequential tracing, or thin-film feature work.

Those changes should be developed after the structural refactor or on independent
branches.
