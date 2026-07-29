# File Formats And Plugins

## Native STAROPT Projects

The only native desktop project extension is:

- `.staropt`

STAROPT is a versioned binary container, not JSON with a renamed suffix. Its fixed
header contains the `STAROPT` magic bytes, an independent container version,
Brotli-compression flags, compressed and uncompressed lengths, and a SHA-256 digest
of the payload. The versioned payload stores all optical configurations and the
active-configuration index. Saves use a temporary file and atomic replacement.

Each configuration uses schema-4 `OpticSnapshot`. The current snapshot schema stores:

- optic name
- aperture
- backend name
- fields
- wavelengths
- surfaces
- rich surface component snapshots
- radius pickups, solves, merit operands, and environment settings

Container integrity is only the first validation layer. Before construction, the
loader also requires non-empty field/wavelength/surface tables, exactly one finite
positive primary wavelength, finite physical and environmental state, contiguous
surface numbering, known component layouts, bounded encoded collections, and
valid pickup/solve references and type-appropriate merit-operand parameters. Zemax
compatibility rows whose generic integer slots do not represent Workbench optical
references are validated as opaque source parameters rather than mislabeled
surface/field/wavelength references. Construction happens in a temporary `Optic`;
the active state is replaced only after every component succeeds. Schemas 1 through
3 are migrated into safe schema-4 state before validation.

The loader recognizes STAROPT by both extension and content. See
[STAROPT Project Format](STAROPT_FILE_FORMAT.md) for the binary layout and
validation rules.

Legacy `.optiland.json`, `.optic.json`, `.json`, and `.optiland` snapshots remain
readable for migration and regression fixtures, but the desktop Save command no
longer creates them.

## Python Optiland JSON Subset

`OpticJsonStore` also detects the recursive dictionary schema emitted by Python Optiland 0.5.8 `Optic.to_dict()`. The validated bidirectional subset covers:

- EPD, image-F-number, object-NA, and float-by-stop-size system apertures
- angle fields and weighted primary/non-primary wavelengths
- plane, standard, planar/standard grating, biconic, representable toroidal, pure polynomial/Chebyshev/fringe Zernike, and representable high-order even/odd asphere surfaces with coordinate transforms
- homogeneous catalog, ideal, and Abbe materials
- centered/annular/offset radial, centered/asymmetric rectangular, offset elliptical, polygon/file-backed, and recursive union/intersection/difference physical apertures
- uniform, Gaussian, cosine-squared, Hann, polynomial, super-Gaussian, and Tukey apodization
- refractive/reflective, transmissive/reflective thin-lens, plane-surface phase interactions with constant, linear-grating, radial, or grid profiles, and transmissive/reflective diffractive interactions on grating geometry
- simple Python coating dictionaries on the Workbench adapter path

Use **File > Export Python Optiland JSON** or the `.optiland-python.json` suffix for an explicit Python export. Unsupported Python components fail explicitly; they are not silently replaced. STAROPT is the lossless project format for the optical model, rich surface components, radius pickups, solve settings, merit operands, environment, and multi-configuration systems. GUI preferences are stored separately in `AppSettings`; optimization runs, tolerance definitions/results, plugins, and cached analysis results are not embedded in the project.

## Native Tolerance Files

The tolerancing panel saves editable tolerance definitions as:

- `*.startol.json`

The schema contains a version, ordered enabled/disabled tolerance operands, operand type, target surface, minimum and maximum deviations, normal/uniform distribution, comments, evaluation criterion, Monte Carlo count/seed, compensation iterations, and yield limit. The loader validates surface ranges, finite ordered limits, duplicate operands, and the presence of at least one active non-compensator before accepting a file.

This is a Workbench-owned, human-readable interchange format. It is not presented as binary- or text-compatible with Zemax proprietary `.TOL` files. STAROPT project saves and Python/commercial exchange files do not silently embed these tolerance definitions. See [Tolerancing](TOLERANCING.md).

Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, so external Python retention of `SimpleCoating` is tracked separately from the Workbench adapter's dictionary support.

## CAD Exchange

The desktop **File > Export CAD** command currently writes:

- STEP (`.step` / `.stp`)
- AP203 `CONFIG_CONTROL_DESIGN`
- millimetre geometry
- one closed faceted B-rep per grouped lens element

`CadExportService` takes an immutable snapshot of the active optic, builds the
same sampled 3D lens scene used by the viewer, validates that each triangle mesh
is closed and consistently oriented, and then writes the STEP exchange file
without requiring an external CAD application.

This is an experimental mesh exchange path. It does not preserve analytic
spheres/aspheres, NURBS definitions, optical materials, coatings, tolerances, or
assembly constraints as native CAD features. STEP syntax and mesh closure are
covered by automated tests, but production interoperability with every receiving
CAD kernel is not claimed. Open and inspect the result in the target CAD system
before using it for mechanical design or manufacturing. STAROPT remains the
lossless optical project format.

Python Optiland 0.5.8 also emits incomplete planar-grating dictionaries and has broken grating `from_dict()` constructors. Workbench preserves order, period, and groove angle in its adapter and native snapshots, but external Python reload of grating exports is not part of the validated contract.

See [Python Optiland JSON interoperability](PYTHON_JSON_INTEROP.md) for schema and external Python round-trip validation.

## Commercial Sequential Formats

Supported extensions:

- Zemax `.zmx`
- Zemax material catalog `.agf` (build-time conversion into Workbench `.ogdb` storage)
- CODE V `.seq`
- OSLO `.len`
- plain sequential `.lens`, `.dat`, `.txt`

Zemax `.zmx` import follows the Python Optiland 0.5.8 `zemax_handler.py` and `ZemaxToOpticConverter` boundary. It includes:

- UTF-16 LE/BE, UTF-8, and Latin-1 text decoding
- sequential-mode validation
- entrance-pupil diameter, image F-number, object numerical aperture, and floating-stop aperture definitions
- angle, object-height, paraxial-image-height, and Zemax real-image-height fields
- field weights and X/Y vignette compression
- wavelengths, weights, and primary-wavelength selection
- glass catalog declarations and `GLAS` index/Abbe fallback data
- standard, even-asphere, odd-asphere, and basic toroidal surfaces
- coordinate-break decenter, tilt, and thickness transforms
- mirror material continuity, comments, stop flags, and semi-diameters

The ZMX importer rejects non-sequential mode, unsupported Zemax surface types, negative thickness, coordinate-break order flags, theodolite field definitions, and toroidal conic/polynomial terms. Real-image-height chief rays are solved at the primary wavelength in local image-surface coordinates. Vignette decenter and tangent-angle operands are read but not represented by the current field model. Coatings, solves, pickups, polarization, multi-configuration operands, and unsupported freeform data are not imported.

The required operand boundary is defined by
[Zemax sequential operand support specification](ZEMAX_OPERAND_SUPPORT.md): 333
unique sequential codes must ultimately be imported, edited, evaluated, validated,
and round-tripped; only explicitly obsolete and non-sequential-only operands are
excluded. The current importer implements only a subset, and compatibility-only

`GCAT` and `GLAS` resolve against the bundled Zemax database first during ZMX import and then against the embedded 1,740-entry Optiland 0.5.8/refractiveindex.info compatibility database. SCHOTT, OHARA, HOYA, HIKARI, CDGM, SUMITA, LZOS, and the other bundled glass categories use their actual dispersion formulas and catalog metadata during tracing and analysis. Same-named glasses are selected by `GCAT`; an unknown glass falls back to `AbbeMaterial` only when its `GLAS` record supplies valid nd/Vd values. Otherwise import fails explicitly.

### Zemax AGF material catalogs

The build converter reads the official human-readable AGF master format. The parser supports `CC`, `NM`, `GC`, `ED`, `CD`, `TD`, `MD`, `OD`, `LD`, repeated `IT`, and repeated `BD` records, including dispersion formula numbers 1 through 13. It also accepts actual Glasscat compatibility variants such as UTF-16 files, `_` missing values, old two-field `BD` rows, incomplete `IT` rows, and duplicate names. Catalog names remain available to ZMX `GCAT` resolution, so glasses such as `H-ZLAF96` are resolved from `CDGM-ZEMAX202309` without a constant-index fallback.

The 63 source catalogs are stored as one schema-versioned, compressed `zemax-glass-catalogs.ogdb` resource containing 5,502 glass records. The desktop application loads it automatically; users do not select or repeatedly import Glasscat files. `tools/OptilandWorkbench.GlassCatalogConverter` regenerates the resource when the source catalog set changes.

Lens-library conversion uses the same material path. Bundled Workbench glass data is
always resolved first. When a downloaded lens package includes AGF data, only catalogs
containing still-missing glass names are registered. Those supplemental catalogs are
immediately converted to Workbench `.ogcat` files under the user's
`OptilandWorkbench/glass-catalogs` directory, so they become normal reusable material
catalogs rather than per-lens temporary data.

ZMX export writes a complete sequential header with aperture, field, wavelength, primary-wave, and `GCAT` data. Catalog glasses retain their manufacturer identity, while custom Abbe-compatible materials include nd/Vd fallback operands. CODE V, OSLO, and plain sequential text continue to use the common subset:

- surface number
- label/comment
- radius or curvature
- thickness
- material
- semi-diameter
- conic
- stop flag
- reflective flag

Use STAROPT when full state preservation matters. Python-derived Zemax fixtures are generated by `tools/python-reference/generate_zemax_reference.py` and validated by `ZemaxImportTests`. The embedded glass resource and n/k golden values are generated by `generate_glass_catalog.py` and `generate_glass_reference.py`, then validated by `GlassCatalogTests`.

Five manually openable ZMX systems under `samples/lenses` cover a cemented achromat, double Gauss, telephoto, finite-conjugate macro, and real-image-height workflow. Automated tests import each file, verify bundled catalog-glass resolution, trace every defined chief ray to the image, and build its viewer scene.

Downloaded lens-library sources are intentionally separate from repository samples. See
[Packaged lens library](LENS_LIBRARY.md) for the offline build, source, license,
glass-resolution, packaging, and storage rules.

The desktop application routes by extension through `IOpticalDocumentService`:

```csharp
await application.Documents.OpenAsync(path);
await application.Documents.SaveAsync(path);
```

`OpticalDocumentService` delegates format detection to the split
`OpticalWorkspaceModel`, which recognizes STAROPT content, legacy Workbench JSON,
Python Optiland JSON, or an `OpticalFormatCatalog` adapter automatically.
`OptilandConnector` remains only as a thin source-compatibility facade.

## Plugin Model

Plugins implement:

```csharp
public interface IOptilandPlugin
{
    string Name { get; }

    void Register(PluginRegistry registry);
}
```

Plugins can register:

- geometry factories
- material instances
- analysis factories

Example:

```csharp
public sealed class ExamplePlugin : IOptilandPlugin
{
    public string Name => "example";

    public void Register(PluginRegistry registry)
    {
        registry.RegisterGeometry("example-plane", () => new PlaneGeometry());
        registry.RegisterMaterial(new ConstantIndexMaterial("EXAMPLE-N", 1.52));
        registry.RegisterAnalysis("example-report", optic => new PlaceholderAnalysis(optic, "Example Report"));
    }
}
```

Discovery:

```csharp
var registry = new PluginLoader().LoadFromDirectory("plugins");
```

or for tests/in-process registration:

```csharp
var registry = new PluginLoader().LoadFromAssembly(typeof(SomePlugin).Assembly);
```

Failed plugin assemblies or plugin registration exceptions are collected in `PluginRegistry.Warnings`; they do not block other plugins from loading.
