# Packaged Lens Library

## Runtime model

The desktop application does not synchronize, download, extract, or convert lens
files. The library is built once before release and shipped as read-only native
data:

```text
LensLibrary/
  index.json
  projects/*.staropt
```

At startup the application reads `index.json` and opens only `.staropt` projects
for the parameter view and 2D preview. It contains no lens-library `HttpClient`,
ZMX conversion pipeline, ZIP extractor, AGF loader, or synchronization command.
All lens materials must resolve against the application's own glass database.

Open the independent library page from **Database > Lens Library**. It is not
embedded in the material library. Selecting an entry updates its parameters and
2D preview without changing the current design; double-clicking it opens the
packaged `.staropt` project and activates the lens editor.

## Offline library build

Library preparation belongs to the repository-maintenance tool
`tools/OptilandWorkbench.LensLibraryBuilder`, not the desktop application. Its
manifest points to already downloaded local files or directories. It:

1. safely extracts ZIP inputs into a temporary directory;
2. scans local ZMX files;
3. resolves every material against the Workbench glass database;
4. imports every supported ZMX configuration;
5. writes checksummed `.staropt` projects and a compact `index.json`.

Build the public source library after its files have been downloaded into the
ignored `local-data/lens-library/originals/` tree:

```bash
dotnet run \
  --project tools/OptilandWorkbench.LensLibraryBuilder \
  -- \
  tools/lens-library-public-sources.json \
  src/OptilandWorkbench.App/Assets/LensLibrary
```

`tools/lens-library-release.json` builds the current 61-entry release library:
56 individual microscope objectives from the locally staged Chapter 21 companion
files and five industrial-imaging examples. Microscope sources use a positive
objective allow-list. Tube lenses, Fourier-imaging configurations, complete
microscope systems, and condensers are excluded; the combined microscope-system
and condenser file also depends on an unsupported folded `SCBD` layout.

## Source and Git policy

The first source manifest covers public microscope and industrial designs:

| Category | Source | Terms |
|---|---|---|
| Microscope | *Introduction to Modern Optical System Design (2nd ed.)*, Chapter 21 companion files | Redistribution permission pending |
| Microscope objectives | Figshare standalone patent-derived objective files only | CC BY 4.0 |
| Industrial | TI DLP4500 optics design files | TI website terms |

Downloaded ZMX/ZAR/ZIP files and extracted working data remain under
`local-data/lens-library/`, which is ignored by Git. Only the reviewed release
artifacts (`index.json` and `.staropt`) are packaged with the application.

ZAR archives must be expanded by an approved external preparation step before the
builder runs; unsupported constructs fail the offline build instead of being
silently approximated.
