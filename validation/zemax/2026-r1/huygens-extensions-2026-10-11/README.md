# MS-L7 Huygens propagation and spectral controls — 2026-10-11

Local OpticStudio 26.1.0, build 260127, EnterpriseEdition, valid API license. The source SHA-256 remains `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`. The host uses an isolated API application and never saves the prescription. Independent Cooke and Tessar follow-up remains excluded by user scope.

| Case | Captured settings and purpose |
| --- | --- |
| `spherical-field{1,3}-{linear,real,imaginary}` | Wave 1, pupil/image 32, 4 µm, chief center. Auto selects Spherical. The linear grids are finite and independently comparable; some wide complex cells are nonfinite and are excluded from complex certification. |
| `spherical8-field{1,3}-linear` | Wave 1, pupil/image 32, 8 µm. Independent larger-window Spherical controls. |
| `spherical-gaussian32-field{1,3}-linear` | Wave 1, pupil/image 32, 4 µm, explicit Gaussian factor 32. Spherical illumination controls. |
| `spherical64-field{1,3}-linear` | Wave 1, pupil 64/image 32, 2 µm. Despite the directory prefix, Auto selects Planar: the image curvature remains below a quarter wavelength. Explicit Spherical is a diagnostic override and must not replace Auto acceptance. |
| `polychromatic-field{1,3}-linear` | All three captured wavelengths with their original weights, pupil/image 32, 0.25 µm, chief center. Auto selects Planar and preserves one physical image grid. |
| `polarized-field{1,3}-linear` | Wave 1, pupil/image 32, 0.25 µm; analysis Use Polarization on; system Unpolarized true. Current experimental scalar weighting has a measured residual and is not counted as a passing certificate. |

Fields 1/3 are the original object heights 0/-15 mm. Every case retains its request, actual settings, system settings, environment, Auto/Planar/Spherical readback, CFG, native text and numeric grids. The manifest identifies each saved file except itself.

Six scalar Auto-Spherical intensity grids, two 64-pupil Auto-Planar grids and two polychromatic grids form the strict ten-case native comparison. Each compares all 1024 original pixels through WorkbenchRuntime and JSON, with maximum absolute difference < 1e-5 and relative L2 < 5e-5. Unavailable complex cells and the two experimental polarization cases are preserved separately as nonpassing evidence.

Spherical phase is evaluated after projecting the formal traced reference-sphere rays back to the axial paraxial exit-pupil plane, with inverse-distance amplitude and the existing finite cell footprint. These sampling and amplitude conventions are inferred from the saved controls; they are not claims about universal defaults. No captured value or fitted coefficient enters product computation.

This scope does not certify strongly polarized vector fields, absorbing-interface complex transport, arbitrary tilted/folded/complex pupils, multiple configurations or other lenses. See [the repair report](../../../../docs/HUYGENS_SPHERICAL_REPAIR_2026-10-11.md).
