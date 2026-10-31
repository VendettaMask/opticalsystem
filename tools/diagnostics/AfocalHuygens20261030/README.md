# Afocal Huygens sampling study

Run `dotnet run -c Release --project tools/diagnostics/AfocalHuygens20261030 -- REPOSITORY NEW_OUTPUT`
after preparing the independent native-disk captures described by the
[report](../../../docs/AFOCAL_HUYGENS_QUADRATURE_2026-10-30.md).
The tool rejects an existing output directory and verifies native grid axes.
It compares legacy native, legacy Core and explicit Gaussian circular pupil
quadrature with an independent Airy series, retaining the finite-window
under-sampling counterexamples. Diagnostic arrays are never input to product Core.

The adjacent `AfocalHuygensIndex20261030` utility compiles against an explicit
`CoreProbeAssemblyPath` without rebuilding that Core. Separate artifacts paths
retain old/current/fixed assemblies' scaling diagnostics. It is a reproduction
of an existing regression, not new independent lens/native certification.

`Seal-HuygensQuadrature.ps1` seals this stage without rerunning applications or
tests. Do not redirect its own output into the scanned artifact prefix while
sealing. Native controls use the existing hidden, owned ZOS-API runner and
`captureScreenshots=false`; do not attach to or control the user's desktop.

The [checked sampling stage](../../../docs/AFOCAL_HUYGENS_SAMPLING_CHECKS_2026-10-31.md)
adds a separate explicit mode with independent rule checks and cumulative
spectral/field budgets. `Seal-CheckedSampling.ps1` extracts measured JSON from
the final scoped TRX files and compares fixed-mode replay with the immutable
previous archive. It does not run tests, native applications or the desktop.
