using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

// Offline higher-precision control only. Decimal arithmetic does not claim to
// emulate the native compiler or replace any formal Core runtime operation.
internal static class Energy32ExtendedSnellAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var source = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/native-step-audit-v3/inputs.json"));
        var controls = new List<object>();
        foreach (var roundEta in new[] { false, true })
        foreach (var roundCos in new[] { false, true })
        foreach (var roundRoot in new[] { false, true })
        {
            var rows = new List<object>(); var exact = 0; var nExact = 0; var squared = 0d; var max = 0d;
            foreach (var input in source.RootElement.EnumerateArray())
            {
                var d = V(input.GetProperty("direction")); var n = V(input.GetProperty("normal"));
                var dot = d.X * n.X + d.Y * n.Y + d.Z * n.Z;
                if (dot > 0) { dot = -dot; n = (-n.X, -n.Y, -n.Z); }
                var cos = -dot;
                var eta = D(input.GetProperty("n1").GetDouble()) / D(input.GetProperty("n2").GetDouble());
                if (roundEta) eta = D((double)eta);
                if (roundCos) cos = D((double)cos);
                var root = Sqrt(1 - eta * eta * (1 - cos * cos));
                if (roundRoot) root = D(Math.Sqrt((double)(1 - eta * eta * (1 - cos * cos))));
                var factor = eta * cos - root;
                var x = (double)(eta * d.X + factor * n.X);
                var y = (double)(eta * d.Y + factor * n.Y);
                var z = (double)(eta * d.Z + factor * n.Z);
                var expected = input.GetProperty("expected");
                var dx = x - expected.GetProperty("x").GetDouble();
                var dy = y - expected.GetProperty("y").GetDouble();
                var dz = z - expected.GetProperty("z").GetDouble();
                if (dx == 0 && dy == 0 && dz == 0) exact++;
                if (dz == 0) nExact++;
                squared += dx * dx + dy * dy + dz * dz;
                max = Math.Max(max, Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))));
                rows.Add(new { lens = input.GetProperty("lens").GetString(), surface = input.GetProperty("surface").GetInt32(), x, y, z, dx, dy, dz });
            }
            controls.Add(new { roundEta, roundCos, roundRoot, exact, nExact, count = source.RootElement.GetArrayLength(), squared, maximum = max, rows });
            Console.WriteLine($"eta53={roundEta}, cos53={roundCos}, sqrt53={roundRoot}: exact={exact}, n={nExact}, max={max:R}");
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), controls);
    }
    private static (decimal X, decimal Y, decimal Z) V(JsonElement e) => (D(e.GetProperty("x").GetDouble()), D(e.GetProperty("y").GetDouble()), D(e.GetProperty("z").GetDouble()));
    private static decimal D(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff) - 1075;
        var mantissa = (decimal)((bits & 0xfffffffffffff) | 1UL << 52);
        while (exponent < 0) { mantissa /= 2; exponent++; }
        while (exponent > 0) { mantissa *= 2; exponent--; }
        return (bits >> 63) != 0 ? -mantissa : value == 0 ? 0 : mantissa;
    }
    private static decimal Sqrt(decimal value)
    {
        if (value == 0) return 0;
        var x = (decimal)Math.Sqrt((double)value);
        for (var i = 0; i < 10; i++) x = (x + value / x) / 2;
        return x;
    }
}
