using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Differentiation;

internal sealed class OpticalDifferentialContext
{
    private readonly Optic _optic;
    private readonly IReadOnlyList<OpticalDesignParameter> _parameters;
    private (DifferentialScalar Power, DifferentialScalar StopHeight)? _paraxial;
    public int Dimension { get; }
    public int ParameterCount => _parameters.Count;

    public OpticalDifferentialContext(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters, int extraDimensions = 0)
    {
        OpticalDesignParameters.Validate(optic, parameters);
        _optic = optic;
        _parameters = parameters;
        Dimension = parameters.Count + extraDimensions;
    }

    public DifferentialScalar Parameter(int surface, OpticalDesignParameterKind kind, double value, int coefficient = 0)
    {
        for (var index = 0; index < _parameters.Count; index++)
            if (_parameters[index] is var parameter && (parameter.SurfaceIndex == surface || parameter.LinkedSurfaceIndex == surface) && parameter.Kind == kind
                && (kind != OpticalDesignParameterKind.EvenAsphereCoefficient || parameter.CoefficientIndex == coefficient))
                return DifferentialScalar.Variable(value, Dimension, index, parameter.Scale);
        return value;
    }

    public DifferentialScalar Position(int surface)
    {
        DifferentialScalar position = _optic.SurfaceGroup.Items[surface].CoordinateSystem.Origin.Z;
        for (var index = 0; index < _parameters.Count; index++)
            if (_parameters[index] is { Kind: OpticalDesignParameterKind.Thickness } parameter)
            {
                if (parameter.SurfaceIndex < surface) position += DifferentialScalar.Variable(0, Dimension, index, parameter.Scale);
                if (parameter.LinkedSurfaceIndex is { } linked && linked < surface) position += DifferentialScalar.Variable(0, Dimension, index, parameter.Scale);
            }
        return position;
    }

    public DifferentialScalar Index(int surface, double wavelength)
    {
        if (surface < 0) return 1;
        var material = _optic.SurfaceGroup.Items[surface].MaterialAfter;
        if (!_parameters.Any(parameter => (parameter.SurfaceIndex == surface || parameter.LinkedSurfaceIndex == surface)
                && parameter.Kind is OpticalDesignParameterKind.MaterialIndex or OpticalDesignParameterKind.MaterialAbbe))
            return material.RefractiveIndex(wavelength);
        var (ndValue, vdValue) = OpticalDesignParameters.RelaxedGlassCoordinates(material);
        var nd = Parameter(surface, OpticalDesignParameterKind.MaterialIndex, ndValue);
        var vd = Parameter(surface, OpticalDesignParameterKind.MaterialAbbe, vdValue);
        var b = (nd - 1) / vd / (1 / (.486 * .486) - 1 / (.656 * .656));
        var micrometers = wavelength / 1000;
        return nd + b * (1 / (micrometers * micrometers) - 1 / (.587 * .587));
    }

    public (DifferentialScalar Sag, DifferentialScalar RadialSlope) Profile(int surface, DifferentialScalar x, DifferentialScalar y)
    {
        var profile = OpticalDesignParameters.Profile(_optic.SurfaceGroup.Items[surface].Geometry);
        var c = Parameter(surface, OpticalDesignParameterKind.Curvature, profile.Curvature);
        var k = Parameter(surface, OpticalDesignParameterKind.Conic, profile.Conic);
        var u = x * x + y * y;
        var argument = 1 - (1 + k) * c * c * u;
        if (argument.Value <= 1e-14) throw new ArithmeticException("The conic profile derivative is singular or outside its domain.");
        var root = DifferentialScalar.Sqrt(argument);
        var sag = c * u / (1 + root);
        var slope = c / (2 * root);
        DifferentialScalar power = 1;
        for (var index = 0; index < profile.Coefficients.Length; index++)
        {
            var coefficient = Parameter(surface, OpticalDesignParameterKind.EvenAsphereCoefficient, profile.Coefficients[index], index);
            slope += (index + 1) * coefficient * power;
            power *= u;
            sag += coefficient * power;
        }
        return (sag, slope);
    }

    public (DifferentialScalar X, DifferentialScalar Y, DifferentialScalar Z) Normal(int surface, DifferentialScalar x, DifferentialScalar y)
    {
        DifferentialScalar nx, ny;
        if (_optic.SurfaceGroup.Items[surface].Geometry is EvenAsphereGeometry)
        {
            // Differentiate the SAME finite spatial-difference normal used by formal Core.
            // These are not parameter-difference probes and do not create perturbed optics.
            const double step = 1e-5;
            nx = -(Profile(surface, x + step, y).Sag - Profile(surface, x - step, y).Sag) / (2 * step);
            ny = -(Profile(surface, x, y + step).Sag - Profile(surface, x, y - step).Sag) / (2 * step);
        }
        else
        {
            var slope = Profile(surface, x, y).RadialSlope;
            nx = -2 * x * slope;
            ny = -2 * y * slope;
        }
        var length = DifferentialScalar.Sqrt(nx * nx + ny * ny + 1);
        return (nx / length, ny / length, 1 / length);
    }

    public (DifferentialScalar Power, DifferentialScalar StopHeight) Paraxial()
    {
        if (_paraxial is { } cached) return cached;
        if (_parameters.Any(parameter => parameter.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient && parameter.CoefficientIndex == 0))
            throw new NotSupportedException("The design spot/power derivative contract fixes the quadratic asphere term A2 at zero; geometry-only tangents remain supported.");
        var wavelength = _optic.Wavelengths.First(wave => wave.IsPrimary).Nanometers;
        DifferentialScalar a = 1, b = 0, c = 0, d = 1;
        DifferentialScalar stopHeight = double.NaN;
        var indexBefore = Index(-1, wavelength);
        for (var index = 0; index < _optic.SurfaceGroup.Items.Count; index++)
        {
            var surface = _optic.SurfaceGroup.Items[index];
            if (surface.IsStop) stopHeight = a * _optic.Aperture.Value / 2;
            var indexAfter = Index(index, wavelength);
            var curvature = Parameter(index, OpticalDesignParameterKind.Curvature, OpticalDesignParameters.Profile(surface.Geometry).Curvature);
            var power = -(indexAfter - indexBefore) * curvature / indexAfter;
            var ratio = indexBefore / indexAfter;
            c = power * a + ratio * c;
            d = power * b + ratio * d;
            if (double.IsFinite(surface.Thickness))
            {
                var thickness = Parameter(index, OpticalDesignParameterKind.Thickness, surface.Thickness);
                a += thickness * c;
                b += thickness * d;
            }
            indexBefore = indexAfter;
        }
        var formalPower = _optic.Paraxial.EstimateOpticalPower();
        Verify(-c, formalPower, "paraxial power");
        var stopIndex = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var formalStop = _optic.Paraxial.TraceNormalizedPupil(0, [1.0], wavelength / 1000).Heights[stopIndex][0];
        Verify(stopHeight, formalStop, "stop calibration");
        _paraxial = ((-c).WithValue(formalPower), stopHeight.WithValue(formalStop));
        return _paraxial.Value;
    }

    internal static void Verify(DifferentialScalar result, double formal, string quantity)
    {
        if (!result.IsFinite || !double.IsFinite(formal) || Math.Abs(result.Value - formal) > 1e-7 * Math.Max(1, Math.Abs(formal)))
            throw new ArithmeticException($"Formal Core and derivative {quantity} values disagree or are unavailable: formal={formal:R}, derivative={result.Value:R}.");
    }
}
