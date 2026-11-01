using System.Globalization;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Explicit Cornell gonioreflectometer spectral profile, not a generic ASTM parser.
/// Ring azimuths are linearly interpolated onto their union without dropping any knot.
/// The measured polar range is the support; unmeasured polar caps are zero, not extrapolated.</summary>
internal static class CornellAstmBsdfReader
{
    internal static MeasuredBsdfData Read(string text, string path, string sha, BsdfFileImportOptions options, CancellationToken token)
    {
        if (options.Sides == BsdfImportSides.FileDefined) throw new InvalidDataException("Cornell ASTM 须显式声明入射侧。");
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length != 0).ToArray();
        string Field(string key)
        {
            var matches=lines.Where(l=>l.StartsWith(key+" ",StringComparison.Ordinal)).ToArray();
            return matches.Length==1?matches[0][(key.Length+1)..].Trim():throw new InvalidDataException("Cornell 光谱文件缺少唯一字段 "+key);
        }
        if (!Field("LAB_NAME").StartsWith("Cornell University", StringComparison.Ordinal) || Field("SYSTEM_NAME") != "Cornell/Kodak gonioreflectometer" || Field("MEAS_NAME") != "BRDF")
            throw new InvalidDataException("仅支持有已核实弧度/辐射度约定的 Cornell 光谱 BRDF 配置；不猜测其他 ASTM 单位。");
        if (!int.TryParse(Field("NUM_POINTS"), out var count) || count < 2 || count > MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("Cornell 光谱行数无效。");
        var variables = Field("VARS").Split(',');
        if (variables.Length is < 6 or > 36 || !variables.Take(4).SequenceEqual(new[] { "theta_i", "phi_i", "theta_s", "phi_s" })) throw new InvalidDataException("Cornell 光谱列定义无效。");
        double Number(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException("Cornell 光谱数值无效。");
        var waves = variables.Skip(4).Select(s => s.EndsWith("nm", StringComparison.Ordinal) ? Number(s[..^2]) : throw new InvalidDataException("需要明确 nm 光谱列，不能将 XYZ/RGB 当作辐射度。")).ToArray();
        if (waves.Any(w => w is < 200 or > 20000) || waves.Zip(waves.Skip(1)).Any(p => p.First >= p.Second)) throw new InvalidDataException("Cornell 光谱轴无效。");
        var selected = options.AstmWavelengthNanometers is { } wave ? new[] { wave } : waves;
        if (selected.Any(w => !double.IsFinite(w) || !waves.Contains(w))) throw new InvalidDataException("Cornell 导入只选择原实测波长节点，不进行光谱外推或猜测。");
        var start = Array.FindIndex(lines, l => l.StartsWith("VARS ", StringComparison.Ordinal)) + 1;
        if (lines.Length - start != count) throw new InvalidDataException("Cornell 光谱行数与声明不一致。");
        var rows = new List<double[]>(count);
        for (var i = start; i < lines.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var cells = lines[i].Split(',');
            if (cells.Length != variables.Length) throw new InvalidDataException("Cornell 光谱行列数不一致。");
            var row = cells.Select(Number).ToArray();
            if (row[0] < 0 || row[0] > Math.PI / 2 || row[1] != 0 || row[2] < 0 || row[2] > Math.PI / 2 || row[3] < -Math.PI - 1e-6 || row[3] > Math.PI + 1e-6 || row.Skip(4).Any(v => v < 0))
                throw new InvalidDataException("Cornell 弧度方向或非负辐射度无效。");
            rows.Add(row);
        }
        if (rows.Select(r => (r[0], r[1], r[2], r[3])).Distinct().Count() != count) throw new InvalidDataException("Cornell 光谱存在重复方向。");
        var incidents=rows.Select(r=>r[0]).Distinct().ToArray();
        if(options.AstmIncidentThetaDegrees is { } selectedIncidence)
        {
            // Original angles have six decimal places in radians. This tolerance
            // only identifies a declared original node; there is no angle resampling.
            var matches=incidents.Where(a=>double.IsFinite(selectedIncidence)&&Math.Abs(a*180/Math.PI-selectedIncidence)<=5e-5).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Cornell 入射角须唯一匹配原实测节点；不选择最近角或外推。");
            rows=rows.Where(r=>r[0]==matches[0]).ToList();
        }
        double Phi(double radians) { var p = radians % (2 * Math.PI); return (p < 0 ? p + 2 * Math.PI : p) * 180 / Math.PI; }
        var sides = options.Sides == BsdfImportSides.BothDeclaredIdentical ? new[] { BsdfSide.Front, BsdfSide.Back } : new[] { options.Sides == BsdfImportSides.Front ? BsdfSide.Front : BsdfSide.Back };
        var points = new List<MeasuredBsdfPoint>();
        foreach (var incident in rows.GroupBy(r => r[0]).OrderBy(g => g.Key))
        {
            var rings = incident.GroupBy(r => r[2]).OrderBy(g => g.Key).ToArray();
            var phiAxis = incident.Select(r => Phi(r[3])).Append(0).Append(360).Distinct().Order().ToArray();
            if (rings.Length < 2 || phiAxis.Length > 257 || (long)points.Count + (long)rings.Length * phiAxis.Length * selected.Length * sides.Length > MeasuredBsdfData.MaximumPoints)
                throw new InvalidDataException("Cornell 完整角节点展开超过预算；请显式选择一个原实测波长，不自动降采样。");
            foreach (var ring in rings)
            {
                var sorted = ring.Select(r => (Phi: Phi(r[3]), Row: r)).OrderBy(p => p.Phi).ToArray();
                if (sorted.Select(p => p.Phi).Distinct().Count() != sorted.Length) throw new InvalidDataException("Cornell 极角环存在重复方位节点。");
                foreach (var p in phiAxis)
                {
                    token.ThrowIfCancellationRequested(); var phi = p == 360 ? 0 : p;
                    var upper = Array.FindIndex(sorted, q => q.Phi >= phi); if (upper < 0) upper = 0;
                    var lower = (upper + sorted.Length - 1) % sorted.Length;
                    var a = sorted[lower].Phi; var b = sorted[upper].Phi; if (b <= a) b += 360; var query = phi < a ? phi + 360 : phi; var t = sorted.Length == 1 ? 0 : (query - a) / (b - a);
                    foreach (var w in selected) foreach (var side in sides)
                    {
                        var column = Array.IndexOf(waves, w) + 4;
                        var value = sorted[lower].Row[column] * (1 - t) + sorted[upper].Row[column] * t;
                        points.Add(new(side, w, incident.Key * 180 / Math.PI, ring.Key * 180 / Math.PI, p, value, 0));
                    }
                }
            }
        }
        return new(Field("SAMPLE_NAME"), Path.GetFileName(path), sha, points, sourceFormat: "CornellAstmSpectral/1",
            importNotes: $"Cornell/Kodak measured radiometric spectral BRDF; original {count} directions, {waves.Length} bands; selected {rows.Count} directions at {string.Join(",",rows.Select(r=>r[0].ToString("G17",CultureInfo.InvariantCulture)).Distinct())} incident radians, {string.Join(",", selected.Select(w=>w.ToString("G17",CultureInfo.InvariantCulture)))} nm. Radians converted explicitly. Each ring's periodic linear azimuth interpolation retained on its union (a single-sample ring is constant); linear in cosine between measured polar rings. This interpolation is not independent measurement of gaps. No added ideal specular peak. Unmeasured polar caps are zero by explicit support convention, not certified zero physical scattering. No XYZ calibration approval implied.",
            angularSupport: BsdfAngularSupport.MeasuredPolarRangeZeroOutside);
    }
}
