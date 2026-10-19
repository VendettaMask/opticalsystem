// Read-only native controls for original Yolo MFE surface references.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

internal static class CoordinateBreakControl
{
    public static int Main(string[] args)
    {
        string api = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string candidate = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (File.Exists(args[2])) throw new IOException("Use a fresh control output.");
        string source = Path.GetFullPath(args[1]);
        string before = Hash(source);
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new InvalidOperationException("Initializer failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new InvalidOperationException("Licensed OpticStudio 2026 R1 required");
            var system = app.PrimarySystem;
            if (!system.LoadFile(source, false)) throw new InvalidOperationException("LoadFile failed");
            system.MCE.SetCurrentConfiguration(1);
            system.UpdateStatus();
            var rows = new List<object>();
            var points = new double[,] { { 0, 0 }, { .25, 0 }, { -.25, 0 }, { 0, .25 }, { 0, -.25 } };
            foreach (int surface in new[] { 9, 13 })
                foreach (string code in new[] { "REAX", "REAY" })
                    for (int p = 0; p < points.GetLength(0); p++)
                    {
                        double px = points[p, 0], py = points[p, 1];
                        double value = system.MFE.GetOperandValue((MeritOperandType)Enum.Parse(typeof(MeritOperandType), code),
                            surface, 1, 0, 0, px, py, 0, 0);
                        int number, error, vignette;
                        double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                        var tool = system.Tools.OpenBatchRayTrace();
                        try
                        {
                            var data = tool.CreateNormUnpol(1, RaysType.Real, surface);
                            if (!data.AddRay(1, 0, 0, px, py, OPDMode.None)) throw new InvalidOperationException("AddRay failed");
                            tool.RunAndWaitForCompletion();
                            if (!data.StartReadingResults() || !data.ReadNextResult(out number, out error, out vignette,
                                out x, out y, out z, out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                                throw new InvalidOperationException("Native control ray unavailable");
                        }
                        finally { tool.Close(); }
                        double coordinate = code == "REAX" ? x : y;
                        bool consistent = !double.IsNaN(value) && !double.IsInfinity(value)
                            && Math.Abs(value - coordinate) <= 1e-6 + 1e-6 * Math.Abs(coordinate);
                        rows.Add(new { code, surface, px, py, error, vignette, referenceConsistent = consistent,
                            batchCoordinate = double.IsNaN(coordinate) || double.IsInfinity(coordinate) ? (double?)null : coordinate,
                            value = double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value });
                    }
            if (Hash(source) != before) throw new IOException("Source changed.");
            File.WriteAllText(args[2], new JavaScriptSerializer().Serialize(new { source, sourceSha256 = before,
                app.ZOSMajorVersion, app.ZOSMinorVersion, licenseStatus = app.LicenseStatus.ToString(), rows }));
            return 0;
        }
        finally { if (app != null) app.CloseApplication(); }
    }

    private static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
