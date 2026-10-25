using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class FacetProbe
{
    internal static async Task Run(string output,bool masks=false)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var mask in masks?Enumerable.Range(0,64):new[]{0,4,6,12,14,50,63})
        {
            var diagonal=mask.ToString("D2");
            var folder=Path.Combine(output,diagonal);Directory.CreateDirectory(folder);
            var source="artifacts/validation/afocal-20261024/selected-sources/Afocal/Binocular_System.ZMX";
            var local=Path.Combine(folder,"Binocular_System.ZMX");File.Copy(source,local);
            foreach(var name in new[]{"PORRO_PRISM-LH.POB","PORRO_PRISM-RH.POB"})
            {
                var lines=File.ReadAllLines(Path.Combine("artifacts/validation/afocal-20261024",name));
                var rectangle=0;
                var transformed=lines.SelectMany(line=>
                {
                    var t=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(t.Length==7&&t[0]=="R")return (mask&(1<<rectangle++))==0
                        ?new[]{$"T {t[1]} {t[2]} {t[3]} {t[5]} {t[6]}",$"T {t[1]} {t[3]} {t[4]} {t[5]} {t[6]}"}
                        :new[]{$"T {t[1]} {t[2]} {t[4]} {t[5]} {t[6]}",$"T {t[2]} {t[3]} {t[4]} {t[5]} {t[6]}"};
                    return new[]{line};
                });
                File.WriteAllLines(Path.Combine(folder,name),transformed);
            }
            var imported=await WorkbenchExecutor.Import(local);
            for(var c=1;c<=2;c++)
            {
                var optic=imported.Configurations[c-1];
                var native=JsonFiles.Read<JsonElement>($"artifacts/validation/afocal-20261024/matrix-stage3/rays/003-binocular_system-c{c}-w2/native/ray-audit.json");
                var expected=native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
                var inputs=native.GetProperty("inputs").EnumerateArray().ToArray();var maxP=0d;var maxD=0d;
                for(var i=0;i<inputs.Length;i++)
                {
                    if(masks && i%12!=1)continue;
                    var p=inputs[i];double D(string n)=>p.GetProperty(n).GetDouble();
                    var ray=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(D("hx"),D("hy"),D("px"),D("py"),optic.Wavelengths[1].Micrometers,false)).Single()!;
                    var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;var pos=frame.ToLocalPoint(ray.Position);var dir=frame.ToLocalDirection(ray.Direction);
                    double N(string n)=>expected[i].GetProperty(n).GetDouble();
                    maxP=Math.Max(maxP,Math.Max(Math.Abs(pos.X-N("x")),Math.Abs(pos.Y-N("y"))));
                    maxD=Math.Max(maxD,Math.Max(Math.Abs(dir.X-N("l")),Math.Abs(dir.Y-N("m"))));
                }
                var curvature=masks?null:new FieldCurvatureAnalysis(optic,101,1e-5,wavelengthNumber:2,parabasalRayPair:OpticalSystemDesign.Core.Services.ParabasalRayPair.ChiefAndPositive).GenerateData();
                var distortion=masks?null:new DistortionAnalysis(optic,101,wavelengthNumber:2).GenerateData();
                rows.Add(new{diagonal,configuration=c,maxP,maxD,curvature,distortion});Console.WriteLine($"{diagonal} c{c}: p={maxP:G9} d={maxD:G9}");
            }
            JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
        }
        JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
    }
}
