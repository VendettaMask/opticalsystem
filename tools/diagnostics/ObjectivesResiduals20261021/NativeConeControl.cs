// Offline, read-only licensed API controls. Compile with the formal ZemaxHost
// sources and the installed 2026 R1 API; never save a source lens or merit row.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeConeControl
{
    [STAThread]
    public static int Main(string[] args)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var request = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(args[0]));
        if (request.ContainsKey("inputDoubleBits"))
            foreach (var pair in (Dictionary<string, object>)request["inputDoubleBits"])
            {
                string[] parts = pair.Key.TrimStart('/').Split('/');
                object container = request;
                for (int i = 0; i < parts.Length - 1; i++)
                    container = container is IDictionary ? ((IDictionary)container)[parts[i]] : ((IList)container)[int.Parse(parts[i])];
                double value = BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse((string)pair.Value,
                    System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)));
                if (container is IDictionary) ((IDictionary)container)[parts[parts.Length - 1]] = value;
                else ((IList)container)[int.Parse(parts[parts.Length - 1])] = value;
            }
        string api = (string)request["zosApiPath"];
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        Host.Output = output;
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(request, api);
    }

    private static int Run(Dictionary<string, object> request, string api)
    {
        string output = Host.Output;
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new InvalidOperationException("API initialization failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new InvalidOperationException("Licensed 2026 R1 API required");
            Host.Write("environment.json", Host.Object("version", app.OpticStudioVersion, "license", app.LicenseStatus.ToString(), "validLicense", app.IsValidLicenseForAPI));
            var rows = new List<object>();
            foreach (Dictionary<string, object> job in (IEnumerable)request["jobs"])
            {
                string input = (string)job["input"];
                var system = app.PrimarySystem;
                if (!system.LoadFile(input, false)) throw new InvalidOperationException("Load failed: " + input);
                system.MCE.SetCurrentConfiguration(1);
                string originalUnit = system.SystemData.Units.LensUnits.ToString();
                if (originalUnit != "Millimeters")
                {
                    var scaling = system.Tools.OpenScale();
                    try
                    {
                        scaling.ScaleByUnits = true;
                        scaling.ScaleToUnit = ZOSAPI.Tools.General.ScaleToUnits.Millimeters;
                        scaling.RunAndWaitForCompletion();
                    }
                    finally { scaling.Close(); }
                    if (system.SystemData.Units.LensUnits.ToString() != "Millimeters") throw new InvalidOperationException("Unit scaling failed");
                }
                if (job.ContainsKey("apertureScale"))
                    system.SystemData.Aperture.ApertureValue *= Convert.ToDouble(job["apertureScale"]);
                if (job.ContainsKey("probePlaneDll"))
                {
                    int probeSurface = job.ContainsKey("probeSurface") ? Convert.ToInt32(job["probeSurface"]) : 1;
                    string incidentMaterial = system.LDE.GetSurfaceAt(probeSurface - 1).Material;
                    var plane = system.LDE.InsertNewSurfaceAt(probeSurface);
                    plane.Thickness = 0;
                    plane.Radius = 0;
                    plane.Material = incidentMaterial;
                    var type = plane.GetSurfaceTypeSettings(ZOSAPI.Editors.LDE.SurfaceType.UserDefined);
                    type.Filename = Path.GetFileName((string)job["probePlaneDll"]);
                    if (!type.IsValid || !plane.ChangeType(type)) throw new InvalidOperationException("Diagnostic probe DLL rejected");
                    plane.Comment = Path.GetFileName((string)job["probePlaneDll"]);
                    plane.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.Par1).DoubleValue = Convert.ToDouble(job["probeCaptureId"]);
                }
                system.UpdateStatus();
                if (job.ContainsKey("rayAuditRequest"))
                {
                    Host.Request = (Dictionary<string, object>)job["rayAuditRequest"];
                    Host.Output = Path.Combine(output, (string)job["id"]);
                    Directory.CreateDirectory(Host.Output);
                    Host.Write("request.json", job);
                    Host.Write("unit-normalization.json", Host.Object("originalUnit", originalUnit,
                        "convertedUnit", system.SystemData.Units.LensUnits.ToString(), "savedInput", false));
                    var indices = new List<object>();
                    for (int surface = 0; surface < system.LDE.NumberOfSurfaces; surface++)
                    {
                        var values = new double[system.SystemData.Wavelengths.NumberOfWavelengths];
                        int count = system.LDE.GetIndex(surface, values.Length, values);
                        indices.Add(Host.Object("surface", surface, "material", system.LDE.GetSurfaceAt(surface).Material,
                            "activeCurvature", system.MFE.GetOperandValue(MeritOperandType.CVVA, surface, 0, 0, 0, 0, 0, 0, 0),
                            "radius", double.IsInfinity(system.LDE.GetSurfaceAt(surface).Radius) ? (object)null : system.LDE.GetSurfaceAt(surface).Radius,
                            "count", count, "indices", values));
                    }
                    Host.Write("refractive-indices.json", indices);
                    if (job.ContainsKey("probeCapturePath")) File.WriteAllBytes((string)job["probeCapturePath"], new byte[0]);
                    NativeRayAuditCapture.Run(system);
                    if (job.ContainsKey("probeCapturePath"))
                        File.Copy((string)job["probeCapturePath"], Path.Combine(Host.Output, "probe-rays.bin"), false);
                    Host.Output = output;
                    Console.WriteLine(job["id"]);
                    continue;
                }
                if (job.ContainsKey("analysisRequest"))
                {
                    Host.Request = (Dictionary<string, object>)job["analysisRequest"];
                    Host.Output = Path.Combine(output, (string)job["id"]);
                    Directory.CreateDirectory(Host.Output);
                    Host.Write("request.json", job);
                    Host.Write("aperture.json", Host.Object("originalUnit", originalUnit,
                        "evaluatedUnit", system.SystemData.Units.LensUnits.ToString(),
                        "aperture", Host.Properties(system.SystemData.Aperture), "savedInput", false));
                    Host.Write("model.json", Host.Object("mode", system.Mode.ToString(),
                        "surfaceCount", system.LDE.NumberOfSurfaces, "configurationCount", system.MCE.NumberOfConfigurations,
                        "units", Host.Properties(system.SystemData.Units), "aperture", Host.Properties(system.SystemData.Aperture),
                        "rayAiming", Host.Properties(system.SystemData.RayAiming), "environment", Host.Properties(system.SystemData.Environment),
                        "fields", Enumerable.Range(1, system.SystemData.Fields.NumberOfFields).Select(i => Host.Object("number", i, "data", Host.Properties(system.SystemData.Fields.GetField(i)))).ToArray(),
                        "wavelengths", Enumerable.Range(1, system.SystemData.Wavelengths.NumberOfWavelengths).Select(i => Host.Object("number", i, "data", Host.Properties(system.SystemData.Wavelengths.GetWavelength(i)))).ToArray(),
                        "surfaces", Enumerable.Range(0, system.LDE.NumberOfSurfaces).Select(i => Host.Properties(system.LDE.GetSurfaceAt(i))).ToArray()));
                    if (originalUnit != "Millimeters")
                        Host.Write("unit-normalization.json", Host.Object("originalUnit", originalUnit, "convertedUnit", system.SystemData.Units.LensUnits.ToString(),
                            "method", "OpticStudio.Tools.OpenScale.ScaleByUnits", "savedInput", false));
                    var analysis = system.Analyses.New_Analysis_SettingsFirst(Host.EnumValue<AnalysisIDM>((string)Host.Request["analysisType"]));
                    try
                    {
                        var settings = analysis.GetSettings();
                        new ContractAdapter().Configure(settings);
                        Host.Write("captured-settings.json", Host.Properties(settings));
                        settings.SaveTo(Path.Combine(Host.Output, "settings.CFG"));
                        if (job.ContainsKey("probeCapturePath"))
                        {
                            Host.Write("probe-context.json", Host.Object("primaryWorkingFNumber", system.MFE.GetOperandValue(MeritOperandType.WFNO, 0, 0, 0, 0, 0, 0, 0, 0),
                                "entrancePupil", system.SystemData.Aperture.ApertureValue,
                                "probeSurface", Host.Properties(system.LDE.GetSurfaceAt(job.ContainsKey("probeSurface") ? Convert.ToInt32(job["probeSurface"]) : 1))));
                            File.WriteAllBytes((string)job["probeCapturePath"], new byte[0]);
                        }
                        var status = analysis.ApplyAndWaitForCompletion();
                        if (status != null && status.ErrorCode != ErrorType.Success) throw new InvalidOperationException(status.Text);
                        var result = analysis.GetResults();
                        Host.Write("data.json", typeof(Host).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { result }));
                        if (!result.GetTextFile(Path.Combine(Host.Output, "data.txt"))) throw new InvalidOperationException("Text export failed");
                        if (job.ContainsKey("probeCapturePath"))
                            File.Copy((string)job["probeCapturePath"], Path.Combine(Host.Output, "probe-rays.bin"), false);
                    }
                    finally { analysis.Close(); }
                    Host.Output = output;
                    Console.WriteLine(job["id"]);
                    continue;
                }
                var probe = system.MFE.AddOperand(); probe.ChangeType(MeritOperandType.WFNO);
                var schema = Enumerable.Range(2, 8).Select(i => Host.Object("column", i, "properties", Host.Properties(probe.GetCellAt(i)))).ToArray();
                double primaryWorkingFNumber = system.MFE.GetOperandValue(MeritOperandType.WFNO, 0, 0, 0, 0, 0, 0, 0, 0);
                var waves = new List<object>();
                for (int wave = 1; wave <= system.SystemData.Wavelengths.NumberOfWavelengths; wave++)
                {
                    var tool = system.Tools.OpenBatchRayTrace();
                    if (tool == null) throw new InvalidOperationException("Batch unavailable");
                    try
                    {
                        var coordinates = new List<double[]> { new[] { 0d, 0d }, new[] { 0d, 1d }, new[] { 0d, -1d }, new[] { 1d, 0d }, new[] { -1d, 0d } };
                        foreach (double diagonal in new[] { 1 / Math.Sqrt(2), Math.Sqrt(.5), (1 - 1e-12) / Math.Sqrt(2) })
                            foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
                                coordinates.Add(new[] { sx * diagonal, sy * diagonal });
                        var data = tool.CreateNormUnpol(coordinates.Count, RaysType.Real, system.LDE.NumberOfSurfaces - 1);
                        for (int i = 0; i < coordinates.Count; i++)
                            if (!data.AddRay(wave, 0, 0, coordinates[i][0], coordinates[i][1], OPDMode.CurrentAndChief))
                                throw new InvalidOperationException("Ray rejected");
                        tool.RunAndWaitForCompletion();
                        if (!data.StartReadingResults()) throw new InvalidOperationException("No results");
                        var rays = new List<object>();
                        int number, error, vignette;
                        double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                        while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z,
                            out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                            rays.Add(Host.Object("number", number, "error", error, "vignette", vignette,
                                "x", x, "y", y, "z", z, "l", l, "m", m, "n", n, "opd", opd, "intensity", intensity));
                        if (rays.Count != coordinates.Count) throw new InvalidOperationException("Incomplete rays");
                        waves.Add(Host.Object("wave", wave, "micrometers", system.SystemData.Wavelengths.GetWavelength(wave).Wavelength,
                            "primaryWorkingFNumber", primaryWorkingFNumber, "inputs", coordinates, "rays", rays));
                    }
                    finally { tool.Close(); }
                }
                rows.Add(Host.Object("id", job.ContainsKey("id") ? job["id"] : job["lens"],
                    "apertureScale", job.ContainsKey("apertureScale") ? job["apertureScale"] : 1d,
                    "lens", job["lens"], "input", input, "originalUnit", originalUnit,
                    "evaluatedUnit", system.SystemData.Units.LensUnits.ToString(), "savedInput", false, "waveSchema", schema, "waves", waves));
                Host.Write("cones.json", rows);
                Console.WriteLine(job["lens"]);
            }
            Host.Output = output;
            Host.Write("completion.json", Host.Object("completedUtc", DateTime.UtcNow.ToString("o"), "allRequestedCapturesComplete", true,
                "savedInput", false, "shutdownExitRecordedSeparately", true));
            return 0;
        }
        catch (Exception e) { Host.Write("error.json", Host.Object("error", e.ToString())); Console.Error.WriteLine(e); return 2; }
        finally { if (app != null) app.CloseApplication(); }
    }
}
