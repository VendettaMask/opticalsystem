using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Materials;

/// <summary>
/// An immutable spatial material with optional profile-specific dispersion. The profile is attached to the entrance
/// surface's frame when a ray transmits into this material, not to a global origin.
/// </summary>
public sealed class GradientIndexMaterial : IMaterial
{
    public GradientIndexMaterial(string name, ISpatialRefractiveIndex profile, double maximumPathLength,
        GradientIndexIntegrationOptions? integrationOptions = null,
        IReadOnlyDictionary<string, GradientIndexVariableRange>? variables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Equals("Air", StringComparison.OrdinalIgnoreCase) || name.Trim().Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GRIN 材料不能使用保留名称 Air 或 MIRROR。", nameof(name));
        ArgumentNullException.ThrowIfNull(profile);
        if (profile is not (Gradient1IndexProfile or Gradient2IndexProfile or Gradient3IndexProfile or Gradient4IndexProfile or Gradient5IndexProfile))
            throw new NotSupportedException("GRIN 材料当前只接受可严格保存、不可变的 Gradient 1～5 分布。");
        if (!double.IsFinite(maximumPathLength) || maximumPathLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPathLength));
        IntegrationOptions = integrationOptions ?? new();
        GradientIndexRayIntegrator.ValidateOptions(IntegrationOptions);
        if (IntegrationOptions.RetainPath)
            throw new NotSupportedException("顺序 GRIN 曲线节点的场景/缓存传输尚未接入；材料配置不能启用路径节点保留。");
        Name = name;
        Profile = profile;
        MaximumPathLength = maximumPathLength;
        var coefficients = GradientIndexProfileData.Values(profile);
        var copy = new Dictionary<string, GradientIndexVariableRange>(StringComparer.Ordinal);
        foreach (var pair in variables ?? new Dictionary<string, GradientIndexVariableRange>())
        {
            if (!coefficients.TryGetValue(pair.Key, out var value) || pair.Value is null)
                throw new ArgumentException("GRIN 优化变量引用未知的分布系数。");
            pair.Value.Validate(pair.Key, value);
            copy.Add(pair.Key, pair.Value);
        }
        Variables = new System.Collections.ObjectModel.ReadOnlyDictionary<string, GradientIndexVariableRange>(copy);
    }

    public string Name { get; }
    public ISpatialRefractiveIndex Profile { get; }
    public double MaximumPathLength { get; }
    public GradientIndexIntegrationOptions IntegrationOptions { get; }
    public IReadOnlyDictionary<string, GradientIndexVariableRange> Variables { get; }
    public IPropagationModel PropagationModel { get; } = new SpatialPropagation();

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) =>
        Profile.RefractiveIndex(localPosition, wavelengthNanometers);

    public double RefractiveIndex(double wavelengthNanometers) =>
        throw new NotSupportedException("GRIN 折射率需要空间位置；不能用基准 n 替代当前光线或分析位置的折射率。");

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;
    public IMaterial Clone() => new GradientIndexMaterial(Name, Profile, MaximumPathLength, IntegrationOptions, Variables);

    internal LocatedGradientIndexMaterial At(CoordinateSystem coordinates) => new(this, coordinates);

    private sealed class SpatialPropagation : IPropagationModel
    {
        public string Kind => "continuous-gradient-index";
        public RealRay Propagate(RealRay ray, double distance) =>
            throw new NotSupportedException("GRIN 顺序传播必须使用体坐标和目标表面进行连续积分，不能调用均匀段传播。");
        public IPropagationModel Clone() => new SpatialPropagation();
    }
}

/// <summary>Per-path medium state. Reflection retains this frame; transmission binds the next one.</summary>
internal sealed class LocatedGradientIndexMaterial(GradientIndexMaterial material, CoordinateSystem coordinates) : IMaterial
{
    internal GradientIndexMaterial Material { get; } = material;
    internal CoordinateSystem Coordinates { get; } = coordinates;
    public string Name => Material.Name;
    public IPropagationModel PropagationModel => Material.PropagationModel;
    public double RefractiveIndex(double wavelengthNanometers) => Material.RefractiveIndex(wavelengthNanometers);
    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;
    public IMaterial Clone() => new LocatedGradientIndexMaterial((GradientIndexMaterial)Material.Clone(), Coordinates);
}
