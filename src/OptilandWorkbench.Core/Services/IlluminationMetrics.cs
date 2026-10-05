using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Services;

public enum IlluminationSamplingKind { AdaptivePupil, UniformImageCosine }

/// <summary>Transmission-weighted image-space pupil integral.</summary>
public sealed record IlluminationMetricResult(double ProjectedCosineArea, int ValidRays, int SampledPupilNodes)
{
    public IlluminationSamplingKind Sampling { get; init; } = IlluminationSamplingKind.AdaptivePupil;
    public int IntegrationSamples { get; init; }
    // An equal-irradiance ideal circular pupil: A = pi / (4 E^2).
    // Refractive index is already reflected in the traced angular cone.
    public double EffectiveFNumber => ProjectedCosineArea > 0
        ? 0.5 * Math.Sqrt(Math.PI / ProjectedCosineArea) : double.PositiveInfinity;
}

/// <summary>
/// Integrates the full pupil through formal sequential ray tracing. A polar mesh
/// is mapped into image direction-cosine space; interior quadrature and adaptive
/// aperture-boundary tracing retain obscurations and nonuniform transmission.
/// The optional uniform image-cosine grid inversely solves each sample through
/// the same formal tracer; its cell placement is not certified native-equivalent.
/// Optional unpolarized Fresnel power transport retains the complete Jones chain;
/// unsupported complex-index interfaces and coating models are rejected.
/// </summary>
public static partial class IlluminationMetrics
{
    /// <summary>Axis-referenced RI, distinct from peak-normalized analysis curves.
    /// All five vignetting factors are removed on an isolated copy when necessary.
    /// Physical apertures and transmission remain active.</summary>
    public static double RelativeToAxis(Optic optic, (double Hx, double Hy) normalizedField,
        double wavelengthMicrometers, int rayDensity = 10, bool usePolarization = false,
        IlluminationSamplingKind sampling = IlluminationSamplingKind.AdaptivePupil)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var working = AnalysisTrace.PrepareVignettingFactors(optic, true);
        var results = EvaluateFields(working, [(0, 0), normalizedField], wavelengthMicrometers,
            rayDensity, usePolarization, sampling);
        if (results[0].ProjectedCosineArea <= 0)
            throw Unavailable("axis-referenced illumination requires a transmitting on-axis pupil");
        var ratio = results[1].ProjectedCosineArea / results[0].ProjectedCosineArea;
        return double.IsFinite(ratio) ? ratio : throw Unavailable("axis-referenced illumination is not finite");
    }

    /// <summary>Bounded parallel evaluation of independent fields on one immutable analysis snapshot.</summary>
    public static IReadOnlyList<IlluminationMetricResult> EvaluateFields(Optic optic,
        IReadOnlyList<(double Hx, double Hy)> fields, double wavelengthMicrometers, int rayDensity = 10,
        bool usePolarization = false, IlluminationSamplingKind sampling = IlluminationSamplingKind.AdaptivePupil)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(fields);
        ComputationCancellation.ThrowIfCancellationRequested();
        var results = new IlluminationMetricResult[fields.Count];
        var errors = new Exception?[fields.Count];
        var cancellation = ComputationCancellation.Current;
        Parallel.For(0, fields.Count, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
        }, (index, state) =>
        {
            try { results[index] = Evaluate(optic, fields[index], wavelengthMicrometers, rayDensity, cancellation, usePolarization, sampling); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors[index] = exception;
                state.Stop();
            }
        });
        if (errors.FirstOrDefault(error => error is not null) is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return results;
    }

    public static IlluminationMetricResult Evaluate(Optic optic,
        (double Hx, double Hy) normalizedField, double wavelengthMicrometers, int rayDensity = 10,
        CancellationToken cancellationToken = default, bool usePolarization = false,
        IlluminationSamplingKind sampling = IlluminationSamplingKind.AdaptivePupil)
    {
        ArgumentNullException.ThrowIfNull(optic);
        cancellationToken.ThrowIfCancellationRequested();
        ComputationCancellation.ThrowIfCancellationRequested();
        if (rayDensity is < 5 or > 128)
            throw new ArgumentOutOfRangeException(nameof(rayDensity), "Illumination density must be in 5..128.");
        if (!Enum.IsDefined(sampling)) throw new ArgumentOutOfRangeException(nameof(sampling));
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers));
        if (!double.IsFinite(normalizedField.Hx) || !double.IsFinite(normalizedField.Hy))
            throw new ArgumentOutOfRangeException(nameof(normalizedField));
        if (optic.SurfaceGroup.Items.Count < 2)
            throw Unavailable("an object and image surface are required");
        if (optic.SurfaceGroup.Items.Any(surface => surface.ScatteringModel is not null))
            throw Unavailable("stochastic scattering is not supported by a deterministic pupil integral");
        return new Integrator(optic, normalizedField, wavelengthMicrometers, cancellationToken, usePolarization).Evaluate(rayDensity, sampling);
    }

    private sealed partial class Integrator
    {
        private const int MaximumNodes = SequentialTraceLimits.MaximumRayCount;
        private const int MaximumDepth = 6;
        private readonly Optic _optic;
        private readonly CancellationToken _cancellation;
        private readonly Dictionary<(double X, double Y), Node> _nodes = new();
        private readonly OpticalSurface _image;
        private readonly Func<double, double, RealRay> _generate;
        private readonly bool _uniformOpenPupil;
        private readonly bool _usePolarization;
        private Vector3D _tangentX;
        private Vector3D _tangentY;
        private int _orientation;
        private int _validRays;
        private double _boundaryChordAngle;

        internal Integrator(Optic optic, (double Hx, double Hy) field, double wavelength,
            CancellationToken cancellation, bool usePolarization)
        {
            _optic = optic;
            _cancellation = cancellation;
            _usePolarization = usePolarization;
            _image = optic.SurfaceGroup.Items[^1];
            _generate = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(
                field.Hx, field.Hy, wavelength, aimAtStop: true);
            _uniformOpenPupil = !usePolarization && (optic.Apodization is null or UniformApodization)
                && optic.SurfaceGroup.Items.All(surface => surface.PhysicalAperture is null
                    && surface.CoatingModel is NoneCoatingModel
                    && surface.InteractionModel is RefractiveReflectiveInteractionModel
                    && surface.MaterialBefore.PropagationModel is HomogeneousPropagationModel
                    && surface.MaterialAfter.PropagationModel is HomogeneousPropagationModel
                    && surface.MaterialBefore.ExtinctionCoefficient(wavelength * 1000) == 0
                    && surface.MaterialAfter.ExtinctionCoefficient(wavelength * 1000) == 0);
        }

        internal IlluminationMetricResult Evaluate(int density, IlluminationSamplingKind sampling)
        {
            _boundaryChordAngle = 2 * Math.PI / (24 * density);
            var source = _generate(0, 0);
            var chief = _optic.SequentialRayTracer.DiagnoseApertures(source, _cancellation, stopAtIncidentImage: true).UnclippedImage
                ?? throw Unavailable("the geometric chief ray does not reach the image surface");
            var localHit = _image.CoordinateSystem.ToLocalPoint(chief.Position);
            var normal = Normalize(_image.Geometry.SurfaceNormal(localHit));
            var reference = Math.Abs(normal.X) < 0.95 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            _tangentX = Normalize(reference - normal * Dot(reference, normal));
            _tangentY = Normalize(Cross(normal, _tangentX));

            if (sampling == IlluminationSamplingKind.UniformImageCosine)
                return IntegrateUniformImageCosine(density);

            var center = Sample(0, 0);
            var previous = new[] { center };
            var area = 0.0;
            var radialSteps = (density + 1) / 2;
            for (var ring = 1; ring <= radialSteps; ring++)
            {
                // Interior arc lengths scale with ring radius; only the outer
                // boundary needs the fine angular polygon used for solid angle.
                var angles = ring == radialSteps ? 24 * density : Math.Max(12, 6 * ring);
                var radius = ring / (double)radialSteps;
                var current = Enumerable.Range(0, angles).Select(index =>
                {
                    var angle = 2 * Math.PI * index / angles;
                    return Sample(radius * Math.Cos(angle), radius * Math.Sin(angle));
                }).ToArray();
                if (ring == 1)
                {
                    for (var index = 0; index < angles; index++)
                        area += Integrate(center, current[index], current[(index + 1) % angles], 0);
                }
                else
                {
                    // Join consecutive rings in increasing azimuth, producing a
                    // complete oriented triangulation even when counts differ.
                    var inner = 0;
                    var outer = 0;
                    while (inner < previous.Length || outer < current.Length)
                    {
                        var a = previous[inner % previous.Length];
                        var b = current[outer % current.Length];
                        if ((inner + 1) * current.Length < (outer + 1) * previous.Length)
                        {
                            area += Integrate(a, b, previous[(inner + 1) % previous.Length], 0);
                            inner++;
                        }
                        else
                        {
                            area += Integrate(a, b, current[(outer + 1) % current.Length], 0);
                            outer++;
                        }
                    }
                }
                previous = current;
            }
            if (!double.IsFinite(area) || area < 0)
                throw Unavailable("the weighted pupil area is not finite and nonnegative");
            return new(area, _validRays, _nodes.Count);
        }

        private Node Sample(double x, double y)
        {
            _cancellation.ThrowIfCancellationRequested();
            ComputationCancellation.ThrowIfCancellationRequested();
            if (_nodes.TryGetValue((x, y), out var node)) return node;
            if (_nodes.Count >= MaximumNodes)
                throw Unavailable("pupil integration exceeded its bounded ray budget");
            RealRay source;
            try { source = _generate(x, y); }
            catch (RayAimingException)
            {
                node = new(x, y, 0, 0, 0, false, false);
                _nodes.Add((x, y), node);
                return node;
            }
            var diagnostic = _optic.SequentialRayTracer.DiagnoseApertures(source, _cancellation,
                stopAtIncidentImage: true, usePolarization: _usePolarization);
            if (diagnostic.UnclippedImage is not { } image)
                node = new(x, y, 0, 0, 0, false, false);
            else
            {
                // Irradiance is incident on the image, before a possible image-surface
                // interaction. The shared trace supplies the actual incident direction.
                var direction = Normalize(_image.CoordinateSystem.ToLocalDirection(image.IncidentDirection ?? image.Direction));
                var intensity = _usePolarization
                    ? diagnostic.PolarizationWeightedIntensity ?? throw Unavailable("polarized ray power is missing")
                    : image.Intensity;
                if (!double.IsFinite(intensity) || intensity < 0)
                    throw Unavailable("ray transmission must be finite and nonnegative");
                node = new(x, y, Dot(direction, _tangentX), Dot(direction, _tangentY),
                    intensity, diagnostic.InsideAllApertures);
                if (node.Inside && node.Weight > 0) _validRays++;
            }
            _nodes.Add((x, y), node);
            return node;
        }

        private Node Midpoint(Node a, Node b) => Sample((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private double Integrate(Node a, Node b, Node c, int depth)
        {
            // The scalar weight is provably one for this restricted open model.
            // Retain the interior mesh and its orientation checks, but do not trace
            // additional weight quadrature points for a constant integrand.
            if (_uniformOpenPupil && a.Inside && b.Inside && c.Inside)
            {
                var interior = Sample((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3);
                if (interior.Inside)
                    return Commit(a, b, interior) + Commit(b, c, interior) + Commit(c, a, interior);
            }
            var ab = Midpoint(a, b);
            var bc = Midpoint(b, c);
            var ca = Midpoint(c, a);
            var center = Sample((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3);
            var insideCount = new[] { a, b, c, ab, bc, ca, center }.Count(node => node.Inside);
            if (insideCount == 0) return 0;
            if (insideCount == 7)
            {
                CheckOrientation(a, ab, ca);
                CheckOrientation(ab, b, bc);
                CheckOrientation(ca, bc, c);
                CheckOrientation(ab, bc, ca);
                return Value(a, b, c);
            }
            else if (depth >= 2 && (a.Inside != b.Inside || b.Inside != c.Inside))
            {
                if (Clip(a, b, c) is { } clipped) return clipped;
                if (depth == MaximumDepth)
                    throw Unavailable("an aperture boundary is unresolved; increase ray density");
            }
            else if (depth == MaximumDepth)
            {
                throw Unavailable("an aperture boundary is unresolved; increase ray density");
            }
            return Integrate(a, ab, ca, depth + 1) + Integrate(ab, b, bc, depth + 1)
                + Integrate(ca, bc, c, depth + 1) + Integrate(ab, bc, ca, depth + 1);
        }

        private double? Clip(Node a, Node b, Node c)
        {
            var input = new[] { a, b, c };
            var polygon = new List<Node>(4);
            var crossings = new List<Node>(2);
            var previous = c;
            foreach (var current in input)
            {
                if (previous.Inside != current.Inside)
                {
                    var crossing = Boundary(previous, current);
                    polygon.Add(crossing);
                    crossings.Add(crossing);
                }
                if (current.Inside) polygon.Add(current);
                previous = current;
            }
            if (crossings.Count == 2)
            {
                var p = crossings[0];
                var q = crossings[1];
                var length = Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2));
                var radius = Math.Max(0.01, Math.Min(Math.Sqrt(p.X * p.X + p.Y * p.Y), Math.Sqrt(q.X * q.X + q.Y * q.Y)));
                if (length > radius * _boundaryChordAngle) return null;
            }
            var area = 0.0;
            for (var index = 1; index + 1 < polygon.Count; index++)
                area += Commit(polygon[0], polygon[index], polygon[index + 1]);
            return area;
        }

        private Node Boundary(Node a, Node b)
        {
            var inside = a.Inside ? a : b;
            var outside = a.Inside ? b : a;
            for (var iteration = 0; iteration < 24; iteration++)
            {
                var middle = Midpoint(inside, outside);
                if (middle.Inside) inside = middle;
                else outside = middle;
            }
            return inside;
        }

        private static double SignedArea(Node a, Node b, Node c) =>
            ((b.L - a.L) * (c.M - a.M) - (b.M - a.M) * (c.L - a.L)) / 2;

        private double Value(Node a, Node b, Node c)
        {
            if (_uniformOpenPupil) return Math.Abs(SignedArea(a, b, c));
            // Interior quadrature is essential: some apodizers define zero exactly
            // on the pupil boundary while their interior limit is nonzero.
            var p = Sample((4 * a.X + b.X + c.X) / 6, (4 * a.Y + b.Y + c.Y) / 6);
            var q = Sample((a.X + 4 * b.X + c.X) / 6, (a.Y + 4 * b.Y + c.Y) / 6);
            var r = Sample((a.X + b.X + 4 * c.X) / 6, (a.Y + b.Y + 4 * c.Y) / 6);
            var intensity = (p.Inside ? p.Weight : 0) + (q.Inside ? q.Weight : 0) + (r.Inside ? r.Weight : 0);
            return Math.Abs(SignedArea(a, b, c)) * intensity / 3;
        }

        private double Commit(Node a, Node b, Node c)
        {
            CheckOrientation(a, b, c);
            return Value(a, b, c);
        }

        private void CheckOrientation(Node a, Node b, Node c)
        {
            var area = SignedArea(a, b, c);
            if (Math.Abs(area) > 1e-22)
            {
                var orientation = Math.Sign(area);
                if (_orientation != 0 && orientation != _orientation)
                    throw Unavailable("the pupil mapping folds in image direction-cosine space");
                _orientation = orientation;
            }
        }
    }

    private readonly record struct Node(double X, double Y, double L, double M, double Weight, bool Inside, bool Mapped = true);
    private static AnalysisDataUnavailableException Unavailable(string reason) => new("Relative Illumination", reason);
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static Vector3D Normalize(Vector3D value) => double.IsFinite(value.Length) && value.Length > 1e-15
        ? value / value.Length : throw Unavailable("a pupil projection direction is degenerate");
}
