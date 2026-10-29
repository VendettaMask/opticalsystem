using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Settings.RayTracing;
using ZOSAPI.Tools.RayTrace;

internal static class NativeNormalsCapture
{
    internal static void Run(IOpticalSystem source, IZOSAPI_Application app)
    {
        source.NCE.SetActiveSurface(6);
        var system = app.CreateNewSystem(SystemType.NonSequential);
        system.SystemData.Units.LensUnits = source.SystemData.Units.LensUnits;
        var originalWave = source.SystemData.Wavelengths.GetWavelength(2);
        system.SystemData.Wavelengths.GetWavelength(1).Wavelength = originalWave.Wavelength;
        system.SystemData.NonSequentialData.GlueDistanceInLensUnits = source.SystemData.NonSequentialData.GlueDistanceInLensUnits;
        if (system.NCE.CopyObjectsFrom(source.NCE, 1, 1, 1) != 1) throw new InvalidOperationException("Cannot copy original native polygon group");
        Host.Write("extracted-group.json", Host.Object("object", Host.Properties(system.NCE.GetObjectAt(1)),
            "sourceObject", Host.Properties(source.NCE.GetObjectAt(1)), "wavelength", originalWave.Wavelength,
            "purpose", "Independent extracted group normal readback", "originalFullSystemRequest", false));
        var analysis = system.Analyses.New_Analysis_SettingsFirst(AnalysisIDM.NSCSingleRayTrace);
        if (analysis == null) throw new InvalidOperationException("NSC normal analysis unavailable");
        try
        {
            var settings = (IAS_NSCSingleRayTrace)analysis.GetSettings();
            settings.Wavelength.SetWavelengthNumber(1);
            settings.RefObject = 0;
            settings.RaySourceX = 0; settings.RaySourceY = 0;
            settings.RaySourceZ = -source.LDE.GetSurfaceAt(5).Thickness;
            settings.RaySourceL = 0; settings.RaySourceM = 0; settings.RaySourceN = 1;
            settings.UsePolarization = false; settings.SplitNSCRays = false; settings.ScatterNSCRays = false;
            settings.ShowXYZ = true; settings.ShowLMN = true; settings.ShowNormal = true; settings.ShowPath = true;
            settings.DecimalPrecision = 17;
            settings.SaveRaysFile = "";
            Host.Write("normal-settings.json", Host.Properties(settings));
            var applied = analysis.ApplyAndWaitForCompletion();
            if (applied != null && applied.ErrorCode != ErrorType.Success) throw new InvalidOperationException(applied.Text);
            var result = analysis.GetResults();
            bool saved = result.GetTextFile(Path.Combine(Host.Output, "normal-data.txt"));
            Host.Write("normal-result.json", Host.Object("saved", saved, "data", Host.Properties(result.NSCSingleRayTraceData),
                "messages", Enumerable.Range(0, result.NumberOfMessages).Select(i => Host.Properties(result.GetMessageAt(i))).ToArray()));
            var data = result.NSCSingleRayTraceData;
            if (!saved || data == null || !data.IsValid || data.NumberOfSegments < 8) throw new InvalidOperationException("Valid native normal data unavailable");
            var segments = new List<object>();
            for (int segment = 0; segment <= data.NumberOfSegments; segment++)
            {
                int level, parent, hit, face, inside, xybin, lmbin;
                RayStatus status;
                double x, y, z, l, m, n, exr, exi, eyr, eyi, ezr, ezi, intensity, length, nx, ny, nz, index, start, phaseOf, phaseAt;
                if (!data.ReadSegmentFull(segment, out level, out parent, out hit, out face, out inside, out status,
                    out x, out y, out z, out l, out m, out n, out exr, out exi, out eyr, out eyi, out ezr, out ezi,
                    out intensity, out length, out xybin, out lmbin, out nx, out ny, out nz, out index, out start, out phaseOf, out phaseAt)) continue;
                segments.Add(Host.Object("segment", segment, "hitObject", hit, "hitFace", face, "inside", inside, "status", status.ToString(),
                    "x", x, "y", y, "z", z, "l", l, "m", m, "n", n, "normalX", nx, "normalY", ny, "normalZ", nz, "refractiveIndex", index));
            }
            Host.Write("native-normals.json", segments);
        }
        finally { analysis.Close(); }
    }
}
