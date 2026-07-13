# Python Optiland JSON Interoperability

## Formats

Workbench JSON and Python Optiland JSON are different schemas:

- Workbench native JSON uses `SchemaVersion`, rich component snapshots, and is the preferred lossless project format.
- Python Optiland 0.5.8 JSON is the recursive dictionary returned by `Optic.to_dict()` and uses `version`, `aperture`, `fields`, `wavelengths`, and `surface_group`.

`OpticJsonStore` detects the schema from document content. Python's standard JSON encoder emits bare `Infinity` for infinite object and plane coordinates; the importer normalizes those tokens without changing string content.

## GUI Workflow

- Open an official Python dictionary JSON through **File > Open**.
- Export the active supported system through **File > Export Python Optiland JSON**.
- Continue using ordinary **Save As** with `.optiland.json` when Workbench-specific components must round-trip losslessly.

The explicit Python export suffix is `.optiland-python.json`.

## Validated Subset

| Area | Supported |
| --- | --- |
| System aperture | EPD, image F-number, object NA |
| Fields | AngleField |
| Wavelengths | Micrometer and nanometer values, weights, primary wavelength |
| Geometry | Plane, StandardGeometry, BiconicGeometry, representable ToroidalGeometry, and high-order EvenAsphere/OddAsphere coefficients that do not use Python's first r/r² departure term |
| Materials | Python catalog material, IdealMaterial, AbbeMaterial |
| Physical aperture | RadialAperture, RectangularAperture |
| Interaction | RefractiveReflectiveModel, including mirrors |
| Coating | SimpleCoating dictionaries in Workbench import/export |
| Sequential data | Thickness, stop, coordinate position and rotation |

Unsupported geometry, material, aperture, coating, or interaction types fail with `NotSupportedException`. Export never silently replaces an unsupported optical component.

Python Optiland 0.5.8 can emit `SimpleCoating.to_dict()` with the same fields. Its `Optic.from_dict()` surface-linking path can relink arbitrary surface coatings to Fresnel coatings, so external Python retention of `SimpleCoating` is not part of the validated bidirectional contract yet.

## Numerical Validation

The official Optiland 0.5.8 Cooke Triplet and Tessar Lens dictionaries are imported directly and checked against committed per-surface golden data.

The reverse direction is checked outside the .NET process:

```python
import json
from optiland.optic import Optic

with open("optic.optiland-python.json") as stream:
    optic = Optic.from_dict(json.load(stream))
```

For both validated samples, Python-loaded C# exports reproduce EFL, F-number, entrance pupil diameter, entrance pupil location, and representative real-ray results exactly.

## Not Yet Supported

- Polynomial, Chebyshev, Zernike, Forbes, NURBS, grid-sag, and grating geometries.
- Python EvenAsphere/OddAsphere files with a nonzero first departure coefficient that cannot be represented by the current Workbench high-order asphere model.
- Python ToroidalGeometry files with nonzero `conic_yz` or `coeffs_poly_y` terms that cannot be represented by the current Workbench toroidal model.
- Python-preserved coating round-trips beyond the raw SimpleCoating dictionary, Fresnel/polarized coatings, thin-film/TMM coating stacks, BSDFs, phase and diffractive interactions.
- Pickups, solves, apodization, polarization, and telecentric field modes.
- Lossless conversion of Workbench plugins or custom propagation models.
