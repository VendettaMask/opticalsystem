using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Scattering;

namespace OptilandWorkbench.Core.Domain;

public sealed class OpticalSurface : NotifyObject
{
    private int _number;
    private string _label = "Surface";
    private double _radius;
    private double _thickness = 1.0;
    private string _material = "Air";
    private string _coating = "None";
    private double _semiDiameter = 10.0;
    private double _conic;
    private bool _isStop;
    private bool _isReflective;
    private IGeometry _geometry = new PlaneGeometry();
    private IMaterial _materialBefore = new AirMaterial();
    private IMaterial _materialAfter = new AirMaterial();
    private ICoatingModel _coatingModel = new NoneCoatingModel();
    private IInteractionModel _interactionModel = new RefractiveReflectiveInteractionModel();
    private CoordinateSystem _coordinateSystem = CoordinateSystem.Global;

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, value);
    }

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Max(0, value));
    }

    public string Material
    {
        get => _material;
        set => SetProperty(ref _material, string.IsNullOrWhiteSpace(value) ? "Air" : value.Trim());
    }

    public string Coating
    {
        get => _coating;
        set => SetProperty(ref _coating, string.IsNullOrWhiteSpace(value) ? "None" : value.Trim());
    }

    public double SemiDiameter
    {
        get => _semiDiameter;
        set => SetProperty(ref _semiDiameter, Math.Max(0.1, value));
    }

    public double Conic
    {
        get => _conic;
        set => SetProperty(ref _conic, value);
    }

    public bool IsStop
    {
        get => _isStop;
        set => SetProperty(ref _isStop, value);
    }

    public bool IsReflective
    {
        get => _isReflective;
        set
        {
            if (SetProperty(ref _isReflective, value))
            {
                InteractionModel = new RefractiveReflectiveInteractionModel(value);
            }
        }
    }

    public bool IsPlane => Math.Abs(Radius) < 1e-9;

    public IGeometry Geometry
    {
        get => _geometry;
        set => SetProperty(ref _geometry, value);
    }

    public IMaterial MaterialBefore
    {
        get => _materialBefore;
        set => SetProperty(ref _materialBefore, value);
    }

    public IMaterial MaterialAfter
    {
        get => _materialAfter;
        set => SetProperty(ref _materialAfter, value);
    }

    public string MaterialAfterName => MaterialAfter.Name == "Air" && Material != "Air" ? Material : MaterialAfter.Name;

    public ICoatingModel CoatingModel
    {
        get => _coatingModel;
        set => SetProperty(ref _coatingModel, value);
    }

    public IInteractionModel InteractionModel
    {
        get => _interactionModel;
        set => SetProperty(ref _interactionModel, value);
    }

    public IPhysicalAperture? PhysicalAperture { get; set; }

    public IScatteringModel? ScatteringModel { get; set; }

    public CoordinateSystem CoordinateSystem
    {
        get => _coordinateSystem;
        set => SetProperty(ref _coordinateSystem, value);
    }

    public SurfaceRayTraceResult TraceRay(
        RealRay inputRay,
        IMaterial materialBefore,
        IMaterial materialAfter,
        double cumulativePathLength,
        double cumulativeOpticalPathLength)
    {
        var ray = inputRay.Normalize();
        var refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
        var refractiveIndexAfter = materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
        var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
        var distance = Geometry.DistanceToIntersection(localOrigin, localDirection);
        if (distance is null)
        {
            var stoppedRay = ray with { Intensity = 0 };
            var sample = new RayTraceSample(
                Number,
                Label,
                ray.Origin,
                ray.Direction,
                0,
                true,
                CumulativePathLength: cumulativePathLength,
                CumulativeOpticalPathLength: cumulativeOpticalPathLength);
            return new SurfaceRayTraceResult(
                stoppedRay,
                sample,
                refractiveIndexBefore,
                cumulativePathLength,
                cumulativeOpticalPathLength,
                StopTracing: true);
        }

        var localHit = localOrigin + (localDirection * distance.Value);
        var segmentLength = Math.Max(0, distance.Value);
        var segmentOpticalPathLength = Math.Abs(segmentLength * refractiveIndexBefore);
        var nextCumulativePathLength = cumulativePathLength + segmentLength;
        var nextCumulativeOpticalPathLength = cumulativeOpticalPathLength + segmentOpticalPathLength;
        var extinctionCoefficient = materialBefore.ExtinctionCoefficient(ray.WavelengthNanometers);
        var wavelengthMicrometers = ray.WavelengthNanometers / 1000.0;
        var attenuation = extinctionCoefficient <= 0
            ? 1.0
            : Math.Exp((-4.0 * Math.PI * extinctionCoefficient * segmentLength * 1000.0) / wavelengthMicrometers);
        var propagatedRay = materialBefore.PropagationModel.Propagate(ray, segmentLength) with
        {
            OpticalPathDifference = ray.OpticalPathDifference + segmentOpticalPathLength,
            Intensity = ray.Intensity * attenuation
        };

        var vignetted = PhysicalAperture is not null && !PhysicalAperture.Contains(localHit);
        if (vignetted)
        {
            var stoppedRay = propagatedRay with { Intensity = 0 };
            var sample = new RayTraceSample(
                Number,
                Label,
                propagatedRay.Origin,
                ray.Direction,
                0,
                true,
                segmentLength,
                segmentOpticalPathLength,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength);
            return new SurfaceRayTraceResult(
                stoppedRay,
                sample,
                refractiveIndexBefore,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength,
                StopTracing: true);
        }

        var normal = CoordinateSystem.ToGlobalDirection(Geometry.SurfaceNormal(localHit));
        var isReflectiveInteraction = IsReflective
            || (InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: true });
        var context = new SurfaceInteractionContext(
            normal,
            refractiveIndexBefore,
            refractiveIndexAfter,
            ray.WavelengthNanometers,
            isReflectiveInteraction);

        var tracedRay = InteractionModel.Interact(propagatedRay, context);
        tracedRay = CoatingModel.Apply(tracedRay, context);
        tracedRay = ScatteringModel?.Scatter(tracedRay, normal) ?? tracedRay;

        var tracedSample = new RayTraceSample(
            Number,
            Label,
            tracedRay.Origin,
            tracedRay.Direction,
            tracedRay.Intensity,
            false,
            segmentLength,
            segmentOpticalPathLength,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength);
        return new SurfaceRayTraceResult(
            tracedRay,
            tracedSample,
            refractiveIndexAfter,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength,
            StopTracing: !tracedRay.IsAlive);
    }

    public void SyncCompositionFromLegacyProperties(double zPosition)
    {
        Geometry = IsPlane ? new PlaneGeometry() : new StandardGeometry(Radius, Conic);
        MaterialAfter = new MaterialRegistry().Resolve(Material);
        CoatingModel = Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? new NoneCoatingModel()
            : new ThinFilmStackCoating(new[] { new ThinFilmLayer(Coating, 120) });
        InteractionModel = new RefractiveReflectiveInteractionModel(IsReflective);
        // Optiland's semi_aperture controls the drawn/mechanical envelope only.
        // Rays are clipped only when an explicit physical aperture is configured.
        PhysicalAperture = null;
        CoordinateSystem = new CoordinateSystem(new Backend.Vector3D(0, 0, zPosition));
    }

    public OpticalSurface Clone()
    {
        return new OpticalSurface
        {
            Number = Number,
            Label = Label,
            Radius = Radius,
            Thickness = Thickness,
            Material = Material,
            Coating = Coating,
            SemiDiameter = SemiDiameter,
            Conic = Conic,
            IsStop = IsStop,
            IsReflective = IsReflective,
            Geometry = Geometry.Clone(),
            MaterialBefore = MaterialBefore.Clone(),
            MaterialAfter = MaterialAfter.Clone(),
            CoatingModel = CoatingModel.Clone(),
            InteractionModel = InteractionModel.Clone(),
            PhysicalAperture = PhysicalAperture?.Clone(),
            ScatteringModel = ScatteringModel?.Clone(),
            CoordinateSystem = CoordinateSystem
        };
    }

    public override string ToString()
    {
        return $"{Number}: {Label}";
    }
}

public sealed record SurfaceRayTraceResult(
    RealRay Ray,
    RayTraceSample Sample,
    double RefractiveIndexAfter,
    double CumulativePathLength,
    double CumulativeOpticalPathLength,
    bool StopTracing);
