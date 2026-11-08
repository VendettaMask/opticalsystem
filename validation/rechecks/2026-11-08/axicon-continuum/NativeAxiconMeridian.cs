using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using ZOSAPI;
using ZOSAPI.Analysis;
using ZOSAPI.Editors.LDE;
using ZOSAPI.Tools.RayTrace;

internal static class NativeAxiconMeridian
{
    private const int BatchSize = 32768;

    [STAThread]
    private static int Main(string[] args)
    {
        var api = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs e)
        {
            var path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Capture(api, args);
    }

    private static int Capture(string api, string[] args)
    {
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api))
                throw new InvalidOperationException("Native API initialization failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new InvalidOperationException("A licensed standalone OpticStudio 2026 R1 session is required");
            var system = app.PrimarySystem;
            using (var sha = SHA256.Create())
            {
                var sourceHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[1]))).Replace("-", "");
                if (sourceHash != "2306D27415B9ADCD38497A6B9885C9727577A0EA7D7409E46DCB2BB8CA9B705F")
                    throw new InvalidDataException("Native Axicon prescription identity differs");
            }
            if (!system.LoadFile(args[1], false)) throw new IOException("Could not load Axicon prescription");
            system.UpdateStatus();
            var surfaceCount = system.LDE.NumberOfSurfaces;
            var image = surfaceCount - 1;
            if (surfaceCount != 6 || system.SystemData.Fields.NumberOfFields != 1 ||
                system.SystemData.Fields.GetField(1).X != 0 || system.SystemData.Fields.GetField(1).Y != 0 ||
                system.SystemData.Wavelengths.GetWavelength(1).Wavelength != .55 ||
                Enumerable.Range(0, surfaceCount).Any(i => system.LDE.GetSurfaceAt(i).Type != SurfaceType.Standard))
                throw new InvalidOperationException("Expected the original axial sequential Axicon prescription");

            var thickness = new double[surfaceCount];
            var indices = new double[surfaceCount];
            for (var i = 0; i < surfaceCount; ++i)
            {
                thickness[i] = system.LDE.GetSurfaceAt(i).Thickness;
                var values = new double[system.SystemData.Wavelengths.NumberOfWavelengths];
                if (system.LDE.GetIndex(i, values.Length, values) < 1 || !(values[0] > 0))
                    throw new InvalidDataException("Native refractive index unavailable");
                indices[i] = values[0];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "surface {0}, thickness {1:R}, refractive index after {2:R}", i, thickness[i], indices[i]));
            }
            var htf = system.Analyses.New_Analysis(AnalysisIDM.HuygensThroughFocusMtf);
            try
            {
                var typed = (ZOSAPI.Analysis.Settings.Mtf.IAS_HuygensThroughFocusMtf)htf.GetSettings();
                Console.WriteLine("Native through-focus sampling enum: " + string.Join(", ", Enum.GetNames(typed.PupilSampleSize.GetType())));
            }
            finally { htf.Close(); }

            var tool = system.Tools.OpenBatchRayTrace();
            if (tool == null) throw new InvalidOperationException("Native normalized ray tool unavailable");
            try
            {
                var nodes = File.ReadAllLines(args[2]).Select(s => s.Split(',').Select(Parse).ToArray()).ToArray();
                using (var compressed = new GZipStream(File.Create(args[3]), CompressionLevel.Optimal))
                using (var writer = new BinaryWriter(compressed))
                {
                    writer.Write(nodes.Length);
                    for (var offset = 0; offset < nodes.Length; offset += BatchSize)
                    {
                        var count = Math.Min(BatchSize, nodes.Length - offset);
                        var path = new double[count];
                        var prior = new double[count, 3];
                        var imageRay = new double[count, 4];
                        for (var surface = 1; surface <= image; ++surface)
                        {
                            var data = tool.CreateNormUnpol(count, RaysType.Real, surface);
                            for (var j = 0; j < count; ++j)
                                if (!data.AddRay(1, 0, 0, nodes[offset + j][0], 0, OPDMode.None))
                                    throw new InvalidOperationException("Native batch rejected explicit pupil coordinate");
                            tool.RunAndWaitForCompletion();
                            if (!data.StartReadingResults()) throw new InvalidOperationException("Native batch has no results");
                            int number, error, vignette;
                            double x, y, z, l, m, n, nx, ny, nz, opd, intensity;
                            var jRead = 0;
                            while (data.ReadNextResult(out number, out error, out vignette, out x, out y, out z,
                                out l, out m, out n, out nx, out ny, out nz, out opd, out intensity))
                            {
                                var j = number - 1;
                                if (j != jRead++ || error != 0 || vignette != 0 || !Finite(x + y + z + l + m + n))
                                    throw new InvalidDataException("A radial ray failed or is out of order");
                                if (surface == 1) path[j] = indices[0] * z;
                                else
                                {
                                    var dx = x - prior[j, 0];
                                    var dy = y - prior[j, 1];
                                    var dz = z + thickness[surface - 1] - prior[j, 2];
                                    path[j] += indices[surface - 1] * Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                }
                                prior[j, 0] = x;
                                prior[j, 1] = y;
                                prior[j, 2] = z;
                                if (surface == image)
                                {
                                    imageRay[j, 0] = path[j] - indices[image] * (x * l + y * m + z * n);
                                    imageRay[j, 1] = indices[image] * Math.Sqrt(l * l + m * m);
                                    imageRay[j, 2] = indices[image] * n;
                                    imageRay[j, 3] = Math.Sqrt(x * x + y * y);
                                }
                            }
                            if (jRead != count) throw new InvalidDataException("Native batch incomplete");
                            data.ClearData();
                        }
                        for (var j = 0; j < count; ++j)
                        {
                            writer.Write(nodes[offset + j][0]);
                            writer.Write(nodes[offset + j][1]);
                            for (var column = 0; column < 4; ++column) writer.Write(imageRay[j, column]);
                        }
                        Console.WriteLine("Captured " + (offset + count) + " of " + nodes.Length);
                    }
                }
            }
            finally { tool.Close(); }
            Console.WriteLine(app.OpticStudioVersion + "; " + app.Mode);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { if (app != null) app.CloseApplication(); }
    }

    private static double Parse(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }
    private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
}



