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
            NumericParameterGuard.RequireNotNaN(value, nameof(Radius));
            if (SetProperty(ref _radius, value))
            {
                SynchronizeGeometryFromLegacyParameters();
            }
        }
    }

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(
            ref _thickness,
            NumericParameterGuard.RequireFiniteOrPositiveInfinity(value, nameof(Thickness)));
    }

    public string Material
    {
        get => _material;
        set => SetProperty(ref _material, string.IsNullOrWhiteSpace(value) ? "Air" : value.Trim());
    }

    public string Coating
    {
        get => _coating;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "None" : value.Trim();
            if (!SetProperty(ref _coating, normalized))
            {
                return;
            }

            CoatingModel = normalized.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? new NoneCoatingModel()
                : new ApproximateTransmissionRippleCoating(new[] { new ThinFilmLayer(normalized, 120) });
        }
    }

    public double SemiDiameter
    {
        get => _semiDiameter;
        set => SetProperty(
            ref _semiDiameter,
            NumericParameterGuard.ClampMinimumFinite(value, 0.1, nameof(SemiDiameter)));
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
            NumericParameterGuard.RequireFinite(value, nameof(Conic));
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
            SetProperty(
                ref _material,
                value ? "MIRROR" : MaterialAfter.Name,
                nameof(Material));
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
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _materialAfter, value))
            {
                SetProperty(
                    ref _material,
                    IsReflective ? "MIRROR" : value.Name,
                    nameof(Material));
            }
        }
    }

    public string MaterialAfterName => MaterialAfter.Name;

    public ICoatingModel CoatingModel
    {
        get => _coatingModel;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _coatingModel, value))
            {
                SetProperty(ref _coating, LegacyCoatingName(value), nameof(Coating));
            }
        }
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
                    SetProperty(
                        ref _material,
                        IsReflective ? "MIRROR" : MaterialAfter.Name,
                        nameof(Material));
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
        double cumulativeOpticalPathLength,
        bool ignorePhysicalAperture = false)
    {
        var result = TraceRayState(
            RayState.FromRealRay(inputRay),
            materialBefore,
            materialAfter,
            cumulativePathLength,
            cumulativeOpticalPathLength,
            ignorePhysicalAperture);
        return new SurfaceRayTraceValueResult(
            result.Ray.ToRealRay(),
            result.Sample,
            result.OutgoingRefractiveIndex,
            result.OutgoingMaterial,
            result.InteractionKind,
            result.CumulativePathLength,
            result.CumulativeOpticalPathLength,
            result.StopTracing);
    }

    internal void InitializeFromLegacyProperties(double zPosition)
    {
        SynchronizeGeometryFromLegacyParameters();
        MaterialAfter = new MaterialRegistry().Resolve(Material);
        CoatingModel = Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? new NoneCoatingModel()
            : new ApproximateTransmissionRippleCoating(new[] { new ThinFilmLayer(Coating, 120) });
        InteractionModel = new RefractiveReflectiveInteractionModel(IsReflective);
        PhysicalAperture = null;
        CoordinateSystem = new CoordinateSystem(new Backend.Vector3D(0, 0, zPosition));
    }

    private static string LegacyCoatingName(ICoatingModel coating)
    {
        return coating switch
        {
            NoneCoatingModel => "None",
            ApproximateTransmissionRippleCoating { Layers.Count: 1 } stack => stack.Layers[0].MaterialName,
            ApproximateTransmissionRippleCoating => "Experimental Ripple Approximation",
            SimpleCoatingModel => "Simple",
            _ => coating.Kind
        };
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
