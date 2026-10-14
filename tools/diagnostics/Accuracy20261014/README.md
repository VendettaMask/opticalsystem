# FCGS/FCGT and illumination controls

This offline diagnostic uses the product Core for recalculation and the licensed machine's ZOS-API for independent native controls. It has no product runtime role and never saves the loaded lens. Independent Cooke/Tessar cases remain excluded.

The committed manifest names four unchanged source files, their SHA-256 identities, and native capture directories:

```powershell
dotnet run --project tools/diagnostics/Accuracy20261014/Accuracy20261014.csproj -c Release -- . validation/zemax/2026-r1/fcgt-efno-2026-10-14/manifest.json artifacts/validation/accuracy-matrix.json
```

FCGS/FCGT are checked against the existing `1e-5 mm` target. EFNO/RELI rows remain numerical observations marked `Difference`; finite output is never treated as native equivalence. Compare the same Samp/Wave/Field/Pol columns, not a peak-normalized analysis curve against an axis-normalized operand.

Compile `NativeAccuracyCapture.cs` with the repository `tools/OptilandWorkbench.ZemaxComparison/ZemaxHost/*.cs` helpers using the Windows .NET Framework x64 compiler, `/platform:x64 /main:NativeAccuracyCapture`, and references to `System.Web.Extensions.dll` and the installed `ZOSAPI.dll`, `ZOSAPI_Interfaces.dll`, `ZOSAPI_NetHelper.dll`. Configure Framework v4.8 activation as the existing native host does; put the installed API directory on the process PATH. Do not copy or commit proprietary API binaries.

The capture request contains `zosApiPath` and `input`; optional `illuminationCfg` loads an explicitly selected analysis configuration. Run the client with `<request.json> <fresh-output-directory>`. It rejects nonempty outputs, verifies a valid 2026 R1 API license, captures the native operand column types, then evaluates FCGS/FCGT at 101 normalized +Y fields for each active wavelength and EFNO/RELI at Samp 5/10/20/40, Pol=0, for every defined field and wavelength. No default analysis settings are inferred from the operand controls. The ray audit records its actual wavelength separately.

The Core field-scan default remains the symmetric `1e-5` differential pair. The Application field-curvature preset and FCGS/FCGT operands use the chief and positive X/Y parabasal rays at `0.001`, retaining the additive cross response of the generalized Cartesian calculation. The UI exposes the reference and pupil interval, and explicit legacy settings remain available. These are product conventions verified on the named controls, not universal OpticStudio defaults. The scan analysis swaps its components for an X scan; the operands retain fixed Cartesian X (FCGS) and Y (FCGT).

An optional `signedAxisControls` capture request records axis, negative, and oblique normalized fields `(0,0)`, `(0,±1)`, `(±1,0)`, `(.3,.4)`, and `(-.3,-.4)` for every active wavelength. These are 140 additional curvature controls. The explicit coupled formula is inferred from the native controls; diagonal and image-defocus invariants are independently checked. It is not presented as a published universal native algorithm or a principal-curvature/eigenfocus calculation.

The native transmission and aberration-removal discussion is documented by [Ansys's Relative Illumination help](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html) and [the vendor engineer's FAQ, slides 7–11](https://community.zemax.com/people-pointers-9/faq-about-relative-illumination-1006). They do not specify every finite-grid implementation detail. Current Core scalar-power quadrature retains material attenuation; native Pol=0 uses a different transmission convention. The originally observed 0.309% analysis difference is not a bound on the expanded MFE matrix. Grid placement, aberration removal, and transmission must be resolved independently; no fitted gain or changed reference values are used.
