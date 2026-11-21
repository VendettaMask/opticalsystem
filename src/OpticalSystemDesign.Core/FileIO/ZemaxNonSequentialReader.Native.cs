using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.FileIO;

public static partial class ZemaxNonSequentialReader
{
    private static readonly HashSet<string> NativeGlobalMetadata = new(StringComparer.Ordinal)
    {
        "AUTH", "IWDP", "PFIL", "LANG", "ENPD", "ENVD", "GFAC", "RAIM", "PUSH", "SDMA", "OMMA", "FTYP", "ROPD", "HYPR", "PICB",
        "XFLN", "YFLN", "FWGN", "VDXN", "VDYN", "VCXN", "VCYN", "VANN", "XAIM", "YAIM", "ZAIM", "GLRS", "GSTD", "LUID", "SSID", "FIMP", "MEMA"
    };

    private static void MapNativeObjects(List<ZemaxNscObject> objects, List<ZemaxNscRecord> records,
        List<ZemaxNscDiagnostic> issues, List<NonSequentialMeshAsset> meshes)
    {
        var visiting = new HashSet<(int Object, int Column)>();
        var resolved = new Dictionary<(int Object, int Column), double>();
        var catalogs = records.Where(r => r.Keyword == "GCAT").SelectMany(r => r.Values).ToArray();
        var registry = new OpticalSystemDesign.Core.Materials.MaterialRegistry();
        string Material(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Air";
            var material = registry.Resolve(name, catalogs);
            return material is OpticalSystemDesign.Core.Materials.CatalogGlassMaterial glass
                ? glass.Manufacturer + ":" + glass.CatalogName : material.Name;
        }
        double Value(int number, int column)
        {
            if (number < 1 || number > objects.Count) throw new InvalidDataException("拾取引用了不存在的对象。");
            var key = (number, column); if (resolved.TryGetValue(key, out var cached)) return cached;
            if (!visiting.Add(key)) throw new InvalidDataException("非序列参数拾取存在循环。");
            try
            {
                var rows = objects[number - 1].Records;
                double result;
                if (column >= 7)
                {
                    var row = rows.Single(r => r.Keyword == "NSOD" && Integer(r.Values[0]) == column - 6);
                    result = Number(row.Values[1]);
                    var solve = row.Values.Count > 2 ? Integer(row.Values[2]) : 0;
                    if (solve == 2) result = Value(Integer(row.Values[3]), Integer(row.Values[6])) * Number(row.Values[4]) + Number(row.Values[5]);
                    else if (solve != 0) throw new InvalidDataException("尚未支持该参数求解类型。");
                }
                else
                {
                    if (column is < 1 or > 6) throw new InvalidDataException("拾取列编号无效。");
                    result = Number(rows.Single(r => r.Keyword == "NSOP").Values[column - 1]);
                    var solveRow = rows.FirstOrDefault(r => r.Keyword == "NSOV");
                    var solve = solveRow is null ? 0 : Integer(solveRow.Values[column - 1]);
                    if (solve == 2)
                    {
                        var source = rows.Single(r => r.Keyword == "NSOU"); var parameter = rows.Single(r => r.Keyword == "NSOW");
                        var scale = rows.Single(r => r.Keyword == "NSOS"); var offset = rows.Single(r => r.Keyword == "NSOO");
                        var sourceColumn = Integer(parameter.Values[column - 1]);
                        // Zero selects the same pose column, rather than a nonexistent column zero.
                        result = Value(Integer(source.Values[column - 1]), sourceColumn == 0 ? column : sourceColumn) * Number(scale.Values[column - 1]) + Number(offset.Values[column - 1]);
                    }
                    else if (solve != 0) throw new InvalidDataException("尚未支持该位姿求解类型。");
                }
                resolved[key] = result; return result;
            }
            finally { visiting.Remove(key); }
        }

        for (var i = 0; i < objects.Count; i++)
        {
            var source = objects[i];
            try
            {
                var rows = source.Records.ToArray(); var header = rows[0];
                foreach (var row in rows.Where(r => r.Keyword is not ("NSOH" or "NSOP" or "NSOD"))) CheckNativeProperty(row, source.Number, issues);
                var pose = rows.Single(r => r.Keyword == "NSOP");
                var poseValues = Enumerable.Range(1, 6).Select(c => F(Value(source.Number, c))).Concat(pose.Values.Skip(6)).ToArray();
                var parameters = rows.Where(r => r.Keyword == "NSOD").ToDictionary(r => Integer(r.Values[0]), r => Value(source.Number, Integer(r.Values[0]) + 6));
                double P(int n) => parameters.TryGetValue(n, out var value) ? value : throw new InvalidDataException($"缺少参数 {n}。");
                NonSequentialObjectDefinition? mapped;
                if (source.Type == "NSC_RBLK")
                {
                    if (parameters.Keys.Any(k => k is < 1 or > 9)) throw new InvalidDataException("矩形体含未知参数。");
                    var mesh = RectangularVolumeMesh(P(1), P(2), P(3), P(4), P(5), P(6), P(7), P(8), P(9)); meshes.Add(mesh);
                    mapped = NativeObject(new MeshObjectParameters(mesh.Id, NonSequentialSurfaceBehavior.Refractive, Material(poseValues.ElementAtOrDefault(6) ?? "Air")));
                }
                else if (source.Type == "NSC_NULL") mapped = NativeObject(new NullObjectParameters());
                else
                {
                    var mapRows = new List<ZemaxNscRecord> { header with { Values = source.Type == "NSC_DETC" ? new[] { "NSC_DETE", "0", "0" } : header.Values }, pose with { Values = poseValues } };
                    foreach (var row in rows.Where(r => r.Keyword == "NSOD"))
                    {
                        var values = row.Values.ToArray(); values[1] = F(P(Integer(values[0])));
                        if (values.Length > 2) values[2] = "0"; if (values.Length > 6) values[6] = "0";
                        if (source.Type == "NSC_DETC" && Integer(values[0]) == 16) values[1] = "0";
                        mapRows.Add(row with { Values = values });
                    }
                    mapped = Map(source.Number, source.Type == "NSC_DETC" ? "NSC_DETE" : source.Type, mapRows.ToArray(), issues);
                    if (mapped?.Parameters is DetectorRectangleParameters detector && source.Type == "NSC_DETC")
                        mapped = mapped with { Parameters = detector with { RecordColor = true, PolarizationComponent = (int)P(16) } };
                }
                if (mapped is not null && mapped.Parameters is SourceParameters emitter)
                {
                    var pol = rows.FirstOrDefault(r => r.Keyword == "NSCS" && r.Values[0] == "6");
                    var physics = pol is null ? emitter.Physics : new NonSequentialSourcePhysics(new SystemPolarization(
                        Integer(pol.Values[1]) != 0, Number(pol.Values[2]), Number(pol.Values[3]), Number(pol.Values[4]), Number(pol.Values[5])), PhaseDegrees: Number(pol.Values[6]))
                    { UseSobolSampling = pol.Values[10] == "1" };
                    var spectrum = rows.FirstOrDefault(r => r.Keyword == "NSCS" && r.Values[0] == "7" && r.Values.ElementAtOrDefault(1) == "3");
                    if (spectrum is not null && Integer(spectrum.Values[2]) != 0)
                    {
                        if (Integer(spectrum.Values[2]) != 1) throw new InvalidDataException("此光源的颜色模型尚未支持；未替换其光谱。");
                        physics = (physics ?? new()) with
                        {
                            Spectrum = NonSequentialColorimetry.FitSpectrum(Number(spectrum.Values[3]), Number(spectrum.Values[4]), Number(spectrum.Values[5]),
                            Number(spectrum.Values[6]) * 1000, Number(spectrum.Values[7]) * 1000, Integer(spectrum.Values[8])),
                            SpectrumProtocol = NonSequentialColorimetry.SpectrumFitProtocol
                        };
                        issues.Add(new(spectrum.Line, source.Number, "TristimulusSpectrumFit", "XYZ 颜色采用已记录的非负光谱拟合；XYZ 不能唯一确定光谱，尚未认证原软件逐像素颜色一致性。", Blocking: false));
                    }
                    mapped = mapped with { Parameters = emitter with { Physics = physics } };
                }
                if (mapped is not null) mapped = mapped with { Name = $"{source.Number} {source.Type}" };
                var crystal = rows.FirstOrDefault(r => r.Keyword == "NSBR" && r.Values[0] == "0");
                if (mapped is not null && crystal is not null)
                {
                    if (Integer(crystal.Values[1]) != 3 || Integer(crystal.Values[2]) is not (0 or 1)) throw new InvalidDataException("当前晶体追迹支持波片的全部分支或仅折射模式；其他晶体模式尚未实现。");
                    var axis = rows.Single(r => r.Keyword == "NSBR" && r.Values[0] == "1");
                    var vector = new Vector3D(Number(axis.Values[1]), Number(axis.Values[2]), Number(axis.Values[3]));
                    if (vector.Length == 0) vector = new(0, 0, 1);
                    var material = poseValues.ElementAtOrDefault(6) ?? throw new InvalidDataException("晶体缺少材料。");
                    mapped = mapped with
                    {
                        Parameters = mapped.Parameters with
                        {
                            Waveplate = new(Material(material + "-E"), vector / vector.Length)
                            { TraceReflections = Integer(crystal.Values[2]) == 0 }
                        }
                    };
                }
                objects[i] = source with { MappedObject = mapped };

                NonSequentialObjectDefinition NativeObject(NonSequentialObjectParameters p) => new(Guid.NewGuid(), $"{source.Number} {source.Type}",
                    p is NullObjectParameters ? NonSequentialObjectKind.Null : NonSequentialObjectKind.Mesh, true, true,
                    new(new(Number(poseValues[0]), Number(poseValues[1]), Number(poseValues[2])), Number(poseValues[3]), Number(poseValues[4]), Number(poseValues[5])), null, null, p);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException or FormatException or IndexOutOfRangeException or KeyNotFoundException)
            { issues.Add(new(source.Records[0].Line, source.Number, "ObjectDefinition", e.Message)); }
        }
    }

    private static void CheckNativeProperty(ZemaxNscRecord row, int number, List<ZemaxNscDiagnostic> issues)
    {
        if (row.Keyword is "NOID" or "NSOA" or "NSOV" or "NSOU" or "NSOW" or "NSOS" or "NSOO" or "NSOQ" or "NSBR") return;
        if (row.Keyword == "NSCS")
        {
            var v = row.Values;
            var known = v[0] switch
            {
                "1" => v.Count == 3 && v[2] is "_NONE_" or "",
                "2" => v.Count >= 6 && v[2] == "0" && Number(v[3]) == 0 && Number(v[4]) == 0 && Number(v[5]) == 1 && v.Skip(6).All(s => Number(s) == 0),
                "4" => v.Count == 11 && v[2] == "0" && v[3] == "0" && v[4] == "2" && v[5] == "0" && v[6] == "0" && v[7] == "0" && v[8] == "1" && v[9] == "0" && v[10] == "0",
                "6" => v.Count == 12 && v.Skip(7).Take(3).All(s => Number(s) == 0) && v[10] is "0" or "1" && v[11] == "0",
                "7" => v.Count >= 2 && v[1] switch
                {
                    "1" => v.SequenceEqual(new[] { "7", "1", "0", "1", "1" }),
                    "2" => v.Count == 4 && Number(v[2]) == 0 && Number(v[3]) == 0,
                    "3" => v.Count == 10 && Integer(v[2]) is 0 or 1,
                    _ => false
                },
                "8" => v.SequenceEqual(new[] { "8", "0", "0", "1.000000000000E+00", "0" }),
                "10" => v.Count == 3 && v[2] == "",
                "11" => v.SequenceEqual(new[] { "11", "1", "0", "0", "0" }),
                "20" or "23" => v.Count == 1,
                "21" => v.Skip(1).All(s => Number(s) == 0),
                "22" => v.Count == 6 && v[1] == "0" && v[2] == "1" && v[3] is "0" or "1" && v[4] == "0" && v[5] == "1",
                "24" => v.Count is 2 or 3 && v.Skip(1).All(s => Number(s) == 0) || v.Count == 5 && v[2] == "0" && Number(v[3]) == 0 && Number(v[4]) == 1,
                "40" => v.Count == 7 && v[1] == "0" && v.Skip(5).All(s => Number(s) == 0),
                _ => false
            };
            if (known) return;
        }
        issues.Add(new(row.Line, number, "UnmappedObjectRecord", $"对象属性 {row.Keyword} {string.Join(' ', row.Values.Take(3))} 的有效设置尚未映射。"));
    }

    private static NonSequentialMeshAsset RectangularVolumeMesh(double xf, double yf, double length, double xb, double yb, double fx, double fy, double bx, double by)
    {
        if (new[] { xf, yf, length, xb, yb }.Any(v => v <= 0) || new[] { fx, fy, bx, by }.Any(v => Math.Abs(v) >= 85)) throw new InvalidDataException("矩形体尺寸/倾角无效。");
        if (xf != xb && (fy != 0 || by != 0) || yf != yb && (fx != 0 || bx != 0)) throw new InvalidDataException("非共面侧面的矩形体需要显式曲面划分定义。");
        double Tan(double angle) => Math.Tan(angle * Math.PI / 180);
        var vertices = new List<Vector3D>();
        foreach (var rear in new[] { false, true })
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var x = sx * (rear ? xb : xf); var y = sy * (rear ? yb : yf);
                vertices.Add(new(x, y, (rear ? length : 0) + x * Tan(rear ? bx : fx) + y * Tan(rear ? by : fy)));
            }
        if (Enumerable.Range(0, 4).Any(i => vertices[i + 4].Z <= vertices[i].Z)) throw new InvalidDataException("矩形体前后面相交。");
        var triangles = new[] { new NonSequentialMeshTriangle(0, 2, 1, 1), new(0, 3, 2, 1), new(4, 5, 6, 2), new(4, 6, 7, 2),
            // Native face zero (all sides) maps to the mesh engine's positive side-face identity 3.
            new(0, 1, 5, 3), new(0, 5, 4, 3), new(1, 2, 6, 3), new(1, 6, 5, 3), new(2, 3, 7, 3), new(2, 7, 6, 3), new(3, 0, 4, 3), new(3, 4, 7, 3) };
        return NonSequentialStlImporter.FromGeometry(vertices, triangles, "NSC rectangular volume", "NSC_RBLK/planar-faces");
    }
}
