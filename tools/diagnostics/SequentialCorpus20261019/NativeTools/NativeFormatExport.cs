// Offline Framework host. Saves only new workspace exports, never source files.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using ZOSAPI;

internal static class NativeFormatExport
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
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        string root = Path.GetFullPath(args[2]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var jobs = (IEnumerable)serializer.DeserializeObject(File.ReadAllText(args[1]));
        IZOSAPI_Application app = null;
        var results = new List<object>();
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new InvalidOperationException("Initializer failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new InvalidOperationException("Licensed OpticStudio 2026 R1 required");
            File.WriteAllText(Path.Combine(args[2], "environment.json"), serializer.Serialize(new { app.ZOSMajorVersion, app.ZOSMinorVersion, app.OpticStudioVersion, licenseStatus = app.LicenseStatus.ToString(), app.IsValidLicenseForAPI }));
            foreach (Dictionary<string, object> job in jobs)
            {
                string source = Path.GetFullPath((string)job["input"]);
                string output = Path.GetFullPath((string)job["output"]);
                if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase) || output.Equals(source, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
                    throw new IOException("Export must use a new file in the validated workspace output root");
                string before = Hash(source);
                try
                {
                    var system = app.PrimarySystem;
                    if (!system.LoadFile(source, false)) throw new InvalidOperationException("LoadFile failed");
                    system.MCE.SetCurrentConfiguration(1);
                    system.UpdateStatus();
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    system.SaveAs(output);
                    if (!File.Exists(output) || Hash(source) != before) throw new IOException("Export missing or input changed");
                    results.Add(new { id = job["id"], input = source, output, success = true, sourceSha256 = before, exportSha256 = Hash(output), configurationCount = system.MCE.NumberOfConfigurations });
                    Console.WriteLine("EXPORTED " + job["id"]);
                }
                catch (Exception exception)
                {
                    results.Add(new { id = job["id"], input = source, output, success = false, sourceSha256 = before, error = exception.ToString() });
                    Console.WriteLine("FAILED " + job["id"] + " " + exception.Message);
                }
                File.WriteAllText(Path.Combine(args[2], "export-results.json"), serializer.Serialize(results));
            }
        }
        finally { if (app != null) app.CloseApplication(); }
        return 0;
    }

    private static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
