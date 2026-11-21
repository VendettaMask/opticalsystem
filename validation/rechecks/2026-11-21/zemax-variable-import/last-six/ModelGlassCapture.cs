// Validation only. Uses an independent owned server instance; never a product runtime dependency.
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.Editors;

internal static class ModelGlassCapture
{
    [STAThread]
    private static int Main(string[] args)
    {
        string api = args[0]; string output = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(api, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(api, output);
    }
    private static int Run(string api, string output)
    {
        IZOSAPI_Application app = null;
        var json = new JavaScriptSerializer();
        try
        {
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new Exception("API initialization failed");
            app = new ZOSAPI_Connection().CreateNewApplication();
            if (app == null || !app.IsValidLicenseForAPI) throw new Exception("No valid independent API license");
            var system = app.PrimarySystem;
            system.New(false);
            while (system.LDE.NumberOfSurfaces < 3) system.LDE.InsertNewSurfaceAt(1);
            var surface = system.LDE.GetSurfaceAt(1);
            surface.Material = "N-BK7";
            double[][] cases = {
                new[] {1.6,50.0,0.0}, new[] {1.7,50.0,0.0}, new[] {1.6,60.0,0.0}, new[] {1.6,50.0,.01},
                new[] {1.61340067698,44.2947763681,-.0086}, new[] {1.52,64.0,0.0}, new[] {1.8,25.0,.02},
                new[] {1.3,15.0,-.04}, new[] {2.1,95.0,.04}, new[] {1.72,33.3,-.017},
                new[] {1.6135,44.2,-.0085}, new[] {1.5,0.0,0.0}, new[] {1.5,0.0,.01}
            };
            var waves = new List<double> {365,400,435.8343,486.1327,546.074,587.5618,656.2725,706.5188,780,850,1014};
            for (int i=0;i<=32;i++) if (!waves.Contains(400+10*i)) waves.Add(400+10*i);
            waves.Sort();
            var results = new List<object>();
            foreach (var values in cases)
            {
                var data = surface.MaterialCell.CreateSolveType(SolveType.MaterialModel);
                data._S_MaterialModel.IndexNd = values[0]; data._S_MaterialModel.AbbeVd = values[1]; data._S_MaterialModel.dPgF = values[2];
                data._S_MaterialModel.VaryIndex = true; data._S_MaterialModel.VaryAbbe = true; data._S_MaterialModel.VarydPgF = true;
                var status = surface.MaterialCell.SetSolveData(data);
                var rows = new List<object>();
                foreach (double wavelength in waves)
                {
                    system.SystemData.Wavelengths.GetWavelength(1).Wavelength = wavelength/1000;
                    var indices = new double[1]; int count = system.LDE.GetIndex(1, 1, indices);
                    rows.Add(new { WavelengthNanometers=wavelength, Index=indices[0], Count=count });
                }
                results.Add(new { Nd=values[0], Vd=values[1], DPgF=values[2], Status=status.ToString(), Values=rows });
            }
            File.WriteAllText(output,json.Serialize(new { Scope="Model-glass refractive-index reference only; independent hidden API server", Version=app.OpticStudioVersion,
                License=app.LicenseStatus.ToString(), Cases=results }));
            return 0;
        }
        catch(Exception e) { File.WriteAllText(output,json.Serialize(new { Error=e.ToString() })); return 1; }
        finally { if(app!=null) app.CloseApplication(); }
    }
}
