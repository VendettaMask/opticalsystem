using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Editors.LDE;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeGridProbe
{
    private static readonly int[] Densities = { 5, 10, 15, 16, 17, 18, 19, 20, 21, 25, 30, 40, 100, 101, 128 };
    [STAThread]
    public static int Main(string[] args)
    {
        Host.Output = args[2];
        if (Directory.Exists(Host.Output) && Directory.EnumerateFileSystemEntries(Host.Output).Any()) return 2;
        Directory.CreateDirectory(Host.Output);
        string api = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(api, args[1]);
    }
    private static int Run(string api, string input)
    {
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new Exception("Initialize failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            Host.Write("environment.json", Host.Object("version", app.OpticStudioVersion, "major", app.ZOSMajorVersion,
                "minor", app.ZOSMinorVersion, "validLicense", app.IsValidLicenseForAPI, "license", app.LicenseStatus.ToString()));
            if (!app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1) throw new Exception("Wrong environment");
            var s = app.PrimarySystem;
            foreach (double width in new[] { 0.0, .3, .5, .7 })
            {
                if (!s.LoadFile(input, false)) throw new Exception("Load failed");
                string name = "square-" + width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string folder = Path.Combine(Host.Output, name); Directory.CreateDirectory(folder);
                if (width > 0)
                {
                    var aperture = s.LDE.GetSurfaceAt(1).ApertureData;
                    var settings = aperture.CreateApertureTypeSettings(SurfaceApertureTypes.RectangularAperture);
                    settings._S_RectangularAperture.XHalfWidth = width;
                    settings._S_RectangularAperture.YHalfWidth = width;
                    if (!aperture.ChangeApertureTypeSettings(settings)) throw new Exception("Aperture update failed");
                }
                s.UpdateStatus();
                Host.Write(name + "-model-before.json", Host.Object("aperture", Host.Properties(s.SystemData.Aperture),
                    "stop", Host.Properties(s.LDE.GetSurfaceAt(1)),
                    "stopAperture", Host.Properties(s.LDE.GetSurfaceAt(1).ApertureData.CurrentTypeSettings._S_RectangularAperture)));
                var rows = new List<object>();
                string controlPath = Path.Combine(folder, "control.zmx");
                s.SaveAs(controlPath);
                foreach (bool reload in new[] { false, true })
                {
                    if (reload && !s.LoadFile(controlPath, false)) throw new Exception("Reload failed");
                    foreach (int density in Densities)
                    {
                        double value = s.MFE.GetOperandValue(MeritOperandType.EFNO, density, 1, 1, 0, 0, 0, 0, 0);
                        rows.Add(Host.Object("kind", reload ? "afterReload" : "beforeReload", "density", density, "value", value));
                    }
                }
                var operands = new List<IMFERow>();
                foreach (int density in Densities)
                {
                    var row = s.MFE.AddOperand(); row.ChangeType(MeritOperandType.EFNO);
                    row.GetCellAt(2).IntegerValue = density; row.GetCellAt(3).IntegerValue = 1;
                    row.GetCellAt(4).IntegerValue = 1; row.GetCellAt(5).IntegerValue = 0; row.Weight = 1;
                    operands.Add(row);
                }
                s.MFE.CalculateMeritFunction();
                foreach (var row in operands)
                    rows.Add(Host.Object("kind", "mfeRow", "density", row.GetCellAt(2).IntegerValue, "value", row.Value));
                Host.Write(name + "-operands.json", rows);
                CaptureRays(s, name);
                Console.WriteLine("Completed " + name);
            }
            if (!s.LoadFile(input, false)) throw new Exception("Final reset failed");
            Host.Write("complete.json", Host.Object("complete", true, "input", input));
            return 0;
        }
        catch (Exception e) { Host.Write("error.json", Host.Object("error", e.ToString())); Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (app != null)
            {
                try { app.PrimarySystem.LoadFile(input, false); }
                finally { app.CloseApplication(); }
            }
        }
    }
    private static void CaptureRays(IOpticalSystem s, string name)
    {
        var tool = s.Tools.OpenBatchRayTrace();
        try
        {
            var data = tool.CreateNormUnpol(441, RaysType.Real, 1);
            for (int y = -10; y <= 10; y++)
                for (int x = -10; x <= 10; x++) data.AddRay(1, 0, 0, x / 10.0, y / 10.0, OPDMode.None);
            tool.RunAndWaitForCompletion(); data.StartReadingResults();
            var rows = new List<object>();
            int number, error, vignette; double x1, y1, z, l, m, n, nx, ny, nz, opd, intensity;
            while (data.ReadNextResult(out number, out error, out vignette, out x1, out y1, out z, out l, out m, out n,
                out nx, out ny, out nz, out opd, out intensity))
                rows.Add(Host.Object("number", number, "error", error, "vignette", vignette, "x", x1, "y", y1,
                    "l", l, "m", m, "n", n, "intensity", intensity));
            Host.Write(name + "-rays.json", rows);
        }
        finally { tool.Close(); }
    }
}
