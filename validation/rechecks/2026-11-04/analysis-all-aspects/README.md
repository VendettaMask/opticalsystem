# All analysis drawings: automatic and manual aspects

All **25 graphical `AnalysisPresentationKind` categories** use the shared Auto / 16:9 / 4:3 outer-page policy, retaining Auto as the default. The four coefficient/text presentations continue using their existing readable report layout. The numerical result, plot instances, series/options, axis metadata, curve visibility and zoom/pan/surface-camera state are retained through width changes and manual overrides.

## Outer layout protection

An equal-aspect chart derives its numerical axis extent from its inner rectangle. Simply widening that rectangle changed some unzoomed ranges, even though the input data and options were unchanged. The initial all-family check reproduced this in six cases: field-curvature/distortion panes, an equal-aspect footprint fixture, both configuration-matrix themes, OPD panes and through-focus spots.

`AnalysisPanel` now passes a presentation-only layout flag from the existing typed `EqualAspect` options, including pane options. `UniformDiagramPage` centers the complete original 1200-DIP plot-root layout on either selected sheet, adding 200-DIP side space on the 1600-DIP sheet. The plot height remains 740 DIP, caption height 160 DIP and logical fonts unchanged. The same children stay attached to the same outer adapter. This keeps the inner rectangle, grid/field/legend arrangement and numerical ranges stable without editing axis calculations or capturing/restoring private state. Ordinary curve charts retain the already approved use of horizontal sheet space.

These are the only two production files changed in this step: the outer result-page construction and the outer sheet container. Scientific curve/surface/scene renderers, data mapping, optical calculations, axes and interaction handlers were not edited. Live display, PNG and report construction reuse this same layout path.

## Verification

Default-output Release compilation succeeded without warnings or errors in the final run. There are **139 distinct passing cases**, with no unresolved failures/skips in the selected scope, combining the following batches by full test name:

- **34** presentation cases: all 25 graphical kinds, four additional Isekai families, one explicitly equal-aspect footprint and four additional planar/contour/surface modes. Each checks automatic wide/narrow switching, both manual modes, exact rendered curve-axis ranges before and after zoom, stable data/control identities, surface pan/zoom/rotation and legend hide/restore. These 34 passed in [the expanded run](all-families-expanded.trx).
- **15** existing automatic/aspect settings, camera, persistence and report-builder cases also passed in that expanded run.
- The final single run passed **90/90**: **17** real workbench analyses plus **73** fixed-source/layout/font/caption/export regressions. See [final TRX](all-families-final-regression.trx) and [build/test log](final-run.log). The 73 comprise AnalysisResponsivePlotTests 43, UniformDiagramExportTests 10, AnalysisFrameDpiTests 9, AnalysisResultBookTabsTests 8 and DiagramViewportFitTests 3. Existing fixed-4:3 tests explicitly choose that manual mode; new automatic cases independently exercise resizing.

Real analyses cover Spot Diagram, Ray Fan, Field Curvature and Distortion, Wavefront Map, Through Focus, Incoherent Irradiance, Footprint Diagram and all ten MTF entries (Fourier/Huygens/geometric frequency/focus/field plus sampled MTF). Each calculates once and reuses that result through the layout changes. Cooke is used except the existing Double Gauss sample for through-focus spots and irradiance. Small UI validation settings are selected only from the analysis's declared choices and ranges, and do not claim production defaults or numerical precision acceptance.

The [initial run](all-families-initial.trx) has 28 passes and the six actual range-drift reproductions above. The expanded run has **61 passes / 5 failures**, not 66/66: five newly added real-analysis fixtures initially supplied invalid generic UI values (`FieldNumber=0`, a scalar wavefront Sampling choice and FieldDensity below its declared minimum). The final fixtures obtain valid choices/ranges from the real service descriptors; all 17 then pass. Its 12 earlier real-analysis passes overlap the final 17 and are not double-counted. No production parameter validation or acceptance rule was relaxed.

All validation and captures were headless/off-screen. The running user application was not controlled or restarted. This is presentation/state validation, not a full-repository baseline, new Zemax accuracy certification, native foreground acceptance, installation, commit, push or release. Existing unrelated Core/Application/theme work was preserved.

## Visual evidence

Actual computed results, before synthetic zoom actions:

- [Spot wide](captures/real-Spot-Diagram-wide.png) / [narrow](captures/real-Spot-Diagram-narrow.png)
- [Ray fan wide](captures/real-Ray-Fan-wide.png) / [narrow](captures/real-Ray-Fan-narrow.png)
- [Footprint wide](captures/real-Footprint-Diagram-wide.png) / [narrow](captures/real-Footprint-Diagram-narrow.png)
- [Fourier through-focus MTF wide](captures/real-Fourier-Through-Focus-MTF-wide.png) / [narrow](captures/real-Fourier-Through-Focus-MTF-narrow.png)
- [Wavefront wide](captures/real-Wavefront-Map-wide.png) / [narrow](captures/real-Wavefront-Map-narrow.png)

The presentation-kind fixtures are synthetic layout inputs, never runtime optical data. For example, [Isekai configuration-matrix layout](captures/ConfigurationMatrixSpot-Isekai-default-wide.png) checks retained original grid geometry with added outer side space. Capture names without `real-` are those synthetic cases; `narrow-zoomed` explicitly retains the test's keyboard zoom/pan, not a data alteration. [The manifest](manifest.json) records authoritative final outcomes, the complete kind coverage, scoped source/unchanged-renderer hashes and capture hashes without copying a source tree.
