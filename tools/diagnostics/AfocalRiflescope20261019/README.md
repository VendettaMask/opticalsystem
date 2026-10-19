# Scoped Riflescope afocal controls

This offline C# diagnostic prepares the explicit controls visible in the user's
Riflescope screenshot: 0.486 µm ray fans, three-field polychromatic FFT MTF, and
field curvature/distortion. Pupil sampling and ray count are explicitly 64; the
hidden settings of the screenshot are not assumed known. All optical calculations
use the normal Application/Core. Native capture loads workspace copies in its
own licensed OpticStudio 2026 R1 instance and does not save original inputs.

Start with a fresh single-source Sequential matrix, using the existing corpus
runner and only `Afocal/Afocal Riflescope.ZMX` in the selected-source directory.
The diagnostic `prepare <matrix>` command appends four named capture controls;
`workbench <matrix>` evaluates those recorded requests. Native jobs are executed
serially with the corpus chunk runner. Preserve completed captures and use a new
directory for changed settings.

`formal-config <matrix>` writes a complete explicit configuration for the repaired
formal comparison CLI. It retains every original focal tolerance, explicitly
selects the existing Single Ray Trace/IncidentAngle degree limits for the new
angular Ray Fan comparison, and selects all fields/wavelengths for FFT MTF with
a canonical maximum of 1.7188733853924698 cycles/mrad (0.5 cycles/arcminute).
Pass that file to the normal tool with `--config`, selecting `Ray Fan`, `MTF`,
and `Field Curvature and Distortion` through repeated `--analysis` arguments.

The formal tool now owns unit validation, normalization and comparison. This
diagnostic does not substitute a second comparator or alternate optical engine.
The [completed report](../../../docs/AFOCAL_COMPARISON_REPAIR_2026-10-19.md)
distinguishes passed ray/fan controls, unresolved MTF/distortion differences and
incompatible field-curvature quantities.
