// Offline C# 5 host. Each request reloads an immutable lens copy; only the API
// application is reused. Compiled with a recorded, mechanically patched Host.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using ZOSAPI;

internal static class NativeBatchHost
{
    internal static IZOSAPI_Application App;

    [STAThread]
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
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var jobs = (IEnumerable)serializer.DeserializeObject(File.ReadAllText(args[1]));
        int failures = 0;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new InvalidOperationException("Initializer failed");
            App = new ZOSAPI_Connection().CreateNewApplication();
            if (App == null || !App.IsValidLicenseForAPI || App.ZOSMajorVersion != 26 || App.ZOSMinorVersion != 1)
                throw new InvalidOperationException("A licensed OpticStudio 2026 R1 application is required");
            foreach (Dictionary<string, object> job in jobs)
            {
                string request = (string)job["request"], output = (string)job["output"];
                Directory.CreateDirectory(output);
                if (File.Exists(Path.Combine(output, "batch-result.json"))) throw new IOException("Output already completed: " + output);
                Console.WriteLine("START " + job["id"]);
                var started = DateTime.UtcNow;
                int result = Host.Main(new[] { request, output });
                File.WriteAllText(Path.Combine(output, "batch-result.json"), serializer.Serialize(new
                {
                    id = job["id"], exitCode = result, startedUtc = started.ToString("o"), completedUtc = DateTime.UtcNow.ToString("o"),
                    semantics = "Persistent application; source reloaded for every request; no lens saved"
                }));
                if (result != 0) failures++;
                Console.WriteLine("END " + job["id"] + " exit=" + result);
            }
        }
        finally { if (App != null) App.CloseApplication(); }
        Console.WriteLine("Native failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
