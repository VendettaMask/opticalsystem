# File Formats And Plugins

## Native JSON

Native files use `OpticSnapshot` with a schema version. The current schema stores:

- optic name
- aperture
- backend name
- fields
- wavelengths
- surfaces
- rich surface component snapshots

Preferred extensions:

- `.optiland.json`
- `.optic.json`
- `.json`
- `.optiland`

The JSON path is strict round-trip oriented. It should be used for internal work and regression fixtures.

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

Use **File > Export Python Optiland JSON** or the `.optiland-python.json` suffix for an explicit Python export. Unsupported Python components fail explicitly; they are not silently replaced. Workbench native JSON is the lossless format for the optical model, rich surface components, radius pickups, and solve settings. GUI preferences are stored separately in `AppSettings`; optimization runs, tolerancing results, plugins, and multi-configuration sessions are not embedded in optic JSON.

Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, so external Python retention of `SimpleCoating` is tracked separately from the Workbench adapter's dictionary support.

Python Optiland 0.5.8 also emits incomplete planar-grating dictionaries and has broken grating `from_dict()` constructors. Workbench preserves order, period, and groove angle in its adapter and native snapshots, but external Python reload of grating exports is not part of the validated contract.

See [Python Optiland JSON interoperability](PYTHON_JSON_INTEROP.md) for schema and external Python round-trip validation.

## Commercial Sequential Formats

Supported extensions:

- Zemax `.zmx`
- CODE V `.seq`
- OSLO `.len`
- plain sequential `.lens`, `.dat`, `.txt`

Zemax `.zmx` import follows the Python Optiland 0.5.8 `zemax_handler.py` and `ZemaxToOpticConverter` boundary. It includes:

- UTF-16 LE/BE, UTF-8, and Latin-1 text decoding
- sequential-mode validation
- entrance-pupil diameter, image F-number, object numerical aperture, and floating-stop aperture definitions
- angle, object-height, and paraxial-image-height fields
- field weights and X/Y vignette compression
- wavelengths, weights, and primary-wavelength selection
- glass catalog declarations and `GLAS` index/Abbe fallback data
- standard, even-asphere, odd-asphere, and basic toroidal surfaces
- coordinate-break decenter, tilt, and thickness transforms
- mirror material continuity, comments, stop flags, and semi-diameters

The ZMX importer rejects non-sequential mode, unsupported Zemax surface types, negative thickness, coordinate-break order flags, real-image/theodolite field definitions, and toroidal conic/polynomial terms. Vignette decenter and tangent-angle operands are read but not represented by the current field model. Coatings, solves, pickups, polarization, multi-configuration operands, and unsupported freeform data are not imported.

`GCAT` and `GLAS` resolve against the embedded 1,740-entry Optiland 0.5.8/refractiveindex.info glass database. SCHOTT, OHARA, HOYA, HIKARI, CDGM, SUMITA, LZOS, and the other bundled glass categories use their actual dispersion formulas or tabulated n/k values during tracing and analysis. Same-named glasses are selected by `GCAT`; an unknown glass falls back to `AbbeMaterial` only when its `GLAS` record supplies valid nd/Vd values. Otherwise import fails explicitly.

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

Use native JSON when full state preservation matters. Python-derived Zemax fixtures are generated by `tools/python-reference/generate_zemax_reference.py` and validated by `ZemaxImportTests`. The embedded glass resource and n/k golden values are generated by `generate_glass_catalog.py` and `generate_glass_reference.py`, then validated by `GlassCatalogTests`.

The application routes by extension through `OptilandConnector`:

```csharp
await connector.LoadAsync(path);
await connector.SaveAsync(path);
```

The connector detects Workbench JSON, Python Optiland JSON, or an `OpticalFormatCatalog` adapter automatically.

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
