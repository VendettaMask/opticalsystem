using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class SamplingProbe
{
    internal static void Run(string audit,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh probe output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var m in JsonFiles.Read<JsonElement>(Path.Combine(audit,"baseline-v4/models.json")).EnumerateArray())
        {
            var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(m.GetProperty("snapshot").GetString()!));
            var w=optic.Wavelengths.FirstOrDefault(w=>w.IsPrimary)??optic.Wavelengths[0];
            var last=optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var probes=new List<object>();
            foreach(var h in new[]{.01,.001,.0001,.00001})
            {
                var coordinates=new[]{(0d,0d),(-h,0d),(h,0d),(0d,-h),(0d,h)};
                var wave=WavefrontEngine.GenerateChiefRaySamples(optic,(0,0),w,coordinates,aimAtStop:optic.RayAimingEnabled);
                var x=wave.Samples.Select(s=>last.ToLocalPoint(new(s.PupilX,s.PupilY,s.PupilZ))).ToArray();
                var dx=(x[2]-x[1])/h;var dy=(x[4]-x[3])/h;
                var traces=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,0,w.Micrometers,coordinates.Select(p=>new OpticalSystemDesign.Core.Raytrace.PupilSample(p.Item1,p.Item2,1)).ToArray(),optic.RayAimingEnabled));
                var directions=traces.Select(s=>last.ToLocalDirection(s!.Direction)).ToArray();
                probes.Add(new{h,referencePupilX=new[]{dx.X,dx.Y,dx.Z},referencePupilY=new[]{dy.X,dy.Y,dy.Z},areaDiameter=Math.Sqrt(Math.Abs(dx.X*dy.Y-dx.Y*dy.X)),
                    chiefDirections=directions.Select(d=>new[]{d.X,d.Y,d.Z}).ToArray()});
            }
            var geometry=new List<object>();
            foreach(var h in new[]{.001,.0001})foreach(var a in new[]{.001,.0001,.00001})
            {
                var g=ExitPupilGeometryEngine.Compute(optic,(0,0),w,new[]{(0d,0d),(h,0d),(0d,h)},a);
                geometry.Add(new{h,a,center=last.ToLocalPoint(g.Center),diameter=2*g.DisplayRadiusMillimeters/h,points=g.Points});
            }
            rows.Add(new{id=m.GetProperty("id").GetString(),configuration=m.GetProperty("configuration").GetInt32(),paraxialDiameter=optic.Paraxial.EstimateExitPupilDiameter(),paraxialPosition=optic.Paraxial.EstimateExitPupilLocation(),probes,geometry});
        }
        JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
    }
}
