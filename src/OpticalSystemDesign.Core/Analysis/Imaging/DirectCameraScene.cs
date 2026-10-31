using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record DirectSceneMaterial(Guid ObjectId, IReadOnlyList<double> Reflectance,
    IReadOnlyList<double>? EmittedRadiance = null, IReadOnlyList<double>? DarkReflectance = null,
    double CheckerSizeMillimeters = 0, bool TwoSided = false);
public sealed record DirectPointLight(Vector3D Position, IReadOnlyList<double> BandPowerWatts);
public readonly record struct DirectSceneSample(double RadianceWattsPerSquareMeterSteradian, double Reflectance, int ShadowQueries, int BlockedLights);

/// <summary>Opaque NSC scene geometry with direct Lambertian illumination and optional emissive surfaces.
/// Positions are mm, source band powers W, emitted band radiances W/m2/sr. No RGB-to-spectrum conversion.</summary>
public sealed class DirectCameraScene
{
    private readonly NonSequentialDocumentTracer.IntersectionScene _query;
    private readonly IReadOnlyDictionary<Guid, DirectSceneMaterial> _materials;
    private readonly IReadOnlyList<DirectPointLight> _lights;
    public IReadOnlyList<double> WavelengthsNanometers { get; }
    public int ObjectCount { get; }
    public IReadOnlyList<Guid> ObjectIds { get; }

    public DirectCameraScene(NonSequentialDocument geometry, IEnumerable<DirectSceneMaterial> materials,
        IEnumerable<DirectPointLight> lights)
    {
        ArgumentNullException.ThrowIfNull(geometry); ArgumentNullException.ThrowIfNull(materials); ArgumentNullException.ThrowIfNull(lights);
        geometry = geometry.Clone();
        if (geometry.Objects.Count is < 1 or > 64 || geometry.Wavelengths.Count is < 1 or > 16)
            throw new ArgumentException("Direct camera scenes support 1–64 opaque objects and 1–16 spectral bands.");
        if (geometry.Objects.Any(o => o.Kind is not (NonSequentialObjectKind.PlaneRectangle or NonSequentialObjectKind.Box or NonSequentialObjectKind.Sphere)
            || o.ReferenceObjectId is not null || o.ContainingObjectId is not null))
            throw new NotSupportedException("直射场景首版支持独立矩形、球体和长方体，均按不透明表面处理。");
        if (geometry.Objects.Any(o => o.SurfaceBsdf is not null))
            throw new NotSupportedException("NSC 表面 BSDF 应通过非序列追迹和探测器功率输入，不可被直射场景的独立材质覆盖。");
        WavelengthsNanometers = Array.AsReadOnly(geometry.Wavelengths.Select(w => w.Nanometers).ToArray());
        if (WavelengthsNanometers.Any(w => !double.IsFinite(w) || w is < 200 or > 20000))
            throw new ArgumentException("Scene wavelength is outside the camera range.");
        var records = materials.Take(65).ToArray(); var emitters = lights.Take(9).ToArray();
        if (records.Any(m => m is null) || emitters.Any(l => l is null)
            || records.Length != geometry.Objects.Count || records.Select(m => m.ObjectId).Distinct().Count() != records.Length
            || geometry.Objects.Any(o => records.All(m => m.ObjectId != o.Id)) || emitters.Length > 8)
            throw new ArgumentException("Every scene object needs one material; at most eight point lights are supported.");
        var bandCount = WavelengthsNanometers.Count;
        _materials = records.Select(m => m with
        {
            Reflectance = Freeze(m.Reflectance, bandCount, 1),
            DarkReflectance = m.DarkReflectance is null ? null : Freeze(m.DarkReflectance, bandCount, 1),
            EmittedRadiance = m.EmittedRadiance is null ? null : Freeze(m.EmittedRadiance, bandCount, 1e6)
        }).ToDictionary(m => m.ObjectId);
        if (records.Any(m => !double.IsFinite(m.CheckerSizeMillimeters) || m.CheckerSizeMillimeters is < 0 or > 1e6))
            throw new ArgumentException("Invalid checker dimensions.");
        _lights = Array.AsReadOnly(emitters.Select(l =>
        {
            if (!Finite(l.Position)) throw new ArgumentException("Point light position must be finite.");
            return l with { BandPowerWatts = Freeze(l.BandPowerWatts, bandCount, 1e6) };
        }).ToArray());
        _query = NonSequentialDocumentTracer.CompileIntersections(geometry); ObjectCount = geometry.Objects.Count;
        ObjectIds = Array.AsReadOnly(geometry.Objects.Where(o => o.Enabled).Select(o => o.Id).ToArray());
    }

    public NonSequentialSceneIntersection? Intersect(RealRay ray, double maximumDistance = double.PositiveInfinity)
        => _query.Intersect(ray, maximumDistance);
    public NonSequentialSceneIntersection? IntersectObject(Guid objectId, RealRay ray) => _query.IntersectObject(objectId, ray);
    internal bool IsTwoSided(Guid objectId) => _materials[objectId].TwoSided;

    public DirectSceneSample Evaluate(NonSequentialSceneIntersection hit, Vector3D towardCamera, int band)
    {
        if ((uint)band >= WavelengthsNanometers.Count) throw new ArgumentOutOfRangeException(nameof(band));
        var material = _materials[hit.ObjectId]; var normal = hit.Normal;
        if (Dot(normal, towardCamera) <= 0)
        {
            if (!material.TwoSided) return default;
            normal = -normal;
        }
        var rho = material.Reflectance[band];
        if (material.CheckerSizeMillimeters > 0 && material.DarkReflectance is not null
            && ((long)Math.Floor(hit.LocalPosition.X / material.CheckerSizeMillimeters)
                + (long)Math.Floor(hit.LocalPosition.Y / material.CheckerSizeMillimeters)) % 2 != 0)
            rho = material.DarkReflectance[band];
        var radiance = material.EmittedRadiance?[band] ?? 0;
        var queries = 0; var blocked = 0;
        foreach (var light in _lights)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var delta = light.Position - hit.Position; var distance = delta.Length;
            if (distance <= 2e-6) throw new ArgumentException("A point light cannot coincide with the evaluated surface.");
            var direction = delta / distance; var cosine = Dot(normal, direction);
            if (cosine <= 0 || light.BandPowerWatts[band] == 0) continue;
            queries++;
            var obstruction = _query.Intersect(new(hit.Position + normal * 1e-6, direction, WavelengthsNanometers[band]), distance - 2e-6);
            if (obstruction is not null) { blocked++; continue; }
            // Isotropic point source: P/(4*pi*r^2); Lambertian BRDF rho/pi. mm^2 converts to m^2 once.
            var irradiance = light.BandPowerWatts[band] * cosine / (4 * Math.PI * distance * distance * 1e-6);
            radiance += irradiance * rho / Math.PI;
        }
        return new(radiance, rho, queries, blocked);
    }

    private static IReadOnlyList<double> Freeze(IReadOnlyList<double> values, int count, double maximum)
    {
        if (values is null || values.Count != count) throw new ArgumentException("Spectral array dimensions do not match.");
        var copy = values.ToArray();
        if (copy.Any(v => !double.IsFinite(v) || v < 0 || v > maximum)) throw new ArgumentException("Spectral values are outside the passive model range.");
        return Array.AsReadOnly(copy);
    }
    internal static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static bool Finite(Vector3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
