using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.ZemaxComparison;

internal static class GridTensorWindows
{
    internal static async Task Run(string study)
    {
        var output=Path.Combine(study,"tensor-window-assessment.json");
        if(File.Exists(output))throw new IOException("Fresh tensor-window scope required");
        var records=new List<object>();
        foreach(var model in JsonFiles.Read<JsonElement[]>(Path.Combine(study,"models.json")))
        {
            var optic=await new ZemaxZmxImporter().ImportFileAsync(model.GetProperty("input").GetString()!);
            var g=(GridGradientIndexProfile)((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).Profile;
            var native=model.GetProperty("native").GetString()!;
            var series=JsonFiles.Read<JsonElement>(Path.Combine(native,"data.json")).GetProperty("dataSeries")[0];
            var axis=model.GetProperty("axis").GetString()!;
            if(axis!="X")continue;
            var y=model.GetProperty("y").GetDouble();var z=model.GetProperty("z").GetDouble();
            var nodes=new double[g.Nx,g.Ny];
            for(var a=0;a<g.Nx;a++)for(var b=0;b<g.Ny;b++)nodes[a,b]=g.Evaluate(new(g.Origin.X+a*g.Spacing.X,g.Origin.Y+b*g.Spacing.Y,z),550).Index;
            var points=new List<object>();
            for(var i=0;i<series.GetProperty("x").GetArrayLength();i++)
            {
                var x=series.GetProperty("x")[i].GetDouble();var n=series.GetProperty("y")[i][0].GetDouble();
                var best=double.PositiveInfinity;var bx=-1;var by=-1;var pairs=new List<int[]>();
                for(var sx=0;sx<=g.Nx-4;sx++)for(var sy=0;sy<=g.Ny-4;sy++)
                {
                    var wx=Weights((x-g.Origin.X)/g.Spacing.X-sx);var wy=Weights((y-g.Origin.Y)/g.Spacing.Y-sy);
                    var v=0d;for(var a=0;a<4;a++)for(var b=0;b<4;b++)v+=nodes[sx+a,sy+b]*wx[a]*wy[b];
                    var residual=Math.Abs(v-n);if(residual<best){best=residual;bx=sx;by=sy;}
                    if(residual<1e-11)pairs.Add([sx,sy]);
                }
                points.Add(new {x,y,z,nativeIndex=n,bestX=bx,bestY=by,bestResidual=best,exactPairs=pairs});
            }
            records.Add(new {model,origin=g.Origin,spacing=g.Spacing,g.Nx,g.Ny,g.Nz,points});
        }
        JsonFiles.Write(output,new {diagnosticOnly=true,runtimeUse=false,selectionFromNativeMustNotBePromoted=true,records});
    }
    private static double[] Weights(double q)
    {
        var k=Math.Clamp((int)Math.Floor(q),0,2);var t=q-k;var s=1-t;
        double[] m1=[4,-9,6,-1],m2=[-1,6,-9,4],w=new double[4];
        for(var i=0;i<4;i++)
        {
            var left=k==1?m1[i]:k==2?m2[i]:0;var right=k==0?m1[i]:k==1?m2[i]:0;
            w[i]=(i==k?s:i==k+1?t:0)+((s*s*s-s)*left+(t*t*t-t)*right)/15;
        }
        return w;
    }
}
