using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.ZemaxComparison;

// Diagnostic enumeration of mathematically defined four-node natural splines.
// Native values select no runtime coefficient or product material rule.
internal static class GridWindowCandidates
{
    internal static async Task Run(string study)
    {
        var output = Path.Combine(study, "window-candidates.json");
        if (File.Exists(output)) throw new IOException("Fresh candidate output required");
        var records = new List<object>();
        foreach (var model in JsonFiles.Read<JsonElement[]>(Path.Combine(study, "models.json")))
        {
            if (model.GetProperty("axis").GetString() != "X") continue;
            var optic = await new ZemaxZmxImporter().ImportFileAsync(model.GetProperty("input").GetString()!);
            var p = (GridGradientIndexProfile)((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).Profile;
            var y = model.GetProperty("y").GetDouble(); var z = model.GetProperty("z").GetDouble();
            var native = Path.Combine(model.GetProperty("native").GetString()!, "data.json");
            var series = JsonFiles.Read<JsonElement>(native).GetProperty("dataSeries")[0];
            var points = new List<object>();
            for (var n = 0; n < series.GetProperty("x").GetArrayLength(); n++)
            {
                var x = series.GetProperty("x")[n].GetDouble();
                var expected = series.GetProperty("y")[n][0].GetDouble();
                var candidates = new List<(int Start, double Value)>();
                for (var start = 0; start <= p.Nx - 4; start++)
                {
                    var f = new double[4];
                    // Nodes in X, continuous shared-Core interpolation in other axes.
                    for (var i = 0; i < 4; i++) f[i] = p.Evaluate(new(p.Origin.X + (start+i)*p.Spacing.X, y, z), 550).Index;
                    var q = (x - p.Origin.X) / p.Spacing.X - start;
                    var k = Math.Clamp((int)Math.Floor(q), 0, 2); var t = q-k; var s=1-t;
                    double[] m1=[4,-9,6,-1], m2=[-1,6,-9,4]; var value=0d;
                    for (var i=0;i<4;i++)
                    {
                        var l=k==1?m1[i]:k==2?m2[i]:0; var r=k==0?m1[i]:k==1?m2[i]:0;
                        value+=f[i]*((i==k?s:i==k+1?t:0)+((s*s*s-s)*l+(t*t*t-t)*r)/15);
                    }
                    candidates.Add((start,value));
                }
                var best=candidates.MinBy(c=>Math.Abs(c.Value-expected));
                var centered=(x-(p.Origin.X+(p.Nx-1)*p.Spacing.X/2))/p.Spacing.X;
                var declaredStart=Math.Clamp((p.Nx-3)/2+(int)(Math.Sign(centered)*Math.Ceiling(Math.Abs(centered))),0,p.Nx-4);
                var declared=candidates[declaredStart];
                points.Add(new { x, nativeIndex=expected, bestStart=best.Start, residual=best.Value-expected,
                    declaredStart, declaredResidual=declared.Value-expected,
                    exactStarts=candidates.Where(c=>Math.Abs(c.Value-expected)<1e-11).Select(c=>c.Start).ToArray() });
            }
            records.Add(new { model, points, nativeSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(native))) });
        }
        JsonFiles.Write(output,new { diagnosticOnly=true, runtimeUse=false, includesExtrapolatedWindows=true,
            declaredRule="Clamp((N-3)/2_integer + sign(q-center)*ceil(abs(q-center)), 0, N-4); four-node natural spline, continuous global other axes", records });
    }
}
