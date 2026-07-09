using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

public sealed record SequentialTrace(IReadOnlyList<IReadOnlyList<RayTraceSample>> RayHistories);

public sealed class SequentialRayTracer
{
    private readonly Optic _optic;

    public SequentialRayTracer(Optic optic)
    {
        _optic = optic;
        RayGenerator = new RayGenerator(optic);
    }

    public RayGenerator RayGenerator { get; }

    public IRayAimer RayAimer { get; private set; } = new ParaxialRayAimer();

    public void SetAiming(string mode)
    {
        RayAimer = mode.ToLowerInvariant() switch
        {
            "iterative" => new IterativeRayAimer(),
            "robust" => new RobustRayAimer(),
            "cached" => new CachedRayAimer(new ParaxialRayAimer()),
            _ => new ParaxialRayAimer()
        };
    }

    public SequentialTrace Trace()
    {
        var bundle = RayGenerator.Generate();
        return Trace(bundle);
    }

    public SequentialTrace Trace(RealRayBundle bundle)
    {
        var histories = new List<IReadOnlyList<RayTraceSample>>();

        foreach (var sourceRay in bundle.Rays)
        {
            var ray = sourceRay.Normalize();
            var currentIndex = 1.0;
            var history = new List<RayTraceSample>();

            foreach (var surface in _optic.SurfaceGroup.Items)
            {
                var localOrigin = surface.CoordinateSystem.ToLocalPoint(ray.Origin);
                var localDirection = surface.CoordinateSystem.ToLocalDirection(ray.Direction);
                var distance = surface.Geometry.DistanceToIntersection(localOrigin, localDirection);
                if (distance is null)
                {
                    history.Add(new RayTraceSample(surface.Number, surface.Label, ray.Origin, ray.Direction, 0, true));
                    ray = ray with { Intensity = 0 };
                    break;
                }

                var localHit = localOrigin + (localDirection * distance.Value);
                var globalHit = surface.CoordinateSystem.ToGlobalPoint(localHit);
                var vignetted = surface.PhysicalAperture is not null && !surface.PhysicalAperture.Contains(localHit);
                if (vignetted)
                {
                    ray = ray with { Origin = globalHit, Intensity = 0 };
                    history.Add(new RayTraceSample(surface.Number, surface.Label, globalHit, ray.Direction, 0, true));
                    break;
                }

                var normal = surface.CoordinateSystem.ToGlobalDirection(surface.Geometry.SurfaceNormal(localHit));
                var nextMaterial = ResolveMaterial(surface.MaterialAfterName);
                var nextIndex = nextMaterial.RefractiveIndex(ray.WavelengthNanometers);
                var context = new Interactions.SurfaceInteractionContext(
                    normal,
                    currentIndex,
                    nextIndex,
                    ray.WavelengthNanometers,
                    surface.IsReflective);

                ray = ray with { Origin = globalHit };
                ray = surface.InteractionModel.Interact(ray, context);
                ray = surface.CoatingModel.Apply(ray, context);
                ray = surface.ScatteringModel?.Scatter(ray, normal) ?? ray;
                currentIndex = nextIndex;
                history.Add(new RayTraceSample(surface.Number, surface.Label, ray.Origin, ray.Direction, ray.Intensity, false));

                if (!ray.IsAlive)
                {
                    break;
                }
            }

            histories.Add(history);
        }

        return new SequentialTrace(histories);
    }

    private IMaterial ResolveMaterial(string material)
    {
        return _optic.Materials.Resolve(material);
    }
}
