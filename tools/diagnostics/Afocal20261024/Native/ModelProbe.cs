// Read-only OpticStudio 2026 R1 capture; compile separately with the Framework compiler.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

internal static class ModelProbe
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            var path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Run(args); }
        catch(Exception e) { for(var x=e; x!=null; x=x.InnerException) Console.Error.WriteLine(x.GetType().FullName + ": " + x.Message); return 2; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (File.Exists(args[2])) throw new IOException("Use a fresh capture output.");
        IZOSAPI_Application app = null;
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new Exception("Initializer failed");
            Console.WriteLine("Initialized API");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion != 26 || app.ZOSMinorVersion != 1)
                throw new Exception("Licensed OpticStudio 2026 R1 required");
            var rows = new List<object>();
            foreach (var source in File.ReadAllLines(args[1]))
            {
                if (!app.PrimarySystem.LoadFile(source, false)) throw new Exception("LoadFile failed: " + source);
                Console.WriteLine("Loaded " + Path.GetFileName(source));
                var s = app.PrimarySystem;
                for (int config = 1; config <= s.MCE.NumberOfConfigurations; config++)
                {
                    s.MCE.SetCurrentConfiguration(config);
                    s.UpdateStatus();
                    var nsc = new List<object>();
                    int objectCount = 0;
                    try { objectCount = s.NCE.NumberOfObjects; } catch { }
                    for (int o = 1; o <= objectCount; o++)
                    {
                        var obj = s.NCE.GetObjectAt(o);
                        nsc.Add(new { row = Props(obj), geometry = Props(obj.ObjectData) });
                    }
                    rows.Add(new { source, config, advanced = Props(s.SystemData.Advanced),
                        aperture = Props(s.SystemData.Aperture), units = Props(s.SystemData.Units),
                        exitPupil = s.MFE.GetOperandValue(MeritOperandType.EXPP,0,0,0,0,0,0,0,0),
                        exitDiameter = s.MFE.GetOperandValue(MeritOperandType.EXPD,0,0,0,0,0,0,0,0),
                        surfaces = Enumerable.Range(0,s.LDE.NumberOfSurfaces).Select(i => Props(s.LDE.GetSurfaceAt(i))).ToArray(), nsc });
                    Console.WriteLine(Path.GetFileName(source) + " c" + config + " " + s.SystemData.Advanced.ReferenceOPD);
                }
            }
            File.WriteAllText(args[2],new JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Serialize(new {
                version=app.OpticStudioVersion, license=app.LicenseStatus.ToString(), rows }));
            return 0;
        }
        finally { if (app != null) app.CloseApplication(); }
    }
    private static Dictionary<string,object> Props(object obj)
    {
        var result = new Dictionary<string,object>();
        if(obj == null) return result;
        foreach(var p in obj.GetType().GetInterfaces().SelectMany(t=>t.GetProperties()).GroupBy(p=>p.Name).Select(g=>g.First()))
        {
            if(p.GetIndexParameters().Length != 0) continue;
            try {
                var v=p.GetValue(obj,null);
                if(v==null || v is string || v is bool || v is int || v is uint) result[p.Name]=v;
                else if(v is double) result[p.Name]=double.IsNaN((double)v)||double.IsInfinity((double)v)?null:v;
                else if(v.GetType().IsEnum) result[p.Name]=v.ToString();
            } catch(Exception e) { result[p.Name]="Unavailable: "+e.Message; }
        }
        return result;
    }
}
