using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

internal static class NormalFormulaDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        var all = new List<object>();
        foreach (var config in new[] { 1, 2 })
        {
            var file = Path.Combine(repository, "artifacts/validation/afocal-native-resume-20261029-02", config == 1 ? "PORRO_PRISM-LH.POB" : "PORRO_PRISM-RH.POB");
            var vertices = new Dictionary<int, Vector3D>();
            var faces = new List<Vector3D[]>();
            foreach (var line in File.ReadLines(file))
            {
                var t = line.Split('!')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                if (t[0] == "V") vertices.Add(int.Parse(t[1]), new(D(t[2]), D(t[3]), D(t[4])));
                if (t[0] is "T" or "R")
                {
                    faces.Add([vertices[int.Parse(t[1])], vertices[int.Parse(t[2])], vertices[int.Parse(t[3])]]);
                    if (t[0] == "R") faces.Add([vertices[int.Parse(t[1])], vertices[int.Parse(t[3])], vertices[int.Parse(t[4])]]);
                }
            }
            var formulas = new List<(int Face, string Formula, Vector3D Normal)>();
            for (var f = 0; f < faces.Count; f++)
            {
                var v = faces[f];
                var forms = new[] {
                    ("edge", Cross(v[1]-v[0],v[2]-v[0])),
                    ("consecutive", Cross(v[1]-v[0],v[2]-v[1])),
                    ("absolute", Cross(v[0],v[1])+Cross(v[1],v[2])+Cross(v[2],v[0])),
                    ("newell", new Vector3D((v[0].Y-v[1].Y)*(v[0].Z+v[1].Z)+(v[1].Y-v[2].Y)*(v[1].Z+v[2].Z)+(v[2].Y-v[0].Y)*(v[2].Z+v[0].Z),
                        (v[0].Z-v[1].Z)*(v[0].X+v[1].X)+(v[1].Z-v[2].Z)*(v[1].X+v[2].X)+(v[2].Z-v[0].Z)*(v[2].X+v[0].X),
                        (v[0].X-v[1].X)*(v[0].Y+v[1].Y)+(v[1].X-v[2].X)*(v[1].Y+v[2].Y)+(v[2].X-v[0].X)*(v[2].Y+v[0].Y)))
                };
                foreach (var (name, n) in forms)
                {
                    formulas.Add((f, name+"/divide", n/n.Length));
                    formulas.Add((f, name+"/reciprocal", n*(1/n.Length)));
                }
            }
            var native = JsonFiles.Read<JsonElement>(Path.Combine(repository, $"artifacts/validation/fft-profile-normal-control-20261029-03/binocular-c{config}-normals/native-normals.json"));
            var matches = native.EnumerateArray().Where(p => p.GetProperty("hitObject").GetInt32() == 1).Select(p =>
            {
                var n = new Vector3D(p.GetProperty("normalX").GetDouble(),p.GetProperty("normalY").GetDouble(),p.GetProperty("normalZ").GetDouble());
                var ranked = formulas.Select(f => new { face = f.Face, formula = f.Formula,
                    error = Math.Min((f.Normal-n).Length,(f.Normal+n).Length), normal = f.Normal }).OrderBy(f => f.error).ToArray();
                return new { segment = p.GetProperty("segment").GetInt32(), native = n,
                    exact = ranked.Where(f => f.error == 0).ToArray(), best = ranked.Take(4).ToArray() };
            }).ToArray();
            all.Add(new { config, matches, formulas = formulas.Select(f => new { f.Face, f.Formula, f.Normal }) });
            Console.WriteLine(JsonSerializer.Serialize(new { config, matches }, JsonFiles.Options));
        }
        JsonFiles.Write(output, all);
        return 0;
    }
    static double D(string s) => double.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
    static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
}
