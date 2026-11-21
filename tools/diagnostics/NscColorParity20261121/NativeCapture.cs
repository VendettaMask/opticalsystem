// Read-only source capture in a separate, windowless ZOS-API server.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.Settings.RayTracing;

internal static class NativeCapture
{
    static string output;
    static void Write(string name, object value) { File.WriteAllText(Path.Combine(output, name), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value)); }
    static object Props(object value)
    {
        var result = new Dictionary<string, object>();
        if (value == null) return result;
        foreach (var p in value.GetType().GetInterfaces().SelectMany(t => t.GetProperties()).GroupBy(p => p.Name).Select(g => g.First()))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            if (!(p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string))) continue;
            try { var v = p.GetValue(value, null); result[p.Name] = v is Enum ? v.ToString() : v; } catch { }
        }
        return result;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        { var path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        try { return Run(args); } catch (Exception e) { Console.Error.WriteLine(e); return 2; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        output = args[2]; Directory.CreateDirectory(output);
        if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new Exception("API initialization failed");
        IZOSAPI_Application app = null;
        try
        {
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI) throw new Exception("API license unavailable");
            if (app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1) throw new Exception("This reference requires OpticStudio 2026 R1.");
            app.ShowChangesInUI = false;
            Write("environment.json", new { major = app.ZOSMajorVersion, minor = app.ZOSMinorVersion, sp = app.ZOSSPVersion, mode = app.Mode.ToString(), validLicense = app.IsValidLicenseForAPI });
            var system = app.PrimarySystem;
            if (!system.LoadFile(args[1], false)) throw new Exception("Cannot load input");
            Console.WriteLine("Loaded mode=" + system.Mode + "; objects=" + system.NCE.NumberOfObjects);
            var source = system.NCE.GetObjectAt(1);
            if (args.Length > 3) return Controls(system, source, Path.GetDirectoryName(args[1]), args[3] == "samples");
            Console.WriteLine("Source=" + (source == null ? "null" : source.Type.ToString()));
            Write("source.json", new { source = Props(source), sourcePhysics = Props(source.SourcesData) });
            Write("settings.json", new { source = Props(source), sourcePhysics = Props(source.SourcesData), color = Props(source.SourcesData.SourceColorSettings == null ? null : source.SourcesData.SourceColorSettings._S_CIE1931Tristimulus),
                detector = Props(system.NCE.GetObjectAt(4)), detectorType = Props(system.NCE.GetObjectAt(4).ObjectData),
                objects = Enumerable.Range(1, system.NCE.NumberOfObjects).Select(i => new { number = i, pose = Props(system.NCE.GetObjectAt(i)), geometry = Props(system.NCE.GetObjectAt(i).ObjectData) }).ToArray(),
                trace = Props(system.SystemData.NonSequentialData), units = Props(system.SystemData.Units) });
            foreach (var name in Enum.GetNames(typeof(AnalysisIDM)).Where(s => s == "PrescriptionDataSettings"))
            {
                var report = system.Analyses.New_Analysis((AnalysisIDM)Enum.Parse(typeof(AnalysisIDM), name));
                try { report.ApplyAndWaitForCompletion(); report.GetResults().GetTextFile(Path.Combine(output, "prescription.txt")); } finally { report.Close(); }
            }
            Console.WriteLine("Loaded sample and source definition");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var tool = system.Tools.OpenNSCRayTrace();
            try
            {
                tool.SplitNSCRays = true; tool.ScatterNSCRays = false; tool.UsePolarization = true;
                tool.IgnoreErrors = false; tool.SaveRays = false; tool.ClearDetectors(0);
                tool.RunAndWaitForCompletion();
                Write("trace-status.json", new { elapsedSeconds = timer.Elapsed.TotalSeconds, status = Props(tool) });
            }
            finally { tool.Close(); }
            var power = new double[10000]; var x = new double[10000]; var y = new double[10000]; var z = new double[10000];
            for (var i = 0; i < 10000; i++)
            {
                if (!system.NCE.GetColorDetectorData(4, i + 1, false, 1, 0, 0, out power[i])) throw new Exception("No color detector power");
                system.NCE.GetColorDetectorData(4, i + 1, false, 9, 0, 0, out x[i]);
                system.NCE.GetColorDetectorData(4, i + 1, false, 10, 0, 0, out y[i]);
                system.NCE.GetColorDetectorData(4, i + 1, false, 11, 0, 0, out z[i]);
            }
            Write("detector.json", new { pixelsX = 100, pixelsY = 100, order = "pixel number: x fastest, lower-left first", power, x, y, z, totalPower = power.Sum() });
            var viewer = system.Analyses.New_Analysis(AnalysisIDM.DetectorViewer);
            try
            {
                var settings = (IAS_DetectorViewer)viewer.GetSettings(); settings.Detector.SetDetectorNumber(4);
                settings.ShowAs = DetectorViewerShowAsTypes.TrueColor; settings.Smoothing = 0;
                viewer.ApplyAndWaitForCompletion();
                var data = viewer.GetResults();
                data.GetTextFile(Path.Combine(output, "detector-viewer.txt"));
                var grid = data.GetDataGridRgb(0); var r = new float[10000]; var g = new float[10000]; var b = new float[10000];
                grid.FillValues(10000, r, g, b);
                Write("viewer-rgb.json", new { r, g, b, settings = Props(settings), grid = Props(grid) });
            }
            finally { viewer.Close(); }
            Console.WriteLine("Captured trace; detector W=" + power.Sum().ToString("R"));
            return 0;
        }
        finally { if (app != null) app.CloseApplication(); }
    }
    static int Controls(ZOSAPI.IOpticalSystem system, ZOSAPI.Editors.NCE.INCERow source, string inputDirectory, bool samples)
    {
        var rows = new List<object>();
        var ellipse = (ZOSAPI.Editors.NCE.IObjectSourceEllipse)source.ObjectData;
        ellipse.NumberOfAnalysisRays = samples ? 64 : 1;
        if (!samples)
        {
            ellipse.WaveNumber = 1; ellipse.XHalfWidth = 1e-8; ellipse.YHalfWidth = 1e-8;
            source.SourcesData.SourceColor = ZOSAPI.Editors.NCE.SourceColorMode.SystemWavelengths;
        }
        foreach (var plateLength in samples ? new[] { .638 } : new[] { .638, .6381 })
        foreach (var position in !samples && plateLength == .638 ? new[] { -3.0, 0.0, 3.0 } : new[] { 0.0 })
        {
            ((ZOSAPI.Editors.NCE.IObjectRectangularVolume)system.NCE.GetObjectAt(3).ObjectData).ZLength = plateLength;
            source.YPosition = position;
            var database = Path.Combine(inputDirectory, "control-" + position.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".ZRD");
            var tool = system.Tools.OpenNSCRayTrace();
            try
            {
                tool.SplitNSCRays = true; tool.UsePolarization = true; tool.ScatterNSCRays = false;
                tool.SaveRays = true; tool.SaveRaysFile = database; tool.ClearDetectors(0); tool.RunAndWaitForCompletion();
                Write("control-trace-" + position + ".json", Props(tool));
            }
            finally { tool.Close(); }
            var reader = system.Tools.OpenRayDatabaseReader();
            try
            {
                reader.ZRDFile = database; reader.RunAndWaitForCompletion(); var result = reader.GetResults();
                if (result == null) throw new Exception("ZRD read failed: " + reader.ErrorMessage + "; file=" + database + "; exists=" + File.Exists(database));
                int ray, wave, count;
                double wavelength;
                while (result.ReadNextResult(out ray, out wave, out wavelength, out count))
                {
                    int level, parent, obj, face, inside, xy, lm; ZOSAPI.Tools.RayTrace.RayStatus status;
                    double x,y,z,l,m,n,exr,exi,eyr,eyi,ezr,ezi,power,length,nx,ny,nz,index,start,phase,at;
                    while (result.ReadNextSegmentFull(out level,out parent,out obj,out face,out inside,out status,
                        out x,out y,out z,out l,out m,out n,out exr,out exi,out eyr,out eyi,out ezr,out ezi,
                        out power,out length,out xy,out lm,out nx,out ny,out nz,out index,out start,out phase,out at))
                        rows.Add(new {position,plateLength,ray,wavelength,level,parent,obj,face,inside,status=status.ToString(),x,y,z,l,m,n,exr,exi,eyr,eyi,ezr,ezi,power,length,nx,ny,nz,index,start,phase,at});
                }
            }
            finally { reader.Close(); }
        }
        Write(samples ? "ray-samples.json" : "ray-controls.json",rows); return 0;
    }
}
