using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;

var outputDirectory = Path.GetFullPath(args.Length == 0
    ? Path.Combine("samples", "non-sequential")
    : args[0]);
Directory.CreateDirectory(outputDirectory);

var samples = new[]
{
    BasicLens(),
    FresnelGhosts(),
    TirLightPipe(),
    FoldedMirrors(),
    MultiWavelengthSources(),
    EmbeddedStlBaffle(),
    EllipseSourceFootprint(),
    TwoAngleAnisotropicSource(),
    RadialIntensitySource(),
    VolumeRectangleSource(),
    VolumeEllipseSource(),
    VolumeCylinderSource()
};
var previewDirectory = Path.Combine(outputDirectory, "previews");
Directory.CreateDirectory(previewDirectory);
var manifestEntries = new List<SampleManifestEntry>();
foreach (var sample in samples)
{
    var trace = TraceAndValidate(sample);
    var path = Path.Combine(outputDirectory, sample.FileName);
    await StarOptProjectStore.SaveAsync(sample.Project, path);
    var restored = await StarOptProjectStore.LoadAsync(path);
    restored.NonSequentialDocument?.Validate();
    string? previewFile = null;
    if (sample.GeneratePreview)
    {
        previewFile = Path.Combine("previews", Path.GetFileNameWithoutExtension(sample.FileName) + ".svg")
            .Replace('\\', '/');
        await WritePreviewAsync(
            Path.Combine(outputDirectory, previewFile.Replace('/', Path.DirectorySeparatorChar)),
            sample,
            trace);
    }
    var detectorResults = BuildDetectorResults(sample.Project.NonSequentialDocument!, trace);
    manifestEntries.Add(new SampleManifestEntry(
        sample.FileName,
        sample.Title,
        sample.Lesson,
        sample.Project.NonSequentialDocument!.Objects.Count,
        sample.Project.NonSequentialDocument.MeshAssets.Count,
        trace.TotalBranchCount,
        trace.EnergyBalance.SourcePowerWatts,
        trace.EnergyBalance.DetectorPowerWatts,
        trace.EnergyBalance.AbsorbedPowerWatts,
        sample.SuggestedFilters,
        sample.SourceKind?.ToString(),
        previewFile,
        detectorResults));
    Console.WriteLine(
        $"{sample.FileName}: objects={sample.Project.NonSequentialDocument.Objects.Count}, "
        + $"branches={trace.TotalBranchCount}, detector={trace.EnergyBalance.DetectorPowerWatts:G8} W");
}

var manifest = new SampleManifest(
    2,
    "STAROPT non-sequential teaching samples",
    "Millimeter",
    manifestEntries);
var manifestPath = Path.Combine(outputDirectory, "index.json");
var temporaryManifestPath = manifestPath + ".tmp";
await File.WriteAllTextAsync(
    temporaryManifestPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }),
    new UTF8Encoding(false));
File.Move(temporaryManifestPath, manifestPath, overwrite: true);

static TeachingSample BasicLens()
{
    const int scene = 1;
    var objects = new[]
    {
        Object(scene, 1, "矩形扩展光源", NonSequentialObjectKind.SourceRectangle, new Vector3D(0, 0, 0),
            new SourceRectangleParameters(4, 4, 3, 1, 1, 20, 2_000)),
        Object(scene, 2, "双凸标准镜片", NonSequentialObjectKind.StandardLens, new Vector3D(0, 0, 30),
            new StandardLensParameters(45, -45, 0, 0, 6, 12, "N-BK7")),
        Object(scene, 3, "像面探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(30, 30, 128, 128))
    };
    var document = Document(scene, "01 基础：光源—镜片—探测器", objects, split: false);
    return Sample(scene, "01-basic-lens-detector.staropt", "基础镜片与探测器",
        "认识对象编辑器、3D布局、分析追迹和探测器功率。", document,
        new[] { "D3", "SEQ(Q1,H2,T2,D3)" },
        result => Require(result.EnergyBalance.DetectorPowerWatts > 0.5, "基础样例应有大部分功率到达探测器。"));
}

static TeachingSample FresnelGhosts()
{
    const int scene = 2;
    var objects = new[]
    {
        Object(scene, 1, "窄角矩形光源", NonSequentialObjectKind.SourceRectangle, new Vector3D(0, 0, 0),
            new SourceRectangleParameters(2, 2, 1, 1, 1, 20, 400)),
        Object(scene, 2, "Fresnel分支镜片", NonSequentialObjectKind.StandardLens, new Vector3D(0, 0, 20),
            new StandardLensParameters(40, -40, 0, 0, 5, 10, "N-BK7")),
        Object(scene, 3, "前向主光探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 60),
            new DetectorRectangleParameters(30, 30, 96, 96)),
        Object(scene, 4, "后向鬼像探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, -10),
            new DetectorRectangleParameters(30, 30, 96, 96), rotationY: 180)
    };
    var document = Document(scene, "02 分支：Fresnel主光与鬼像", objects, split: true,
        minimumIntensity: 1e-8, maximumBranches: 50_000);
    return Sample(scene, "02-fresnel-main-and-ghost.staropt", "Fresnel主光与鬼像路径",
        "比较透射主光、前表面反射和镜片内部多次反射，练习路径筛选。", document,
        new[] { "D3", "D4", "SEQ(Q1,H2,R2,D4)", "R2" },
        result =>
        {
            Require(result.TotalBranchCount > 800, "Fresnel样例应生成父子分支。 ");
            Require(result.Detectors.Count(frame => frame.TotalPowerWatts > 0) == 2,
                "Fresnel样例的前后探测器都应接收到功率。");
        });
}

static TeachingSample TirLightPipe()
{
    const int scene = 3;
    var pipeId = Id(scene, 1);
    var objects = new[]
    {
        Object(scene, 1, "N-BK7矩形光管", NonSequentialObjectKind.Box, new Vector3D(0, 0, 50),
            new BoxParameters(10, 10, 100, "N-BK7", NonSequentialSurfaceBehavior.Refractive)),
        Object(scene, 2, "光管内点光源", NonSequentialObjectKind.SourcePoint, new Vector3D(0, 0, 1),
            new SourcePointParameters(1, 1, 25, 20, 1_000), containingObjectId: pipeId),
        Object(scene, 3, "光管出口探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 99),
            new DetectorRectangleParameters(9.8, 9.8, 100, 100), containingObjectId: pipeId)
    };
    var document = Document(scene, "03 全反射：矩形光管", objects, split: true,
        minimumIntensity: 1e-9, maximumSegments: 100);
    return Sample(scene, "03-total-internal-reflection-light-pipe.staropt", "矩形光管全反射",
        "观察光线在实体介质中的进入状态、侧壁全反射、重复命中和出口功率。", document,
        new[] { "D3", "R1 & D3", "SEQ(Q2,R1,D3)" },
        result =>
        {
            Require(result.EnergyBalance.DetectorPowerWatts > 0.8, "光管出口应接收主要功率。");
            Require(result.Branches.Any(branch => branch.Segments.Any(segment =>
                segment.InteractionKind == OptilandWorkbench.Core.Interactions.RayInteractionKind.TotalInternalReflection)),
                "光管样例必须包含全反射路径。");
        });
}

static TeachingSample FoldedMirrors()
{
    const int scene = 4;
    var objects = new[]
    {
        Object(scene, 1, "单射线光源", NonSequentialObjectKind.SourceRay, new Vector3D(0, 0, 0),
            new SourceRayParameters(LocalDirection: new Vector3D(0, 0, 1))),
        Object(scene, 2, "第一折叠镜", NonSequentialObjectKind.PlaneRectangle, new Vector3D(0, 0, 20),
            new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Reflective), rotationY: -45),
        Object(scene, 3, "第二折叠镜", NonSequentialObjectKind.PlaneRectangle, new Vector3D(30, 0, 20),
            new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Reflective), rotationY: 135),
        Object(scene, 4, "折叠光路探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(30, 0, 50),
            new DetectorRectangleParameters(20, 20, 64, 64))
    };
    var document = Document(scene, "04 反射：双镜折叠光路", objects, split: false);
    return Sample(scene, "04-two-mirror-folded-path.staropt", "双反射镜折叠光路",
        "通过倾斜平面建立无预定义表面顺序的空间折叠路径。", document,
        new[] { "D4", "SEQ(Q1,H2,R2,H3,R3,D4)" },
        result =>
        {
            Require(Math.Abs(result.EnergyBalance.DetectorPowerWatts - 1) < 1e-12,
                "折叠光路单射线应完整到达探测器。");
            var branch = result.Branches.Single();
            Require(branch.Segments.Count == 3, "折叠光路应命中两面反射镜和一个探测器。");
        });
}

static TeachingSample MultiWavelengthSources()
{
    const int scene = 5;
    var objects = new[]
    {
        Object(scene, 1, "蓝光高斯源", NonSequentialObjectKind.SourceGaussian, new Vector3D(-8, 0, 0),
            new SourceGaussianParameters(0.8, 0.8, 1, 0.25, 1, 20, 800)),
        Object(scene, 2, "绿光高斯源", NonSequentialObjectKind.SourceGaussian, new Vector3D(0, 0, 0),
            new SourceGaussianParameters(0.8, 0.8, 1, 0.5, 2, 20, 800)),
        Object(scene, 3, "红光高斯源", NonSequentialObjectKind.SourceGaussian, new Vector3D(8, 0, 0),
            new SourceGaussianParameters(0.8, 0.8, 1, 0.25, 3, 20, 800)),
        Object(scene, 4, "三色探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 100),
            new DetectorRectangleParameters(40, 30, 160, 120))
    };
    var wavelengths = new[]
    {
        new NonSequentialWavelength("F 蓝光", 486.1, 0.25, false),
        new NonSequentialWavelength("d 绿光", 587.6, 0.5, true),
        new NonSequentialWavelength("C 红光", 656.3, 0.25, false)
    };
    var document = Document(scene, "05 多源：三波长探测", objects, split: false, wavelengths: wavelengths);
    return Sample(scene, "05-three-wavelength-sources.staropt", "三波长多光源探测",
        "学习独立波长表、多个光源的功率归一和探测器按波长累计。", document,
        new[] { "W1 & D4", "W2 & D4", "W3 & D4", "Q2 & D4" },
        result =>
        {
            var detector = result.Detectors.Single();
            Require(detector.PowerByWavelength.Values.Count(values => values.Sum() > 0) == 3,
                "三种系统波长都应在探测器上有功率。");
            Require(result.EnergyBalance.DetectorPowerWatts > 0.99, "三光源功率应基本全部到达探测器。");
        });
}

static TeachingSample EmbeddedStlBaffle()
{
    const int scene = 6;
    var asset = NonSequentialStlImporter.Import(
        Encoding.ASCII.GetBytes(SquareRingStl()),
        "teaching-square-baffle.stl") with
    { Id = Id(scene, 90) };
    var objects = new[]
    {
        Object(scene, 1, "宽角矩形光源", NonSequentialObjectKind.SourceRectangle, new Vector3D(0, 0, 0),
            new SourceRectangleParameters(8, 8, 8, 1, 1, 20, 4_000)),
        new NonSequentialObjectDefinition(
            Id(scene, 2),
            "内嵌STL方孔挡光环",
            NonSequentialObjectKind.Mesh,
            true,
            true,
            new CoordinateSystem(new Vector3D(0, 0, 40)),
            null,
            null,
            new MeshObjectParameters(asset.Id, NonSequentialSurfaceBehavior.Absorbing, "Air", true)),
        Object(scene, 3, "杂散光探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(36, 36, 144, 144))
    };
    var document = Document(scene, "06 机械：内嵌STL挡光环", objects, split: false, meshAssets: new[] { asset });
    return Sample(scene, "06-embedded-stl-baffle.staropt", "内嵌STL机械挡光环",
        "比较通过中心方孔的有效路径与被机械挡光环吸收的路径，并验证工程不依赖原STL文件。", document,
        new[] { "D3", "H2 & A", "SEQ(Q1,H2,A)", "M2 & D3" },
        result =>
        {
            Require(result.EnergyBalance.DetectorPowerWatts > 0.05, "应有光线穿过中心方孔到达探测器。");
            Require(result.EnergyBalance.AbsorbedPowerWatts > 0.05, "应有光线被STL挡光环吸收。");
        });
}

static TeachingSample EllipseSourceFootprint()
{
    const int scene = 7;
    var objects = new[]
    {
        Object(scene, 1, "椭圆面光源", NonSequentialObjectKind.SourceEllipse, new Vector3D(0, 0, 0),
            new SourceEllipseParameters(12, 6, 8, 1, 1, 40, 10_000)),
        Object(scene, 2, "近场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 2),
            new DetectorRectangleParameters(18, 12, 96, 64, Absorb: false)),
        Object(scene, 3, "远场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(40, 28, 120, 84))
    };
    var document = Document(scene, "07 光源：椭圆面近场与远场", objects, split: false);
    return SourceSample(scene, "07-ellipse-source-footprint.staropt", "椭圆面光源空间轮廓",
        "比较近场椭圆发光口径与传播后的远场光斑，练习探测器空间分布查看。", document,
        NonSequentialObjectKind.SourceEllipse, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result =>
        {
            Require(result.Detectors.Count == 2, "椭圆光源样例应包含近场和远场探测器。");
            Require(result.Detectors.All(item => item.TotalPowerWatts > 0.99), "两个探测器都应接收到完整功率。");
            var near = DetectorStatistics(document, result.Detectors[0]);
            Require(near.RmsXMillimeters > near.RmsYMillimeters * 1.7, "椭圆近场的X向宽度应明显大于Y向。");
        });
}

static TeachingSample TwoAngleAnisotropicSource()
{
    const int scene = 8;
    var objects = new[]
    {
        Object(scene, 1, "双角度椭圆光源", NonSequentialObjectKind.SourceTwoAngle, new Vector3D(0, 0, 0),
            new SourceTwoAngleParameters(2, 2, NonSequentialSourceApertureShape.Ellipse, 18, 5, 1, 1, 40, 10_000)),
        Object(scene, 2, "近场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 2),
            new DetectorRectangleParameters(8, 8, 80, 80, Absorb: false)),
        Object(scene, 3, "远场角分布探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(60, 24, 150, 72))
    };
    var document = Document(scene, "08 光源：X/Y双角度分布", objects, split: false);
    return SourceSample(scene, "08-two-angle-anisotropic-source.staropt", "双角度各向异性光源",
        "观察X/Y独立发散角形成的椭圆远场光斑，并比较空间口径与角度分布。", document,
        NonSequentialObjectKind.SourceTwoAngle, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result =>
        {
            var far = DetectorStatistics(document, result.Detectors[1]);
            Require(far.RmsXMillimeters > far.RmsYMillimeters * 2.5, "双角度光源的远场X向宽度应显著大于Y向。");
        });
}

static TeachingSample RadialIntensitySource()
{
    const int scene = 9;
    var objects = new[]
    {
        Object(scene, 1, "径向强度光源", NonSequentialObjectKind.SourceRadial, new Vector3D(0, 0, 0),
            new SourceRadialParameters(new[]
            {
                new SourceRadialSample(0, 1),
                new SourceRadialSample(10, 1),
                new SourceRadialSample(20, 0.55),
                new SourceRadialSample(35, 0)
            }, 1, 1, 40, 10_000)),
        Object(scene, 2, "中距离探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 20),
            new DetectorRectangleParameters(30, 30, 100, 100, Absorb: false)),
        Object(scene, 3, "远场径向探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 100),
            new DetectorRectangleParameters(145, 145, 145, 145))
    };
    var document = Document(scene, "09 光源：径向强度分布", objects, split: false);
    return SourceSample(scene, "09-radial-intensity-distribution.staropt", "径向强度分布光源",
        "使用角度—相对强度表建立轴对称光源，并在不同传播距离验证同一角分布。", document,
        NonSequentialObjectKind.SourceRadial, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result =>
        {
            var far = DetectorStatistics(document, result.Detectors[1]);
            Require(Math.Abs(far.CentroidXMillimeters) < 1.5 && Math.Abs(far.CentroidYMillimeters) < 1.5,
                "径向光源远场质心应接近光轴。");
            Require(Math.Abs(far.RmsXMillimeters - far.RmsYMillimeters) < 1.5,
                "径向光源远场应近似轴对称。");
        });
}

static TeachingSample VolumeRectangleSource()
{
    const int scene = 10;
    var objects = new[]
    {
        Object(scene, 1, "长方体体光源", NonSequentialObjectKind.SourceVolumeRectangle, new Vector3D(0, 0, 0),
            new SourceVolumeRectangleParameters(12, 6, 4, 3, 1, 1, 40, 10_000)),
        Object(scene, 2, "体源近场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 5),
            new DetectorRectangleParameters(18, 12, 108, 72, Absorb: false)),
        Object(scene, 3, "体源远场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(30, 20, 120, 80))
    };
    var document = Document(scene, "10 光源：长方体体发射", objects, split: false);
    return SourceSample(scene, "10-volume-rectangle-source.staropt", "长方体体光源",
        "观察三维长方体内部均匀起点投影到近场和远场探测器的变化。", document,
        NonSequentialObjectKind.SourceVolumeRectangle, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result => Require(result.Detectors.All(item => item.TotalPowerWatts > 0.99),
            "长方体体光源的近场和远场都应接收到完整功率。"));
}

static TeachingSample VolumeEllipseSource()
{
    const int scene = 11;
    var objects = new[]
    {
        Object(scene, 1, "椭球体光源", NonSequentialObjectKind.SourceVolumeEllipse, new Vector3D(0, 0, 0),
            new SourceVolumeEllipseParameters(6, 3, 2, 3, 1, 1, 40, 10_000)),
        Object(scene, 2, "椭球近场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 5),
            new DetectorRectangleParameters(18, 12, 108, 72, Absorb: false)),
        Object(scene, 3, "椭球远场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(30, 20, 120, 80))
    };
    var document = Document(scene, "11 光源：椭球体发射", objects, split: false);
    return SourceSample(scene, "11-volume-ellipse-source.staropt", "椭球体光源",
        "比较椭球体发射区域的平滑近场轮廓与传播后的远场分布。", document,
        NonSequentialObjectKind.SourceVolumeEllipse, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result =>
        {
            var near = DetectorStatistics(document, result.Detectors[0]);
            Require(near.RmsXMillimeters > near.RmsYMillimeters * 1.7, "椭球近场X向宽度应大于Y向。");
        });
}

static TeachingSample VolumeCylinderSource()
{
    const int scene = 12;
    var objects = new[]
    {
        Object(scene, 1, "椭圆柱体光源", NonSequentialObjectKind.SourceVolumeCylinder, new Vector3D(0, 0, 0),
            new SourceVolumeCylinderParameters(6, 3, 6, 3, 1, 1, 40, 10_000)),
        Object(scene, 2, "圆柱近场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 6),
            new DetectorRectangleParameters(18, 12, 108, 72, Absorb: false)),
        Object(scene, 3, "圆柱远场探测器", NonSequentialObjectKind.DetectorRectangle, new Vector3D(0, 0, 80),
            new DetectorRectangleParameters(30, 20, 120, 80))
    };
    var document = Document(scene, "12 光源：椭圆柱体发射", objects, split: false);
    return SourceSample(scene, "12-volume-cylinder-source.staropt", "椭圆柱体光源",
        "观察椭圆柱体内部均匀发射，并与椭球体的近场边缘形态进行比较。", document,
        NonSequentialObjectKind.SourceVolumeCylinder, new[] { "H2", "D3", "SEQ(Q1,H2,D3)" },
        result =>
        {
            var near = DetectorStatistics(document, result.Detectors[0]);
            Require(near.RmsXMillimeters > near.RmsYMillimeters * 1.7, "椭圆柱近场X向宽度应大于Y向。");
        });
}

static NonSequentialDocument Document(
    int scene,
    string name,
    IReadOnlyList<NonSequentialObjectDefinition> objects,
    bool split,
    double minimumIntensity = 1e-9,
    int maximumSegments = 1_000,
    int maximumBranches = 1_000_000,
    IReadOnlyList<NonSequentialWavelength>? wavelengths = null,
    IReadOnlyList<NonSequentialMeshAsset>? meshAssets = null) => new(
        name,
        wavelengths ?? new[] { new NonSequentialWavelength("d 线", 587.6, 1, true) },
        objects,
        "Air",
        new NonSequentialTraceSettings(
            20,
            10_000,
            1_000_000,
            maximumSegments,
            maximumBranches,
            minimumIntensity,
            scene,
            split),
        meshAssets);

static NonSequentialObjectDefinition Object(
    int scene,
    int number,
    string name,
    NonSequentialObjectKind kind,
    Vector3D origin,
    NonSequentialObjectParameters parameters,
    double rotationX = 0,
    double rotationY = 0,
    double rotationZ = 0,
    Guid? referenceObjectId = null,
    Guid? containingObjectId = null) => new(
        Id(scene, number),
        name,
        kind,
        true,
        true,
        new CoordinateSystem(origin, rotationX, rotationY, rotationZ),
        referenceObjectId,
        containingObjectId,
        parameters);

static TeachingSample Sample(
    int scene,
    string fileName,
    string title,
    string lesson,
    NonSequentialDocument document,
    IReadOnlyList<string> suggestedFilters,
    Action<NonSequentialDocumentTraceResult> validation)
{
    var optic = Optic.CreateBlank();
    optic.Name = $"非序列教学 {scene:00}：{title}";
    return new TeachingSample(
        fileName,
        title,
        lesson,
        new StarOptProjectDocument(new[] { optic }, 0, NonSequentialDocument: document),
        suggestedFilters,
        validation);
}

static TeachingSample SourceSample(
    int scene,
    string fileName,
    string title,
    string lesson,
    NonSequentialDocument document,
    NonSequentialObjectKind sourceKind,
    IReadOnlyList<string> suggestedFilters,
    Action<NonSequentialDocumentTraceResult> validation) =>
    Sample(scene, fileName, title, lesson, document, suggestedFilters, validation) with
    {
        SourceKind = sourceKind,
        GeneratePreview = true
    };

static NonSequentialDocumentTraceResult TraceAndValidate(TeachingSample sample)
{
    var optic = sample.Project.Configurations[sample.Project.ActiveConfigurationIndex];
    var document = sample.Project.NonSequentialDocument
        ?? throw new InvalidOperationException($"样例 {sample.FileName} 缺少非序列文档。");
    var result = new NonSequentialDocumentTracer().Trace(
        document,
        optic.Materials,
        new NonSequentialDocumentTraceRequest(OutputMode: NonSequentialTraceOutputMode.InMemory));
    Require(Math.Abs(result.EnergyBalance.SourcePowerWatts - result.EnergyBalance.AccountedPowerWatts) < 1e-8,
        $"样例 {sample.FileName} 的能量不守恒。");
    foreach (var expression in sample.SuggestedFilters)
    {
        var filter = NonSequentialPathFilter.Parse(expression);
        Require(result.Branches.Any(branch => filter.IsMatch(document, branch)),
            $"样例 {sample.FileName} 的建议筛选“{expression}”没有匹配路径。");
    }
    sample.Validation(result);
    return result;
}

static IReadOnlyList<DetectorResult> BuildDetectorResults(
    NonSequentialDocument document,
    NonSequentialDocumentTraceResult trace) => document.Objects
    .Where(item => item.Kind == NonSequentialObjectKind.DetectorRectangle)
    .Select(item => DetectorStatistics(document, trace.Detectors.Single(frame => frame.DetectorId == item.Id)))
    .ToArray();

static DetectorResult DetectorStatistics(NonSequentialDocument document, NonSequentialDetectorFrame frame)
{
    var definition = document.Objects.Single(item => item.Id == frame.DetectorId);
    var parameters = (DetectorRectangleParameters)definition.Parameters;
    var pixels = CombinedPower(frame);
    var total = pixels.Sum();
    if (total <= 0)
    {
        return new DetectorResult(frame.DetectorName, frame.TotalPowerWatts, 0, 0, 0, 0, 0);
    }

    var centroidX = 0d;
    var centroidY = 0d;
    for (var y = 0; y < frame.PixelsY; y++)
        for (var x = 0; x < frame.PixelsX; x++)
        {
            var power = pixels[(y * frame.PixelsX) + x];
            centroidX += PixelCoordinate(x, frame.PixelsX, parameters.WidthMillimeters) * power;
            centroidY += PixelCoordinate(y, frame.PixelsY, parameters.HeightMillimeters) * power;
        }
    centroidX /= total;
    centroidY /= total;

    var varianceX = 0d;
    var varianceY = 0d;
    for (var y = 0; y < frame.PixelsY; y++)
        for (var x = 0; x < frame.PixelsX; x++)
        {
            var power = pixels[(y * frame.PixelsX) + x];
            var deltaX = PixelCoordinate(x, frame.PixelsX, parameters.WidthMillimeters) - centroidX;
            var deltaY = PixelCoordinate(y, frame.PixelsY, parameters.HeightMillimeters) - centroidY;
            varianceX += deltaX * deltaX * power;
            varianceY += deltaY * deltaY * power;
        }

    return new DetectorResult(
        frame.DetectorName,
        frame.TotalPowerWatts,
        pixels.Max(),
        centroidX,
        centroidY,
        Math.Sqrt(varianceX / total),
        Math.Sqrt(varianceY / total));
}

static double[] CombinedPower(NonSequentialDetectorFrame frame)
{
    var combined = new double[frame.PixelsX * frame.PixelsY];
    foreach (var wavelength in frame.PowerByWavelength.Values)
    {
        for (var index = 0; index < combined.Length; index++) combined[index] += wavelength[index];
    }
    return combined;
}

static double PixelCoordinate(int index, int count, double length) =>
    (((index + 0.5) / count) - 0.5) * length;

static async Task WritePreviewAsync(
    string path,
    TeachingSample sample,
    NonSequentialDocumentTraceResult trace)
{
    var document = sample.Project.NonSequentialDocument
        ?? throw new InvalidOperationException($"样例 {sample.FileName} 缺少非序列文档。");
    var detectors = document.Objects
        .Where(item => item.Kind == NonSequentialObjectKind.DetectorRectangle)
        .Select(item => trace.Detectors.Single(frame => frame.DetectorId == item.Id))
        .Take(2)
        .ToArray();
    var builder = new StringBuilder(512_000);
    builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    builder.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"470\" viewBox=\"0 0 1200 470\">");
    builder.AppendLine("<rect width=\"1200\" height=\"470\" fill=\"#f4f7fb\"/>");
    builder.AppendLine($"<text x=\"24\" y=\"31\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"20\" font-weight=\"700\" fill=\"#172033\">{Escape(sample.Title)}</text>");
    builder.AppendLine("<text x=\"1176\" y=\"30\" text-anchor=\"end\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"12\" fill=\"#667085\">固定种子 · 分析光线 10,000 · 对数色标</text>");
    DrawLayout(builder, document, trace, 20, 52, 360, 380);
    for (var index = 0; index < detectors.Length; index++)
    {
        DrawDetector(builder, document, detectors[index], 400 + (index * 390), 52, 370, 380);
    }
    builder.AppendLine("</svg>");

    var temporaryPath = path + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, builder.ToString(), new UTF8Encoding(false));
    File.Move(temporaryPath, path, overwrite: true);
}

static void DrawLayout(
    StringBuilder builder,
    NonSequentialDocument document,
    NonSequentialDocumentTraceResult trace,
    double left,
    double top,
    double width,
    double height)
{
    DrawPanel(builder, left, top, width, height, "3D布局（X-Z投影）");
    var segments = trace.Branches.Take(180).SelectMany(item => item.Segments).ToArray();
    var points = segments.SelectMany(item => new[] { item.Start, item.End })
        .Concat(document.Objects.Select(item => item.LocalCoordinateSystem.Origin))
        .ToArray();
    var minX = points.Min(item => item.X);
    var maxX = points.Max(item => item.X);
    var minZ = points.Min(item => item.Z);
    var maxZ = points.Max(item => item.Z);
    ExpandRange(ref minX, ref maxX);
    ExpandRange(ref minZ, ref maxZ);
    var plotLeft = left + 24;
    var plotTop = top + 44;
    var plotWidth = width - 48;
    var plotHeight = height - 78;
    double MapX(double value) => plotLeft + ((value - minX) / (maxX - minX) * plotWidth);
    double MapZ(double value) => plotTop + plotHeight - ((value - minZ) / (maxZ - minZ) * plotHeight);

    foreach (var segment in segments)
    {
        builder.AppendLine($"<line x1=\"{N(MapX(segment.Start.X))}\" y1=\"{N(MapZ(segment.Start.Z))}\" x2=\"{N(MapX(segment.End.X))}\" y2=\"{N(MapZ(segment.End.Z))}\" stroke=\"#f4a261\" stroke-width=\"0.75\" opacity=\"0.28\"/>");
    }
    foreach (var item in document.Objects)
    {
        var origin = item.LocalCoordinateSystem.Origin;
        var color = item.Parameters is SourceParameters ? "#ef7d32" : "#2878b5";
        builder.AppendLine($"<circle cx=\"{N(MapX(origin.X))}\" cy=\"{N(MapZ(origin.Z))}\" r=\"4\" fill=\"{color}\" stroke=\"white\" stroke-width=\"1.5\"/>");
    }
    builder.AppendLine($"<text x=\"{N(left + 18)}\" y=\"{N(top + height - 13)}\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"11\" fill=\"#667085\">橙色：光源 / 蓝色：探测器 / 仅显示确定性抽样光线</text>");
}

static void DrawDetector(
    StringBuilder builder,
    NonSequentialDocument document,
    NonSequentialDetectorFrame frame,
    double left,
    double top,
    double width,
    double height)
{
    DrawPanel(builder, left, top, width, height, frame.DetectorName);
    var definition = document.Objects.Single(item => item.Id == frame.DetectorId);
    var parameters = (DetectorRectangleParameters)definition.Parameters;
    var pixels = CombinedPower(frame);
    const int targetColumns = 40;
    const int targetRows = 40;
    var columns = Math.Min(targetColumns, frame.PixelsX);
    var rows = Math.Min(targetRows, frame.PixelsY);
    var reduced = Downsample(pixels, frame.PixelsX, frame.PixelsY, columns, rows);
    var maximum = reduced.Max();
    var plotSize = 286d;
    var plotLeft = left + ((width - plotSize) / 2);
    var plotTop = top + 46;
    var cellWidth = plotSize / columns;
    var cellHeight = plotSize / rows;
    for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var value = maximum <= 0 ? 0 : reduced[(row * columns) + column] / maximum;
            var scaled = Math.Log10(1 + (999 * value)) / 3;
            builder.AppendLine($"<rect x=\"{N(plotLeft + (column * cellWidth))}\" y=\"{N(plotTop + ((rows - row - 1) * cellHeight))}\" width=\"{N(cellWidth + 0.08)}\" height=\"{N(cellHeight + 0.08)}\" fill=\"{HeatColor(scaled)}\"/>");
        }
    builder.AppendLine($"<rect x=\"{N(plotLeft)}\" y=\"{N(plotTop)}\" width=\"{N(plotSize)}\" height=\"{N(plotSize)}\" fill=\"none\" stroke=\"#344054\" stroke-width=\"1\"/>");
    var statistics = DetectorStatistics(document, frame);
    builder.AppendLine($"<text x=\"{N(left + 18)}\" y=\"{N(top + height - 38)}\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"11\" fill=\"#344054\">功率 {statistics.PowerWatts:G6} W　峰值 {statistics.PeakPixelPowerWatts:G4} W/像素</text>");
    builder.AppendLine($"<text x=\"{N(left + 18)}\" y=\"{N(top + height - 18)}\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"11\" fill=\"#667085\">尺寸 {parameters.WidthMillimeters:G4} × {parameters.HeightMillimeters:G4} mm　RMS {statistics.RmsXMillimeters:G4} / {statistics.RmsYMillimeters:G4} mm</text>");
}

static double[] Downsample(double[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
{
    var result = new double[targetWidth * targetHeight];
    for (var targetY = 0; targetY < targetHeight; targetY++)
        for (var targetX = 0; targetX < targetWidth; targetX++)
        {
            var startX = targetX * sourceWidth / targetWidth;
            var endX = Math.Max(startX + 1, (targetX + 1) * sourceWidth / targetWidth);
            var startY = targetY * sourceHeight / targetHeight;
            var endY = Math.Max(startY + 1, (targetY + 1) * sourceHeight / targetHeight);
            var total = 0d;
            for (var y = startY; y < endY; y++)
                for (var x = startX; x < endX; x++) total += source[(y * sourceWidth) + x];
            result[(targetY * targetWidth) + targetX] = total;
        }
    return result;
}

static void DrawPanel(StringBuilder builder, double left, double top, double width, double height, string title)
{
    builder.AppendLine($"<rect x=\"{N(left)}\" y=\"{N(top)}\" width=\"{N(width)}\" height=\"{N(height)}\" rx=\"10\" fill=\"white\" stroke=\"#d0d5dd\"/>");
    builder.AppendLine($"<text x=\"{N(left + 18)}\" y=\"{N(top + 27)}\" font-family=\"Microsoft YaHei, sans-serif\" font-size=\"14\" font-weight=\"600\" fill=\"#344054\">{Escape(title)}</text>");
}

static void ExpandRange(ref double minimum, ref double maximum)
{
    if (maximum - minimum < 1e-6)
    {
        minimum -= 1;
        maximum += 1;
        return;
    }
    var padding = (maximum - minimum) * 0.08;
    minimum -= padding;
    maximum += padding;
}

static string HeatColor(double value)
{
    value = Math.Clamp(value, 0, 1);
    var red = (int)Math.Round(25 + (230 * value));
    var green = (int)Math.Round(35 + (170 * Math.Sin(Math.PI * value)));
    var blue = (int)Math.Round(110 + (130 * (1 - value)));
    return $"rgb({red},{green},{blue})";
}

static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

static Guid Id(int scene, int number) => Guid.Parse($"{scene:X8}-0000-0000-0000-{number:X12}");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string SquareRingStl() => """
solid teaching_square_baffle
facet normal 0 0 1
 outer loop
  vertex -15 -15 0
  vertex 15 -15 0
  vertex 15 -4 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex -15 -15 0
  vertex 15 -4 0
  vertex -15 -4 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex -15 4 0
  vertex 15 4 0
  vertex 15 15 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex -15 4 0
  vertex 15 15 0
  vertex -15 15 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex -15 -4 0
  vertex -4 -4 0
  vertex -4 4 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex -15 -4 0
  vertex -4 4 0
  vertex -15 4 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex 4 -4 0
  vertex 15 -4 0
  vertex 15 4 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex 4 -4 0
  vertex 15 4 0
  vertex 4 4 0
 endloop
endfacet
endsolid teaching_square_baffle
""";

sealed record TeachingSample(
    string FileName,
    string Title,
    string Lesson,
    StarOptProjectDocument Project,
    IReadOnlyList<string> SuggestedFilters,
    Action<NonSequentialDocumentTraceResult> Validation,
    NonSequentialObjectKind? SourceKind = null,
    bool GeneratePreview = false);

sealed record SampleManifest(
    int Version,
    string Name,
    string LengthUnit,
    IReadOnlyList<SampleManifestEntry> Samples);

sealed record SampleManifestEntry(
    string File,
    string Title,
    string Lesson,
    int ObjectCount,
    int MeshAssetCount,
    int BranchCount,
    double SourcePowerWatts,
    double DetectorPowerWatts,
    double AbsorbedPowerWatts,
    IReadOnlyList<string> SuggestedFilters,
    string? SourceKind,
    string? PreviewFile,
    IReadOnlyList<DetectorResult> DetectorResults);

sealed record DetectorResult(
    string Name,
    double PowerWatts,
    double PeakPixelPowerWatts,
    double CentroidXMillimeters,
    double CentroidYMillimeters,
    double RmsXMillimeters,
    double RmsYMillimeters);
