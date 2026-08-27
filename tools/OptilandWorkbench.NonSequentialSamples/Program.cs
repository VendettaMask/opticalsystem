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
    EmbeddedStlBaffle()
};
var manifestEntries = new List<SampleManifestEntry>();
foreach (var sample in samples)
{
    var trace = TraceAndValidate(sample);
    var path = Path.Combine(outputDirectory, sample.FileName);
    await StarOptProjectStore.SaveAsync(sample.Project, path);
    var restored = await StarOptProjectStore.LoadAsync(path);
    restored.NonSequentialDocument?.Validate();
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
        sample.SuggestedFilters));
    Console.WriteLine(
        $"{sample.FileName}: objects={sample.Project.NonSequentialDocument.Objects.Count}, "
        + $"branches={trace.TotalBranchCount}, detector={trace.EnergyBalance.DetectorPowerWatts:G8} W");
}

var manifest = new SampleManifest(
    1,
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
    Action<NonSequentialDocumentTraceResult> Validation);

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
    IReadOnlyList<string> SuggestedFilters);
