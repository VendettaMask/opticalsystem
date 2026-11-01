// A separate face group for each unchanged triangle makes HitFace an explicit
// diagnostic tag, not an inference from a nearly equal normal. Never a product input.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Settings.RayTracing;
using ZOSAPI.Tools.RayTrace;

internal static class NativeTaggedFacetCapture
{
    internal static void Run(IOpticalSystem source, IZOSAPI_Application app)
    {
        string mode = (string)Host.Request["diagnosticPolygonTriangles"];
        if (mode != "02-tagged" && mode != "02-tagged-reverse") throw new InvalidOperationException("Explicit triangle tags required");
        if (Host.Int("groupSurface") != 6) throw new InvalidOperationException("Frozen binocular group required");
        var inputs = ((IEnumerable)Host.Request["pointInputs"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length < 1 || inputs.Length > 64) throw new InvalidOperationException("One to sixty-four bounded probes required");
        var launches = new List<Dictionary<string, object>>();
        var tool = source.Tools.OpenBatchRayTrace();
        try
        {
            foreach (var point in inputs)
            {
                int error, vignette;
                double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                if (!tool.SingleRayNormUnpol(RaysType.Real, 5, Host.Int("wavelength"),
                    N(point, "hx"), N(point, "hy"), N(point, "px"), N(point, "py"), false,
                    out error, out vignette, out x, out y, out z, out l, out m, out n,
                    out nx, out ny, out nz, out opd, out intensity) || error != 0 || vignette != 0)
                    throw new InvalidOperationException("Native sequential entry ray failed");
                launches.Add(Host.Object("input", point, "x", x, "y", y, "z", z - source.LDE.GetSurfaceAt(5).Thickness,
                    "l", l, "m", m, "n", n));
            }
        }
        finally { tool.Close(); }
        source.NCE.SetActiveSurface(6);
        var system = app.CreateNewSystem(SystemType.NonSequential);
        system.SystemData.Units.LensUnits = source.SystemData.Units.LensUnits;
        system.SystemData.Wavelengths.GetWavelength(1).Wavelength = source.SystemData.Wavelengths.GetWavelength(Host.Int("wavelength")).Wavelength;
        system.SystemData.NonSequentialData.GlueDistanceInLensUnits = source.SystemData.NonSequentialData.GlueDistanceInLensUnits;
        if (system.NCE.CopyObjectsFrom(source.NCE, 1, 1, 1) != 1) throw new InvalidOperationException("Cannot copy tagged polygon group");
        var analysis = system.Analyses.New_Analysis_SettingsFirst(AnalysisIDM.NSCSingleRayTrace);
        if (analysis == null) throw new InvalidOperationException("Native NSC trace unavailable");
        var rays = new List<object>();
        try
        {
            var settings = (IAS_NSCSingleRayTrace)analysis.GetSettings();
            settings.Wavelength.SetWavelengthNumber(1);
            settings.RefObject = 0;
            settings.UsePolarization = false; settings.SplitNSCRays = false; settings.ScatterNSCRays = false;
            settings.ShowXYZ = true; settings.ShowLMN = true; settings.ShowNormal = true; settings.ShowPath = true;
            settings.DecimalPrecision = 17; settings.SaveRaysFile = "";
            for (int i = 0; i < launches.Count; i++)
            {
                var launch = launches[i];
                settings.RaySourceX = N(launch, "x"); settings.RaySourceY = N(launch, "y"); settings.RaySourceZ = N(launch, "z");
                settings.RaySourceL = N(launch, "l"); settings.RaySourceM = N(launch, "m"); settings.RaySourceN = N(launch, "n");
                var applied = analysis.ApplyAndWaitForCompletion();
                if (applied != null && applied.ErrorCode != ErrorType.Success) throw new InvalidOperationException(applied.Text);
                var result = analysis.GetResults();
                var data = result.NSCSingleRayTraceData;
                if (data == null || !data.IsValid || data.NumberOfSegments < 8) throw new InvalidOperationException("Valid tagged trace unavailable");
                if (!result.GetTextFile(Path.Combine(Host.Output, "ray-" + i.ToString("D2") + ".txt"))) throw new IOException("Native text capture failed");
                var segments = new List<object>();
                for (int segment = 0; segment <= data.NumberOfSegments; segment++)
                {
                    int level, parent, hit, face, inside, xybin, lmbin;
                    RayStatus status;
                    double x, y, z, l, m, n, exr, exi, eyr, eyi, ezr, ezi, intensity, length, nx, ny, nz, index, start, phaseOf, phaseAt;
                    if (!data.ReadSegmentFull(segment, out level, out parent, out hit, out face, out inside, out status,
                        out x, out y, out z, out l, out m, out n, out exr, out exi, out eyr, out eyi, out ezr, out ezi,
                        out intensity, out length, out xybin, out lmbin, out nx, out ny, out nz, out index, out start, out phaseOf, out phaseAt)) continue;
                    if (hit == 1 && (face < 0 || face >= 16)) throw new InvalidOperationException("Triangle face tag unavailable");
                    segments.Add(Host.Object("segment", segment, "hitObject", hit, "triangleTag", hit == 1 ? (object)face : null,
                        "inside", inside, "status", status.ToString(), "x", x, "y", y, "z", z, "l", l, "m", m, "n", n,
                        "normalX", nx, "normalY", ny, "normalZ", nz, "refractiveIndex", index));
                }
                if (segments.Count != data.NumberOfSegments) throw new InvalidOperationException("Incomplete tagged segments");
                rays.Add(Host.Object("probe", i, "launch", launch, "segments", segments));
                Host.Write("tagged-paths.json", Host.Object("configuration", Host.Int("configuration"), "mode", mode,
                    "tagMeaning", "Explicit one-triangle-per-face-group in diagnostic POB", "originalFullSystemRequest", false,
                    "rays", rays));
            }
        }
        finally { analysis.Close(); }
    }
    private static double N(Dictionary<string, object> point, string key) { return Convert.ToDouble(point[key]); }
}
