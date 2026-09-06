using System;
using System.Collections;
using System.Collections.Generic;
using ZOSAPI;
using ZOSAPI.Tools.RayTrace;

// These Workbench scans do not have the same inputs as the three native IHT curves.
// Compare explicit native real-ray coordinates/directions with the same input list instead.
internal static class AngleRayCapture
{
    internal static void Add(IOpticalSystem system, Dictionary<string, object> raw)
    {
        string fieldType = (string)Host.Request["fieldDefinition"];
        if (fieldType != "ObjectHeight" && fieldType != "Angle")
            throw new CaptureFailure("UnsupportedFieldMapping", "Native angle-ray scan requires explicit object-height or angle fields");
        var inputs = new List<double[]>();
        if ((string)Host.Request["canonicalAnalysisKey"] == "Angle vs Image Height - Through Pupil")
            for (int i = 0; i < 33; i++) inputs.Add(new[] { 0d, 0d, 0d, -1 + i / 16d });
        else
        {
            double radius = Convert.ToDouble(Host.Request["maximumFieldRadius"]);
            foreach (IList f in (IEnumerable)Host.Request["definedFields"])
                inputs.Add(new[] { radius == 0 ? 0 : Convert.ToDouble(f[0]) / radius, radius == 0 ? 0 : Convert.ToDouble(f[1]) / radius, 0d, 0d });
        }
        var tool = system.Tools.OpenBatchRayTrace();
        try
        {
            var data = tool.CreateNormUnpol(inputs.Count, RaysType.Real, system.LDE.NumberOfSurfaces - 1);
            foreach (var p in inputs) data.AddRay(Host.Int("wavelength"), p[0], p[1], p[2], p[3], OPDMode.None);
            tool.RunAndWaitForCompletion(); data.StartReadingResults();
            var rows = new List<object>();
            int number, error, vignette; double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
            while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z, out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                rows.Add(Host.Object("number", number, "error", error, "vignette", vignette, "y", Host.Finite(y), "m", Host.Finite(m)));
            if (rows.Count != inputs.Count) throw new InvalidOperationException("Native batch-ray count differs from requested input list");
            raw["angleRayInputs"] = inputs; raw["angleRays"] = rows;
            raw["angleRaySemantics"] = "IBatchRayTrace normalized real rays; image-local Y and outgoing M; selected primary wavelength; no IHT curve substitution";
        }
        finally { tool.Close(); }
    }
}
