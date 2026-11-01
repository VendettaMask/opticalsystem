using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

internal static class GridJacobianRules
{
    internal static async Task Run(string root)
    {
        var results=new List<object>();
        foreach(var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
        {
            var id=job.GetProperty("id").GetString()!;var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
            (double Y,double U) Trace(double height,double slope)
            {
                var ray=new RealRay(new(0,height-slope, -1),new(0,slope,1),550);
                using var trace=optic.SequentialRayTracer.Trace(new RealRayBundle(new[]{ray}),TraceRequest.Selected([3]));
                var sample=trace.GetSurfaceSamples(3)[0]!.Value;return(sample.Position.Y,sample.Direction.Y/sample.Direction.Z);
            }
            foreach(var h in new[]{.001,.0001,.00001})
            {
                var a=Trace(h,0);var b=Trace(-h,0);var c=Trace(0,h);var d=Trace(0,-h);var chief=Trace(0,0);
                var A=(a.Y-b.Y)/(2*h);var C=(a.U-b.U)/(2*h);var B=(c.Y-d.Y)/(2*h);var D=(c.U-d.U)/(2*h);
                results.Add(new{id,h,A,B,C,D,efl=-1/C,chief});
            }
        }
        File.WriteAllText(Path.Combine(root,"real-jacobian-controls.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine(JsonSerializer.Serialize(r));
    }
}
