# Manufacturability and Optical Drawings

The **Manufacturing & Drawings** Ribbon category contains two document tools:

- **Manufacturability Review** converts consecutive refracting surfaces and their intervening glass into physical optical elements, then reports pass, review, or invalid findings.
- **Optical Drawing** creates an A4 or A3 portrait Chinese drawing preview for one optical element and exports the same vector artwork as PDF.

## Manufacturability Review

The review uses editable process-screening limits for:

- minimum center thickness;
- minimum sampled edge thickness;
- maximum clear-diameter-to-center-thickness ratio;
- minimum absolute-radius-to-diameter ratio;
- maximum surface edge slope.

It also detects a conic surface outside its real sag domain, intersecting front and back surfaces, and non-standard surface types that need a dedicated process and metrology review. These checks are deterministic early-stage warnings. They do not replace a supplier's tooling, material, tolerance, coating, mounting, environmental, or yield assessment.

## Optical Drawing

The preview and PDF share one renderer, so the exported document preserves the geometry and annotations shown in the application. The **Manufacturing & Drawings** Ribbon exposes separate `ISO 10110` and `GB/T 13323` drawing commands; each command opens a stable document with its own persisted standard setting and production layout:

- `ISO 10110` uses an upper geometry band, lower S1/material/S2 specification columns, and the title block.
- `GB/T 13323—2009` uses the same dimensionally correct geometry with separate `对材料的要求` and `对零件的要求` tables. Each S1/S2 row contains a dedicated `D（有效孔径）` field and its surface requirements.

Both layouts include:

- sectioned optical-element geometry at a preferred drawing scale, optical axis, outside diameter, center thickness, and local S1/S2 radial dimensions with arrowheads, radius tolerances, `R∞` plane notation, and center marks when the curvature center lies inside the drawing area;
- separate S1, material, and S2 specification columns, including catalog refractive index and Abbe number when the glass is known;
- local S1/S2 coating instructions, edge treatment, drawing number, part name, revision, designer, and reviewer;
- stress birefringence, bubbles and inclusions, homogeneity and striae, surface-form tolerance, centering/tilt, surface imperfections, and surface texture;
- Chinese technical requirements and a compact title block.

The sheet uses the bundled S.T.A.R.Labs company wordmark in the title block; an imported PNG can override it for the current drawing and **Restore Default** returns to the bundled asset. The source wordmark's baked light checkerboard is removed and its visible content is cropped at render time without redrawing the lettering. The sheet also uses the bundled Noto Sans CJK SC engineering font under the SIL Open Font License. Mathematical and optical notation is typeset with real glyphs and positioned subscripts, so `±`, `×`, `≤`, `λ`, `φ`, `n_d`, `V_d`, and similar notation remains consistent in both the preview and exported PDF without depending on fonts installed on the workstation.

Each single optical element uses local manufacturing-surface identifiers `S1` and `S2`, independent of its global surface numbers in the sequential prescription. Additional local surface numbers are reserved for a future cemented-assembly drawing mode.

The drawing pipeline is implemented independently in C# with SkiaSharp. Preview generation, dimension geometry, Chinese and mathematical text, and vector PDF export do not invoke Python or depend on an external CAD application. [OtoCAD Community](https://github.com/otocad/Otocad-Community) is used only as a public behavioral reference for portrait-sheet proportions and the separation of geometry, surface/material specifications, and the title block; no OtoCAD source or runtime component is incorporated.

The implementation separates the ISO 10110-1 preferred tabular indication area from the dimensioned geometry. The geometry area contains the physical section, mechanical dimensions, surface identifiers, and radii. Optical-glass material marks inside the ISO section use the three-stroke short-long-short pattern. Effective aperture is an optical test region and is therefore written as `⌀e` in each surface specification column; it is not drawn as a second mechanical diameter. Surface form, centring/tilt, surface imperfections, surface texture, and coating instructions remain in their S1/S2 columns instead of being repeated on crossing leaders.

Mechanical dimensions and associated tolerances follow the responsibilities of ISO 129-1. Extension lines start clear of the part outline and extend beyond the dimension line, and upper/lower deviations are stacked beside the nominal size. Drawing scale is selected from the preferred ISO 5455 series and the actual ratio is printed in the title block. The renderer rejects inverted deviation limits, negative or non-finite tolerance values, and missing required material/surface indication payloads.

The surface-form values use the current nanometre preference from ISO 10110-5:2026. Material imperfections are governed by ISO 10110-18:2018, which replaced the withdrawn ISO 10110-2, ISO 10110-3, and ISO 10110-4 documents. Final release drawings still require clause-level engineering review against licensed normative texts, contractual requirements, and the selected manufacturer's process capability.

The Chinese layout targets the current recommended national standard [GB/T 13323—2009《光学制图》](https://openstd.samr.gov.cn/bzgk/gb/newGbInfo?hcno=02E54A04F98AA1CFEE19B70884E73D7E). The national standards service records it as current, confirmed for continued validity in 2025, and as the complete replacement for the withdrawn GB/T 13323—1991. The software therefore does not offer the withdrawn 1991 edition as a production drawing mode.

Standards referenced by the UI and renderer:

- [ISO 10110-1:2019: General](https://www.iso.org/standard/57574.html)
- [ISO 10110-5:2026: Surface form tolerances](https://www.iso.org/standard/86355.html)
- [ISO 10110-6:2025: Centring and tilt tolerances](https://www.iso.org/standard/82181.html)
- [ISO 10110-7:2017: Surface imperfections](https://www.iso.org/standard/65444.html)
- [ISO 10110-8:2019: Surface texture](https://www.iso.org/standard/69532.html)
- [ISO 10110-9:2016: Surface treatment and coating](https://www.iso.org/standard/63596.html)
- [ISO 10110-18:2018: Material imperfections](https://www.iso.org/standard/68155.html)
- [ISO 129-1:2018: General principles for dimensions and tolerances](https://www.iso.org/standard/64007.html)
- [ISO 5455:1979: Recommended drawing scales](https://www.iso.org/standard/11500.html)
