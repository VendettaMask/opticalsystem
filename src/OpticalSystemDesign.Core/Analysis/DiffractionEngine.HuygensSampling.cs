using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record HuygensRefinementOptions(int MaximumRadialOrder = 512, int MaximumAzimuthalOrder = 512,
    long MaximumOperations = AnalysisResourceLimits.MaximumDirectPsfOperations);

public sealed record HuygensSamplingTrial(string Rule, int RadialOrder, int AzimuthalOrder,
    double AngularOffsetRadians, int Nodes, long CumulativeOperations)
{
    public double? ReferenceAmplitude { get; init; }
    public bool GeometricChiefReferenceUsed { get; init; }
}

/// <summary>Measured sampling agreement, not a rigorous continuum error bound.
/// Checks apply to the reported fixed physical window and existing pupil map.</summary>
public sealed record HuygensSamplingDiagnostics(string Status, int InitialSampling,
    int EffectiveRadialOrder, int EffectiveAzimuthalOrder, int FinalNodes, long CumulativeNodes,
    long CumulativeOperations, double Tolerance, double? MaximumIntensityDisagreement,
    double? MaximumReferenceAmplitudeDisagreement, IReadOnlyList<HuygensSamplingTrial> Trials)
{
    internal const string ExceptionKey = "HuygensSamplingDiagnostics";
    internal const string PreviousChecksKey = "HuygensSamplingPreviousChecks";
    public long WorkLimit { get; init; }
    public int RadialOrderLimit { get; init; }
    public int AzimuthalOrderLimit { get; init; }
    public double WavelengthNanometers { get; init; }
    public double FieldX { get; init; }
    public double FieldY { get; init; }
    public string PupilPartition { get; init; } = "FullDisk";
    public int ApertureBoundaryCount { get; init; }
    public string AperturePartitionScope => "UnpoweredHomogeneousPlanarPrefix";
    public string OpticalPathConvention => "TracePhaseInclusiveOpticalPathLength";
    public string Scope => "PsfQuadratureOnFixedReportedWindow";
    public bool IsContinuumErrorBound => false;

    internal static AnalysisData? Unavailable(string name, AnalysisDataUnavailableException exception)
    {
        if (exception.Data[ExceptionKey] is not HuygensSamplingDiagnostics diagnostics) return null;
        var previous = exception.Data[PreviousChecksKey] as IReadOnlyList<HuygensSamplingDiagnostics> ?? Array.Empty<HuygensSamplingDiagnostics>();
        return new AnalysisData(name, new Dictionary<string, object>
        {
            ["Status"] = exception.Reason,
            ["AfocalIntegration"] = AfocalHuygensIntegration.CheckedGaussianPupil.ToString(),
            ["SamplingStatus"] = diagnostics.Status,
            ["SamplingChecks"] = previous.Append(diagnostics).ToArray()
        }, Outcome: AnalysisOutcome.Unavailable, OutcomeReason: exception.Reason);
    }
}

public static partial class DiffractionEngine
{
    public const double HuygensSamplingTolerance = 1e-10;

    public static PsfResult ComputeCheckedAfocalHuygensPsf(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int initialSampling, int imageSize, double pitchMilliradians,
        HuygensRefinementOptions? refinement = null, bool aimAtStop = false, double defocus = 0,
        Wavelength? referenceWavelength = null, (double X, double Y)? imageCenterOffset = null,
        bool useParaxialPupilDefocus = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!optic.ImageSpaceAfocal) throw new NotSupportedException("Checked circular Huygens integration requires afocal image space.");
        AnalysisResourceLimits.ValidateDirectPsfWork(initialSampling, imageSize);
        if (!double.IsFinite(pitchMilliradians) || pitchMilliradians <= 0) throw new ArgumentOutOfRangeException(nameof(pitchMilliradians));
        if (!double.IsFinite(defocus)) throw new ArgumentOutOfRangeException(nameof(defocus));
        if (imageCenterOffset is { } offset && (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y)))
            throw new ArgumentOutOfRangeException(nameof(imageCenterOffset));
        refinement ??= new();
        if (refinement.MaximumRadialOrder is < 1 or > 512 || refinement.MaximumAzimuthalOrder is < 4 or > 2048
            || refinement.MaximumOperations < 0 || refinement.MaximumOperations > AnalysisResourceLimits.MaximumDirectPsfOperations)
            throw new ArgumentOutOfRangeException(nameof(refinement));
        var trials = new List<HuygensSamplingTrial>();
        long operations = 0, totalNodes = 0;
        double? intensityDisagreement = null, referenceDisagreement = null;
        var radial = initialSampling / 2; var azimuthal = checked(2 * initialSampling);
        // The normalization pupil is on axis and may differ from the field pupil.
        // Never reuse a clipped field domain to define the reference amplitude.
        var domain = HuygensPupilDomain.TryCreate(optic, field, wavelength, aimAtStop);
        var offAxis = field.Hx != 0 || field.Hy != 0;
        var referenceDomain = offAxis ? HuygensPupilDomain.TryCreate(optic, (0, 0), wavelength, aimAtStop) : domain;
        PsfResult? current = null;
        HuygensSamplingDiagnostics Diagnostics(string status) => new(status, initialSampling,
            radial, azimuthal, current?.PupilQuadratureNodeCount ?? 0, totalNodes, operations,
            HuygensSamplingTolerance, intensityDisagreement, referenceDisagreement, trials.ToArray())
            { WorkLimit = refinement.MaximumOperations, RadialOrderLimit = refinement.MaximumRadialOrder,
                AzimuthalOrderLimit = refinement.MaximumAzimuthalOrder, WavelengthNanometers = wavelength.Nanometers,
                FieldX = field.Hx, FieldY = field.Hy,
                PupilPartition = domain is null && referenceDomain is null ? "FullDisk" : "PlanarApertureBoundaries",
                ApertureBoundaryCount = (domain?.BoundaryCount ?? 0) + (offAxis ? referenceDomain?.BoundaryCount ?? 0 : 0) };
        AnalysisDataUnavailableException Failure(string status, string reason)
        {
            var error = new AnalysisDataUnavailableException("Huygens PSF", reason);
            error.Data[HuygensSamplingDiagnostics.ExceptionKey] = Diagnostics(status);
            return error;
        }
        void Check(int r, int m)
        {
            if (r > refinement.MaximumRadialOrder || m > refinement.MaximumAzimuthalOrder)
                throw Failure("OrderLimitReached", "circular Huygens sampling did not stabilize within the declared radial/azimuthal limits");
            if ((long)r * m > ApertureSampler.MaximumPupilSampleCount)
                throw Failure("NodeLimitReached", "circular Huygens sampling exceeds the shared pupil-node limit");
        }
        PsfResult Evaluate(string rule, int r, int m, double angle = 0)
        {
            Check(r, m);
            var availableNodes = (int)Math.Min(ApertureSampler.MaximumPupilSampleCount,
                (refinement.MaximumOperations - operations) / ((long)imageSize * imageSize));
            IReadOnlyList<PupilSample> Rule(HuygensPupilDomain? pupil, int limit) => pupil is not null
                ? pupil.Generate(r, m, angle != 0, limit)
                : (long)r * m > limit ? throw new HuygensNodeBudgetException()
                    : ApertureSampler.GenerateHuygensGaussianQuadrature(r, m, angle);
            IReadOnlyList<PupilSample> samples;
            IReadOnlyList<PupilSample>? referenceSamples = null;
            try
            {
                samples = Rule(domain, availableNodes);
                if (offAxis) referenceSamples = Rule(referenceDomain, availableNodes - samples.Count);
            }
            catch (HuygensNodeBudgetException)
            {
                throw Failure("WorkLimitReached", "circular Huygens sampling did not stabilize within the shared cumulative work limit");
            }
            var nodes = samples.Count + (referenceSamples?.Count ?? 0);
            var work = checked((long)nodes * imageSize * imageSize);
            ComputationCancellation.ThrowIfCancellationRequested();
            operations += work; totalNodes += nodes;
            trials.Add(new(rule, r, m, angle, nodes, operations));
            try
            {
                var result = ComputeGaussianAfocalHuygensPsf(optic, field, wavelength, initialSampling, imageSize,
                    pitchMilliradians, aimAtStop, defocus, referenceWavelength, imageCenterOffset,
                    useParaxialPupilDefocus, samples, domain is not null || referenceDomain is not null, referenceSamples);
                trials[^1] = trials[^1] with { ReferenceAmplitude = result.HuygensReferenceAmplitude,
                    GeometricChiefReferenceUsed = result.GeometricChiefReferenceUsed };
                return result;
            }
            catch (AnalysisDataUnavailableException error)
            {
                error.Data[HuygensSamplingDiagnostics.ExceptionKey] = Diagnostics("PupilUnavailable");
                throw;
            }
        }
        current = Evaluate("Initial", radial, azimuthal);
        while (true)
        {
            var nextRadial = Math.Min(checked(radial * 2), refinement.MaximumRadialOrder);
            var nextAzimuthal = Math.Min(checked(azimuthal * 2 + 1), refinement.MaximumAzimuthalOrder);
            var radialAtLimit = radial == nextRadial;
            var angularAtLimit = azimuthal == nextAzimuthal;
            if (radialAtLimit && radial < 2 || angularAtLimit && azimuthal <= 4)
                throw Failure("OrderLimitReached", "circular Huygens sampling requires distinct radial and angular confirmation orders");
            Check(nextRadial, nextAzimuthal);
            // Independent radial and angular refinement, odd angular order and
            // a shifted fine rule expose common periodic-grid alias plateaus.
            var radialOnly = Evaluate(radialAtLimit ? "RadialConfirmation" : "Radial",
                radialAtLimit ? Math.Max(1, radial - 1) : nextRadial, azimuthal);
            var angularOnly = Evaluate(angularAtLimit ? "AngularConfirmation" : "Angular", radial,
                angularAtLimit ? Math.Max(4, azimuthal - 1) : nextAzimuthal);
            var fine = Evaluate("Combined", nextRadial, nextAzimuthal);
            var shifted = Evaluate("Shifted", nextRadial, nextAzimuthal, Math.PI / nextAzimuthal);
            (double Intensity, double Reference) Difference(PsfResult other)
            {
                var intensity = 0d;
                for (var y = 0; y < imageSize; y++) for (var x = 0; x < imageSize; x++)
                    intensity = Math.Max(intensity, Math.Abs(fine.Values[y, x] - other.Values[y, x]) / 100);
                var a = fine.HuygensReferenceAmplitude!.Value; var b = other.HuygensReferenceAmplitude!.Value;
                return (intensity, Math.Abs(a - b) / Math.Max(a, b));
            }
            intensityDisagreement = 0; referenceDisagreement = 0;
            foreach (var other in new[] { current, radialOnly, angularOnly, shifted })
            {
                var difference = Difference(other);
                intensityDisagreement = Math.Max(intensityDisagreement.Value, difference.Intensity);
                referenceDisagreement = Math.Max(referenceDisagreement.Value, difference.Reference);
            }
            if (intensityDisagreement <= HuygensSamplingTolerance && referenceDisagreement <= HuygensSamplingTolerance)
            {
                radial = nextRadial; azimuthal = nextAzimuthal; current = fine;
                return fine with { AfocalIntegration = AfocalHuygensIntegration.CheckedGaussianPupil,
                    HuygensSamplingChecks = new[] { Diagnostics("SamplingStable") } };
            }
            static bool Unresolved((double Intensity, double Reference) d) =>
                d.Intensity > HuygensSamplingTolerance || d.Reference > HuygensSamplingTolerance;
            var needsRadial = Unresolved(Difference(radialAtLimit ? radialOnly : angularOnly));
            var needsAngular = Unresolved(Difference(angularAtLimit ? angularOnly : radialOnly)) || Unresolved(Difference(shifted));
            if (radialAtLimit && needsRadial || angularAtLimit && needsAngular)
                throw Failure("OrderLimitReached", "circular Huygens sampling did not stabilize within the declared radial/azimuthal limits");
            // Promote only the unresolved coordinate. Independent cross/shifted
            // probes remain mandatory at the next step and before every acceptance.
            if (needsRadial && !needsAngular) { radial = nextRadial; current = radialOnly; }
            else if (needsAngular && !needsRadial) { azimuthal = nextAzimuthal; current = angularOnly; }
            else { radial = nextRadial; azimuthal = nextAzimuthal; current = fine; }
        }
    }
}
