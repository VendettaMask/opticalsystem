using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

internal static class CurvatureProbe
{
    [STAThread] public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender,ResolveEventArgs e) {
            var p=Path.Combine(args[0],new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
        try{return Run(args);}catch(Exception e){Console.Error.WriteLine(e);return 2;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run(string[] args)
    {
        if(File.Exists(args[2]))throw new IOException("Fresh output required");
        IZOSAPI_Application app=null;
        try
        {
            if(!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0]))throw new Exception("Initialize failed");
            app=new ZOSAPI_Connection().CreateNewApplication();
            if(app==null||!app.IsValidLicenseForAPI||app.ZOSMajorVersion!=26||app.ZOSMinorVersion!=1)throw new Exception("Licensed 2026 R1 required");
            if(!app.PrimarySystem.LoadFile(args[1],false))throw new Exception("Load failed");
            var s=app.PrimarySystem;var rows=new List<object>();
            for(int c=1;c<=s.MCE.NumberOfConfigurations;c++)
            {
                s.MCE.SetCurrentConfiguration(c);s.UpdateStatus();
                foreach(var h in new[]{0d,.01,.5,.84,1d})
                foreach(var code in new[]{"FCGS","FCGT"})
                {
                    double v=s.MFE.GetOperandValue((MeritOperandType)Enum.Parse(typeof(MeritOperandType),code),0,2,0,h,0,0,0,0);
                    rows.Add(new{configuration=c,hy=h,code,value=v});
                }
            }
            File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(new{version=app.OpticStudioVersion,license=app.LicenseStatus.ToString(),rows}));return 0;
        }
        finally{if(app!=null)app.CloseApplication();}
    }
}
