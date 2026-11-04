# 4:3 / 16:9 drawing layout

This is the historical manual-aspect stage, initially defaulting to 4:3. The subsequent default-Auto behavior and its independent verification are recorded in [automatic drawing aspect](../drawing-aspect-auto/README.md).

The user's request is to support both drawing aspects while changing presentation only. Format and Font → Image export → Drawing aspect now selects 4:3 (1200×900 DIP, default) or 16:9 (1600×900 DIP). Both keep a 740-DIP plot area and 160-DIP caption. Choosing an aspect suggests an export width at the current pixel height; pixel dimensions and print DPI remain independently editable.

The current result/control instances are retained. Changing the sheet width does not assign new series/options, rerun analyses, reset the viewport, alter typed axes or change the source document. Square plots retain their square host. Live fitting, actual-size measurement, Isekai book fitting, ordinary PNG, layout-view compositing and report builders use the selected sheet width. Reports omit the export canvas padding and retain their established physical frame; a wider source at the same physical width has smaller physical lettering. The decorative Isekai perimeter remains outside the source aspect.

## Verification

Default-output Release compilation completed successfully. The final run log contains no compiler warnings or errors. Validation was entirely headless/off-screen; the user's application was not controlled or restarted.

There are **39 distinct passing cases**, with no skipped cases, across two scoped runs:

- Existing regressions: UniformDiagramExportTests 10/10, DiagramViewportFitTests 3/3, AnalysisResultBookTabsTests 8/8 and AnalysisFrameDpiTests 9/9. These 30 passing cases are in [the broader run](drawing-aspect-final.trx).
- New aspect checks: **9/9** in [the final corrected run](drawing-aspect-corrected.trx), with [build/test output](final-run.log). These include real Cooke PSF results in Light/Lumen/Isekai, source-control identity and DTO checks, preservation of zoom/axes and pixel equality after returning to 4:3, narrow/wide viewports, square plots and hidden/restored legends, both layout dimensions and camera identity, analysis/layout report builders, preference persistence/defaults/reset, preview isolation and save-failure restoration.

The broader run has 37 passes and one failed fixture check; do not describe that individual run as 38/38. Its new square-legend fixture initially supplied the series label as an axis label, leaving the actual series Name empty. The corrected fixture explicitly names the series and enables its legend. The nine-case corrected run replaces the eight overlapping new cases from the broader run; it is not added to all 38 cases. The first [focused attempt](drawing-aspect.trx) preserves the same fixture failure. A sandbox testhost startup timeout preceded these runs; local test-process communication was then allowed. An intermediate rebuild also encountered a temporary concurrent Isekai material compilation error in another change; the final default-output build succeeded without modifying that material code in this task.

This validates presentation and state preservation. It does not certify new optical accuracy, a full repository test baseline, native foreground interaction, installation or publication. Existing unrelated Core/Application/material work was not reverted or included as an optical change in this task.

## Visual evidence

The PSF images use the actual Cooke example result at the existing 64×64 pupil setting; they are not reconstructions of the user's attached Double Gauss screenshot. The surface and square-legend fixtures are explicitly synthetic layout inputs, never product optical data.

- [Lumen 16:9](captures/psf-Lumen-16-9-overview.png) / [Lumen 4:3](captures/psf-Lumen-4-3-overview.png)
- [Light 16:9](captures/psf-Light-16-9-overview.png) / [Light 4:3](captures/psf-Light-4-3-overview.png)
- [Isekai 16:9](captures/psf-Isekai-16-9-overview.png) / [Isekai 4:3](captures/psf-Isekai-4-3-overview.png)
- [Square plot with hidden legend, 16:9](captures/square-hidden-16-9.png)
- [Report analysis source, 16:9](captures/builder-wavefront-16-9.png) / [report layout source, 16:9](captures/builder-layout-16-9.png)

Both source heights and caption heights remain equal. Logical fonts and the company mark retain their size; the selected width changes horizontal drawing space and caption partition positions. Captures with no `overview` suffix retain the intentionally zoomed PSF state used for the preservation checks. [The manifest](manifest.json) records source and capture hashes without copying the source tree.
