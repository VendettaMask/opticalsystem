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

- EPD, image-F-number, and object-NA system apertures
- angle fields and weighted primary/non-primary wavelengths
- plane, standard, biconic, representable toroidal, pure polynomial/Chebyshev/fringe Zernike, and representable high-order even/odd asphere surfaces with coordinate transforms
- catalog, ideal, and Abbe materials
- radial and rectangular physical apertures
- refractive/reflective and non-reflective thin-lens interactions
- simple Python coating dictionaries on the Workbench adapter path

Use **File > Export Python Optiland JSON** or the `.optiland-python.json` suffix for an explicit Python export. Unsupported Python components fail explicitly; they are not silently replaced. Workbench native JSON remains the lossless format for Workbench-only geometry, plugin, optimization, tolerancing, solve, pickup, and GUI state.

Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, so external Python retention of `SimpleCoating` is tracked separately from the Workbench adapter's dictionary support.

See [Python Optiland JSON interoperability](PYTHON_JSON_INTEROP.md) for schema and external Python round-trip validation.

## Commercial Sequential Subset

The commercial format layer intentionally starts with a common sequential lens subset. Supported extensions:

- Zemax `.zmx`
- CODE V `.seq`
- OSLO `.len`
- plain sequential `.lens`, `.dat`, `.txt`

The common subset includes:

- surface number
- label/comment
- radius or curvature
- thickness
- material
- semi-diameter
- conic
- stop flag
- reflective flag

Fields, wavelengths, coatings beyond simple labels, freeform-specific fields, solves, pickups, tolerancing data, and GUI settings are not preserved by the commercial subset. Use native JSON when full state preservation matters.

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
