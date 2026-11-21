# Lens scaling — 2026-11-21

Scope: sequential lens scaling and prescription length units. No full 95-lens accuracy matrix, native foreground app interaction, packaging or remote publication is part of this check.

## Behavior

- Settings → Lens Tools → Scale Lens (`scale-lens`, also registered in command search).
- Positive finite factor, existing inclusive first/last surface range, including Object and Image. Factor 1 is a no-op. All configurations are staged and validated together, then committed as one undo step. Stale dialog revisions, unsupported components, underflow/overflow, and the existing 0.1 mm minimum semi-diameter reject without mutation.
- Lengths scale by `f`, coefficients of raw monomial degree `p` by `f^(1-p)`, normalized sag amplitudes by `f`, normalized Chebyshev-sag coefficients by `1/f`. Conic constants and orientation angles remain unchanged. Geometry uses canonical component kinds, never localized labels.
- Radius/thickness/semi-diameter affine pickups update `scale *= targetMultiplier/sourceMultiplier`, `offset *= targetMultiplier`. Multi-configuration curvature and shape coefficient pickups use their declared dimensions. Total-track solve targets account for the scaled thicknesses, preserving the result when solves are reapplied.
- Coordinate origins account for changed preceding axial gaps and the current surface's local offsets, retaining rotations. Downstream unselected surfaces follow changed preceding gaps. Positive infinite object distance remains infinite.
- Full-range scaling additionally changes entrance-pupil diameter and height-based fields. Angular fields, wavelengths, material dispersion/thermal data, coating thicknesses, drawing settings and merit targets retain their values. Independent NSC documents are retained.
- Unit conversion changes persisted prescription display units only: mm, cm, m, in. The calculation model remains in mm, so physical geometry and optical results do not drift. The main lens length cells and thickness pickup offset convert input/output; component dialogs and analyses retain explicitly labeled units.
- Native snapshot schema 9 / STAROPT project 25 store non-default units. Older inputs default to mm. Files using only mm may retain earlier compatible project formats.

Supported dimension rules cover plane/tilted plane, standard/grating surfaces, even/odd/normalized aspheres, biconic/separable biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q/normal-slope, irregular, fringe-sag, grid-sag and concentric zones; ordinary aperture primitives and Boolean combinations; refractive/reflective/reference, thin lens, paraxial lens, ruled grating and simple diffractive interactions. GRIN, polygon-group/opaque geometry and phase-profile interactions are explicitly rejected pending complete dimensional rules. This is not a claim of complete vendor Scale Lens parity.

## Verification

Focused sources: `tests/OpticalSystemDesign.Tests/LensScalingTests.cs`, `LensScaleWindowTests.cs`, and the existing `SurfaceEditorRowTests.cs`; linked by `focused/LensScaling.csproj`. The default .NET SDK is 10.0.300. Build artifacts use a task-owned directory because concurrent work held the ordinary App PDB. NuGet audit was disabled for this invocation because the advisory endpoint was unavailable; this does not claim a vulnerability audit.

Final result: **49 passed, 0 failed, 0 skipped**, in `results/lens-scaling-final.trx`. This comprises 35 scaling checks (including 14 sag families), 7 existing editor-row checks and 7 headless dialog cases. Six screenshots cover Light/Dark/Lumen/Isekai/Pixel and Japanese 18 DIP at 430 DIP width. The footer stays visible; invalid input leaves the original document unchanged. Light, narrow Japanese, Isekai and Dark captures were visually inspected; the final explicit theme surface binding removes the Isekai black background.

Both the isolated and ordinary Release App builds passed. The ordinary build at `src/OpticalSystemDesign.App/bin/Release/net10.0` finished with 0 warnings and 0 errors; see `default-build.log`. All UI checks use Avalonia Headless; the user's running desktop app is untouched. Task-owned binaries and temporary settings are removed after validation; source, TRX, screenshots and the verification manifest remain.

Primary feature reference: [Ansys OpticStudio — Scale Lens](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Scale_Lens.html). The reference supports the factor/range/unit workflow and dimension-dependent asphere scaling; local tests establish this implementation's actual scope.

## Reproduction

```powershell
$env:OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY = "$PWD/validation/rechecks/2026-11-21/lens-scaling/settings"
$env:OPTICAL_SYSTEM_DESIGN_LENS_SCALE_CAPTURE_DIR = "$PWD/validation/rechecks/2026-11-21/lens-scaling/captures"
dotnet test validation/rechecks/2026-11-21/lens-scaling/focused/LensScaling.csproj -c Release -p:NuGetAudit=false -p:UseArtifactsOutput=true -p:ArtifactsPath="$PWD/validation/rechecks/2026-11-21/lens-scaling/build" --logger 'trx;LogFileName=lens-scaling-final.trx' --results-directory validation/rechecks/2026-11-21/lens-scaling/results
```
