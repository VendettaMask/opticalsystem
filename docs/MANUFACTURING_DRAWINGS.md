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

The preview and PDF share one renderer, so the exported document preserves the geometry and annotations shown in the application. The sheet follows a manufacturing-drawing layout with an upper geometry band, a lower three-column specification band, and a bottom title block. It includes:

- sectioned optical-element geometry, optical axis, clear aperture, diameter, center thickness, and local S1/S2 surface radii;
- separate S1, material, and S2 specification columns, including catalog refractive index and Abbe number when the glass is known;
- local S1/S2 coating instructions, edge treatment, drawing number, part name, revision, designer, and reviewer;
- stress birefringence, bubbles and inclusions, homogeneity and striae, surface-form tolerance, centering/tilt, surface imperfections, and surface texture;
- Chinese technical requirements and a compact title block.

The sheet uses the bundled Noto Sans CJK SC engineering font under the SIL Open Font License. Mathematical and optical notation is typeset with real glyphs and positioned subscripts, so `±`, `×`, `≤`, `λ`, `φ`, `n_d`, `V_d`, and similar notation remains consistent in both the preview and exported PDF without depending on fonts installed on the workstation.

Each single optical element uses local manufacturing-surface identifiers `S1` and `S2`, independent of its global surface numbers in the sequential prescription. Additional local surface numbers are reserved for a future cemented-assembly drawing mode.

The drawing pipeline is implemented independently in C# with SkiaSharp. Preview generation, dimension geometry, Chinese and mathematical text, and vector PDF export do not invoke Python or depend on an external CAD application. [OtoCAD Community](https://github.com/otocad/Otocad-Community) is used only as a public behavioral reference for portrait-sheet proportions and the separation of geometry, surface/material specifications, and the title block; no OtoCAD source or runtime component is incorporated.

The implementation follows the general drawing layout and indication categories of the ISO 10110 series. It uses the current surface-form unit preference in nanometers while retaining explicit units for every numeric field. The generated sheet is labelled as an `ISO 10110 系列参考图样`; final release drawings still require engineering review against the licensed standards, contractual requirements, and the selected manufacturer's process capability.

Standards referenced by the UI and renderer:

- [ISO 10110-1:2019: General](https://www.iso.org/standard/57574.html)
- [ISO 10110-5:2026: Surface form tolerances](https://www.iso.org/standard/86355.html)
- [ISO 10110-6:2025: Centring and tilt tolerances](https://www.iso.org/standard/82181.html)
- [ISO 10110-7:2017: Surface imperfections](https://www.iso.org/standard/65444.html)
- [ISO 10110-8:2019: Surface texture](https://www.iso.org/standard/69532.html)
