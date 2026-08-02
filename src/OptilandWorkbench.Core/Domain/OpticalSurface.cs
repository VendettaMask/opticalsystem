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

public sealed partial class OpticalSurface : NotifyObject
{
    private int _number;
    private string _label = "Surface";
    private double _radius;
    private double _thickness = 1.0;
    private string _material = "Air";
    private string _coating = "None";
    private double _semiDiameter = 10.0;
    private bool _semiDiameterFixed;
    private double _conic;
    private bool _isStop;
    private bool _radiusVariable;
    private bool _thicknessVariable;
    private bool _synchronizingGeometry;
    private IGeometry _geometry = new PlaneGeometry();
    private IMaterial _materialBefore = new AirMaterial();
    private IMaterial _materialAfter = new AirMaterial();
    private ICoatingModel _coatingModel = new NoneCoatingModel();
    private IInteractionModel _interactionModel = new RefractiveReflectiveInteractionModel();
    private IPhysicalAperture? _physicalAperture;
    private IScatteringModel? _scatteringModel;
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
        set
        {
            if (SetProperty(ref _radius, value))
            {
                SynchronizeGeometryFromLegacyParameters();
            }
        }
    }

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, value);
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

    public bool SemiDiameterFixed
    {
        get => _semiDiameterFixed;
        set => SetProperty(ref _semiDiameterFixed, value);
    }

    public double Conic
    {
        get => _conic;
        set
        {
            if (SetProperty(ref _conic, value))
            {
                SynchronizeGeometryFromLegacyParameters();
            }
        }
    }

    public bool IsStop
    {
        get => _isStop;
        set => SetProperty(ref _isStop, value);
    }

    public bool IsReflective
    {
        get => InteractionIsReflective(_interactionModel);
        set
        {
            if (value == IsReflective)
            {
                return;
            }

            var next = WithReflectivity(_interactionModel, value);
            SetProperty(ref _interactionModel, next, nameof(InteractionModel));
            RaisePropertyChanged();
        }
    }

    public bool RadiusVariable
    {
        get => _radiusVariable;
        set => SetProperty(ref _radiusVariable, value);
    }

    public bool ThicknessVariable
    {
        get => _thicknessVariable;
        set => SetProperty(ref _thicknessVariable, value);
    }

    public bool IsPlane => Math.Abs(Radius) < 1e-9;

    public IGeometry Geometry
    {
        get => _geometry;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _geometry, value))
            {
                SynchronizeLegacyParametersFromGeometry(value);
            }
        }
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
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var wasReflective = IsReflective;
            if (SetProperty(ref _interactionModel, value))
            {
                if (wasReflective != IsReflective)
                {
                    RaisePropertyChanged(nameof(IsReflective));
                }
            }
        }
    }

    public IPhysicalAperture? PhysicalAperture
    {
        get => _physicalAperture;
        set => SetProperty(ref _physicalAperture, value);
    }

    public IScatteringModel? ScatteringModel
    {
        get => _scatteringModel;
        set => SetProperty(ref _scatteringModel, value);
    }

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
        var result = TraceRayValue(
            inputRay,
            materialBefore,
            materialAfter,
            cumulativePathLength,
            cumulativeOpticalPathLength);
        return new SurfaceRayTraceResult(
            result.Ray,
            result.Sample.ToRayTraceSample(),
            result.OutgoingRefractiveIndex,
            result.OutgoingMaterial,
            result.InteractionKind,
            result.CumulativePathLength,
            result.CumulativeOpticalPathLength,
            result.StopTracing);
    }

    internal SurfaceRayTraceValueResult TraceRayValue(
        RealRay inputRay,
        IMaterial materialBefore,
        IMaterial materialAfter,
        double cumulativePathLength,
        double cumulativeOpticalPathLength)
    {
        var ray = inputRay.IsNormalized ? inputRay : inputRay.Normalize();
        var refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
        var refractiveIndexAfter = materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
        var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
        var distance = Geometry.DistanceToIntersection(localOrigin, localDirection);
        if (distance is null)
        {
            var stoppedRay = ray with { Intensity = 0 };
            var sample = new RayTraceSampleValue(
                Number,
                Label,
                ray.Origin,
                ray.Direction,
                0,
                true,
                CumulativePathLength: cumulativePathLength,
                CumulativeOpticalPathLength: cumulativeOpticalPathLength);
            return new SurfaceRayTraceValueResult(
                stoppedRay,
                sample,
                refractiveIndexBefore,
                materialBefore,
                InteractionKind: null,
                cumulativePathLength,
                cumulativeOpticalPathLength,
                StopTracing: true);
        }

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
        var localPropagatedHit = CoordinateSystem.ToLocalPoint(propagatedRay.Origin);

        var vignetted = PhysicalAperture is not null && !PhysicalAperture.Contains(localPropagatedHit);
        if (vignetted)
        {
            var stoppedRay = propagatedRay with { Intensity = 0 };
            var sample = new RayTraceSampleValue(
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
            return new SurfaceRayTraceValueResult(
                stoppedRay,
                sample,
                refractiveIndexBefore,
                materialBefore,
                InteractionKind: null,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength,
                StopTracing: true);
        }

        var localNormal = Geometry.SurfaceNormal(localPropagatedHit);
        var normal = CoordinateSystem.ToGlobalDirection(localNormal);
        var isReflectiveInteraction = IsReflective;
        var context = new SurfaceInteractionContext(
            localNormal,
            refractiveIndexBefore,
            refractiveIndexAfter,
            ray.WavelengthNanometers,
            isReflectiveInteraction,
            Geometry);

        var localRay = propagatedRay with
        {
            Origin = localPropagatedHit,
            Direction = CoordinateSystem.ToLocalDirection(propagatedRay.Direction)
        };
        var interactionResult = InteractionModel.Interact(localRay, context);
        var outgoingMaterial = interactionResult.Kind == RayInteractionKind.Transmitted
            ? materialAfter
            : materialBefore;
        var coatingContext = context with { IsReflective = interactionResult.IsReflective };
        var interactedLocalRay = CoatingModel.Apply(interactionResult.Ray, coatingContext);
        var tracedRay = interactedLocalRay with
        {
            Origin = CoordinateSystem.ToGlobalPoint(interactedLocalRay.Origin),
            Direction = CoordinateSystem.ToGlobalDirection(interactedLocalRay.Direction)
        };
        tracedRay = ScatteringModel?.Scatter(tracedRay, normal) ?? tracedRay;

        var tracedSample = new RayTraceSampleValue(
            Number,
            Label,
            tracedRay.Origin,
            tracedRay.Direction,
            tracedRay.Intensity,
            false,
            segmentLength,
            segmentOpticalPathLength,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength,
            InteractionKind: interactionResult.Kind);
        return new SurfaceRayTraceValueResult(
            tracedRay,
            tracedSample,
            outgoingMaterial.RefractiveIndex(ray.WavelengthNanometers),
            outgoingMaterial,
            interactionResult.Kind,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength,
            StopTracing: !tracedRay.CanTrace);
    }

    public void SyncCompositionFromLegacyProperties(double zPosition)
    {
        SynchronizeGeometryFromLegacyParameters();
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

    private void SynchronizeGeometryFromLegacyParameters()
    {
        if (_synchronizingGeometry)
        {
            return;
        }

        _synchronizingGeometry = true;
        try
        {
            var next = Geometry switch
            {
                PlaneGratingGeometry grating when Math.Abs(Radius) < 1e-9 => grating,
                StandardGratingGeometry grating when Math.Abs(Radius) < 1e-9 =>
                    new PlaneGratingGeometry(
                        grating.GratingOrder,
                        grating.GratingPeriodMicrometers,
                        grating.GrooveOrientationAngleRadians),
                IGratingGeometry grating => new StandardGratingGeometry(
                    Radius,
                    Conic,
                    grating.GratingOrder,
                    grating.GratingPeriodMicrometers,
                    grating.GrooveOrientationAngleRadians),
                EvenAsphereGeometry even => new EvenAsphereGeometry(Radius, Conic, even.Coefficients),
                OddAsphereGeometry odd => new OddAsphereGeometry(Radius, Conic, odd.Coefficients),
                ForbesQGeometry forbes => new ForbesQGeometry(
                    Radius,
                    Conic,
                    forbes.NormalizationRadius,
                    forbes.QCoefficients),
                BiconicGeometry biconic => new BiconicGeometry(
                    Radius,
                    biconic.RadiusY,
                    Conic,
                    biconic.ConicY),
                SeparableBiconicGeometry biconic => new SeparableBiconicGeometry(
                    Radius,
                    biconic.RadiusY,
                    Conic,
                    biconic.ConicY),
                ToroidalGeometry toroidal => new ToroidalGeometry(toroidal.TangentialRadius, Radius),
                StandardGeometry when Math.Abs(Radius) < 1e-9 => new PlaneGeometry(),
                StandardGeometry => new StandardGeometry(Radius, Conic),
                PlaneGeometry when Math.Abs(Radius) >= 1e-9 => new StandardGeometry(Radius, Conic),
                _ => Geometry
            };
            SetProperty(ref _geometry, next, nameof(Geometry));
        }
        finally
        {
            _synchronizingGeometry = false;
        }
    }

    private void SynchronizeLegacyParametersFromGeometry(IGeometry geometry)
    {
        if (_synchronizingGeometry)
        {
            return;
        }

        _synchronizingGeometry = true;
        try
        {
            var parameters = geometry switch
            {
                StandardGeometry standard => (standard.Radius, standard.Conic),
                StandardGratingGeometry grating => (grating.Base.Radius, grating.Base.Conic),
                PlaneGeometry or PlaneGratingGeometry => (0.0, 0.0),
                EvenAsphereGeometry even => (even.Base.Radius, even.Base.Conic),
                OddAsphereGeometry odd => (odd.Base.Radius, odd.Base.Conic),
                ForbesQGeometry forbes => (forbes.Base.Radius, forbes.Base.Conic),
                BiconicGeometry biconic => (biconic.RadiusX, biconic.ConicX),
                SeparableBiconicGeometry biconic => (biconic.RadiusX, biconic.ConicX),
                ToroidalGeometry toroidal => (toroidal.SagittalRadius, 0.0),
                _ => (_radius, _conic)
            };
            SetProperty(ref _radius, parameters.Item1, nameof(Radius));
            SetProperty(ref _conic, parameters.Item2, nameof(Conic));
        }
        finally
        {
            _synchronizingGeometry = false;
        }
    }

    private static bool InteractionIsReflective(IInteractionModel interaction)
    {
        return interaction switch
        {
            RefractiveReflectiveInteractionModel model => model.IsReflective,
            ThinLensInteractionModel model => model.IsReflective,
            DiffractiveInteractionModel model => model.IsReflective,
            PhaseInteractionModel model => model.IsReflective,
            _ => false
        };
    }

    private static IInteractionModel WithReflectivity(IInteractionModel interaction, bool isReflective)
    {
        return interaction switch
        {
            RefractiveReflectiveInteractionModel => new RefractiveReflectiveInteractionModel(isReflective),
            ThinLensInteractionModel model => new ThinLensInteractionModel(model.FocalLength, isReflective),
            DiffractiveInteractionModel { GrooveFrequencyLinesPerMillimeter: double frequency } model =>
                new DiffractiveInteractionModel(frequency, model.Order ?? 1, isReflective),
            DiffractiveInteractionModel => new DiffractiveInteractionModel(isReflective),
            PhaseInteractionModel model => new PhaseInteractionModel(model.Profile.Clone(), isReflective),
            _ when isReflective => throw new InvalidOperationException(
                $"Interaction model '{interaction.Kind}' does not support reflection."),
            _ => interaction.Clone()
        };
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
            SemiDiameterFixed = SemiDiameterFixed,
            Conic = Conic,
            IsStop = IsStop,
            IsReflective = IsReflective,
            RadiusVariable = RadiusVariable,
            ThicknessVariable = ThicknessVariable,
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
    double OutgoingRefractiveIndex,
    IMaterial OutgoingMaterial,
    RayInteractionKind? InteractionKind,
    double CumulativePathLength,
    double CumulativeOpticalPathLength,
    bool StopTracing)
{
    public double RefractiveIndexAfter => OutgoingRefractiveIndex;
}

internal readonly record struct SurfaceRayTraceValueResult(
    RealRay Ray,
    RayTraceSampleValue Sample,
    double OutgoingRefractiveIndex,
    IMaterial OutgoingMaterial,
    RayInteractionKind? InteractionKind,
    double CumulativePathLength,
    double CumulativeOpticalPathLength,
    bool StopTracing);
