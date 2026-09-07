using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI.SystemData;

internal static class ExportLens
{
    static Dictionary<string, object> Obj(object value) { return (Dictionary<string, object>)value; }
    static double Num(object value) { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
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
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
            for (int file = 1; file < args.Length; file++)
            {
                var candidate = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(args[file]));
                var optic = Obj(candidate["Optic"]);
                var surfaces = (ArrayList)optic["Surfaces"];
                var system = app.PrimarySystem;
                system.New(false);
                system.SystemData.Units.LensUnits = ZemaxSystemUnits.Millimeters;
                system.SystemData.Aperture.ApertureType = ZemaxApertureType.EntrancePupilDiameter;
                system.SystemData.Aperture.ApertureValue = Num(Obj(optic["Aperture"])["Value"]);
                system.SystemData.MaterialCatalogs.AddCatalog("SCHOTT");
                system.SystemData.Fields.SetFieldType(FieldType.Angle);
                var fields = (ArrayList)optic["Fields"];
                for (int i = 0; i < fields.Count; i++)
                {
                    var field = Obj(fields[i]);
                    var row = i == 0 ? system.SystemData.Fields.GetField(1)
                        : system.SystemData.Fields.AddField(0, 0, 1);
                    row.X = Num(field["XAngleDegrees"]); row.Y = Num(field["YAngleDegrees"]); row.Weight = Num(field["Weight"]);
                }
                var waves = (ArrayList)optic["Wavelengths"];
                for (int i = 0; i < waves.Count; i++)
                {
                    var wave = Obj(waves[i]);
                    var row = i == 0 ? system.SystemData.Wavelengths.GetWavelength(1)
                        : system.SystemData.Wavelengths.AddWavelength(Num(wave["Nanometers"]) / 1000, Num(wave["Weight"]));
                    row.Wavelength = Num(wave["Nanometers"]) / 1000; row.Weight = Num(wave["Weight"]);
                    if ((bool)wave["IsPrimary"]) row.MakePrimary();
                }
                while (system.LDE.NumberOfSurfaces < surfaces.Count) system.LDE.InsertNewSurfaceAt(1);
                for (int i = 0; i < surfaces.Count; i++)
                {
                    var source = Obj(surfaces[i]); var row = system.LDE.GetSurfaceAt(i);
                    row.Comment = (string)source["Label"];
                    row.Radius = Num(source["Radius"]); row.Thickness = Num(source["Thickness"]);
                    row.Material = (string)source["Material"] == "Air" ? "" : (string)source["Material"];
                    row.Conic = Num(source["Conic"]);
                    row.SemiDiameter = Num(source["SemiDiameter"]);
                    if (i > 0 && i < surfaces.Count - 1) row.SemiDiameterCell.MakeSolveFixed();
                    if ((bool)source["IsStop"]) row.IsStop = true;
                }
                var output = Path.Combine(Path.GetDirectoryName(args[file]), "candidate.ZMX");
                system.SaveAs(output);
                File.WriteAllText(Path.ChangeExtension(output, ".environment.json"), json.Serialize(new
                {
                    Major = app.ZOSMajorVersion, Minor = app.ZOSMinorVersion, ServicePack = app.ZOSSPVersion,
                    Version = app.OpticStudioVersion, License = app.LicenseStatus.ToString(), Source = Path.GetFileName(args[file]),
                    LensUnits = "mm", WavelengthUnits = "um", FieldType = "Angle", Reference = "Native analysis settings captured separately",
                    SurfaceCount = system.LDE.NumberOfSurfaces, EPD = system.SystemData.Aperture.ApertureValue
                }));
                Console.WriteLine(output);
            }
            return 0;
        }
        finally { if (app != null) app.CloseApplication(); }
    }
}
