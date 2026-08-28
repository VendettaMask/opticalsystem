using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;

namespace OptilandWorkbench.Core.NonSequential;

public enum NonSequentialObjectKind
{
    SourceRay,
    SourcePoint,
    SourceRectangle,
    SourceGaussian,
    PlaneRectangle,
    Sphere,
    Cylinder,
    Box,
    StandardLens,
    Mesh,
    DetectorRectangle,
    SourceEllipse,
    SourceTwoAngle,
    SourceRadial,
    SourceVolumeRectangle,
    SourceVolumeEllipse,
    SourceVolumeCylinder
}

public enum NonSequentialSourceApertureShape
{
    Rectangle,
    Ellipse
}

public enum NonSequentialSurfaceSourceAngularDistribution
{
    LegacyUniformCone,
    VirtualPoint,
    Cosine,
    Gaussian
}

public enum NonSequentialVolumeSourceAngularDistribution
{
    LegacyForwardCone,
    UniformSphere
}

public enum NonSequentialSurfaceBehavior
{
    Refractive,
    Reflective,
    Absorbing
}

public sealed record NonSequentialWavelength(string Label, double Nanometers, double Weight, bool IsPrimary);

public sealed record NonSequentialTraceSettings(
    int LayoutRaysPerSource = 20,
    int AnalysisRaysPerSource = 10_000,
    int MaximumTotalSourceRays = 1_000_000,
    int MaximumSegmentsPerRay = 1_000,
    int MaximumActiveBranches = 1_000_000,
    double MinimumRelativeIntensity = 1e-9,
    int RandomSeed = 1,
    bool SplitFresnelRays = true);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SourceRayParameters), "source-ray")]
[JsonDerivedType(typeof(SourcePointParameters), "source-point")]
[JsonDerivedType(typeof(SourceRectangleParameters), "source-rectangle")]
[JsonDerivedType(typeof(SourceGaussianParameters), "source-gaussian")]
[JsonDerivedType(typeof(SourceEllipseParameters), "source-ellipse")]
[JsonDerivedType(typeof(SourceTwoAngleParameters), "source-two-angle")]
[JsonDerivedType(typeof(SourceRadialParameters), "source-radial")]
[JsonDerivedType(typeof(SourceVolumeRectangleParameters), "source-volume-rectangle")]
[JsonDerivedType(typeof(SourceVolumeEllipseParameters), "source-volume-ellipse")]
[JsonDerivedType(typeof(SourceVolumeCylinderParameters), "source-volume-cylinder")]
[JsonDerivedType(typeof(PlaneRectangleParameters), "plane-rectangle")]
[JsonDerivedType(typeof(SphereParameters), "sphere")]
[JsonDerivedType(typeof(CylinderParameters), "cylinder")]
[JsonDerivedType(typeof(BoxParameters), "box")]
[JsonDerivedType(typeof(StandardLensParameters), "standard-lens")]
[JsonDerivedType(typeof(MeshObjectParameters), "mesh")]
[JsonDerivedType(typeof(DetectorRectangleParameters), "detector-rectangle")]
public abstract record NonSequentialObjectParameters;

public abstract record SourceParameters(
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount) : NonSequentialObjectParameters;

public sealed record SourceRayParameters(
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    Vector3D? LocalOrigin = null,
    Vector3D? LocalDirection = null) : SourceParameters(PowerWatts, WavelengthNumber, 1, 1)
{
    public Vector3D Origin => LocalOrigin ?? Vector3D.Zero;
    public Vector3D Direction => LocalDirection ?? new Vector3D(0, 0, 1);
}

public sealed record SourcePointParameters(
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    double ConeHalfAngleDegrees = 20,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceRectangleParameters(
    double WidthMillimeters = 10,
    double HeightMillimeters = 10,
    double AngularHalfAngleDegrees = 20,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000,
    NonSequentialSurfaceSourceAngularDistribution AngularDistribution = NonSequentialSurfaceSourceAngularDistribution.LegacyUniformCone,
    double SourceDistanceMillimeters = 0,
    double CosineExponent = 1,
    double GaussianX = 1,
    double GaussianY = 1,
    double SourceX = 0,
    double SourceY = 0,
    double MinimumXHalfWidthMillimeters = 0,
    double MinimumYHalfWidthMillimeters = 0) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceGaussianParameters(
    double WaistXMillimeters = 1,
    double WaistYMillimeters = 1,
    double DivergenceHalfAngleDegrees = 5,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceEllipseParameters(
    double WidthMillimeters = 10,
    double HeightMillimeters = 10,
    double AngularHalfAngleDegrees = 20,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000,
    NonSequentialSurfaceSourceAngularDistribution AngularDistribution = NonSequentialSurfaceSourceAngularDistribution.LegacyUniformCone,
    double SourceDistanceMillimeters = 0,
    double CosineExponent = 1,
    double GaussianX = 1,
    double GaussianY = 1,
    double SourceX = 0,
    double SourceY = 0,
    double MinimumXHalfWidthMillimeters = 0,
    double MinimumYHalfWidthMillimeters = 0) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceTwoAngleParameters(
    double WidthMillimeters = 10,
    double HeightMillimeters = 10,
    NonSequentialSourceApertureShape Shape = NonSequentialSourceApertureShape.Rectangle,
    double AngularHalfAngleXDegrees = 20,
    double AngularHalfAngleYDegrees = 10,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceRadialSample(double AngleDegrees, double RelativeIntensity);

public sealed record SourceRadialParameters(
    IReadOnlyList<SourceRadialSample>? Samples = null,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount)
{
    [JsonIgnore]
    public IReadOnlyList<SourceRadialSample> Distribution => Samples ??
        new[] { new SourceRadialSample(0, 1), new SourceRadialSample(30, 0) };
}

public sealed record SourceVolumeRectangleParameters(
    double WidthMillimeters = 10,
    double HeightMillimeters = 10,
    double DepthMillimeters = 10,
    double AngularHalfAngleDegrees = 20,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceVolumeEllipseParameters(
    double SemiAxisXMillimeters = 5,
    double SemiAxisYMillimeters = 5,
    double SemiAxisZMillimeters = 5,
    double AngularHalfAngleDegrees = 20,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceVolumeCylinderParameters(
    double RadiusXMillimeters = 5,
    double RadiusYMillimeters = 5,
    double LengthMillimeters = 10,
    double AngularHalfAngleDegrees = 20,
    double PowerWatts = 1,
    int WavelengthNumber = 1,
    int LayoutRayCount = 20,
    int AnalysisRayCount = 10_000,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record PlaneRectangleParameters(
    double WidthMillimeters = 20,
    double HeightMillimeters = 20,
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Reflective,
    string MaterialBefore = "Air",
    string MaterialAfter = "Air") : NonSequentialObjectParameters;

public sealed record SphereParameters(
    double RadiusMillimeters = 10,
    string Material = "N-BK7",
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Refractive) : NonSequentialObjectParameters;

public sealed record CylinderParameters(
    double RadiusMillimeters = 10,
    double LengthMillimeters = 20,
    string Material = "N-BK7",
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Refractive) : NonSequentialObjectParameters;

public sealed record BoxParameters(
    double WidthMillimeters = 20,
    double HeightMillimeters = 20,
    double LengthMillimeters = 20,
    string Material = "N-BK7",
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Refractive) : NonSequentialObjectParameters;

public sealed record StandardLensParameters(
    double FrontRadiusMillimeters = 50,
    double BackRadiusMillimeters = -50,
    double FrontConic = 0,
    double BackConic = 0,
    double CenterThicknessMillimeters = 5,
    double SemiDiameterMillimeters = 10,
    string Material = "N-BK7") : NonSequentialObjectParameters;

public sealed record MeshObjectParameters(
    Guid MeshAssetId,
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Absorbing,
    string Material = "Air",
    bool TwoSided = true) : NonSequentialObjectParameters;

public sealed record DetectorRectangleParameters(
    double WidthMillimeters = 20,
    double HeightMillimeters = 20,
    int PixelsX = 100,
    int PixelsY = 100,
    bool FrontOnly = true,
    bool Absorb = true) : NonSequentialObjectParameters;

public sealed record NonSequentialObjectDefinition(
    Guid Id,
    string Name,
    NonSequentialObjectKind Kind,
    bool Enabled,
    bool Visible,
    CoordinateSystem LocalCoordinateSystem,
    Guid? ReferenceObjectId,
    Guid? ContainingObjectId,
    NonSequentialObjectParameters Parameters)
{
    public static NonSequentialObjectDefinition Create(NonSequentialObjectKind kind, string? name = null, Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        string.IsNullOrWhiteSpace(name) ? DefaultName(kind) : name.Trim(),
        kind,
        true,
        true,
        CoordinateSystem.Global,
        null,
        null,
        DefaultParameters(kind));

    public static NonSequentialObjectParameters DefaultParameters(NonSequentialObjectKind kind) => kind switch
    {
        NonSequentialObjectKind.SourceRay => new SourceRayParameters(),
        NonSequentialObjectKind.SourcePoint => new SourcePointParameters(),
        NonSequentialObjectKind.SourceRectangle => new SourceRectangleParameters(
            AngularHalfAngleDegrees: 0,
            AngularDistribution: NonSequentialSurfaceSourceAngularDistribution.VirtualPoint),
        NonSequentialObjectKind.SourceGaussian => new SourceGaussianParameters(),
        NonSequentialObjectKind.SourceEllipse => new SourceEllipseParameters(
            AngularHalfAngleDegrees: 0,
            AngularDistribution: NonSequentialSurfaceSourceAngularDistribution.VirtualPoint),
        NonSequentialObjectKind.SourceTwoAngle => new SourceTwoAngleParameters(),
        NonSequentialObjectKind.SourceRadial => new SourceRadialParameters(),
        NonSequentialObjectKind.SourceVolumeRectangle => new SourceVolumeRectangleParameters(
            AngularDistribution: NonSequentialVolumeSourceAngularDistribution.UniformSphere),
        NonSequentialObjectKind.SourceVolumeEllipse => new SourceVolumeEllipseParameters(
            AngularDistribution: NonSequentialVolumeSourceAngularDistribution.UniformSphere),
        NonSequentialObjectKind.SourceVolumeCylinder => new SourceVolumeCylinderParameters(
            AngularDistribution: NonSequentialVolumeSourceAngularDistribution.UniformSphere),
        NonSequentialObjectKind.PlaneRectangle => new PlaneRectangleParameters(),
        NonSequentialObjectKind.Sphere => new SphereParameters(),
        NonSequentialObjectKind.Cylinder => new CylinderParameters(),
        NonSequentialObjectKind.Box => new BoxParameters(),
        NonSequentialObjectKind.StandardLens => new StandardLensParameters(),
        NonSequentialObjectKind.Mesh => throw new InvalidOperationException("网格对象必须通过 STL 导入创建，不能使用空资产。"),
        NonSequentialObjectKind.DetectorRectangle => new DetectorRectangleParameters(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string DefaultName(NonSequentialObjectKind kind) => kind switch
    {
        NonSequentialObjectKind.SourceRay => "单射线光源",
        NonSequentialObjectKind.SourcePoint => "点光源",
        NonSequentialObjectKind.SourceRectangle => "矩形光源",
        NonSequentialObjectKind.SourceGaussian => "高斯光源",
        NonSequentialObjectKind.SourceEllipse => "椭圆面光源",
        NonSequentialObjectKind.SourceTwoAngle => "双角度面光源",
        NonSequentialObjectKind.SourceRadial => "径向分布光源",
        NonSequentialObjectKind.SourceVolumeRectangle => "矩形体光源",
        NonSequentialObjectKind.SourceVolumeEllipse => "椭球体光源",
        NonSequentialObjectKind.SourceVolumeCylinder => "圆柱体光源",
        NonSequentialObjectKind.PlaneRectangle => "矩形平面",
        NonSequentialObjectKind.Sphere => "球体",
        NonSequentialObjectKind.Cylinder => "圆柱体",
        NonSequentialObjectKind.Box => "长方体",
        NonSequentialObjectKind.StandardLens => "标准镜片",
        NonSequentialObjectKind.Mesh => "STL 网格",
        NonSequentialObjectKind.DetectorRectangle => "矩形探测器",
        _ => kind.ToString()
    };
}

public sealed class NonSequentialDocument
{
    public const int MaximumObjectCount = 100_000;
    public const int MaximumWavelengthCount = 1_024;
    public const int MaximumMeshAssetCount = 4_096;
    public const long MaximumMeshAssetBytes = 512L * 1024 * 1024;
    private readonly List<NonSequentialWavelength> _wavelengths;
    private readonly List<NonSequentialObjectDefinition> _objects;
    private readonly List<NonSequentialMeshAsset> _meshAssets;

    public NonSequentialDocument(
        string name,
        IReadOnlyList<NonSequentialWavelength> wavelengths,
        IReadOnlyList<NonSequentialObjectDefinition>? objects = null,
        string ambientMaterial = "Air",
        NonSequentialTraceSettings? traceSettings = null,
        IReadOnlyList<NonSequentialMeshAsset>? meshAssets = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Non-Sequential Scene" : name.Trim();
        AmbientMaterial = string.IsNullOrWhiteSpace(ambientMaterial) ? "Air" : ambientMaterial.Trim();
        TraceSettings = traceSettings ?? new NonSequentialTraceSettings();
        _wavelengths = wavelengths?.ToList() ?? throw new ArgumentNullException(nameof(wavelengths));
        _objects = objects?.ToList() ?? new List<NonSequentialObjectDefinition>();
        _meshAssets = meshAssets?.ToList() ?? new List<NonSequentialMeshAsset>();
        Validate();
    }

    public string Name { get; set; }
    public string AmbientMaterial { get; set; }
    public NonSequentialTraceSettings TraceSettings { get; set; }
    public IReadOnlyList<NonSequentialWavelength> Wavelengths => _wavelengths;
    public IReadOnlyList<NonSequentialObjectDefinition> Objects => _objects;
    public IReadOnlyList<NonSequentialMeshAsset> MeshAssets => _meshAssets;

    public static NonSequentialDocument CreateDefault(string name, IEnumerable<NonSequentialWavelength> wavelengths) =>
        new(name, wavelengths.ToArray());

    public NonSequentialDocument Clone() => new(
        Name,
        _wavelengths.ToArray(),
        _objects.ToArray(),
        AmbientMaterial,
        TraceSettings,
        _meshAssets.ToArray());

    public NonSequentialMeshAsset FindMeshAsset(Guid id) =>
        _meshAssets.FirstOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException($"Non-sequential mesh asset '{id}' was not found.");

    public Guid AddMeshAsset(NonSequentialMeshAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var duplicate = _meshAssets.FirstOrDefault(item =>
            item.Sha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            return duplicate.Id;
        }

        _meshAssets.Add(asset);
        try
        {
            ValidateMeshAssets(requireGeometry: true);
            return asset.Id;
        }
        catch
        {
            _meshAssets.Remove(asset);
            throw;
        }
    }

    public void AttachMeshAssetData(Guid id, byte[] canonicalData)
    {
        ArgumentNullException.ThrowIfNull(canonicalData);
        var index = _meshAssets.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            throw new InvalidDataException($"STAROPT 引用了不存在的网格资产“{id}”。");
        }

        _meshAssets[index] = _meshAssets[index].AttachCanonicalData(canonicalData);
    }

    public void RemoveUnusedMeshAssets()
    {
        var used = _objects
            .Select(item => item.Parameters)
            .OfType<MeshObjectParameters>()
            .Select(item => item.MeshAssetId)
            .ToHashSet();
        _meshAssets.RemoveAll(asset => !used.Contains(asset.Id));
    }

    public void ReplaceWavelengths(IEnumerable<NonSequentialWavelength> wavelengths)
    {
        var replacement = wavelengths?.ToList() ?? throw new ArgumentNullException(nameof(wavelengths));
        var previous = _wavelengths.ToArray();
        _wavelengths.Clear();
        _wavelengths.AddRange(replacement);
        try { Validate(); }
        catch
        {
            _wavelengths.Clear();
            _wavelengths.AddRange(previous);
            throw;
        }
    }

    public void Insert(int index, NonSequentialObjectDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var insertionIndex = Math.Clamp(index, 0, _objects.Count);
        _objects.Insert(insertionIndex, item);
        try { Validate(); }
        catch
        {
            _objects.RemoveAt(insertionIndex);
            throw;
        }
    }

    public void Replace(Guid id, NonSequentialObjectDefinition replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var index = IndexOf(id);
        var previous = _objects[index];
        if (replacement.Id != id)
        {
            throw new ArgumentException("The replacement object must preserve its stable id.", nameof(replacement));
        }

        _objects[index] = replacement;
        try
        {
            Validate();
            RemoveUnusedMeshAssets();
        }
        catch
        {
            _objects[index] = previous;
            throw;
        }
    }

    public void Remove(Guid id)
    {
        var dependent = _objects.FirstOrDefault(item => item.ReferenceObjectId == id || item.ContainingObjectId == id);
        if (dependent is not null)
        {
            throw new InvalidOperationException($"对象“{dependent.Name}”仍引用或包含于待删除对象，不能删除。");
        }

        _objects.RemoveAt(IndexOf(id));
        RemoveUnusedMeshAssets();
    }

    public void Move(Guid id, int destinationIndex)
    {
        var sourceIndex = IndexOf(id);
        var item = _objects[sourceIndex];
        _objects.RemoveAt(sourceIndex);
        _objects.Insert(Math.Clamp(destinationIndex, 0, _objects.Count), item);
    }

    public Vector3D ToWorldPoint(Guid objectId, Vector3D localPoint) =>
        ToWorldPoint(Find(objectId), localPoint, new HashSet<Guid>());

    public Vector3D ToWorldDirection(Guid objectId, Vector3D localDirection) =>
        ToWorldDirection(Find(objectId), localDirection, new HashSet<Guid>());

    public Vector3D ToLocalPoint(Guid objectId, Vector3D worldPoint)
    {
        var chain = ReferenceChain(Find(objectId));
        var point = worldPoint;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            point = chain[index].LocalCoordinateSystem.ToLocalPoint(point);
        }

        return point;
    }

    public Vector3D ToLocalDirection(Guid objectId, Vector3D worldDirection)
    {
        var chain = ReferenceChain(Find(objectId));
        var direction = worldDirection;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            direction = chain[index].LocalCoordinateSystem.ToLocalDirection(direction);
        }

        return direction;
    }

    public void Validate()
    {
        if (_wavelengths.Count == 0 || _wavelengths.Count > MaximumWavelengthCount)
        {
            throw new InvalidDataException("非序列波长表必须包含 1 到 1024 个波长。");
        }

        if (_wavelengths.Count(item => item.IsPrimary) != 1 || _wavelengths.Any(item =>
            string.IsNullOrWhiteSpace(item.Label) || !double.IsFinite(item.Nanometers) || item.Nanometers <= 0
            || !double.IsFinite(item.Weight) || item.Weight < 0))
        {
            throw new InvalidDataException("非序列波长表无效，必须有且仅有一个主波长且数值有限。");
        }

        if (_objects.Count > MaximumObjectCount)
        {
            throw new InvalidDataException("非序列对象数量超过 100000 个上限。");
        }

        if (_objects.Any(item => item.Id == Guid.Empty)
            || _objects.Select(item => item.Id).Distinct().Count() != _objects.Count)
        {
            throw new InvalidDataException("非序列对象必须具有非空且唯一的稳定 ID。");
        }

        var ids = _objects.Select(item => item.Id).ToHashSet();
        ValidateMeshAssets(requireGeometry: false);
        foreach (var item in _objects)
        {
            ValidateObject(item, ids);
            _ = ReferenceChain(item);
            _ = ContainmentChain(item);
        }

        ValidateTraceSettings(TraceSettings);
    }

    private void ValidateObject(NonSequentialObjectDefinition item, HashSet<Guid> ids)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || item.Parameters is null || !Enum.IsDefined(item.Kind)
            || item.ReferenceObjectId == item.Id || item.ContainingObjectId == item.Id
            || item.ReferenceObjectId is Guid reference && !ids.Contains(reference)
            || item.ContainingObjectId is Guid container && !ids.Contains(container))
        {
            throw new InvalidDataException($"非序列对象“{item.Name}”的公共数据或引用无效。");
        }

        var coordinate = item.LocalCoordinateSystem;
        RequireFinite(coordinate.Origin.X, coordinate.Origin.Y, coordinate.Origin.Z,
            coordinate.RotationXDegrees, coordinate.RotationYDegrees, coordinate.RotationZDegrees);
        if (!ParametersMatch(item.Kind, item.Parameters))
        {
            throw new InvalidDataException($"非序列对象“{item.Name}”的类型与参数不匹配。");
        }

        ValidateParameters(item.Name, item.Parameters);
    }

    private static bool ParametersMatch(NonSequentialObjectKind kind, NonSequentialObjectParameters parameters) =>
        (kind, parameters) switch
        {
            (NonSequentialObjectKind.SourceRay, SourceRayParameters) => true,
            (NonSequentialObjectKind.SourcePoint, SourcePointParameters) => true,
            (NonSequentialObjectKind.SourceRectangle, SourceRectangleParameters) => true,
            (NonSequentialObjectKind.SourceGaussian, SourceGaussianParameters) => true,
            (NonSequentialObjectKind.SourceEllipse, SourceEllipseParameters) => true,
            (NonSequentialObjectKind.SourceTwoAngle, SourceTwoAngleParameters) => true,
            (NonSequentialObjectKind.SourceRadial, SourceRadialParameters) => true,
            (NonSequentialObjectKind.SourceVolumeRectangle, SourceVolumeRectangleParameters) => true,
            (NonSequentialObjectKind.SourceVolumeEllipse, SourceVolumeEllipseParameters) => true,
            (NonSequentialObjectKind.SourceVolumeCylinder, SourceVolumeCylinderParameters) => true,
            (NonSequentialObjectKind.PlaneRectangle, PlaneRectangleParameters) => true,
            (NonSequentialObjectKind.Sphere, SphereParameters) => true,
            (NonSequentialObjectKind.Cylinder, CylinderParameters) => true,
            (NonSequentialObjectKind.Box, BoxParameters) => true,
            (NonSequentialObjectKind.StandardLens, StandardLensParameters) => true,
            (NonSequentialObjectKind.Mesh, MeshObjectParameters) => true,
            (NonSequentialObjectKind.DetectorRectangle, DetectorRectangleParameters) => true,
            _ => false
        };

    private void ValidateParameters(string name, NonSequentialObjectParameters parameters)
    {
        switch (parameters)
        {
            case SourceRayParameters ray:
                ValidateSource(name, ray);
                RequireFinite(ray.Origin.X, ray.Origin.Y, ray.Origin.Z, ray.Direction.X, ray.Direction.Y, ray.Direction.Z);
                if (ray.Direction.Length <= 1e-15) throw new InvalidDataException($"光源“{name}”的方向不能为零向量。");
                break;
            case SourcePointParameters point:
                ValidateSource(name, point);
                RequireRange(point.ConeHalfAngleDegrees, 0, 90, name);
                break;
            case SourceRectangleParameters rectangle:
                ValidateSource(name, rectangle);
                RequirePositive(rectangle.WidthMillimeters, rectangle.HeightMillimeters);
                RequireRange(rectangle.AngularHalfAngleDegrees, 0, 90, name);
                ValidateSurfaceSourceDistribution(
                    name,
                    rectangle.AngularDistribution,
                    rectangle.SourceDistanceMillimeters,
                    rectangle.CosineExponent,
                    rectangle.GaussianX,
                    rectangle.GaussianY,
                    rectangle.SourceX,
                    rectangle.SourceY,
                    rectangle.MinimumXHalfWidthMillimeters,
                    rectangle.MinimumYHalfWidthMillimeters,
                    rectangle.WidthMillimeters / 2,
                    rectangle.HeightMillimeters / 2);
                break;
            case SourceGaussianParameters gaussian:
                ValidateSource(name, gaussian);
                RequirePositive(gaussian.WaistXMillimeters, gaussian.WaistYMillimeters);
                RequireRange(gaussian.DivergenceHalfAngleDegrees, 0, 90, name);
                break;
            case SourceEllipseParameters ellipse:
                ValidateSource(name, ellipse);
                RequirePositive(ellipse.WidthMillimeters, ellipse.HeightMillimeters);
                RequireRange(ellipse.AngularHalfAngleDegrees, 0, 90, name);
                ValidateSurfaceSourceDistribution(
                    name,
                    ellipse.AngularDistribution,
                    ellipse.SourceDistanceMillimeters,
                    ellipse.CosineExponent,
                    ellipse.GaussianX,
                    ellipse.GaussianY,
                    ellipse.SourceX,
                    ellipse.SourceY,
                    ellipse.MinimumXHalfWidthMillimeters,
                    ellipse.MinimumYHalfWidthMillimeters,
                    ellipse.WidthMillimeters / 2,
                    ellipse.HeightMillimeters / 2);
                break;
            case SourceTwoAngleParameters twoAngle:
                ValidateSource(name, twoAngle);
                RequirePositive(twoAngle.WidthMillimeters, twoAngle.HeightMillimeters);
                if (!Enum.IsDefined(twoAngle.Shape))
                    throw new InvalidDataException($"光源“{name}”的发光面形状无效。");
                RequireRange(twoAngle.AngularHalfAngleXDegrees, 0, 90, name);
                RequireRange(twoAngle.AngularHalfAngleYDegrees, 0, 90, name);
                break;
            case SourceRadialParameters radial:
                ValidateSource(name, radial);
                ValidateRadialDistribution(name, radial.Distribution);
                break;
            case SourceVolumeRectangleParameters volumeRectangle:
                ValidateSource(name, volumeRectangle);
                RequirePositive(volumeRectangle.WidthMillimeters, volumeRectangle.HeightMillimeters, volumeRectangle.DepthMillimeters);
                RequireRange(volumeRectangle.AngularHalfAngleDegrees, 0, 90, name);
                ValidateVolumeSourceDistribution(name, volumeRectangle.AngularDistribution);
                break;
            case SourceVolumeEllipseParameters volumeEllipse:
                ValidateSource(name, volumeEllipse);
                RequirePositive(volumeEllipse.SemiAxisXMillimeters, volumeEllipse.SemiAxisYMillimeters, volumeEllipse.SemiAxisZMillimeters);
                RequireRange(volumeEllipse.AngularHalfAngleDegrees, 0, 90, name);
                ValidateVolumeSourceDistribution(name, volumeEllipse.AngularDistribution);
                break;
            case SourceVolumeCylinderParameters volumeCylinder:
                ValidateSource(name, volumeCylinder);
                RequirePositive(volumeCylinder.RadiusXMillimeters, volumeCylinder.RadiusYMillimeters, volumeCylinder.LengthMillimeters);
                RequireRange(volumeCylinder.AngularHalfAngleDegrees, 0, 90, name);
                ValidateVolumeSourceDistribution(name, volumeCylinder.AngularDistribution);
                break;
            case PlaneRectangleParameters plane:
                RequirePositive(plane.WidthMillimeters, plane.HeightMillimeters);
                RequireMaterials(name, plane.MaterialBefore, plane.MaterialAfter);
                break;
            case SphereParameters sphere:
                RequirePositive(sphere.RadiusMillimeters);
                RequireMaterials(name, sphere.Material);
                break;
            case CylinderParameters cylinder:
                RequirePositive(cylinder.RadiusMillimeters, cylinder.LengthMillimeters);
                RequireMaterials(name, cylinder.Material);
                break;
            case BoxParameters box:
                RequirePositive(box.WidthMillimeters, box.HeightMillimeters, box.LengthMillimeters);
                RequireMaterials(name, box.Material);
                break;
            case StandardLensParameters lens:
                RequireFinite(lens.FrontRadiusMillimeters, lens.BackRadiusMillimeters, lens.FrontConic, lens.BackConic);
                RequirePositive(lens.CenterThicknessMillimeters, lens.SemiDiameterMillimeters);
                RequireMaterials(name, lens.Material);
                break;
            case MeshObjectParameters mesh:
                var asset = _meshAssets.FirstOrDefault(item => item.Id == mesh.MeshAssetId)
                    ?? throw new InvalidDataException($"网格对象“{name}”引用不存在的网格资产。");
                RequireMaterials(name, mesh.Material);
                if (mesh.Behavior == NonSequentialSurfaceBehavior.Refractive
                    && (!asset.IsClosed || !asset.IsManifold || !asset.IsConnected || !asset.IsOrientable
                        || asset.HasSelfIntersections || asset.SignedVolumeCubicMillimeters <= 0))
                {
                    throw new InvalidDataException($"折射网格对象“{name}”必须引用闭合、连通、可定向、无自相交的正体积流形网格。");
                }
                break;
            case DetectorRectangleParameters detector:
                RequirePositive(detector.WidthMillimeters, detector.HeightMillimeters);
                if (detector.PixelsX <= 0 || detector.PixelsX > 16_384 || detector.PixelsY <= 0 || detector.PixelsY > 16_384
                    || (long)detector.PixelsX * detector.PixelsY > 67_108_864)
                {
                    throw new InvalidDataException($"探测器“{name}”的像素尺寸无效或过大。");
                }

                break;
            default:
                throw new InvalidDataException($"对象“{name}”使用未知参数类型。");
        }
    }

    private void ValidateMeshAssets(bool requireGeometry)
    {
        if (_meshAssets.Count > MaximumMeshAssetCount
            || _meshAssets.Any(item => item.Id == Guid.Empty)
            || _meshAssets.Select(item => item.Id).Distinct().Count() != _meshAssets.Count
            || _meshAssets.Select(item => item.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _meshAssets.Count)
        {
            throw new InvalidDataException("非序列网格资产数量、ID 或内容哈希无效。");
        }

        long totalBytes = 0;
        foreach (var asset in _meshAssets)
        {
            if (string.IsNullOrWhiteSpace(asset.OriginalFileName)
                || string.IsNullOrWhiteSpace(asset.SourceFormat)
                || asset.Sha256.Length != 64
                || !double.IsFinite(asset.UnitScaleToMillimeters) || asset.UnitScaleToMillimeters <= 0
                || asset.VertexCount <= 0 || asset.TriangleCount <= 0
                || asset.TriangleCount > NonSequentialStlImporter.MaximumTriangleCount
                || !Finite(asset.BoundsMinimum) || !Finite(asset.BoundsMaximum)
                || !double.IsFinite(asset.SignedVolumeCubicMillimeters)
                || requireGeometry && !asset.HasGeometry)
            {
                throw new InvalidDataException($"网格资产“{asset.OriginalFileName}”的元数据或几何数据无效。");
            }

            if (asset.CanonicalData is { } data)
            {
                totalBytes = checked(totalBytes + data.Length);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
                if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"网格资产“{asset.OriginalFileName}”的内容哈希无效。");
                }
            }
        }

        if (totalBytes > MaximumMeshAssetBytes)
        {
            throw new InvalidDataException("非序列内嵌网格资产超过 512 MiB 解压大小上限。");
        }

        static bool Finite(OptilandWorkbench.Core.Backend.Vector3D value) =>
            double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    }

    private void ValidateSource(string name, SourceParameters source)
    {
        if (!double.IsFinite(source.PowerWatts) || source.PowerWatts <= 0
            || source.WavelengthNumber <= 0 || source.WavelengthNumber > _wavelengths.Count
            || source.LayoutRayCount <= 0 || source.LayoutRayCount > 10_000
            || source.AnalysisRayCount <= 0 || source.AnalysisRayCount > 1_000_000)
        {
            throw new InvalidDataException($"光源“{name}”的功率、波长或射线数量无效。");
        }
    }

    private static void ValidateSurfaceSourceDistribution(
        string name,
        NonSequentialSurfaceSourceAngularDistribution distribution,
        double sourceDistance,
        double cosineExponent,
        double gaussianX,
        double gaussianY,
        double sourceX,
        double sourceY,
        double minimumXHalfWidth,
        double minimumYHalfWidth,
        double outerXHalfWidth,
        double outerYHalfWidth)
    {
        if (!Enum.IsDefined(distribution))
        {
            throw new InvalidDataException($"光源“{name}”的方向分布无效。");
        }

        RequireFinite(
            sourceDistance,
            cosineExponent,
            gaussianX,
            gaussianY,
            sourceX,
            sourceY,
            minimumXHalfWidth,
            minimumYHalfWidth);
        if (cosineExponent is < 0 or > 100 || gaussianX < 0 || gaussianY < 0
            || minimumXHalfWidth < 0 || minimumYHalfWidth < 0
            || minimumXHalfWidth >= outerXHalfWidth || minimumYHalfWidth >= outerYHalfWidth
            || (minimumXHalfWidth == 0) != (minimumYHalfWidth == 0))
        {
            throw new InvalidDataException($"光源“{name}”的 Zemax 风格分布参数超出允许范围。");
        }

        if (distribution == NonSequentialSurfaceSourceAngularDistribution.VirtualPoint
            && Math.Abs(sourceDistance) <= 1e-15
            && sourceX * sourceX + sourceY * sourceY > 1 + 1e-12)
        {
            throw new InvalidDataException($"光源“{name}”的平行光方向余弦无效。");
        }

        if (distribution == NonSequentialSurfaceSourceAngularDistribution.Gaussian
            && gaussianX <= 0 && gaussianY <= 0)
        {
            throw new InvalidDataException($"光源“{name}”的 Gaussian X/Y 系数不能同时为零。");
        }
    }

    private static void ValidateVolumeSourceDistribution(
        string name,
        NonSequentialVolumeSourceAngularDistribution distribution)
    {
        if (!Enum.IsDefined(distribution))
        {
            throw new InvalidDataException($"体光源“{name}”的方向分布无效。");
        }
    }

    private static void ValidateRadialDistribution(string name, IReadOnlyList<SourceRadialSample> samples)
    {
        if (samples.Count is < 2 or > 4_096
            || Math.Abs(samples[0].AngleDegrees) > 1e-12
            || samples.Any(sample => !double.IsFinite(sample.AngleDegrees)
                || !double.IsFinite(sample.RelativeIntensity)
                || sample.AngleDegrees < 0 || sample.AngleDegrees > 180
                || sample.RelativeIntensity < 0)
            || samples.Zip(samples.Skip(1), (left, right) => right.AngleDegrees > left.AngleDegrees).Any(valid => !valid)
            || samples.All(sample => sample.RelativeIntensity <= 0))
        {
            throw new InvalidDataException(
                $"径向光源“{name}”需要 2 到 4096 个从 0° 开始、角度严格递增且强度非负的样本。");
        }
    }

    private static void ValidateTraceSettings(NonSequentialTraceSettings settings)
    {
        if (settings.LayoutRaysPerSource <= 0 || settings.LayoutRaysPerSource > 10_000
            || settings.AnalysisRaysPerSource <= 0 || settings.AnalysisRaysPerSource > 1_000_000
            || settings.MaximumTotalSourceRays <= 0 || settings.MaximumTotalSourceRays > 10_000_000
            || settings.MaximumSegmentsPerRay <= 0 || settings.MaximumSegmentsPerRay > 100_000
            || settings.MaximumActiveBranches <= 0 || settings.MaximumActiveBranches > 10_000_000
            || !double.IsFinite(settings.MinimumRelativeIntensity)
            || settings.MinimumRelativeIntensity < 0 || settings.MinimumRelativeIntensity >= 1)
        {
            throw new InvalidDataException("非序列追迹设置超出允许范围。");
        }
    }

    private NonSequentialObjectDefinition Find(Guid id) => _objects.FirstOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException($"Non-sequential object '{id}' was not found.");

    private int IndexOf(Guid id)
    {
        var index = _objects.FindIndex(item => item.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Non-sequential object '{id}' was not found.");
    }

    private List<NonSequentialObjectDefinition> ReferenceChain(NonSequentialObjectDefinition item)
    {
        var chain = new List<NonSequentialObjectDefinition> { item };
        var visited = new HashSet<Guid> { item.Id };
        while (item.ReferenceObjectId is Guid parentId)
        {
            if (!visited.Add(parentId)) throw new InvalidDataException("非序列对象参考关系形成循环。");
            item = Find(parentId);
            chain.Add(item);
        }

        return chain;
    }

    private List<NonSequentialObjectDefinition> ContainmentChain(NonSequentialObjectDefinition item)
    {
        var chain = new List<NonSequentialObjectDefinition> { item };
        var visited = new HashSet<Guid> { item.Id };
        while (item.ContainingObjectId is Guid parentId)
        {
            if (!visited.Add(parentId)) throw new InvalidDataException("非序列对象包含关系形成循环。");
            item = Find(parentId);
            chain.Add(item);
        }

        return chain;
    }

    private Vector3D ToWorldPoint(NonSequentialObjectDefinition item, Vector3D localPoint, HashSet<Guid> visited)
    {
        if (!visited.Add(item.Id)) throw new InvalidDataException("非序列对象参考关系形成循环。");
        var parentPoint = item.LocalCoordinateSystem.ToGlobalPoint(localPoint);
        return item.ReferenceObjectId is Guid parentId ? ToWorldPoint(Find(parentId), parentPoint, visited) : parentPoint;
    }

    private Vector3D ToWorldDirection(NonSequentialObjectDefinition item, Vector3D localDirection, HashSet<Guid> visited)
    {
        if (!visited.Add(item.Id)) throw new InvalidDataException("非序列对象参考关系形成循环。");
        var parentDirection = item.LocalCoordinateSystem.ToGlobalDirection(localDirection);
        return item.ReferenceObjectId is Guid parentId
            ? ToWorldDirection(Find(parentId), parentDirection, visited)
            : parentDirection;
    }

    private static void RequireFinite(params double[] values)
    {
        if (values.Any(value => !double.IsFinite(value))) throw new InvalidDataException("非序列对象参数必须是有限数值。");
    }

    private static void RequirePositive(params double[] values)
    {
        RequireFinite(values);
        if (values.Any(value => value <= 0)) throw new InvalidDataException("非序列对象尺寸必须大于零。");
    }

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException($"对象“{name}”的角度参数超出允许范围。");
    }

    private static void RequireMaterials(string name, params string[] materials)
    {
        if (materials.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"对象“{name}”的材料名称不能为空。");
    }
}
