using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Tools.RayTrace;

// Diagnostic input coordinates come from formal Core, never a second pupil-sampling implementation.
internal static class NativeRayAuditCapture
{
    internal static void Run(IOpticalSystem system)
    {
        var inputs = ((IEnumerable)Host.Request["rayAuditInputs"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length < 1 || inputs.Length > 16 * 65536 || Host.Int("surfaceRecordLimit") < 1
            || (long)inputs.Length * (system.LDE.NumberOfSurfaces - 1) > Host.Int("surfaceRecordLimit"))
            throw new InvalidOperationException("Invalid ray audit input count");
        var fields = system.SystemData.Fields;
        var original = Enumerable.Range(1, fields.NumberOfFields).Select(i =>
        {
            var f = fields.GetField(i);
            return new[] { f.VDX, f.VDY, f.VCX, f.VCY, f.TAN };
        }).ToArray();
        IBatchRayTrace tool = null;
        try
        {
            if (Convert.ToBoolean(Host.Request["removeVignettingFactors"])) fields.ClearVignetting();
            system.UpdateStatus();
            // Newly exported prescriptions may not carry saved pupil caches.
            // Ask native OpticStudio to calculate the system before querying
            // direct launch coordinates or opening normalized ray batches.
            var entrancePupilDiameter = system.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.EPDI,
                0, 0, 0, 0, 0, 0, 0, 0);
            var entrancePupilPosition = system.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.ENPP,
                0, 0, 0, 0, 0, 0, 0, 0);
            if (double.IsNaN(entrancePupilDiameter) || double.IsInfinity(entrancePupilDiameter) || entrancePupilDiameter == 0)
                throw new InvalidOperationException("Native system did not calculate a nonzero entrance pupil; ray audit cannot certify its launch coordinates.");
            var state = Enumerable.Range(1, fields.NumberOfFields)
                .Select(i => Host.Object("number", i, "data", Host.Properties(fields.GetField(i)))).ToArray();
            tool = system.Tools.OpenBatchRayTrace();
            if (tool == null) throw new InvalidOperationException("Native batch ray tool unavailable");
            var launches = inputs.Select(p =>
            {
                double x, y, z, l, m, n;
                var ok = tool.GetDirectFieldCoordinates(Host.Int("wavelength"), RaysType.Real,
                    Number(p, "hx"), Number(p, "hy"), Number(p, "px"), Number(p, "py"),
                    out x, out y, out z, out l, out m, out n);
                return Host.Object("fieldIndex", p["fieldIndex"], "pupilIndex", p["pupilIndex"],
                    "success", ok, "x", Host.Finite(x), "y", Host.Finite(y), "z", Host.Finite(z),
                    "l", Host.Finite(l), "m", Host.Finite(m), "n", Host.Finite(n));
            }).ToArray();
            var surfaces = new List<object>();
            for (int surface = 1; surface < system.LDE.NumberOfSurfaces; surface++)
            {
                var data = tool.CreateNormUnpol(inputs.Length, RaysType.Real, surface);
                foreach (var p in inputs)
                    if (!data.AddRay(Host.Int("wavelength"), Number(p, "hx"), Number(p, "hy"),
                        Number(p, "px"), Number(p, "py"), OPDMode.None))
                        throw new InvalidOperationException("Native ray audit AddRay failed");
                tool.RunAndWaitForCompletion();
                if (!data.StartReadingResults()) throw new InvalidOperationException("Native ray audit results unavailable");
                int number, error, vignette;
                double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                var rows = new List<object>();
                while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z,
                    out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                    rows.Add(Host.Object("number", number, "error", error, "vignette", vignette,
                        "x", Host.Finite(x), "y", Host.Finite(y), "z", Host.Finite(z),
                        "l", Host.Finite(l), "m", Host.Finite(m), "n", Host.Finite(n),
                        "nx", Host.Finite(nx), "ny", Host.Finite(ny), "nz", Host.Finite(nz),
                        "intensity", Host.Finite(intensity)));
                if (rows.Count != inputs.Length) throw new InvalidOperationException("Native ray audit result count differs from input count");
                surfaces.Add(Host.Object("surface", surface, "rays", rows));
                data.ClearData();
            }
            Host.Write("ray-audit.json", Host.Object("semantics",
                "Explicit normalized real-ray batch; aperture vignette and propagation error remain separate. Diagnostic continuation is never physical acceptance.",
                "wavelength", Host.Int("wavelength"), "removeVignettingFactors", Host.Request["removeVignettingFactors"],
                "entrancePupilDiameter", Host.Finite(entrancePupilDiameter),
                "entrancePupilPosition", Host.Finite(entrancePupilPosition),
                "fields", state, "rayAiming", Host.Properties(system.SystemData.RayAiming),
                "inputs", inputs, "launches", launches, "surfaces", surfaces));
        }
        finally
        {
            if (tool != null) tool.Close();
            for (int i = 0; i < original.Length; i++)
            {
                var f = fields.GetField(i + 1); var v = original[i];
                f.VDX = v[0]; f.VDY = v[1]; f.VCX = v[2]; f.VCY = v[3]; f.TAN = v[4];
            }
        }
    }
    private static double Number(Dictionary<string, object> p, string key) { return Convert.ToDouble(p[key]); }
}
