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
When a valid imported design uses a real-image-height field that cannot produce
viewer rays, the library still renders its lens geometry and omits the preview
rays instead of failing the entire preview.

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
ignored `local-data/lens-library/originals/user-zmx/public/` tree. Versioned
project samples and fixtures live beside it under `user-zmx/project/`:

```bash
dotnet run \
  --project tools/OptilandWorkbench.LensLibraryBuilder \
  -- \
  tools/lens-library-public-sources.json \
  src/OptilandWorkbench.App/Assets/LensLibrary
```

`tools/lens-library-release.json` builds the 61-entry baseline library: 56
individual microscope objectives from the locally staged Chapter 21 companion
files and five industrial-imaging examples. Running the public-corpus importer
described below adds the 788 currently compatible open designs, producing the
849-entry packaged library. Microscope sources use a positive objective
allow-list. Tube lenses, Fourier-imaging configurations, complete microscope
systems, and condensers are excluded; the combined microscope-system and
condenser file also depends on an unsupported folded `SCBD` layout.

## Source and Git policy

The first source manifest covers public microscope and industrial designs:

| Category | Source | Terms |
|---|---|---|
| Microscope | *Introduction to Modern Optical System Design (2nd ed.)*, Chapter 21 companion files | Redistribution permission pending |
| Microscope objectives | Figshare standalone patent-derived objective files only | CC BY 4.0 |
| Industrial | TI DLP4500 optics design files | TI website terms |

Downloaded ZMX/ZAR/ZIP files and extracted working data remain under
`local-data/lens-library/originals/user-zmx/public/`, which is ignored by Git.
The compact project-owned ZMX samples and test fixture live under the adjacent
`user-zmx/project/` subtree and remain versioned. Only the reviewed release
artifacts (`index.json` and `.staropt`) are packaged with the application.

ZAR archives must be expanded by an approved external preparation step before the
builder runs; unsupported constructs fail the offline build instead of being
silently approximated.

## Single-file Zemax converter and installer

Use the independent `tools/OptilandWorkbench.ZemaxLibraryImporter` executable when
one reviewed Zemax file needs to be converted and added incrementally. Unlike the
batch builder, it does not rebuild or remove existing library entries. It:

1. imports every supported configuration from one sequential `.zmx` file;
2. writes and reloads a checksummed native `.staropt` project for validation;
3. publishes the same native project to `samples/lenses`;
4. publishes it to `Assets/LensLibrary/projects`;
5. upserts one version-1 entry in the existing `Assets/LensLibrary/index.json`
   using the same ID, metadata, field ordering, and sorting rules as the batch
   builder.

On Windows, drag a `.zmx` file onto `Convert-Zemax-Lens.cmd`, or run:

```powershell
.\Convert-Zemax-Lens.cmd "D:\lenses\example.zmx"
```

The default category is `示例镜头`. Metadata can be supplied without editing JSON:

```powershell
.\Convert-Zemax-Lens.cmd "D:\lenses\example.zmx" `
  --name "50 mm 双高斯" `
  --category "摄影镜头" `
  --source-name "内部设计库" `
  --license "内部使用"
```

Run `Convert-Zemax-Lens.cmd --help` for custom repository, example-library, and
lens-library output directories. Reimporting the same source ID and file name
updates the stable entry instead of creating a duplicate. Conversion and all
three output replacements are staged first; an import, serialization, or index
failure leaves the prior libraries intact.

## Public Zemax corpus synchronization

`tools/Sync-Public-ZemaxCorpus.ps1` performs a reproducible public-data search
against the Figshare and Zenodo APIs and the known Mendeley Data optical-design
datasets. It accepts only real `.zmx` attachments whose records declare an open
licence, verifies provider MD5 values when available, computes SHA-256 values,
and records source metadata in:

```text
local-data/lens-library/originals/user-zmx/public/manifest.json
```

The entire download tree is intentionally ignored by Git. Synchronize it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-Public-ZemaxCorpus.ps1
```

`tools/Sync-DanReileyLensExchange.ps1` mirrors all files from the nine public
Google Drive folders embedded in the Dan Reiley Lens Design Exchange. The
download keeps the website category layout under `public/dan-reiley/` and writes
file IDs, original names, SHA-256 values, duplicate relationships, and failures
to `public/dan-reiley-manifest.json`. The site declares every submitted design
file to be public domain. Synchronize it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-DanReileyLensExchange.ps1
```

Build the single-file importer once, then convert every unique downloaded design:

```powershell
dotnet build `
  .\tools\OptilandWorkbench.ZemaxLibraryImporter\OptilandWorkbench.ZemaxLibraryImporter.csproj `
  -c Release `
  -o .\.tmp\public-zmx-importer

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Import-Public-ZemaxCorpus.ps1
```

The importer reads both public manifests when present, selects `.zmx` entries,
and installs every successful file through the same single-file importer into
both `samples/lenses` and `Assets/LensLibrary`, so IDs, metadata, ordering, and
native serialization remain identical to manual imports. Unsupported designs
are retained in the download corpus and listed with their exact converter error
in `conversion-report.json`; they are never silently approximated. Typical
rejections include non-sequential models, user-DLL surfaces, and Zemax surface
types not represented by the current geometry model. The 2026-07-29 synchronized
corpus contains 1,050 ZMX manifest entries: 788 convert successfully, 256 retain
explicit failure reports, and six duplicate-content entries are skipped.
