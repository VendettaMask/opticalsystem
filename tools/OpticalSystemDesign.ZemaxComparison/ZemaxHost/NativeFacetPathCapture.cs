// Independent native diagnostic. Never supplies product geometry or acceptance masks.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.LDE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeFacetPathCapture
{
    internal static void Run(IOpticalSystem system)
    {
        int group = Host.Int("groupSurface");
        var previous = system.LDE.GetSurfaceAt(group - 1);
        if (group != 6 || previous.Type != SurfaceType.Standard || system.LDE.GetSurfaceAt(group).Type != SurfaceType.NonSequential)
            throw new InvalidOperationException("Diagnostic requires the declared unrotated binocular group entry");
        if (Host.Request.ContainsKey("diagnosticGlueDistance"))
        {
            double original = system.SystemData.NonSequentialData.GlueDistanceInLensUnits;
            double requested = Host.Number("diagnosticGlueDistance");
            if (requested < 1e-10 || requested > 1e-3) throw new InvalidOperationException("Bounded glue control required");
            system.SystemData.NonSequentialData.GlueDistanceInLensUnits = requested;
            Host.Write("nsc-settings-control.json", Host.Object("originalGlueDistance", original, "requested", requested,
                "actual", system.SystemData.NonSequentialData.GlueDistanceInLensUnits, "savedInput", false,
                "originalReferenceRequest", false));
        }
        var inputs = ((IEnumerable)Host.Request["pointInputs"]).Cast<Dictionary<string, object>>().ToArray();
        if (inputs.Length < 1 || inputs.Length > 256) throw new InvalidOperationException("Bounded probes required");
        var tool = system.Tools.OpenBatchRayTrace();
        try
        {
            var launches = new List<object>();
            var data = tool.CreateNSC(inputs.Length, 128, 0);
            foreach (var point in inputs)
            {
                int error, vignette;
                double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                if (!tool.SingleRayNormUnpol(RaysType.Real, group - 1, Host.Int("wavelength"),
                    N(point, "hx"), N(point, "hy"), N(point, "px"), N(point, "py"), false,
                    out error, out vignette, out x, out y, out z, out l, out m, out n,
                    out nx, out ny, out nz, out opd, out intensity) || error != 0 || vignette != 0)
                    throw new InvalidOperationException("Cannot obtain physical group-entry ray");
                // The preceding standard surface and group entry share orientation.
                z -= previous.Thickness;
                launches.Add(Host.Object("input", point, "x", x, "y", y, "z", z, "l", l, "m", m, "n", n));
                if (!data.AddRay(Host.Int("wavelength"), group, NSCTraceOptions.None, x, y, z, l, m, n, 0, 0, 0, 0, 0, 0, 0))
                    throw new InvalidOperationException("NSC probe rejected");
            }
            tool.RunAndWaitForCompletion();
            if (!data.StartReadingResults()) throw new InvalidOperationException("NSC results unavailable");
            int number, rayError, wave, count;
            var rays = new List<object>();
            bool polygonControlValid = true;
            while (data.ReadNextResult(out number, out rayError, out wave, out count))
            {
                if (Host.Request.ContainsKey("diagnosticPolygonTriangles") && (rayError != 0 || count < 8)) polygonControlValid = false;
                var segments = new List<object>();
                int level, parent, hit, inside;
                double x, y, z, l, m, n, exr, exi, eyr, eyi, ezr, ezi, intensity, length;
                while (data.ReadNextSegment(out level, out parent, out hit, out inside, out x, out y, out z,
                    out l, out m, out n, out exr, out exi, out eyr, out eyi, out ezr, out ezi, out intensity, out length))
                    segments.Add(Host.Object("level", level, "parent", parent, "hitObject", hit, "insideOf", inside,
                        "x", x, "y", y, "z", z, "l", l, "m", m, "n", n, "intensity", intensity, "pathLength", length));
                rays.Add(Host.Object("number", number, "error", rayError, "wavelength", wave, "segmentCount", count, "segments", segments));
            }
            if (rays.Count != inputs.Length) throw new InvalidOperationException("Incomplete NSC result count");
            Host.Write("nsc-paths.json", Host.Object("configuration", Host.Int("configuration"), "groupSurface", group,
                "launchFrame", "NSC group entry; previous local z minus previous thickness", "launches", launches, "rays", rays,
                "savedInput", false, "internalFacetIdsAvailable", false));
            if (!polygonControlValid) throw new InvalidOperationException("Explicit-triangle control did not trace the original physical path");
        }
        finally { tool.Close(); }
    }
    private static double N(Dictionary<string, object> point, string name) { return Convert.ToDouble(point[name]); }
}
