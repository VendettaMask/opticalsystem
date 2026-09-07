using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Tools.RayTrace;
using ZOSAPI.Editors.MFE;

internal static class CaptureRays
{
    [STAThread]
    static int Main(string[] args)
    {
        string api = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(args);
    }
    static int Run(string[] args)
    {
        if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new InvalidOperationException("ZOS API initialization failed.");
        IZOSAPI_Application app = null;
        try
        {
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI) throw new InvalidOperationException("ZOS API license unavailable.");
            if (app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1) throw new InvalidOperationException("Expected OpticStudio 2026 R1.");
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            for (int file = 1; file < args.Length; file++)
            {
                var system = app.PrimarySystem;
                if (!system.LoadFile(args[file], false)) throw new IOException("Could not load " + args[file]);
                var fields = new[] { 0d, .25, .5, .7, .85, 1 };
                var inputs = new List<double[]>();
                foreach (double field in fields)
                {
                    inputs.Add(new[] { field, 0d, 0d });
                    for (int ring = 1; ring <= 6; ring++)
                        for (int angle = 0; angle < 16; angle++)
                        {
                            double theta = 2 * Math.PI * angle / 16;
                            inputs.Add(new[] { field, ring / 6d * Math.Cos(theta), ring / 6d * Math.Sin(theta) });
                        }
                }
                var tool = system.Tools.OpenBatchRayTrace();
                var rows = new List<object>();
                try
                {
                    var data = tool.CreateNormUnpol(inputs.Count, RaysType.Real, system.LDE.NumberOfSurfaces - 1);
                    foreach (var input in inputs) data.AddRay(1, 0, input[0], input[1], input[2], OPDMode.None);
                    tool.RunAndWaitForCompletion(); data.StartReadingResults();
                    int number, error, vignette; double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                    while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z, out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                        rows.Add(new { Number = number, Error = error, Vignette = vignette, X = x, Y = y, Z = z, L = l, M = m, N = n });
                    if (rows.Count != inputs.Count) throw new InvalidOperationException("Incomplete native ray result.");
                }
                finally { tool.Close(); }
                var output = new
                {
                    Major = app.ZOSMajorVersion, Minor = app.ZOSMinorVersion, ServicePack = app.ZOSSPVersion, Version = app.OpticStudioVersion,
                    Lens = Path.GetFileName(args[file]), Unit = "mm", WavelengthMicrometers = system.SystemData.Wavelengths.GetWavelength(1).Wavelength,
                    WavelengthCount = system.SystemData.Wavelengths.NumberOfWavelengths,
                    Reference = "Absolute image coordinates; common centroid statistics computed separately in validation code.",
                    Sampling = "6 radial rings x 16 angles plus center, equal weights, pupil boundary included; 6 normalized fields; real unpolarized rays",
                    AbsoluteCoordinateToleranceMillimeters = 1e-8, AbsoluteSpotToleranceMillimeters = 1e-8,
                    EFL = system.MFE.GetOperandValue(MeritOperandType.EFFL, 0, 0, 0, 0, 0, 0, 0, 0),
                    EPD = system.MFE.GetOperandValue(MeritOperandType.EPDI, 0, 0, 0, 0, 0, 0, 0, 0),
                    Inputs = inputs, Rays = rows
                };
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[file]), "native-rays.json"), serializer.Serialize(output));
                Console.WriteLine(args[file] + ": " + rows.Count + " native real rays");
            }
            return 0;
        }
        finally { if (app != null) app.CloseApplication(); }
    }
}
