# Engineering report style verification

This diagnostic captures the actual Double Gauss 28 degree field lens once through the managed workbench and the shared off-screen renderer, then writes HTML, PDF, Word and Markdown from the same frozen results. It does not change the source lens or run a second optical engine.

The selection contains 67 supported analyses plus 2D/3D layouts. Image Simulation, Geometric Image Analysis, Geometric Bitmap Image Analysis, Light Source Analysis, Partially Coherent Image Analysis and Extended Diffraction Image Analysis are excluded for this sample. Product export selection remains user controlled, with the existing retired-analysis and prescription-report filters.

Build `ReportStyle.csproj` in Release with a single build worker, then run the diagnostic assembly with the source lens path and an empty capture directory. The assembly identity is `OpticalSystemDesign.Tests` for the existing friend access. Use an independent output/intermediate directory when other tasks are building the application.

`--package-only` reuses saved DTO summaries and the original PNG files. It refreshes display formulas from the same Core basis definition and writes the four containers without recalculating optics or rearranging drawings. `--word-only` refreshes only Word. Historical captures remain intact.

`check-html.cjs` uses the bundled Playwright runtime and an independent headless Edge instance. `inspect-artifacts.py` checks complete native tables, editable Word equations, byte-identical HTML/Word/Markdown image payloads, and pixel-identical PDF drawings at their actual physical dimensions. `render-word.ps1` guards ownership of an independent, hidden Word instance, never controls a pre-existing instance, and exports a private visual-QA PDF. `inspect-word-render.py` checks that actual Word pagination is 80 A4 pages with 63 complete drawing placements and 111 equations.

The bundled document renderer was attempted first; LibreOffice is absent on this Windows host. The Word-rendered PDF is an internal preview, not the product PDF. Word may resample that preview; the final DOCX retains the original PNG bytes, independently verified. Product PDF images are separately checked for identical pixels.

Results and scope: [verification record](../../../validation/rechecks/2026-11-02/report-style/README.md). Large sample artifacts remain under `output/report-style-gauss28-20261102/` and in the desktop directory recorded by the delivery receipt. No frontend desktop interaction, installation, publishing or remote synchronization is performed.
