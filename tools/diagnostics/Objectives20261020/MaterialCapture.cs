using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;
using ZOSAPI;
internal static class MaterialCapture
{
 [STAThread] public static int Main(string[] args) {
  string api=args[0]; AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) { var p=Path.Combine(api,new AssemblyName(e.Name).Name+".dll"); return File.Exists(p)?Assembly.LoadFrom(p):null; }; return Run(args);
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static int Run(string[] args) {
  var output=args[2]; Directory.CreateDirectory(output);
  if(!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(args[0])) throw new Exception("Initialization failed");
  var app=new ZOSAPI_Connection().CreateNewApplication();
  try {
   if(app==null || !app.IsValidLicenseForAPI || app.ZOSMajorVersion!=26 || app.ZOSMinorVersion!=1) throw new Exception("Licensed 2026 R1 required");
   var sys=app.PrimarySystem; if(!sys.LoadFile(args[1],false)) throw new Exception("Load failed");
   var original=Enumerable.Range(1,sys.SystemData.Wavelengths.NumberOfWavelengths).Select(i=>sys.SystemData.Wavelengths.GetWavelength(i).Wavelength).ToArray();
   var points=Enumerable.Range(350,701).Select(i=>i/1000.0).Concat(original).Concat(new[]{.5875618,.4861327,.6562725}).Distinct().OrderBy(x=>x).ToArray();
   var checks=Enumerable.Range(350,700).Select(i=>(i+.5)/1000.0).ToArray();
   var rows=new List<object>(); var validation=new List<object>();
   foreach(var surface in new[]{1,2}) {
    var material=sys.LDE.GetSurfaceAt(surface).Material;
    Func<double,double> index=wave=>{sys.SystemData.Wavelengths.GetWavelength(1).Wavelength=wave; sys.UpdateStatus(); var values=new double[sys.SystemData.Wavelengths.NumberOfWavelengths]; int result=sys.LDE.GetIndex(surface,values.Length,values); if(result<1 || double.IsNaN(values[0]) || double.IsInfinity(values[0]) || values[0]<=1) throw new Exception("Invalid index return"); return values[0];};
    rows.Add(new{ name=material,surface=surface,wavelengthsNanometers=points.Select(x=>x*1000).ToArray(),indices=points.Select(index).ToArray() });
    validation.Add(new{name=material,wavelengthsNanometers=checks.Select(x=>x*1000).ToArray(),indices=checks.Select(index).ToArray()});
   }
   var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
   File.WriteAllText(Path.Combine(output,"materials.json"),json.Serialize(new{version="2026 R1",source=args[1],environment=new{temperature=sys.SystemData.Environment.Temperature,pressure=sys.SystemData.Environment.Pressure},method="LDE.GetIndex; independent material-only dispersion queries; input never saved",materials=rows}));
   File.WriteAllText(Path.Combine(output,"held-out-indices.json"),json.Serialize(validation));
  } finally {if(app!=null)app.CloseApplication();} return 0;
 }
}
