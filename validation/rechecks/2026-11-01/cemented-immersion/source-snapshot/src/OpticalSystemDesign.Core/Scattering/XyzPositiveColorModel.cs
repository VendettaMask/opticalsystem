using System.Security.Cryptography;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record XyzPositiveColorNode(string SampleId, double X, double Y, double Z, IReadOnlyList<double> Ratios);
/// <summary>Positive empirical colour transfer. Normalized XYZ selects neighbours; the
/// weighted radiometric ratios are nonnegative and scale linearly with input intensity.</summary>
public sealed record XyzPositiveColorModel(int NeighbourCount, IReadOnlyList<XyzPositiveColorNode> Nodes, string Method = "PositiveChromaticityKnn/1")
{
    public const int MaximumNodes = 4096;
    internal XyzPositiveColorModel Freeze(int bands)
    {
        if (bands is < 1 or > 32 || Method != "PositiveChromaticityKnn/1" || NeighbourCount is < 1 or > 16 || Nodes is null || Nodes.Count < Math.Max(3, NeighbourCount) || Nodes.Count > MaximumNodes
            || (long)Nodes.Count * bands > XyzRadiometricCalibrator.MaximumObservations || Nodes.Any(p => p is null || string.IsNullOrWhiteSpace(p.SampleId) || p.SampleId.Length > 256
                || new[] { p.X, p.Y, p.Z }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Math.Abs(p.X + p.Y + p.Z - 1) > 2e-15
                || p.Ratios is null || p.Ratios.Count != bands || p.Ratios.Any(v => !double.IsFinite(v) || v < 0 || v > 1e12))
            || Nodes.Select(p => p.SampleId).Distinct(StringComparer.Ordinal).Count() != Nodes.Count) throw new InvalidDataException("XYZ 正值颜色模型的节点、邻域或比值无效/超出预算。");
        return this with { Nodes = Array.AsReadOnly(Nodes.OrderBy(p => p.SampleId, StringComparer.Ordinal).Select(p => p with { Ratios = Array.AsReadOnly(p.Ratios.ToArray()) }).ToArray()) };
    }
    internal string Digest()
    {
        using var bytes = new MemoryStream(); using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Method); writer.Write(NeighbourCount); writer.Write(Nodes.Count);
            foreach (var p in Nodes) { writer.Write(p.SampleId); writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); writer.Write(p.Ratios.Count); foreach (var r in p.Ratios) writer.Write(r); }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }
    internal double[] Evaluate(double x, double y, double z)
    {
        var sum = x + y + z; var bands = Nodes[0].Ratios.Count;
        if (!double.IsFinite(sum) || x < 0 || y < 0 || z < 0) throw new InvalidDataException("XYZ 颜色输入无效。");
        if (sum == 0) return new double[bands]; x /= sum; y /= sum; z /= sum;
        var distances = Enumerable.Repeat(double.PositiveInfinity, NeighbourCount).ToArray(); var indices = new int[NeighbourCount];
        var exact = new double[bands]; var exactCount = 0;
        for (var i = 0; i < Nodes.Count; i++)
        {
            if ((i & 127) == 0) ComputationCancellation.ThrowIfCancellationRequested();
            var p = Nodes[i]; var d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) + (p.Z - z) * (p.Z - z);
            if (d == 0) { exactCount++; for (var b = 0; b < bands; b++) exact[b] += p.Ratios[b]; }
            if (d >= distances[^1]) continue; var at = NeighbourCount - 1;
            while (at > 0 && d < distances[at - 1]) { distances[at] = distances[at - 1]; indices[at] = indices[at - 1]; at--; }
            distances[at] = d; indices[at] = i;
        }
        if (exactCount > 0) return exact.Select(v => sum * (v / exactCount)).ToArray();
        var result = new double[bands]; double norm = 0;
        for (var k = 0; k < NeighbourCount; k++) { var weight = distances[0] / distances[k]; norm += weight; for (var b = 0; b < bands; b++) result[b] += weight * Nodes[indices[k]].Ratios[b]; }
        return result.Select(v => sum * (v / norm)).ToArray();
    }
}
