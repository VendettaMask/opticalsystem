using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using System.Text.Json;

if (args.Length == 2 && args[0] == "focal-guards") { FocalGuards.Run(args[1]); return; }
if (args.Length == 2 && args[0] == "facet-probe") { await FacetProbe.Run(args[1]); return; }
if (args.Length == 2 && args[0] == "facet-masks") { await FacetProbe.Run(args[1],true); return; }
if (args.Length == 3 && args[0] == "ray-audit") { AfocalRayAudit.Run(args[1], args[2]); return; }
if (args.Length == 2 && args[0] == "point-core")
{
    var request = JsonFiles.Read<JsonElement>(Path.Combine(args[1],"request.json"));
    var optic = (await WorkbenchExecutor.Import(request.GetProperty("input").GetString()!)).Configurations[request.GetProperty("configuration").GetInt32()-1];
    var wave = optic.Wavelengths[request.GetProperty("wavelength").GetInt32()-1];
    var output = Path.Combine(args[1],"core.json");
    if(File.Exists(output)) throw new IOException("Fresh output required.");
    var points = request.GetProperty("rayAuditInputs").EnumerateArray().Select(p =>
    {
        var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(p.GetProperty("hx").GetDouble(),p.GetProperty("hy").GetDouble(),
            p.GetProperty("px").GetDouble(),p.GetProperty("py").GetDouble(),wave.Micrometers,optic.RayAimingEnabled);
        var samples=optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
        var final=samples.Last(); var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
        return new {input=p,position=frame.ToLocalPoint(final.Position),direction=frame.ToLocalDirection(final.Direction),history=samples};
    }).ToArray();
    JsonFiles.Write(output,points); return;
}

if(args.Length==2 && args[0]=="point-opd")
{
    var optic=(await WorkbenchExecutor.Import("artifacts/validation/afocal-20261024/selected-sources/Afocal/Afocal Riflescope.ZMX")).ActiveOptic;
    var results=new List<object>();
    foreach(var path in Directory.EnumerateFiles(args[1],"point-reference.json",SearchOption.AllDirectories))
    {
        var n=JsonFiles.Read<JsonElement>(path); var inputs=n.GetProperty("inputs").EnumerateArray().ToArray();
        var wave=n.GetProperty("wavelength").GetInt32();
        var native=n.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
        var field=(Hx:inputs[0].GetProperty("hx").GetDouble(),Hy:inputs[0].GetProperty("hy").GetDouble());
        var pupils=inputs.Select(p=>(p.GetProperty("px").GetDouble(),p.GetProperty("py").GetDouble())).ToArray();
        var core=WavefrontEngine.GenerateChiefRaySamples(optic,field,optic.Wavelengths[wave-1],pupils,aimAtStop:optic.RayAimingEnabled);
        var points=core.Samples.Select((s,i)=>new {s.NormalizedPupilX,s.NormalizedPupilY,s.OpdWaves,native=native[i].GetProperty("opdWaves").GetDouble(),
            difference=s.OpdWaves-native[i].GetProperty("opdWaves").GetDouble()}).ToArray();
        results.Add(new {field,wave,points,max=points.Max(p=>Math.Abs(p.difference))});
        Console.WriteLine($"{field} w{wave}: {points.Max(p=>Math.Abs(p.difference)):G9}");
    }
    var output = Path.Combine(args[1], "core-opd-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
    if (File.Exists(output)) throw new IOException("Fresh output required.");
    JsonFiles.Write(output,results); return;
}

if (args.Length != 2 || args[0] != "inspect") throw new ArgumentException("inspect <fresh-output>");
if (Directory.Exists(args[1])) throw new IOException("Fresh output required.");
Directory.CreateDirectory(args[1]);
var rows = new List<object>();
foreach (var originalSource in File.ReadAllLines("artifacts/validation/afocal-20261024/sources.txt"))
{
    var source=Path.Combine("artifacts/validation/afocal-20261024/selected-sources",
        Path.GetRelativePath(Path.GetFullPath("artifacts/zemax-sequential-corpus/20261021/full-v1/sources"),originalSource));
    var imported = await WorkbenchExecutor.Import(source);
    for (var c=0;c<imported.Configurations.Count;c++)
    {
        var optic=imported.Configurations[c];
        var rayRows=new List<object>();
        foreach(var physicalField in optic.Fields)
        {
            var normalized = FieldCoordinates.Normalize(optic.Fields,physicalField.X,physicalField.Y);
            var field = (Hx:normalized.X, Hy:normalized.Y);
            foreach(var w in optic.Wavelengths)
                foreach(var pupil in new[]{(0d,0d),(.5,0d),(-.5,0d),(0d,.5),(0d,-.5),(1d,0d),(-1d,0d),(0d,1d),(0d,-1d)})
                {
                    try {
                        var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx,field.Hy,pupil.Item1,pupil.Item2,w.Micrometers,optic.RayAimingEnabled);
                        var history=optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
                        rayRows.Add(new{field,w.Nanometers,pupil,history});
                    } catch(Exception e){rayRows.Add(new{field,w.Nanometers,pupil,error=e.ToString()});}
                }
        }
        rows.Add(new {source,configuration=c+1,entrance=optic.Paraxial.EstimateEntrancePupilDiameter(),exit=optic.Paraxial.EstimateExitPupilDiameter(),
            exitPosition=optic.Paraxial.EstimateExitPupilLocation(),rays=rayRows});
        Console.WriteLine(Path.GetFileName(source)+" c"+(c+1)+" inspected "+rayRows.Count);
        JsonFiles.Write(Path.Combine(args[1],"controls.json"),rows);
    }
}
JsonFiles.Write(Path.Combine(args[1],"controls.json"),rows);
