using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class NativeSphereRules
{
    internal static void Run(string study)
    {
        var rows = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls", "model-controls.json"))
            .GetProperty("rows").EnumerateArray().ToArray();
        double Value(string code, double px, double py) => rows.Single(r =>
        {
            var p = r.GetProperty("input");
            return p.GetProperty("code").GetString() == code && p.GetProperty("int1").GetInt32() == 4
                && p.GetProperty("data3").GetDouble() == px && p.GetProperty("data4").GetDouble() == py;
        }).GetProperty("value").GetDouble();
        var cx = Value("REAX", 0, 0); var cy = Value("REAY", 0, 0); var chiefPath = Value("OPTH", 0, 0);
        var output = new List<object>();
        foreach (var row in rows.Where(r => r.GetProperty("input").GetProperty("code").GetString() == "OPDX"))
        {
            var p = row.GetProperty("input"); var px = p.GetProperty("data3").GetDouble(); var py = p.GetProperty("data4").GetDouble();
            if (px == 0 && py == 0) continue;
            var x = Value("REAX", px, py) - cx; var y = Value("REAY", px, py) - cy;
            var l = Value("REAA", px, py); var m = Value("REAB", px, py); var n = Value("REAC", px, py);
            var path = Value("OPTH", px, py); var target = row.GetProperty("value").GetDouble();
            double Opd(double radius)
            {
                var a = l * l + m * m + n * n; var b = -2 * (l * x + m * y);
                var c = x * x + y * y - radius * radius;
                var distance = (-b + Math.Sqrt(Math.Max(0, b * b - 4 * a * c))) / (2 * a);
                return (chiefPath - radius - path + distance) / .00055;
            }
            double lo = .5, hi = 5;
            if ((Opd(lo) - target) * (Opd(hi) - target) > 0) throw new InvalidDataException("No reference-sphere root in diagnostic interval");
            for (var i = 0; i < 70; i++)
            {
                var mid = (lo + hi) / 2;
                if ((Opd(lo) - target) * (Opd(mid) - target) <= 0) hi = mid; else lo = mid;
            }
            output.Add(new { px, py, nativeOpd = target, inferredRadius = (lo + hi) / 2 });
        }
        JsonFiles.Write(Path.Combine(study, "native-sphere-diagnostic.json"), new { diagnosticOnly = true,
            method = "Infer each radius independently using only native OPTH, image hit/direction and OPDX; never a product parameter", cx, cy, chiefPath, rows = output });
        foreach (var item in output) Console.WriteLine(JsonSerializer.Serialize(item));
    }
}
