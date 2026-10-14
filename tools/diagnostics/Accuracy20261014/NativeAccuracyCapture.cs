// Offline native reference capture. Compiled against the licensed host's API;
// does not save the lens or supply product runtime computations.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeAccuracyCapture
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: <request.json> <fresh-output-directory>"); return 1; }
        if (Directory.Exists(args[1]) && Directory.EnumerateFileSystemEntries(args[1]).Any())
        { Console.Error.WriteLine("Capture output must be empty; existing evidence is preserved."); return 2; }
        Host.Output = args[1]; Directory.CreateDirectory(Host.Output);
        File.Copy(args[0], Path.Combine(Host.Output, "request.json"));
        Host.Request = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(args[0]));
        string api = (string)Host.Request["zosApiPath"];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(api);
    }
    private static int Run(string api)
    {
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new InvalidOperationException("API initialization failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null) throw new InvalidOperationException("No native application");
            Host.Write("environment.json", Host.Object("version", app.OpticStudioVersion, "major", app.ZOSMajorVersion,
                "minor", app.ZOSMinorVersion, "validLicense", app.IsValidLicenseForAPI, "license", app.LicenseStatus.ToString()));
            if (!app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new InvalidOperationException("Requires licensed OpticStudio 2026 R1");
            var system = app.PrimarySystem;
            if (!system.LoadFile((string)Host.Request["input"], false)) throw new InvalidOperationException("Load failed");
            system.MCE.SetCurrentConfiguration(1); system.UpdateStatus();
            Host.Write("model.json", Host.Object("fields", Enumerable.Range(1, system.SystemData.Fields.NumberOfFields)
                .Select(i => Host.Properties(system.SystemData.Fields.GetField(i))).ToArray(), "aperture", Host.Properties(system.SystemData.Aperture),
                "rayAiming", Host.Properties(system.SystemData.RayAiming)));
            if (Host.Request.ContainsKey("signedAxisControls"))
            {
                var axisRows = new List<object>();
                double[,] coordinates = { { 0, 0 }, { 0, 1 }, { 0, -1 }, { 1, 0 }, { -1, 0 }, { .3, .4 }, { -.3, -.4 } };
                foreach (string code in new[] { "FCGS", "FCGT" })
                    for (int wave = 1; wave <= system.SystemData.Wavelengths.NumberOfWavelengths; wave++)
                        for (int i = 0; i < coordinates.GetLength(0); i++)
                        {
                            double hx = coordinates[i, 0], hy = coordinates[i, 1];
                            double value = system.MFE.GetOperandValue(Host.EnumValue<MeritOperandType>(code), 0, wave, hx, hy, 0, 0, 0, 0);
                            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Nonfinite signed curvature");
                            axisRows.Add(Host.Object("code", code, "wave", wave, "hx", hx, "hy", hy, "value", value));
                        }
                Host.Write("signed-curvature-operands.json", axisRows);
                return 0;
            }
            var rows = new List<object>();
            foreach (string code in new[] { "EFNO", "RELI" })
            {
                var type = Host.EnumValue<MeritOperandType>(code);
                var schema = system.MFE.AddOperand(); schema.ChangeType(type);
                Host.Write(code + "-schema.json", Enumerable.Range(2, 8).Select(i => Host.Object("column", i, "cell", Host.Properties(schema.GetCellAt(i)))).ToArray());
                foreach (int density in new[] { 5, 10, 20, 40 })
                    for (int wave = 1; wave <= system.SystemData.Wavelengths.NumberOfWavelengths; wave++)
                        for (int field = 1; field <= system.SystemData.Fields.NumberOfFields; field++)
                        {
                            double value = system.MFE.GetOperandValue(type, density, wave, field, 0, 0, 0, 0, 0);
                            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Nonfinite operand");
                            rows.Add(Host.Object("code", code, "density", density, "wave", wave, "field", field, "polarization", 0, "value", value));
                        }
            }
            Host.Write("operands.json", rows);
            var curvatureRows = new List<object>();
            foreach (string code in new[] { "FCGS", "FCGT" })
            {
                var type = Host.EnumValue<MeritOperandType>(code);
                var schema = system.MFE.AddOperand(); schema.ChangeType(type);
                Host.Write(code + "-schema.json", Enumerable.Range(2, 8).Select(i => Host.Object("column", i, "cell", Host.Properties(schema.GetCellAt(i)))).ToArray());
                for (int wave = 1; wave <= system.SystemData.Wavelengths.NumberOfWavelengths; wave++)
                    for (int index = 0; index <= 100; index++)
                    {
                        double hy = index / 100.0;
                        double value = system.MFE.GetOperandValue(type, 0, wave, 0, hy, 0, 0, 0, 0);
                        if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Nonfinite curvature operand");
                        curvatureRows.Add(Host.Object("code", code, "wave", wave, "hx", 0, "hy", hy, "value", value));
                    }
            }
            Host.Write("curvature-operands.json", curvatureRows);
            var rayInputs = (from hy in new[] { 0.0, 0.5, 1.0 } from py in new[] { -1.0, -.5, 0, .5, 1.0 }
                select new { hy, py }).ToArray();
            var tool = system.Tools.OpenBatchRayTrace();
            try
            {
                var data = tool.CreateNormUnpol(rayInputs.Length, RaysType.Real, system.LDE.NumberOfSurfaces - 1);
                int rayWave = Math.Min(2, system.SystemData.Wavelengths.NumberOfWavelengths);
                foreach (var ray in rayInputs) data.AddRay(rayWave, 0, ray.hy, 0, ray.py, OPDMode.None);
                tool.RunAndWaitForCompletion(); data.StartReadingResults();
                var rayRows = new List<object>();
                int number, error, vignette; double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z,
                    out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                    rayRows.Add(Host.Object("number", number, "error", error, "vignette", vignette, "y", y, "m", m, "n", n, "intensity", intensity));
                Host.Write("rays.json", Host.Object("wave", rayWave, "inputs", rayInputs, "rows", rayRows));
            }
            finally { tool.Close(); }
            if (Host.Request.ContainsKey("illuminationCfg"))
            {
                var analysis = system.Analyses.New_Analysis_SettingsFirst(AnalysisIDM.RelativeIllumination);
                try
                {
                    if (!analysis.GetSettings().LoadFrom((string)Host.Request["illuminationCfg"])) throw new InvalidOperationException("CFG load failed");
                    if (!analysis.GetSettings().SaveTo(Path.Combine(Host.Output, "illumination.cfg"))) throw new InvalidOperationException("CFG export failed");
                    analysis.ApplyAndWaitForCompletion();
                    Host.Write("illumination.json", typeof(Host).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { analysis.GetResults() }));
                    analysis.GetResults().GetTextFile(Path.Combine(Host.Output, "illumination.txt"));
                }
                finally { analysis.Close(); }
            }
            return 0;
        }
        catch (Exception e) { Host.Write("error.json", Host.Object("error", e.ToString())); Console.Error.WriteLine(e); return 2; }
        finally { if (app != null) app.CloseApplication(); }
    }
}
