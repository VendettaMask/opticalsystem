# Material details and the current official CDGM catalog

The final implementation directly consolidates the four old bundled CDGM catalogs into **one CDGM catalog with 352 unique materials**, using the manufacturer's current official download, `CDGM-ZEMAX202609.AGF`, headed **Updated Sep. 2026**. There is no old-version selector or browser-only grouping. This follows the user's correction of the initial version-selection proposal.

## Source and conversion

- [CDGM official download page](https://www.cdgmgd.com/go.htm?k=ge_lei_xia_zai&url=downList), [official AGF download](https://www.cdgmgd.com/downloadFile.htm?uid=vkYlfwDmuNtvITOp4NV).
- [Unmodified source and provenance](../../../../third_party/material-catalogs/cdgm/README.md): downloaded 2026-11-04, UTF-16 LE, 1,325,696 bytes, SHA-256 `e0933fe14749919b2b7c5170f814765af570ef53b78171dc98dae6b542f8e9ce`.
- The old English site's numeric-ID link returned “文件不存在”. The Chinese official site's live link delivered the file and its actual filename/version. No third-party repack was used.
- [Conversion log](conversion.log), [locked restore](converter-restore.log), [before/after receipt](catalog-update-receipt.json). The first attempt used `--no-restore` before this existing tool had an assets file and failed NETSDK1004; locked restore then completed successfully.

The existing C# converter now supports replacing a named catalog family without regenerating unrelated catalogs. The exact update command after locked restore was:

```powershell
dotnet run --project tools/OpticalSystemDesign.GlassCatalogConverter/OpticalSystemDesign.GlassCatalogConverter.csproj -c Release --no-restore -- --replace src/OpticalSystemDesign.Core/Materials/Data/zemax-glass-catalogs.ogdb third_party/material-catalogs/cdgm/CDGM-ZEMAX202609.AGF CDGM 'CDGM,CDGM-H,CDGM-ZEMAX202309,CDGM20113-P' src/OpticalSystemDesign.Core/Materials/Data/zemax-glass-catalogs.ogdb
```

The shared parser retains every official field, including dispersion, thermal/mechanical data, cost/durability, transmission and stress records. The converter validates finite positive refractive indices for every retained material at a wavelength inside its declared range, then writes atomically; this is data-integrity validation, not an external numerical certification.

The bundle changes from **63 catalogs / 5,502 records** to **60 catalogs / 5,047 records** (5,046 unique catalog/name pairs). The four old CDGM catalogs had 807 records and 343 unique names. The current official catalog has 352 unique names: 21 added, 12 removed; the receipt records all names. Every one of the **other 59 catalog documents is identical field-for-field** to the original Git bundle. The original bundle identity was checked against the pre-update SHA-256 before comparison; no duplicate old bundle was retained.

The older auxiliary `glass-catalog.json` remains byte-for-byte unchanged. Its 314 old CDGM entries are excluded at runtime so they cannot override the official release or restore removed types. Other manufacturers' resolved-name identities are calculated before that filter. Retired CDGM-H/CDGM-ZEMAX202309/CDGM20113-P lookup keys resolve the current same-name CDGM definition. The project catalog priority still determines ambiguous bare names, including catalog keys with `.AGF` suffixes. Missing official types remain unresolved rather than being approximated. Explicit saved material snapshots keep their own stored coefficients/manufacturer; user project files are not rewritten.

## Material-library details

The page and copied glass report now show **melt frequency, original comment, relative cost, CR, FR, SR, AR and PR**. Data comes from the existing DTO's NM/GC/OD fields, without combining values from different versions. The manufacturer's comment remains selectable, wrapped and untranslated; labels follow Chinese/English/Japanese and the existing theme rules. The existing detail scroll/reflow containers keep fields reachable in narrow windows.

OD order is relative cost / CR / FR / SR / AR / PR. Following the [official AGF format](https://optics.ansys.com/hc/en-us/articles/46891219767827-Zemax-AGF-material-catalog-file-format), `-1` denotes unavailable data; missing or nonfinite values also display `?`, while genuine zero stays `0`. Empty comments display `—`. The relative-cost tooltip identifies its BK7 basis rather than a current purchase quote. No source values are guessed or filled from another material.

The user's example is `D-ZLAF81-25`: frequency **4**, comment **anneal rate:25℃/h**, current official cost **13.7**. The earlier screenshot's **12.3** belongs to the old source. Its five resistance values remain unavailable in the current file and therefore show `?`.

## Verification

Default-output Release compilation succeeds. Final single-run **39/39 passed, zero failed/skipped**, recorded in [final TRX](material-final-with-catalog-extensions.trx) and [final log](final-run-with-catalog-extensions.log):

- **9** `MaterialLibraryPanelTests`: real current source fields and copied report; Light wide/narrow, Dark English, Lumen Japanese and Isekai; all new fields read-only/reachable; one CDGM category and 352 non-repeated names; no version selector; project/revision unchanged by browsing; distinct OD positions, zero/absent/nonfinite handling and selection clearing.
- **7** `CdgmOfficialCatalogTests`: all 352 full source records match the runtime definitions; raw coefficients and formula evaluation match the official input path; all four current/old catalog keys and priority order, with/without `.AGF`; removed types do not reappear; a deliberately different saved snapshot retains its own values. The snapshot fixture is explicitly synthetic, not vendor material data.
- **13** existing `GlassCatalogTests`: parsing/import, catalog priority, snapshots, concurrent resolution, missing values and historical references. Its auxiliary count is now 1,426 rather than 1,740 after excluding 314 old CDGM entries. Historical CDGM n/k checks still use the unchanged historical auxiliary definitions explicitly; they do not falsely certify the new official data against an old reference.
- **9** existing `MaterialAnalysisTests`, plus **1** existing finite wide/narrow master-detail layout case.

Earlier records remain separate: [initial UI run](material-initial.trx) is 8/9 because its newly added serialization check did not allow the catalog's existing nonfinite placeholders; that fixture was corrected. [First official run](material-official.trx) is 35/36 and reproduced a real catalog-priority regression after removing auxiliary CDGM: a bare F2 lookup fell through to SCHOTT before consulting CDGM. The resolver now honors the existing ordered project catalog list across the two sources. [Verified run](material-verified.trx) passed 36/36, then [39-case run](material-final.trx) added snapshot/removed-type/finite-layout coverage. The final run adds the `.AGF` priority assertions to the same 39 cases. Overlapping runs are not summed. Transient shared test-output copy retries in the earlier run are retained in its log; no process was killed or user window closed.

All UI simulation and screenshots were headless/off-screen. No foreground app was controlled or restarted, and no commit, push, package or publication was performed. The manufacturer's updated values can change newly resolved CDGM calculations; the numerical engine and acceptance tolerances were not edited. This scope does not recertify all lens examples, old frozen baselines or the whole repository.

## Actual captures

- [Light wide](captures/material-Light-zh-CN-1280.png) / [Light narrow](captures/material-Light-zh-CN-720.png)
- [Dark English](captures/material-Dark-en-US-1280.png)
- [Lumen Japanese](captures/material-Lumen-ja-JP-1280.png)
- [Isekai English](captures/material-Isekai-en-US-1280.png)

All five show the actual current official `D-ZLAF81-25` entry. The [manifest](manifest.json) records final test identities, capture hashes and scoped source/resource hashes, reusing the retained source and Git identity without copying a source tree.

## Documentation follow-up — 2026-11-04

The Chinese/English main READMEs, documentation index, user guide, current status and embedded-data notes now link to the current material workflow and provenance. The guide explains all eight added fields, unavailable versus real zero values, official-file versus download dates, old lookup keys versus stored snapshots, removed types, and restart/reload behavior. The auxiliary-data introduction now explicitly distinguishes unchanged source bytes from the updated runtime CDGM data.

This follow-up changes documentation only. All material implementation/data hashes still match the recorded manifest; the **39 passing product cases are reused, not rerun or added to a new total**. The local-link/fence checks are recorded in [final documentation audit](documentation-check-final.json) and [its log](documentation-check-final.log); the follow-up receipt is [documentation-review.json](documentation-review.json). The first full scan is retained in `documentation-check.json` / `documentation-check.log`: it found one missing Typography link while its separate popup-font report had not yet appeared. That target was present at recheck; no unrelated source or numerical evidence was rewritten.
