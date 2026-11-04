# Automatic drawing aspect

The subsequent all-analysis coverage and equal-aspect inner-layout protection are recorded separately in [all analysis aspects](../analysis-all-aspects/README.md). The 45-case results below retain their original stage scope.

The drawing-aspect preference now defaults to **Automatic (Auto)**, with manual **16:9** and **4:3** choices retained. Missing/invalid settings and Reset use Auto; saved explicit ratios remain valid. This follows the user's request to change settings and outer presentation containers, without changing scientific drawing code.

Each `ResponsiveAnalysisPlotHost` supplies its actual finite available area, excluding its external frame inset, to its own `UniformDiagramPage`. The page independently selects the existing 1600×900 or 1200×900 sheet. It retains the same 740-DIP plot height, 160-DIP caption and existing child controls. Two differently sized panes do not share an effective aspect. Around the equal-unused-area boundary (approximately 1.54), a ±0.03 aspect dead band preserves the previous choice to prevent divider/scrollbar jitter. Manual modes ignore viewport changes. The initial unresolved sheet and independent report builders use the configured export canvas aspect; a live PNG captures the currently selected live sheet. Choosing Auto retains the independently configured pixel canvas.

Production edits for this step are limited to `DiagramSheetLayout`, `AppSettings`, `DisplaySettingsWindow`, localization, `UniformDiagramPage` and `ResponsiveAnalysisPlotHost`. No changes were made to scientific curve/surface/scene renderers, series construction, axis calculations, optical calculations or zoom/pan handlers. The original drawing is retained, without assigning new series/options or replacing plot controls. Existing Isekai bindings follow the selected sheet through their already shared sizing path.

## Verification

Default-output Release compilation succeeded without warnings/errors in the final run. **45/45** selected cases passed, with no failures or skips: [TRX](automatic-final.trx), [build/test output](final-run.log).

- Automatic behavior: **6/6**. Actual Double Gauss FFT PSF at the existing 64×64 pupil setting, separately in Lumen and Isekai, is calculated once per case and reused across wide/narrow panes and automatic/manual choices. Checks retain exact DTO data, series/options/control identities, rendered numerical axes, zoom/pan state and source document identity; returning to 4:3 reproduces identical source pixels. Both fit and 100% modes retain their semantics. Additional cases cover independent simultaneous panes, small divider jitter, square equal-aspect plots and hidden/restored legends, and 2D/3D source/camera preservation.
- Aspect/settings/report regression: **9/9**. Tests cover default/reset/normalization to Auto, saved manual preference compatibility, all three UI choices, preview isolation, save-failure restoration and saving Auto without replacing the chosen pixel canvas. Existing report builders exercise manual and Auto modes; manual curves/layouts continue preserving their controls and state.
- Existing fixed-sheet regressions: **30/30** (UniformDiagramExportTests 10, DiagramViewportFitTests 3, AnalysisFrameDpiTests 9, AnalysisResultBookTabsTests 8). Their fixed-4:3 contracts now explicitly select manual 4:3, leaving automatic behavior to the independent cases above.

The [initial run](automatic-initial.trx) passed 14/15: the two-pane fixture attempted to reparent a host without first removing it from its old grid. The fixture was corrected; production behavior did not require a change for that failure. Earlier attempts are not counted as separate passing cases. The initial build also displayed cached NU1900 audit-connectivity warnings from a previous restore; the final build/test log has none.

All work was headless/off-screen. No foreground program was launched, controlled or restarted. No full-repository numerical baseline, new Zemax accuracy certification, native desktop acceptance, installation, commit, push or release is claimed. Other ongoing Core/Application/theme work was preserved.

## Visual evidence

These actual Double Gauss PSF captures retain X ≈ −35.568…35.568 µm and Y = 0…1. Source-border pixels add 2 DIP in Lumen and the existing 28-DIP decorative perimeter in Isekai; these are complete source captures, independent of the separate output pixel canvas.

- [Lumen automatic wide](captures/psf-Lumen-auto-16-9.png) / [automatic narrow](captures/psf-Lumen-auto-4-3.png)
- [Isekai automatic wide](captures/psf-Isekai-auto-16-9.png) / [automatic narrow](captures/psf-Isekai-auto-4-3.png)

The square-legend fixture is synthetic layout data, never runtime optical data. [The manifest](manifest.json) records scoped source hashes, unchanged renderer hashes, test outcomes and capture hashes without duplicating a source tree.
