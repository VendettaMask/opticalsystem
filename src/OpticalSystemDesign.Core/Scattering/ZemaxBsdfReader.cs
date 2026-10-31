using System.Globalization;

namespace OpticalSystemDesign.Core.Scattering;

internal static class ZemaxBsdfReader
{
    internal static bool IsFormat(string text) => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'))?.StartsWith("Source", StringComparison.OrdinalIgnoreCase) == true;
    internal static MeasuredBsdfData Read(string text, string path, string sha, BsdfFileImportOptions options, CancellationToken token)
    {
        if (options.Sides == BsdfImportSides.FileDefined) throw new InvalidDataException("Zemax 文件须显式指定入射侧。");
        double wavelength = options.ZemaxWavelengthNanometers ?? 550;
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray(); int at = 0;
        string Line() { token.ThrowIfCancellationRequested(); return at < lines.Length ? lines[at++] : throw new InvalidDataException("Zemax BSDF 提前结束。"); }
        string Value(string key) { var c = Line().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries); return c.Length == 2 && c[0].Equals(key, StringComparison.OrdinalIgnoreCase) ? c[1].Trim() : throw new InvalidDataException("Zemax 缺少字段 " + key); }
        int Count(string key, int max) => int.TryParse(Value(key), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1 && v <= max ? v : throw new InvalidDataException("Zemax 维数超限或无效：" + key);
        double[] Numbers(int n)
        {
            var cells = Line().Split((char[]?)null, n + 1, StringSplitOptions.RemoveEmptyEntries); if (cells.Length != n) throw new InvalidDataException("Zemax BSDF 列数不匹配。");
            return cells.Select(c => double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException("Zemax 数值无效。")).ToArray();
        }
        double[] Axis(string key, int maxCount, double max)
        {
            var a = Numbers(Count(key, maxCount));
            if (a.Any(x => x < 0 || x > max) || a.Zip(a.Skip(1)).Any(v => v.First >= v.Second)) throw new InvalidDataException("Zemax 轴须严格递增且在角度范围内。"); return a;
        }
        if (!Value("Source").Equals("Measured", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不支持的 Zemax Source。");
        var symmetry = Value("Symmetry"); bool plane = symmetry.Equals("PlaneSymmetrical", StringComparison.OrdinalIgnoreCase), fixedAxes = symmetry.Equals("Asymmetrical4D", StringComparison.OrdinalIgnoreCase);
        if (!plane && !fixedAxes && !symmetry.Equals("Asymmetrical", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不支持的 Zemax Symmetry。");
        var spectral = Value("SpectralContent"); bool xyz = spectral.Equals("XYZ", StringComparison.OrdinalIgnoreCase);
        XyzRadiometricCalibration? calibration = null;
        if (xyz)
        {
            if (string.IsNullOrWhiteSpace(options.ZemaxXyzCalibrationPath)) throw new InvalidDataException("Zemax XYZ 需要显式辐射度校准 JSON；XYZ 不能唯一确定光谱。");
            calibration = XyzRadiometricCalibration.Read(options.ZemaxXyzCalibrationPath); wavelength = calibration.Bands[0].WavelengthNanometers;
        }
        else if (!spectral.Equals("Monochrome", StringComparison.OrdinalIgnoreCase) || options.ZemaxWavelengthNanometers is null || !double.IsFinite(wavelength) || wavelength is < 200 or > 20000 || !options.ZemaxRadiometric)
            throw new InvalidDataException("Zemax Monochrome 须声明测量波长和辐射度单位；不能将光度 Y 当作光谱功率。");
        var scatter = Value("ScatterType"); bool transmission = scatter.Equals("BTDF", StringComparison.OrdinalIgnoreCase);
        if (!transmission && !scatter.Equals("BRDF", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Zemax ScatterType 须为 BRDF 或 BTDF。");
        var rotations = Axis("SampleRotation", 50, 360); var incidences = Axis("AngleOfIncidence", 100, 90); var phi = Axis("ScatterAzimuth", 1000, plane ? 180 : 360); var radial = Axis("ScatterRadial", 1000, 180);
        if (radial.Length < 2 || phi.Length < 2 || phi[0] != 0 || plane && phi[^1] != 180
            || fixedAxes && (rotations.Length < 2 || rotations[0] != 0 || rotations[^1] != 360)
            || !fixedAxes && (rotations.Length != 1 || rotations[0] != 0)) throw new InvalidDataException("Zemax 轴不足以定义所声明的对称性。");
        var sides = options.Sides == BsdfImportSides.BothDeclaredIdentical ? new[] { BsdfSide.Front, BsdfSide.Back } : new[] { options.Sides == BsdfImportSides.Front ? BsdfSide.Front : BsdfSide.Back };
        var outputPhi = plane ? phi.Concat(phi.Reverse().Select(p => 360 - p)).Distinct().Order().ToArray() : phi;
        if ((long)rotations.Length * incidences.Length * outputPhi.Length * radial.Length * sides.Length * (xyz ? 3 : 1) > MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("Zemax 展开后节点超过预算；不自动降采样。");
        var points = new List<MeasuredBsdfPoint>(); var integrals = new List<ZemaxBsdfTis>();
        var channelPoints = new List<MeasuredBsdfPoint>[] { new(), new(), new() }; var channelIntegrals = new List<ZemaxBsdfTis>[] { new(), new(), new() };
        foreach (var channel in xyz ? new[] { 0, 1, 2 } : new[] { 1 })
        {
        points = channelPoints[channel]; integrals = channelIntegrals[channel];
        if (Line() != (xyz ? new[] { "TristimulusX", "TristimulusY", "TristimulusZ" }[channel] : "Monochrome") || Line() != "DataBegin") throw new InvalidDataException("Zemax 数据块标记无效。");
        foreach (var a in rotations) foreach (var i in incidences)
        {
            var s = Value("TIS"); if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var tis) || !double.IsFinite(tis) || tis is < 0 or > 1) throw new InvalidDataException("Zemax TIS 须在 0–1 之间。");
            var rows = phi.Select(_ => Numbers(radial.Length)).ToArray();
            if (rows.Any(r => r.Any(v => v < 0))) throw new InvalidDataException("Zemax 分布含负值。");
            foreach (var side in sides)
            {
                integrals.Add(new(side, i, a, tis));
                foreach (var p in outputPhi)
                {
                    var source = Array.BinarySearch(phi, plane && p > 180 ? 360 - p : p);
                    for (var r = 0; r < radial.Length; r++) points.Add(new(side, wavelength, i, radial[r], p, transmission ? 0 : rows[source][r], transmission ? rows[source][r] : 0, a));
                }
            }
        }
        if (Line() != "DataEnd") throw new InvalidDataException("Zemax 数据块未结束。");
        }
        if (at != lines.Length) throw new InvalidDataException("Zemax 尾部有未知记录或额外数据块。");
        points = channelPoints[1]; integrals = channelIntegrals[1];
        var note = string.Format(CultureInfo.InvariantCulture, "Zemax tabular Monochrome; user declares radiometric BSDF at {0:G17} nm; sides={1}; {2}; TIS is absolute scattered fraction, no extra coating/Fresnel gain. Specular-centred grid retained, cosine projection clipped to physical hemisphere; input interpolation is a convex mixture. Declared front indices={3:G17}/{4:G17}.", wavelength, options.Sides, symmetry, options.IncidentIndex, options.TransmittedIndex);
        return new(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path), sha, points, coordinates: fixedAxes ? BsdfCoordinates.FixedMaterialAxes : BsdfCoordinates.IncidentPlane,
            sourceFormat: xyz ? "ZemaxTabularCalibratedXYZ/1" : "ZemaxTabularRadiometric/1", importNotes: xyz ? $"Original XYZ channels retained with independent TIS normalization; calibrated nonnegative spectral mapping; observer={calibration!.Observer}; illuminant={calibration.Illuminant}; calibration SHA256={calibration.SourceSha256}; no unique spectrum inferred. " + note.Replace("user declares radiometric BSDF", "channel storage reference") : note,
            zemaxDefinition: new(transmission, options.IncidentIndex, options.TransmittedIndex, integrals),
            zemaxXyz: xyz ? new(calibration!, channelPoints[0], channelPoints[2], channelIntegrals[0], channelIntegrals[2]) : null);
    }
}
