using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpticalSystemDesign.Core.Scattering;

public enum BsdfImportSides { FileDefined, Front, Back, BothDeclaredIdentical }
public sealed record BsdfFileImportOptions(BsdfImportSides Sides = BsdfImportSides.FileDefined,
    double? ZemaxWavelengthNanometers = null, bool ZemaxRadiometric = false, double IncidentIndex = 1, double TransmittedIndex = 1,
    string? ZemaxXyzCalibrationPath = null, SpeosPeakImportOptions? SpeosPeak = null);

/// <summary>Explicit content dispatch. Vendor relative distributions are converted once, with provenance.</summary>
public static class BsdfFileImporter
{
    public const string FixedCsvHeader = "side,wavelength_nm,theta_i_deg,phi_i_deg,theta_o_deg,phi_o_deg,brdf_sr_inv,btdf_sr_inv";
    public static MeasuredBsdfData Read(string path, BsdfFileImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); if (!Enum.IsDefined(options.Sides)) throw new ArgumentException("BSDF 入射侧声明无效。");
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 1 or > MeasuredBsdfCsv.MaximumBytes) throw new InvalidDataException("BSDF 文件必须在 1 字节至 32 MiB 之间。");
        using var bytes = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = file.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (bytes.Length + read > MeasuredBsdfCsv.MaximumBytes) throw new InvalidDataException("BSDF 文件超过 32 MiB。");
            bytes.Write(buffer, 0, read);
        }
        var raw = bytes.ToArray();
        using var scope = Services.ComputationCancellation.Push(cancellationToken);
        if (SpeosBinaryBsdfReader.IsBinary(raw)) return SpeosBinaryBsdfReader.Read(raw, path, options, cancellationToken);
        var text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF');
        if (text.StartsWith("# OpticalSystemDesign.MeasuredBsdfGrid/1", StringComparison.Ordinal))
        {
            return MeasuredBsdfCsv.Read(path, cancellationToken);
        }
        var sha = Convert.ToHexString(SHA256.HashData(raw));
        MeasuredBsdfData result;
        if (text.StartsWith("# OpticalSystemDesign.MeasuredBsdfGrid/2", StringComparison.Ordinal)) result = ReadFixedCsv(text, path, sha, options);
        else if (text.StartsWith("OPTIS", StringComparison.Ordinal))
        {
            result = SpeosBsdfReader.Read(text, path, sha, options, cancellationToken);
            if (options.SpeosPeak is { } peak) result = SpeosPeakBuilder.Apply(result, options.IncidentIndex, options.TransmittedIndex, peak.WhiteReferenceNanometers, manual: peak);
        }
        else if (ZemaxBsdfReader.IsFormat(text)) result = ZemaxBsdfReader.Read(text, path, sha, options, cancellationToken);
        else throw new InvalidDataException("不支持的 BSDF 格式；需要声明的原生 CSV、Speos 或 Zemax Tabular BSDF，不能猜测单位或光度到辐射度转换。");
        cancellationToken.ThrowIfCancellationRequested(); return result;
    }
    private static MeasuredBsdfData ReadFixedCsv(string text, string path, string sha, BsdfFileImportOptions options)
    {
        using var reader = new StringReader(text); reader.ReadLine(); string? line; var name = Path.GetFileNameWithoutExtension(path); bool header = false;
        var points = new List<MeasuredBsdfPoint>();
        while ((line = reader.ReadLine()) is not null)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested(); line = line.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith('#')) { if (line.StartsWith("# name=", StringComparison.Ordinal)) name = line[7..].Trim(); continue; }
            if (!header) { if (line != FixedCsvHeader) throw new InvalidDataException("固定轴 BSDF CSV 列名无效。"); header = true; continue; }
            var c = line.Split(',', 9); if (c.Length != 8 || !Enum.TryParse<BsdfSide>(c[0], true, out var side) || !c[0].Trim().Equals(side.ToString(), StringComparison.OrdinalIgnoreCase) || !Enum.IsDefined(side)) throw new InvalidDataException("固定轴 BSDF CSV 行无效。");
            double N(int i) => double.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException("BSDF CSV 数值无效。");
            if (points.Count >= MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("BSDF 节点超限。");
            points.Add(new(side, N(1), N(2), N(4), N(5), N(6), N(7), N(3)));
        }
        if (!header) throw new InvalidDataException("BSDF CSV 缺少列名。");
        return new(name, Path.GetFileName(path), sha, points, coordinates: BsdfCoordinates.FixedMaterialAxes, sourceFormat: "NativeCsvFixedAxes/2");
    }
}

internal static class SpeosBsdfReader
{
    private sealed record Block(bool Transmit, double Rotation, double Incidence, double Wavelength, double Energy, Profile Shape);
    private sealed record Spectrum(double Theta, double Rotation, double[] Wavelengths, double[] Fractions);
    internal static MeasuredBsdfData Read(string text, string path, string sha, BsdfFileImportOptions options, CancellationToken token, bool allowProjected = false, bool compactSpectra = false)
    {
        if (options.Sides == BsdfImportSides.FileDefined) throw new InvalidDataException("Speos 文件没有本程序的正反侧定义；请显式选择 Front、Back 或声明正反侧相同。");
        var r = new Reader(text, token); var title = r.Line();
        var v11 = title.Equals("OPTIS - Anisotropic BSDF surface file v11", StringComparison.OrdinalIgnoreCase) || title.Equals("OPTIS - Anisotropic BSDF surface file v11.0", StringComparison.OrdinalIgnoreCase);
        if (!v11 && !title.Equals("OPTIS - BRDF surface file v8.0", StringComparison.OrdinalIgnoreCase)
            && !title.Equals("OPTIS - Anisotropic BSDF surface file v8.0", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("未识别的 Speos 表面文件头。");
        var anisotropic = title.Contains("Anisotropic", StringComparison.OrdinalIgnoreCase);
        compactSpectra |= v11;
        if (r.Integer(0, 1) != 0) throw new InvalidDataException("不支持 Speos 二进制模式；请从原软件另存为文本。");
        r.Line(); var description = r.Description(r.Integer(0, 2048));
        double[]? originalVector = null;
        if (anisotropic) { originalVector = r.Numbers(3); if (originalVector.All(x => x == 0)) throw new InvalidDataException("Speos 各向异性方向向量为零。"); }
        var flags = r.Numbers(2); if (flags.Any(v => v is not (0 or 1)) || flags.Sum() == 0) throw new InvalidDataException("Speos R/T 标志无效。");
        var projected = r.Integer(0, 1) == 0;
        if (projected && !allowProjected && !v11) throw new InvalidDataException("此文本入口需要 BSDF 值模式；不能把 I 当成 BRDF。");
        bool fixedAxes = anisotropic;
        var blocks = new List<Block>(); var referenceNotes = new List<string>();
        SpeosSpectralDefinition? spectralDefinition = null;
        if (!anisotropic)
        {
            var dimensions = r.Numbers(2); int ni = Count(dimensions[0], 64), nw = Count(dimensions[1], 32);
            var incidences = Axis(r.Numbers(ni), 0, 90); var wavelengths = Axis(r.Numbers(nw), 200, 20000);
            for (var lobe = 0; lobe < 2; lobe++) if (flags[lobe] == 1) foreach (var i in incidences) foreach (var w in wavelengths)
            {
                var percent = r.Numbers(1)[0]; Fraction(percent); var shape = ReadProfile(r, lobe == 1, projected);
                blocks.Add(new(lobe == 1, 0, i, w, percent / 100, shape));
            }
        }
        else
        {
            var axes = new Dictionary<bool, (double[] Rotations, double[] Incidences)>();
            for (var lobe = 0; lobe < 2; lobe++) if (flags[lobe] == 1)
            {
                var rotations = Axis(r.Numbers(r.Integer(1, 65)), 0, 360);
                if (rotations[0] != 0 || rotations.Length > 1 && rotations[^1] is not (90 or 180 or 360)) throw new InvalidDataException("Speos 各向异性旋转须为单个 0° 或覆盖 0–90/180/360°。");
                double[]? common = null;
                foreach (var a in rotations)
                {
                    var inc = Axis(r.Numbers(r.Integer(1, 64)), 0, 90);
                    if (common is not null && !common.SequenceEqual(inc)) throw new InvalidDataException("暂不支持不同旋转上的不同入射角网格。"); common = inc;
                }
                axes.Add(lobe == 1, (rotations, common!));
            }
            var spectra = new Dictionary<bool, Spectrum>();
            fixedAxes = axes.Values.Any(a => a.Rotations.Any(rotation => rotation is not (0 or 360)));
            for (var lobe = 0; lobe < 2; lobe++) if (flags[lobe] == 1)
            {
                var angles = r.Numbers(2); if (angles[0] is < 0 or > 90 || angles[1] is < 0 or > 360) throw new InvalidDataException("Speos 光谱参考角无效。");
                r.Line(); var n = r.Integer(1, compactSpectra ? 256 : 32); var wavelengths = new double[n]; var fractions = new double[n];
                for (var k = 0; k < n; k++) { var values = r.Numbers(2); wavelengths[k] = values[0]; Fraction(values[1]); fractions[k] = values[1] / 100; }
                Axis(wavelengths, 200, 20000); spectra.Add(lobe == 1, new(angles[0], angles[1], wavelengths, fractions));
            }
            foreach (var lobe in axes.Keys.Order()) foreach (var a in axes[lobe].Rotations) foreach (var i in axes[lobe].Incidences)
                blocks.Add(new(lobe, a, i, 0, 0, ReadProfile(r, lobe, projected)));
            // Spectrum is measured at its declared reference incidence/orientation. Preserve relative incidence amplitudes.
            var normalized = new List<Block>();
            foreach (var lobe in axes.Keys)
            {
                var s = spectra[lobe]; var group = blocks.Where(b => b.Transmit == lobe).ToArray();
                var reference = ReferenceIntegral(group, s.Theta, s.Rotation);
                referenceNotes.Add(string.Format(CultureInfo.InvariantCulture, "{0} reference theta={1:G17}, anisotropy={2:G17}, raw integral={3:G17}; spectrum nm:fraction={4}",
                    lobe ? "T" : "R", s.Theta, s.Rotation, reference, string.Join(",", s.Wavelengths.Select((w, k) => w.ToString("G17", CultureInfo.InvariantCulture) + ":" + s.Fractions[k].ToString("G17", CultureInfo.InvariantCulture)))));
                if (compactSpectra) referenceNotes[^1] = string.Format(CultureInfo.InvariantCulture, "{0} reference theta={1:G17}, anisotropy={2:G17}, raw integral={3:G17}; {4} spectral samples retained separately; 550 nm is an angular storage tag, not a measurement wavelength.", lobe ? "T" : "R", s.Theta, s.Rotation, reference, s.Wavelengths.Length);
                foreach (var b in group) for (var k = 0; k < (compactSpectra ? 1 : s.Wavelengths.Length); k++)
                {
                    var fraction = compactSpectra ? s.Fractions.Max() : s.Fractions[k];
                    normalized.Add(b with { Wavelength = compactSpectra ? 550 : s.Wavelengths[k], Energy = reference > 0 ? fraction * b.Shape.Integral / reference : fraction == 0 ? 0 : throw new InvalidDataException("Speos 光谱非零但参考角分布为零。") });
                }
            }
            if (compactSpectra) spectralDefinition = new(spectra.Select(s => new SpeosSpectralLobe(s.Key, s.Value.Wavelengths, s.Value.Fractions, Math.Max(s.Value.Fractions.Max(), double.Epsilon))).ToArray());
            blocks = normalized;
        }
        r.End(v11);
        foreach (var group in blocks.GroupBy(b => (b.Transmit, b.Incidence, b.Wavelength)))
        {
            var first = group.FirstOrDefault(b => b.Rotation == 0); var last = group.FirstOrDefault(b => b.Rotation == 360);
            if (first is not null && last is not null && (!MeasuredBsdfGrid.Close(first.Energy, last.Energy) || !first.Shape.Same(last.Shape)))
                throw new InvalidDataException("Speos 0/360° 材料旋转接缝不一致。");
        }
        // Expand only the documented material-axis symmetry; mirror outgoing azimuth together with rotation.
        if (fixedAxes)
        {
            var expanded = new List<Block>();
            foreach (var group in blocks.GroupBy(b => b.Transmit))
            {
                var limit = group.Max(b => b.Rotation);
                foreach (var b in group)
                {
                    expanded.Add(b);
                    if (limit <= 180) expanded.Add(b with { Rotation = 360 - b.Rotation, Shape = b.Shape.Mirror() });
                    if (limit == 90) { expanded.Add(b with { Rotation = 180 + b.Rotation }); expanded.Add(b with { Rotation = 180 - b.Rotation, Shape = b.Shape.Mirror() }); }
                }
            }
            blocks = Deduplicate(expanded);
        }
        var points = ConvertBlocks(blocks, fixedAxes, options.Sides);
        var note = $"Speos text v8.0; side assignment={options.Sides}; shape BSDF normalized to file R/T; anisotropy vector in measurement coordinates="
            + (originalVector is null ? "none" : string.Join(",", originalVector.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))))
            + "; target axis is explicitly object-local +X unless edited; no global pose inferred. " + string.Join("; ", referenceNotes);
        return new(string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(path) : description.Trim(), Path.GetFileName(path), sha, points,
            coordinates: fixedAxes ? BsdfCoordinates.FixedMaterialAxes : BsdfCoordinates.IncidentPlane,
            sourceFormat: v11 ? "SpeosAnisotropicBsdfText11/1" : anisotropic ? "SpeosAnisotropicBsdfText8/1" : "SpeosBrdfText8/1", importNotes: v11 ? note.Replace("text v8.0", "text v11") : note,
            storedQuantity: projected ? BsdfStoredQuantity.ProjectedBsdf : BsdfStoredQuantity.Bsdf, speosSpectral: spectralDefinition);
    }
    private static List<Block> Deduplicate(List<Block> input)
    {
        var output = new List<Block>();
        foreach (var group in input.GroupBy(b => (b.Transmit, b.Rotation, b.Incidence, b.Wavelength)))
        {
            var first = group.First(); foreach (var other in group.Skip(1))
                if (!MeasuredBsdfGrid.Close(first.Energy, other.Energy) || !first.Shape.Same(other.Shape)) throw new InvalidDataException("Speos 声明的对称边界不一致。");
            output.Add(first);
        }
        return output;
    }
    private static double ReferenceIntegral(Block[] blocks, double theta, double rotation)
    {
        var angles = blocks.Select(b => b.Incidence).Distinct().Order().ToArray(); var rotations = blocks.Select(b => b.Rotation).Distinct().Order().ToArray();
        var t = MeasuredBsdfGrid.Bracket(angles, theta); var a = MeasuredBsdfGrid.Bracket(rotations, rotation);
        var incidentWeight = t.Low == t.High ? 0 : (MeasuredBsdfGrid.Cos(theta) - MeasuredBsdfGrid.Cos(angles[t.Low]))
            / (MeasuredBsdfGrid.Cos(angles[t.High]) - MeasuredBsdfGrid.Cos(angles[t.Low]));
        double At(int ti, int ai) => blocks.Single(b => b.Incidence == angles[ti] && b.Rotation == rotations[ai]).Shape.Integral;
        return (At(t.Low, a.Low) * (1 - incidentWeight) + At(t.High, a.Low) * incidentWeight) * (1 - a.T)
            + (At(t.Low, a.High) * (1 - incidentWeight) + At(t.High, a.High) * incidentWeight) * a.T;
    }
    private static IReadOnlyList<MeasuredBsdfPoint> ConvertBlocks(List<Block> blocks, bool fixedAxes, BsdfImportSides sides)
    {
        var result = new List<MeasuredBsdfPoint>(); var hasR = blocks.Any(b => !b.Transmit); var hasT = blocks.Any(b => b.Transmit);
        var input = blocks.GroupBy(b => (b.Wavelength, b.Incidence, Rotation: fixedAxes ? Wrap(180 - b.Rotation) : 0)).ToArray();
        foreach (var group in input)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var rb = group.FirstOrDefault(b => !b.Transmit); var tb = group.FirstOrDefault(b => b.Transmit);
            if (hasR && rb is null || hasT && tb is null) throw new InvalidDataException("暂不支持 R/T 不同的波长或入射网格。");
            var nativeRotation = group.First().Rotation; var shift = fixedAxes ? -nativeRotation : 180d;
            var mu = group.SelectMany(b => b.Shape.Theta).Distinct().Order().ToArray();
            var phi = group.SelectMany(b => b.Shape.Phi.Select(p => Wrap(p + shift))).Append(0).Append(360).Distinct().Order().ToArray();
            foreach (var theta in mu) foreach (var p in phi)
            {
                double Value(Block? b) => b is null || b.Energy == 0 ? 0 : b.Shape.Integral > 0
                    ? b.Shape.Value(theta, Wrap(p - shift)) * b.Energy / b.Shape.Integral : throw new InvalidDataException("Speos 非零 R/T 对应零角分布。");
                var r = Value(rb); var t = Value(tb);
                var incPhis = fixedAxes && group.Key.Rotation == 0 ? new[] { 0d, 360d } : new[] { group.Key.Rotation };
                foreach (var ip in incPhis) foreach (var side in sides == BsdfImportSides.BothDeclaredIdentical ? new[] { BsdfSide.Front, BsdfSide.Back } : new[] { sides == BsdfImportSides.Front ? BsdfSide.Front : BsdfSide.Back })
                {
                    if (result.Count >= MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("商业 BSDF 展开后节点超过 131072；不自动降采样。");
                    result.Add(new(side, group.Key.Wavelength, group.Key.Incidence, theta, p, r, t, ip));
                }
            }
        }
        // A vendor rotation grid need not contain 180°, which maps to native incoming azimuth 0°.
        // Split its periodic cell at 0° using the same convex interpolation as runtime evaluation.
        if (fixedAxes && !result.Any(p => p.IncidentPhiDegrees == 0))
        {
            foreach (var group in result.GroupBy(p => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees)).ToArray())
            {
                Services.ComputationCancellation.ThrowIfCancellationRequested();
                var lo = group.Min(p => p.IncidentPhiDegrees); var hi = group.Max(p => p.IncidentPhiDegrees);
                var low = group.Where(p => p.IncidentPhiDegrees == lo).ToArray(); var high = group.Where(p => p.IncidentPhiDegrees == hi).ToArray();
                Profile Make(MeasuredBsdfPoint[] nodes, bool transmit)
                {
                    var lookup = nodes.ToDictionary(p => (p.OutgoingThetaDegrees, p.OutgoingPhiDegrees));
                    var theta = nodes.Select(p => p.OutgoingThetaDegrees).Distinct().Order().ToArray();
                    var phi = nodes.Select(p => p.OutgoingPhiDegrees).Distinct().Order().ToArray();
                    return new(theta, phi, theta.Select(t => phi.Select(a =>
                    { var p = lookup[(t, a)]; return transmit ? p.BtdfPerSteradian : p.BrdfPerSteradian; }).ToArray()).ToArray());
                }
                var lr = Make(low, false); var lt = Make(low, true); var hr = Make(high, false); var ht = Make(high, true);
                var weight = (360 - hi) / (360 + lo - hi);
                foreach (var theta in lr.Theta.Concat(hr.Theta).Distinct().Order()) foreach (var phi in lr.Phi.Concat(hr.Phi).Distinct().Order())
                {
                    var r = hr.Value(theta, phi) * (1 - weight) + lr.Value(theta, phi) * weight;
                    var t = ht.Value(theta, phi) * (1 - weight) + lt.Value(theta, phi) * weight;
                    foreach (var azimuth in new[] { 0d, 360d })
                    {
                        if (result.Count >= MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("商业 BSDF 展开后节点超过 131072；不自动降采样。");
                        result.Add(new(group.Key.Side, group.Key.WavelengthNanometers, group.Key.IncidentThetaDegrees, theta, phi, r, t, azimuth));
                    }
                }
            }
        }
        return result;
    }
    private static double Wrap(double a) { var v = a % 360; return v < 0 ? v + 360 : v; }
    private static void Fraction(double p) { if (p is < 0 or > 100 || !double.IsFinite(p)) throw new InvalidDataException("Speos R/T 百分比须在 0–100 之间。"); }
    private static int Count(double n, int max) => n >= 1 && n <= max && n == Math.Truncate(n) ? (int)n : throw new InvalidDataException("Speos 节点数量无效或超限。");
    private static double[] Axis(double[] values, double min, double max)
    {
        if (values.Any(v => !double.IsFinite(v) || v < min || v > max) || values.Zip(values.Skip(1)).Any(p => p.First >= p.Second)) throw new InvalidDataException("Speos 轴必须严格递增且在声明范围内。"); return values;
    }
    private static Profile ReadProfile(Reader r, bool transmit, bool projected)
    {
        var sizes = r.Numbers(2); int nt = Count(sizes[0], 129), np = Count(sizes[1], 257); r.AddNodes(checked(nt * np));
        var phi = Axis(r.Numbers(np), 0, 360); if (phi.Length < 2 || phi[0] != 0 || phi[^1] != 360) throw new InvalidDataException("Speos 出射方位须覆盖完整 0–360°；不猜测缺失区域。");
        var theta = new double[nt]; var values = new double[nt][];
        for (var i = 0; i < nt; i++)
        {
            var row = r.Numbers(np + 1); theta[i] = row[0]; values[i] = row[1..];
            if (values[i].Any(v => v < 0)) throw new InvalidDataException("Speos 角分布含负值。");
        }
        Axis(theta, transmit ? 90 : 0, transmit ? 180 : 90);
        if (theta.Length < 2 || theta[0] != (transmit ? 90 : 0) || theta[^1] != (transmit ? 180 : 90)) throw new InvalidDataException("Speos 表格未覆盖完整出射半球。");
        if (transmit) { Array.Reverse(theta); for (var k = 0; k < theta.Length; k++) theta[k] = 180 - theta[k]; Array.Reverse(values); }
        return new(theta, phi, values, projected);
    }
    private sealed class Profile
    {
        public double[] Theta { get; } public double[] Phi { get; } private readonly double[][] _v;
        public double Integral { get; }
        private readonly bool _projected;
        public Profile(double[] theta, double[] phi, double[][] values, bool projected = false)
        {
            _projected = projected;
            Theta = theta; Phi = phi; _v = values; double sum = 0;
            for (var i = 0; i < theta.Length - 1; i++) for (var a = 0; a < phi.Length - 1; a++)
            {
                var lo = MeasuredBsdfGrid.Cos(theta[i + 1]); var hi = MeasuredBsdfGrid.Cos(theta[i]); var d = hi - lo;
                sum += (phi[a + 1] - phi[a]) * Math.PI / 360 * d
                    * ((values[i + 1][a] + values[i + 1][a + 1]) * (projected ? .5 : lo / 2 + d / 6) + (values[i][a] + values[i][a + 1]) * (projected ? .5 : lo / 2 + d / 3));
            }
            Integral = sum;
            for (var i = 0; i < theta.Length; i++) if (!MeasuredBsdfGrid.Close(values[i][0], values[i][^1])) throw new InvalidDataException("Speos 出射接缝不连续。");
        }
        public double Value(double theta, double phi)
        {
            var t = MeasuredBsdfGrid.Bracket(Theta, theta); var a = MeasuredBsdfGrid.Bracket(Phi, phi);
            var muLow = MeasuredBsdfGrid.Cos(Theta[t.Low]); var muHigh = MeasuredBsdfGrid.Cos(Theta[t.High]);
            var f = t.Low == t.High ? 0 : (MeasuredBsdfGrid.Cos(theta) - muLow) / (muHigh - muLow);
            return (_v[t.Low][a.Low] * (1 - a.T) + _v[t.Low][a.High] * a.T) * (1 - f)
                + (_v[t.High][a.Low] * (1 - a.T) + _v[t.High][a.High] * a.T) * f;
        }
        public Profile Mirror() => new(Theta, Phi.Reverse().Select(p => 360 - p).ToArray(), _v.Select(row => row.Reverse().ToArray()).ToArray(), _projected);
        public bool Same(Profile b) => Theta.Concat(b.Theta).Distinct().All(t => Phi.Concat(b.Phi).Distinct().All(p => MeasuredBsdfGrid.Close(Value(t, p), b.Value(t, p))));
    }
    private sealed class Reader(string text, CancellationToken token)
    {
        private readonly StringReader _reader = new(text); private int _nodes;
        public string Line() { token.ThrowIfCancellationRequested(); return _reader.ReadLine() ?? throw new InvalidDataException("Speos 文件提前结束。"); }
        public double[] Numbers(int count)
        {
            var cells = Line().Split((char[]?)null, count + 1, StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length != count) throw new InvalidDataException("Speos 行列数与声明不一致。");
            return cells.Select(c => double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException("Speos 数值无效。")).ToArray();
        }
        public int Integer(int min, int max) { var v = Numbers(1)[0]; return v >= min && v <= max && v == Math.Truncate(v) ? (int)v : throw new InvalidDataException("Speos 整数无效或超限。"); }
        public string Description(int count)
        {
            var data = new char[count]; if (_reader.ReadBlock(data, 0, count) != count || !string.IsNullOrWhiteSpace(Line())) throw new InvalidDataException("Speos 测量说明长度不匹配。"); return new(data);
        }
        public void AddNodes(int n) { _nodes = checked(_nodes + n); if (_nodes > 2 * MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("Speos 原始节点超过 262144（冻结载荷仍有独立预算）。"); }
        public void End(bool v11 = false) { var tail = _reader.ReadToEnd().Trim(); if (tail.Length > 0 && !(v11 && tail == "End of file")) throw new InvalidDataException("Speos 文件含额外数据或未支持块。"); }
    }
}
