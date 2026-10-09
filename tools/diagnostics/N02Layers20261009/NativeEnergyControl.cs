// Diagnostic ZOS-API client, compiled with the repository host helpers and
// the licensed machine's own API assemblies. No optical formula is evaluated here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Settings;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeEnergyControl
{
    [STAThread]
    public static int Main(string[] args)
    {
        Host.Output = args[1]; Directory.CreateDirectory(Host.Output);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Host.Request = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(args[0]));
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
            if (app == null || !app.IsValidLicenseForAPI) throw new InvalidOperationException("API license unavailable");
            Host.Write("environment.json", Host.Object("major", app.ZOSMajorVersion, "minor", app.ZOSMinorVersion,
                "servicePack", app.ZOSSPVersion, "opticStudioVersion", app.OpticStudioVersion,
                "licenseStatus", app.LicenseStatus.ToString(), "validLicense", app.IsValidLicenseForAPI));
            if (app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1) throw new InvalidOperationException("Wrong native version");
            var system = app.PrimarySystem;
            if (!system.LoadFile((string)Host.Request["input"], false)) throw new InvalidOperationException("Native input load failed");
            system.MCE.SetCurrentConfiguration(1);
            system.UpdateStatus();
            var nested = new Dictionary<string, object>();
            foreach (var property in system.SystemData.GetType().GetInterfaces().SelectMany(t => t.GetProperties())
                .Where(p => p.GetIndexParameters().Length == 0).GroupBy(p => p.Name).Select(g => g.First()))
            {
                try { nested[property.Name] = Host.Properties(property.GetValue(system.SystemData, null)); }
                catch (Exception e) { nested[property.Name] = e.Message; }
            }
            Host.Write("system-data-settings.json", nested);
            if ((string)Host.Request["adapter"] == "huygens-methods")
            {
                CaptureHuygensMethods(system);
                return 0;
            }
            if ((string)Host.Request["adapter"] == "pupil")
            {
                CapturePupil(system);
                return 0;
            }
            var schemaRow = system.MFE.AddOperand(); schemaRow.ChangeType(MeritOperandType.DENF);
            Host.Write("denf-column-schema.json", Enumerable.Range(2, 8).Select(i =>
                Host.Object("column", i, "properties", Host.Properties(schemaRow.GetCellAt(i)))).ToArray());
            if ((string)Host.Request["adapter"] == "schema") return 0;
            string[] types = { "Integer", "Integer", "Integer", "Double", "Integer", "Integer", "Integer", "Double" };
            for (int i = 0; i < types.Length; i++)
                if (schemaRow.GetCellAt(2 + i).DataType.ToString() != types[i])
                    throw new InvalidOperationException("Native DENF column contract changed");
            // API columns 2..9: Samp, Wave, Field, Dist, Type, Refp, I Samp, I Delta.
            // Refp=1 uses the FFT centroid; no merit rows or source files are saved.
            var rows = new List<object>();
            foreach (int type in new[] { 1, 2, 3, 4 })
                foreach (double radius in new[] { 0.0125, 0.025, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.5, 1.0, 2.0, 3.0, 4.0 })
                {
                    double value = system.MFE.GetOperandValue(MeritOperandType.DENF, 1, 1, 1, radius, type, 1, 1, 0);
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Nonfinite native DENF");
                    rows.Add(Host.Object("sampling", 1, "wavelength", 1, "field", 1, "type", type,
                        "reference", 1, "distanceMicrometers", radius, "fraction", value));
                }
            Host.Write("denf.json", Host.Object("operand", "DENF", "configuration", 1, "rows", rows,
                "semantics", "Native scalar controls; no fitted scale, changed lens, changed sampling or saved merit function."));
            return 0;
        }
        catch (Exception e) { Host.Write("error.json", Host.Object("error", e.ToString())); Console.Error.WriteLine(e); return 2; }
        finally { if (app != null) app.CloseApplication(); }
    }

    private static void CaptureHuygensMethods(IOpticalSystem system)
    {
        string root = Host.Output;
        var advanced = system.SystemData.Advanced;
        var property = advanced.GetType().GetInterfaces().SelectMany(t => t.GetProperties())
            .First(p => p.Name == "HuygensIntegralMethod");
        var original = property.GetValue(advanced, null);
        string[] methods = Enum.GetNames(property.PropertyType);
        Host.Write("method-schema.json", Host.Object("property", property.Name,
            "original", original.ToString(), "values", methods));
        var capture = typeof(Host).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        var selectors = typeof(Host).GetMethod("Selectors", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            foreach (string name in methods)
            {
                IA_ analysis = null;
                try
                {
                    property.SetValue(advanced, Enum.Parse(property.PropertyType, name), null);
                    if (property.GetValue(advanced, null).ToString() != name) throw new InvalidOperationException("Method rejected");
                    system.UpdateStatus();
                    Host.Output = Path.Combine(root, name); Directory.CreateDirectory(Host.Output);
                    Host.Write("advanced-settings.json", Host.Properties(advanced));
                    analysis = system.Analyses.New_Analysis_SettingsFirst(Host.EnumValue<AnalysisIDM>((string)Host.Request["analysisType"]));
                    var settings = analysis.GetSettings(); new ContractAdapter().Configure(settings);
                    if (!settings.SaveTo(Path.Combine(Host.Output, "settings.CFG"))) throw new InvalidOperationException("Settings export failed");
                    Host.Write("captured-settings.json", Host.Object("properties", Host.Properties(settings),
                        "selectors", selectors.Invoke(null, new object[] { settings }), "method", name, "request", Host.Request));
                    var status = analysis.ApplyAndWaitForCompletion();
                    if (status != null && status.ErrorCode != ErrorType.Success) throw new InvalidOperationException(status.Text);
                    var results = analysis.GetResults();
                    for (int i = 0; i < results.NumberOfMessages; i++)
                        if (results.GetMessageAt(i).ErrorCode != ErrorType.Success) throw new InvalidOperationException(results.GetMessageAt(i).Text);
                    Host.Write("data.json", capture.Invoke(null, new object[] { results }));
                    if (!results.GetTextFile(Path.Combine(Host.Output, "data.txt"))) throw new InvalidOperationException("Text export failed");
                }
                finally { if (analysis != null) analysis.Close(); Host.Output = root; }
            }
        }
        finally { property.SetValue(advanced, original, null); Host.Output = root; }
    }

    private static void CapturePupil(IOpticalSystem system)
    {
        var inputs = ((System.Collections.IEnumerable)Host.Request["rayAuditInputs"])
            .Cast<Dictionary<string, object>>().ToArray();
        var tool = system.Tools.OpenBatchRayTrace();
        if (tool == null) throw new InvalidOperationException("Batch ray trace unavailable");
        try
        {
            var data = tool.CreateNormUnpol(inputs.Length, RaysType.Real, system.LDE.NumberOfSurfaces - 1);
            foreach (var input in inputs)
                if (!data.AddRay(1, 0, 0, Convert.ToDouble(input["px"]), Convert.ToDouble(input["py"]), OPDMode.CurrentAndChief))
                    throw new InvalidOperationException("Pupil ray rejected");
            tool.RunAndWaitForCompletion();
            if (!data.StartReadingResults()) throw new InvalidOperationException("Pupil results unavailable");
            var rows = new List<object>();
            int number, error, vignette;
            double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
            while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z,
                out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                rows.Add(Host.Object("number", number, "error", error, "vignette", vignette,
                    "x", Host.Finite(x), "y", Host.Finite(y), "z", Host.Finite(z),
                    "l", Host.Finite(l), "m", Host.Finite(m), "n", Host.Finite(n),
                    "opd", Host.Finite(opd), "intensity", Host.Finite(intensity)));
            if (rows.Count != inputs.Length) throw new InvalidOperationException("Pupil output count mismatch");
            Host.Write("pupil-grid.json", Host.Object("wavelength", 1, "configuration", 1,
                "field", 1, "opdMode", "CurrentAndChief", "inputs", inputs, "rows", rows,
                "semantics", "Native normalized unpolarized final-surface ray controls. Positive vignetting and propagation errors remain explicit; raw OPD unit/convention must be verified before phase certification."));
        }
        finally { tool.Close(); }
    }
}
