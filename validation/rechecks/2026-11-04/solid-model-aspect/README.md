# Solid Model: automatic and manual outer aspects

Solid Model now uses the shared default **Auto / 16:9 / 4:3** setting under Format and Font / Drawing aspect. Auto follows the actual drawing viewport independently, with the existing hysteresis; manual choices remain fixed when resizing.

Only the outer page, viewport containers and export composition change. The original model, compact metadata and material legend occupy a fixed **1200×900-DIP inner layout** on either a 1200×900 or 1600×900 sheet. A wide sheet adds side space. There is no new 160-DIP formal optical-layout caption. Fit and 100% scrolling share the existing host; settings, shading, cutaway, rays and labelled camera buttons remain at native UI scale outside the scaled drawing.

The same scene/model/ray DTO instances and values, material palette, render/cutaway/rays-visible choices, yaw/pitch and exact pan/zoom remain through aspect changes. Neither `OpticSceneControl` nor `SceneViewport` was edited. No optical calculation, ray sampling, model geometry or camera interaction handler was changed in this task. Other concurrent Core/theme edits were preserved.

## Export composition

PNG uses the live viewport and current camera, with the complete material legend and compact metadata. It excludes navigation, settings and focus decorations. The model is frozen at the final output raster scale and those pixels are centered in the selected outer sheet without interpolation or a second alpha blend. The paper is recorded before the model in the detached export tree. Complete legend exports may extend the page height; the live viewport is never resized to fit an export.

Two concrete export issues were found during verification: detached paper could cover the model in Isekai, and nested vector composition changed antialiasing pixels even when the direct scene and camera were unchanged. Paper ordering and freezing the source raster close those issues in the export layer. Whole-DIP outer height and pixel-aligned footer origin retain complete metadata; height padding alone did not close the pixel mismatch.

## Verification scope

Default-output Release compilation succeeded. The final single run passed **54/54, zero failures/skips**, recorded in [solid-verified.trx](solid-verified.trx) and [verified-run.log](verified-run.log):

- Eight new Solid Model cases: seven combinations of Light/Lumen/Isekai, solid/transparent/wireframe, cutaway and rays hidden; plus native-size controls and focus/settings-free PNG. Each combination traverses automatic wide/narrow/wide, manual wide/narrow in both ratios, return to Auto, and 100% scrolling. It checks exact camera values, source identities/serialized values, document revision, inner viewport size and live scene pixel hashes. The exported model matches the direct source pixel hash over **every complete viewport row** at 1×; the final fractional-alpha row is excluded from that comparison.
- 37 existing viewer-image cases, including actual Skia PNGs, complete 24-material legend, localized captions/overflow gates, stale/empty/export-failure handling, fractional and doubled output scale and retained camera.
- Seven existing native labelled-view-button cases across Solid Model and 3D Layout, languages, themes, narrow widths and larger fonts. This fixture explicitly selects its original 4:3 geometry; the new cases independently test Auto and both manual choices.
- Two existing 2D/3D Layout automatic-aspect scene/camera cases.

The earlier isolated [frozen-composition run](solid-frozen-composition.trx) passes 8/8. Prior records remain truthful: `solid-initial.trx` is 7/8 (new test selected a nonexistent settings class); `solid-expanded.trx` is 52/54 (two old geometry fixtures assumed 4:3 despite Auto); `solid-final.trx` is **47/54**, despite its historical filename, with seven newly strict pixel mismatches. `solid-pixel-diagnostic.trx` and `solid-pixel-aligned.trx` each retain 1/8, demonstrating that integer height alone was insufficient. `final-run.log` belongs to the failed `solid-final.trx`, not the final verified result. Files prefixed `failure-` in captures are diagnostic mismatches, not accepted output.

All computation, native-control simulation and captures were headless/off-screen. The user's running application was not controlled or restarted. This is a scoped presentation/state/export regression, not a full-repository pass rate, numerical accuracy certification, foreground acceptance, installation or publication.

## Actual captures

The seven model variants use a real Cooke scene, calculated once per case. The camera is deliberately rotated, panned and zoomed before switching; wide/narrow captures retain that same camera and data.

- Lumen full window: [wide](captures/model-Lumen-Solid-False-True-wide-window.png) / [narrow](captures/model-Lumen-Solid-False-True-narrow-window.png)
- Light PNG: [wide](captures/model-Light-Solid-False-True-wide.png) / [narrow](captures/model-Light-Solid-False-True-narrow.png)
- Isekai PNG: [wide](captures/model-Isekai-Solid-False-True-wide.png) / [narrow](captures/model-Isekai-Solid-False-True-narrow.png)
- [Complete 24-material legend](export-captures/solid-complete-24-material-legend.png) uses an existing synthetic legend stress fixture, not a real 24-material optical prescription.

The [manifest](manifest.json) records results, source/renderer identities and capture hashes. Existing payloads are referenced in place; no source tree or runtime was copied.
