# Vertical coating-stack analysis — 2026-11-02

The coating editor's horizontal embedded strip has been removed. The top **堆栈几何** command opens one owned, nonmodal analysis window with a vertical dimensional section and a full read-only layer-data tab. Media/substrate controls remain in the editor's compact **介质与基板设置** expander.

## Display and data contract

- Current editing values supply layer order, original material name/ID, stable material-identity color, physical thickness, incident/substrate/exit media, substrate thickness, reference wavelength and design incidence angle. Opening, selecting, resizing and exporting the page do not change the experiment, dirty state or physical results.
- The visual treatment uses front/side faces, highlights, a glass substrate body and a declared incident-direction arrow. This is an illustration: colors identify materials, film thickness is visually enlarged (including a minimum visible band), and the substrate is compressed on a separate scale. Neither tint nor rendered pixel height is a computed transmittance or measured appearance.
- Every layer is drawn in incident-to-substrate order. Small stacks show all annotations; large stacks/large fonts use a readable subset and always include the selected layer. The data tab retains every layer and its complete name, ID and numeric thickness. Pointer/keyboard selection and data-grid selection share the same layer identity.
- Edits refresh an already-open page. Invalid material or thickness clears the old diagram/data, disables export and displays the cause; correcting the input restores the live view. The analysis closes with its owning module.
- Drawing and PNG share the same original 1200×900 DIP source, 160-DIP common caption and outlined company logo. Export is 2404×1804 including the border, retains selection and has bounded atomic output. A narrow window scrolls the original sheet instead of shrinking fonts or rebuilding it.

## Verification

`tests-verified.log`/`vertical-stack-verified.trx`: **6/6 targeted coating cases passed**. These include Light/Dark 20-layer sections, 200 layers at 18-point UI settings, actual prescription/thickness identity, view-only opening, data selection, fixed PNG geometry before/after resize, close/reopen, live updates, invalid-state clearing/recovery, a bare substrate and language changes. Adjacent checks retain the original five reference examples and physical/optical-thickness semantics, including real mouse activation of the unchanged evaluation workflow.

`shared-caption.log`/`shared-caption.trx`: **1/1 existing MTF export case passed**, verifying the shared-caption overload preserves the original layout, typography, DPI and export through window resizing. Together these are **7 distinct cases**, not a full-suite run. The later pointer/keyboard branch is recorded in `tests-input.log`/`vertical-stack-input.trx`; repeated identities are not added to that total.

`build-v2.log` and the verified test build graph record the normal default Release App output, both built-in modules included, with zero warnings/errors after repair. No foreground application was launched or controlled. No coating formula, material spectrum, physical thickness, optimization acceptance gate or file version changed.

## Actual previews and retained failures

- `vertical-stack-Light-20.png`, `vertical-stack-Dark-20.png`, `vertical-stack-Light-200.png`: actual original-sheet PNG captures.
- `vertical-stack-window-*.png`: actual narrow owned-window views with scrolling.
- The `demo-*-1500.png` and geometry-settings captures are the original module's adjacent physical-workflow views, not new solver certification.
- `build.log` retains the first UI compile failures; `tests.log` retains the initial test-analyzer rejection. Neither is a passing run.
- `tests-v2.log`, `tests-final.log`, `tests-examples.log` and `tests-hit-v2.log` retain the genuine evaluation-button hit failure. The diagnostic hit path identifies `ToolTip → OverlayPopupHost`, and `geometry-expanded-before-compute.png` shows the example tooltip over the compute button. Command tooltips now remain visible but do not take pointer input; the original mouse test passes without dismissing popups or substituting a programmatic click. Earlier attempts to isolate the owned window did not resolve that failure and are not promoted to passes.

The source/binary hashes and strict documentation check are recorded beside this file. Current instructions: [module guide](../../../../labs/CoatingDesign/README.md), [UI contract](../../../../docs/UI_DESIGN_SPEC.md).
