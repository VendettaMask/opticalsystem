using System;
using System.Collections.Generic;
using ZOSAPI;
using ZOSAPI.Editors.LDE;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class GrinFirstOrderControl
{
    internal static void Run(IOpticalSystem system)
    {
        var entrance = system.LDE.GetSurfaceAt(1);
        var step = entrance.GetSurfaceCell(SurfaceColumn.Par1);
        var originalStep = step.DoubleValue;
        var objectDistance = system.LDE.GetSurfaceAt(0).Thickness;
        var aperture = system.SystemData.Aperture;
        var launch = new List<double[]>();
        foreach (var h in new[] { .02, .01, .005, .0025, .00125, .000625 })
            foreach (var sign in new[] { -1, 1 })
                launch.Add(new[] { sign * h, 0.0 });
        foreach (var slope in new[] { .01, .005, .0025, .00125, .000625 })
            foreach (var sign in new[] { -1, 1 })
                launch.Add(new[] { -objectDistance * sign * slope, sign * slope });
        var marginalSlope = aperture.ApertureValue / Math.Sqrt(1 - aperture.ApertureValue * aperture.ApertureValue);
        launch.Add(new[] { 0.0, -marginalSlope });
        launch.Add(new[] { 0.0, marginalSlope });
        var records = new List<object>();
        foreach (var maximumStep in new[] { originalStep, .00625, .003125, .0015625 })
        {
            step.DoubleValue = maximumStep;
            system.UpdateStatus();
            if (step.DoubleValue != maximumStep) throw new InvalidOperationException("Step readback differs");
            var efl = system.MFE.GetOperandValue(MeritOperandType.EFFL, 0, 0, 0, 0, 0, 0, 0, 0);
            var epd = system.MFE.GetOperandValue(MeritOperandType.EPDI, 0, 0, 0, 0, 0, 0, 0, 0);
            var surfaces = new List<object>();
            var tool = system.Tools.OpenBatchRayTrace();
            if (tool == null) throw new InvalidOperationException("Batch ray trace unavailable");
            try
            {
                foreach (var surface in new[] { 1, 2 })
                {
                    var data = tool.CreateDirectUnpol(launch.Count, RaysType.Real, 0, surface);
                    foreach (var ray in launch)
                    {
                        double n = 1 / Math.Sqrt(1 + ray[1] * ray[1]);
                        if (!data.AddRay(1, 0, ray[0], 0, 0, ray[1] * n, n))
                            throw new InvalidOperationException("Direct ray rejected");
                    }
                    tool.RunAndWaitForCompletion();
                    if (!data.StartReadingResults()) throw new InvalidOperationException("No ray results");
                    var rows = new List<object>();
                    int number, error, vignette;
                    double x, y, z, l, m, nOut, nx, ny, nz, intensity;
                    while (data.ReadNextResult(out number, out error, out vignette,
                        out x, out y, out z, out l, out m, out nOut, out nx, out ny, out nz, out intensity))
                        rows.Add(Host.Object("number", number, "error", error, "vignette", vignette,
                            "y", Host.Finite(y), "m", Host.Finite(m), "n", Host.Finite(nOut)));
                    if (rows.Count != launch.Count) throw new InvalidOperationException("Incomplete ray results");
                    surfaces.Add(Host.Object("surface", surface, "rays", rows));
                    data.ClearData();
                }
            }
            finally { tool.Close(); }
            records.Add(Host.Object("maximumStep", step.DoubleValue, "effl", Host.Finite(efl),
                "entrancePupilDiameter", Host.Finite(epd), "surfaces", surfaces));
        }
        Host.Write("direct-grin.json", Host.Object("originalStep", originalStep, "wavelength", 1,
            "objectDistance", objectDistance, "launchColumns", new[] { "Y", "dy/dz" }, "launch", launch,
            "rodLength", entrance.Thickness, "surface1BaseIndex", entrance.GetSurfaceCell(SurfaceColumn.Par2).DoubleValue,
            "surface1RadialQuadratic", entrance.GetSurfaceCell(SurfaceColumn.Par3).DoubleValue,
            "objectNa", aperture.ApertureValue, "records", records, "savedInput", false));
    }
}
