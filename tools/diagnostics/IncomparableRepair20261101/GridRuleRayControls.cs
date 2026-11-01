using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;

internal static class GridRuleRayControls
{
    internal static async Task Run(string study)
    {
        var output=Path.Combine(study,"rule-ray-assessment.json");
        if(File.Exists(output)) throw new IOException("Fresh rule-ray assessment required");
        var records=new List<object>();
        foreach(var model in JsonFiles.Read<JsonElement[]>(Path.Combine(study,"models.json")))
        {
            var input=model.GetProperty("input").GetString()!;
            var optic=await new ZemaxZmxImporter().ImportFileAsync(input);
            var profile=(GridGradientIndexProfile)((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).Profile;
            var nativePath=Path.Combine(model.GetProperty("native").GetString()!,"model-controls.json");
            var native=JsonFiles.Read<JsonElement>(nativePath).GetProperty("rows");
            var points=new List<object>();
            foreach(var pupil in model.GetProperty("pupils").EnumerateArray())
            {
                var px=pupil[0].GetDouble();var py=pupil[1].GetDouble();
                var rays=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,px,py,.55,false);
                using var trace=optic.SequentialRayTracer.Trace(rays,TraceRequest.Selected([1,2]));
                var a=trace.GetSurfaceSamples(1)[0]!.Value;var b=trace.GetSurfaceSamples(2)[0]!.Value;
                double N(string code,int surface)=>native.EnumerateArray().Single(r=>
                    r.GetProperty("input").GetProperty("code").GetString()==code &&
                    r.GetProperty("input").GetProperty("int1").GetInt32()==surface &&
                    r.GetProperty("input").GetProperty("data3").GetDouble()==px &&
                    r.GetProperty("input").GetProperty("data4").GetDouble()==py).GetProperty("value").GetDouble();
                var coreBody=(b.PhaseInclusiveOpticalPathLength??b.CumulativeOpticalPathLength)-(a.PhaseInclusiveOpticalPathLength??a.CumulativeOpticalPathLength);
                var nativeBody=N("OPTH",2)-N("OPTH",1);
                points.Add(new { px,py,coreBody,nativeBody,bodyResidual=coreBody-nativeBody,
                    entrance=new {coreX=a.Position.X,coreY=a.Position.Y,nativeX=N("REAX",1),nativeY=N("REAY",1),
                        global=profile.Evaluate(new(a.Position.X,a.Position.Y,0),550),queryIndex=profile.InterfaceRefractiveIndex(new(a.Position.X,a.Position.Y,0),550)},
                    exit=new {coreX=b.Position.X,coreY=b.Position.Y,nativeX=N("REAX",2),nativeY=N("REAY",2),
                        coreL=b.Direction.X,coreM=b.Direction.Y,nativeL=N("REAA",2),nativeM=N("REAB",2)} });
            }
            records.Add(new {model,points,nativeSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativePath)))});
        }
        JsonFiles.Write(output,new {diagnosticOnly=true,runtimeUsesNativeData=false,coreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))),records});
    }
}
