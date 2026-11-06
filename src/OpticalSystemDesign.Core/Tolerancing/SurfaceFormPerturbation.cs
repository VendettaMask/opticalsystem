using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Tolerancing;

public enum SurfaceFormMode
{
    FringeZernike, StandardZernike, IsoPower, IsoIrregularityPv, IsoRotationalIrregularityPv, IsoIrregularityRms
}
public enum IsoSurfaceFormUnit { Nanometers, Fringes, Micrometers }
public enum IsoSurfaceFormStatistics { AtMaximum, TruncatedGaussian }

/// <summary>Adds a manufactured surface sag error in the existing surface coordinates.</summary>
public sealed class SurfaceFormPerturbation : IScaledRangePerturbation, ISharedSurfaceForm
{
    private static readonly IReadOnlyList<(int Number, int N, int M)> StandardOrders =
        ZernikeFitEngine.StandardIndices(ZernikeFitEngine.MaximumStandardTerm);
    private readonly int _surfaceNumber;
    private readonly SurfaceFormMode _mode;
    private readonly (int N, int M)[] _orders;
    private readonly double _sagScale;
    private readonly bool _normalDistribution;
    private readonly IsoSurfaceFormStatistics _isoStatistics;
    private IGeometry? _original;

    public SurfaceFormPerturbation(string name, int surfaceNumber, SurfaceFormMode mode,
        double maximum, bool normalDistribution, int minimumTerm, int maximumTerm,
        IsoSurfaceFormUnit isoUnit, double testWavelengthMicrometers,
        IsoSurfaceFormStatistics isoStatistics = IsoSurfaceFormStatistics.AtMaximum)
    {
        if (surfaceNumber <= 0 || !Enum.IsDefined(mode) || !double.IsFinite(maximum) || maximum < 0
            || !HasValidTerms(mode, minimumTerm, maximumTerm) || !Enum.IsDefined(isoUnit) || !Enum.IsDefined(isoStatistics)
            || UsesTestWavelength(mode, isoUnit) && !SurfaceFringePerturbation.IsValidTestWavelength(testWavelengthMicrometers))
            throw new ArgumentException("Invalid surface form tolerance parameters.");

        Name = OptimizationGuards.RequireName(name, nameof(name));
        Maximum = maximum;
        _surfaceNumber = surfaceNumber;
        _mode = mode;
        _normalDistribution = normalDistribution;
        _isoStatistics = isoStatistics;
        if (mode == SurfaceFormMode.IsoPower || IsIsoIrregularity(mode))
        {
            _orders = [];
            _sagScale = IsoUnitScale(isoUnit, testWavelengthMicrometers);
        }
        else
        {
            _orders = new (int, int)[maximumTerm - minimumTerm + 1];
            for (var i = 0; i < _orders.Length; i++)
            {
                var index = minimumTerm - 1 + i;
                _orders[i] = mode == SurfaceFormMode.FringeZernike
                    ? FringeZernikeBasis.Orders[index]
                    : (StandardOrders[index].N, StandardOrders[index].M);
            }
            _sagScale = mode == SurfaceFormMode.FringeZernike ? 0.0005 * testWavelengthMicrometers : 1;
        }
    }

    public string Name { get; }
    public double Minimum => -Maximum;
    public double Maximum { get; }

    internal int SurfaceNumber => _surfaceNumber;
    int ISharedSurfaceForm.SurfaceNumber => _surfaceNumber;
    internal SurfaceFormMode Mode => _mode;

    public static double IsoUnitScale(IsoSurfaceFormUnit unit, double wavelengthMicrometers) => unit switch
    {
        IsoSurfaceFormUnit.Nanometers => 1e-6,
        IsoSurfaceFormUnit.Micrometers => 1e-3,
        _ => 0.0005 * wavelengthMicrometers
    };

    internal ISampledPerturbation WithRotationalIrregularity(SurfaceFormPerturbation rotational) =>
        new JointIsoPerturbation(this, rotational);

    private sealed class JointIsoPerturbation : ISampledPerturbation, ISharedSurfaceForm
    {
        private readonly SurfaceFormPerturbation _total;
        private readonly SurfaceFormPerturbation _rotational;
        private readonly double _totalMaximum;
        private readonly double _rotationalMaximum;

        public JointIsoPerturbation(SurfaceFormPerturbation total, SurfaceFormPerturbation rotational)
        {
            _total = total;
            _rotational = rotational;
            _totalMaximum = total.Maximum * total._sagScale;
            _rotationalMaximum = rotational.Maximum * rotational._sagScale;
            if (!(_rotationalMaximum < _totalMaximum || (_totalMaximum == 0 && _rotationalMaximum == 0)))
                throw new ArgumentException("Joint ISOC maximum must be smaller than ISOB in physical sag units.");
        }

        public string Name => $"{_total.Name} + {_rotational.Name}";
        public int SurfaceNumber => _total.SurfaceNumber;

        public void Apply(Optic optic) => Apply(optic, new Random(1234));

        public void Apply(Optic optic, Random random)
        {
            if (_totalMaximum == 0) return;

            var totalRandom = _total._isoStatistics == IsoSurfaceFormStatistics.TruncatedGaussian;
            var rotationalRandom = _rotational._isoStatistics == IsoSurfaceFormStatistics.TruncatedGaussian;
            // Shared quantile preserves both marginals when both use the same distribution.
            var fraction = totalRandom || rotationalRandom ? SampleIsoFraction(random,
                totalRandom && !rotationalRandom ? _rotationalMaximum / _totalMaximum : 0) : 1;
            var total = _totalMaximum * (totalRandom ? fraction : 1);
            var rotational = _rotationalMaximum * (rotationalRandom ? fraction : 1);
            if (total == 0) return;
            var figure = IsoSurfaceFigure.CreateJoint(total, rotational, random);
            _total.ApplySag(optic, 0, figure.Coefficients, figure.Orders, figure.Piston);
        }

        public void Revert(Optic optic) => _total.Revert(optic);
    }

    private static double SampleIsoFraction(Random random, double lower)
    {
        if (lower == 0)
            return VariableRangePerturbation.SampleDelta(random, 0, 1, normalDistribution: true, sigmaSpan: 4);

        // A uniform proposal on the conditional interval also works when C is very close to B.
        var mode = Math.Max(0, lower - .5);
        while (true)
        {
            var candidate = lower + (1 - lower) * random.NextDouble();
            var delta = candidate - .5;
            if (random.NextDouble() <= Math.Exp(-32 * (delta * delta - mode * mode))) return candidate;
        }
    }

    public static bool HasValidTerms(SurfaceFormMode mode, int minimumTerm, int maximumTerm) =>
        mode == SurfaceFormMode.IsoPower || IsIsoIrregularity(mode)
        || (minimumTerm >= 2 && maximumTerm >= 3 && minimumTerm <= maximumTerm
            && maximumTerm <= (mode == SurfaceFormMode.FringeZernike
                ? ZernikeFitEngine.MaximumFringeTerm : ZernikeFitEngine.MaximumStandardTerm));

    public static bool UsesTestWavelength(SurfaceFormMode mode, IsoSurfaceFormUnit unit) =>
        mode == SurfaceFormMode.FringeZernike
        || ((mode == SurfaceFormMode.IsoPower || IsIsoIrregularity(mode)) && unit == IsoSurfaceFormUnit.Fringes);

    private static bool IsIsoIrregularity(SurfaceFormMode mode) => mode is
        SurfaceFormMode.IsoIrregularityPv or SurfaceFormMode.IsoRotationalIrregularityPv or SurfaceFormMode.IsoIrregularityRms;

    public static bool Supports(OpticalSurface surface) =>
        surface.InteractionModel is RefractiveReflectiveInteractionModel
        && surface.Geometry is (StandardGeometry or PlaneGeometry { ReferenceIncidenceNormal: null }
            or EvenAsphereGeometry or ToroidalGeometry or FringeSagGeometry or IrregularGeometry or SurfaceFormGeometry)
        && (surface.PhysicalAperture is null or CircularAperture)
        && double.IsFinite(ApertureRadius(surface)) && ApertureRadius(surface) > 0;

    public void Apply(Optic optic) => Apply(optic, new Random(1234));

    public void Apply(Optic optic, Random random)
    {
        if (Maximum == 0) return;
        if (_mode == SurfaceFormMode.IsoPower)
        {
            var power = VariableRangePerturbation.SampleDelta(random, -1, 1, normalDistribution: true);
            ApplySag(optic, power * Maximum * _sagScale, []);
            return;
        }
        if (IsIsoIrregularity(_mode))
        {
            var fraction = _isoStatistics == IsoSurfaceFormStatistics.TruncatedGaussian
                ? SampleIsoFraction(random, 0) : 1;
            ApplyIsoIrregularity(optic, fraction * Maximum * _sagScale, random);
            return;
        }

        var coefficients = new double[_orders.Length];
        var sumSquares = 0.0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            var coefficient = VariableRangePerturbation.SampleDelta(random, -1, 1, _normalDistribution);
            coefficients[i] = coefficient;
            sumSquares += coefficient * coefficient;
        }

        // Standard terms are orthonormal on the unit disk, so the coefficient norm is the sag RMS.
        if (sumSquares == 0 && _mode == SurfaceFormMode.StandardZernike)
        {
            coefficients[0] = 1;
            sumSquares = 1;
        }
        var normalization = _mode == SurfaceFormMode.StandardZernike ? Math.Sqrt(sumSquares) : Math.Sqrt(coefficients.Length);
        for (var i = 0; i < coefficients.Length; i++)
            coefficients[i] = coefficients[i] / normalization * Maximum * _sagScale;
        ApplySag(optic, 0, coefficients);
    }

    public void ApplyMinimum(Optic optic) => ApplyScaledMinimum(optic, 1);
    public void ApplyMaximum(Optic optic) => ApplyScaledMaximum(optic, 1);
    public void ApplyScaledMinimum(Optic optic, double scale) => ApplyEndpoint(optic, Minimum, scale);
    public void ApplyScaledMaximum(Optic optic, double scale) => ApplyEndpoint(optic, Maximum, scale);

    public void Revert(Optic optic)
    {
        if (_original is null) return;
        Target(optic).Geometry = _original;
        _original = null;
    }

    private void ApplyEndpoint(Optic optic, double endpoint, double scale)
    {
        var magnitude = endpoint * VariableRangePerturbation.ValidateScale(scale) * _sagScale;
        if (magnitude == 0) return;
        if (_mode == SurfaceFormMode.IsoPower)
            ApplySag(optic, magnitude, []);
        else if (IsIsoIrregularity(_mode))
            ApplyIsoIrregularity(optic, magnitude, null);
        else
            ApplySag(optic, 0, Enumerable.Repeat(magnitude / Math.Sqrt(_orders.Length), _orders.Length).ToArray());
    }

    private void ApplyIsoIrregularity(Optic optic, double magnitude, Random? random)
    {
        var figure = IsoSurfaceFigure.Create(_mode, magnitude, random);
        ApplySag(optic, 0, figure.Coefficients, figure.Orders, figure.Piston);
    }

    private void ApplySag(Optic optic, double power, double[] coefficients, (int N, int M)[]? orders = null, double piston = 0)
    {
        var surface = Target(optic);
        if (!Supports(surface))
            throw new InvalidOperationException("Surface form tolerances require a supported physical surface with a centered circular aperture.");

        var nominal = surface.Geometry;
        var radius = ApertureRadius(surface);
        var decenterX = 0.0;
        var decenterY = 0.0;
        var extrapolate = true;
        if (_mode == SurfaceFormMode.FringeZernike)
        {
            var baseGeometry = nominal is SurfaceFormGeometry existing ? existing.Nominal : nominal;
            if (baseGeometry is FringeSagGeometry fringe)
            {
                radius = fringe.NormalizationRadius;
                decenterX = fringe.DecenterX;
                decenterY = fringe.DecenterY;
                extrapolate = fringe.Extrapolate;
            }
        }

        if (_mode == SurfaceFormMode.StandardZernike)
            for (var i = 0; i < coefficients.Length; i++)
                coefficients[i] *= ZernikeFitEngine.StandardNormalization(_orders[i].N, _orders[i].M);

        _original = nominal;
        surface.Geometry = new SurfaceFormGeometry(nominal, radius, decenterX, decenterY, extrapolate, power, piston,
            orders ?? _orders, coefficients);
    }

    private OpticalSurface Target(Optic optic) =>
        _surfaceNumber < optic.SurfaceGroup.Items.Count - 1
            ? optic.SurfaceGroup.Items[_surfaceNumber]
            : throw new ArgumentOutOfRangeException(nameof(optic), "A surface form tolerance must target a physical lens surface.");

    internal static void CopyDeformation(IGeometry deformed, OpticalSurface target)
    {
        if (deformed is not SurfaceFormGeometry overlay || !Supports(target))
            throw new InvalidOperationException("The corresponding surface cannot accept the sampled form error.");
        target.Geometry = overlay.WithNominal(target.Geometry);
    }

    private static double ApertureRadius(OpticalSurface surface) =>
        surface.PhysicalAperture is CircularAperture circle ? circle.Radius : surface.SemiDiameter;

    private sealed class SurfaceFormGeometry(
        IGeometry nominal, double radius, double decenterX, double decenterY, bool extrapolate, double power, double piston,
        (int N, int M)[] orders, double[] coefficients) : IGeometry
    {
        public IGeometry Nominal { get; } = nominal;
        public string Kind => "tolerance_surface_form";

        public double Sag(double x, double y) => Nominal.Sag(x, y) + Deformation(x, y).Sag;

        public Vector3D SurfaceNormal(Vector3D point)
        {
            var (sag, dx, dy) = Deformation(point.X, point.Y);
            var normal = Nominal.SurfaceNormal(new Vector3D(point.X, point.Y, point.Z - sag));
            var adjusted = new Vector3D(normal.X - dx * normal.Z, normal.Y - dy * normal.Z, normal.Z);
            return adjusted / adjusted.Length;
        }

        public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
            StandardGeometry.NewtonSolveDistance(origin, direction, Sag, SurfaceNormal);

        public IGeometry Clone() => WithNominal(Nominal.Clone());

        public IGeometry WithNominal(IGeometry geometry) =>
            new SurfaceFormGeometry(geometry, radius, decenterX, decenterY, extrapolate, power, piston, orders, coefficients);

        private (double Sag, double Dx, double Dy) Deformation(double x, double y)
        {
            var u = (x - decenterX) / radius;
            var v = (y - decenterY) / radius;
            var rho2 = u * u + v * v;
            if (!extrapolate && rho2 > 1) return default;
            var sag = piston + power * rho2;
            var dx = 2 * power * u;
            var dy = 2 * power * v;
            for (var i = 0; i < coefficients.Length; i++)
            {
                var coefficient = coefficients[i];
                if (coefficient == 0) continue;
                var (n, m) = orders[i];
                var (value, slopeX, slopeY) = FringeZernikeBasis.EvaluateOrder(n, m, u, v);
                sag += coefficient * value;
                dx += coefficient * slopeX;
                dy += coefficient * slopeY;
            }
            return (sag, dx / radius, dy / radius);
        }
    }
}
