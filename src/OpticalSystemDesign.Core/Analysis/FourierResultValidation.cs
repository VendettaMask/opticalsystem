using OpticalSystemDesign.Core.Services;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.Core.Analysis;

public enum FourierSamplingMode { Fixed, ValidateRequested, Adaptive }

public sealed record FourierValidationOptions(
    FourierSamplingMode Mode = FourierSamplingMode.Fixed,
    int MaximumPupilSampling = 1024,
    double RelativeRmsAccuracy = .001,
    double AbsoluteAccuracy = 1e-7,
    long MaximumNominalPupilCells = AnalysisResourceLimits.MaximumAnalysisRayWork)
{
    internal void Validate(int requested)
    {
        if (Mode == FourierSamplingMode.Fixed)
        {
            if (requested < 2) throw new ArgumentOutOfRangeException(nameof(requested));
            return;
        }
        if (!Enum.IsDefined(Mode) || requested < 2 || MaximumPupilSampling < requested
            || MaximumPupilSampling > DiffractionEngine.MaximumAxisTransformSize / 2
            || !double.IsFinite(RelativeRmsAccuracy) || RelativeRmsAccuracy <= 0 || RelativeRmsAccuracy > .01
            || !double.IsFinite(AbsoluteAccuracy) || AbsoluteAccuracy <= 0 || AbsoluteAccuracy > .001
            || MaximumNominalPupilCells <= 0 || MaximumNominalPupilCells > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new ArgumentOutOfRangeException(nameof(FourierValidationOptions));
    }
}

public sealed record FourierValidationStep(int PupilSampling, bool PhaseResolved,
    double? RelativeRmsChange, double? MaximumAbsoluteChange)
{
    public bool UsesDensePhysicalPupil { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesStreamedPhysicalPupil { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesPhysicalPsfWindow { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaximumPupilPhaseStepWaves { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnavailableReason { get; init; }
}

public sealed record FourierValidationReport(string Protocol, FourierSamplingMode Mode,
    int RequestedPupilSampling, int ReturnedPupilSampling, int MaximumPupilSampling,
    double RelativeRmsAccuracy, double AbsoluteAccuracy, bool Validated,
    IReadOnlyList<FourierValidationStep> Steps, string Reason)
{
    public long EvaluatedNominalPupilCells { get; init; }
    public long MaximumNominalPupilCells { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long EvaluatedStreamedPupilNodes { get; init; }
    public double? RequestedRelativeRmsDeviation { get; init; }
    public double? RequestedMaximumAbsoluteDeviation { get; init; }
}

/// <summary>
/// Explicit, bounded verification of Fourier outputs on the complete requested
/// physical domain. It never changes the fixed-grid API or its phase-risk gate.
/// A certificate is numerical self-convergence, not external-reference acceptance.
/// </summary>
public sealed class FourierResultValidationAnalysis : BaseAnalysis
{
    public const string Protocol = "FourierPhysicalDomainConvergence/v1";
    public const string DensePupilProtocol = "FourierPhysicalDomainConvergence/v2";
    public const string StreamedPupilProtocol = "FourierPhysicalDomainConvergence/v3";
    private readonly string name;
    private readonly int requested;
    private readonly Func<int, BaseAnalysis> create;
    private readonly FourierValidationOptions options;

    public FourierResultValidationAnalysis(Optic optic, string name, int requested,
        Func<int, BaseAnalysis> create, FourierValidationOptions options) : base(optic)
    {
        options.Validate(requested);
        this.name = name; this.requested = requested; this.create = create; this.options = options;
    }
    public override string Name => name;
    public static bool CanValidateSource(Optic optic) => PupilSourceIdentity.Capture(optic) is not null;

    public override AnalysisData GenerateData()
    {
        if (options.Mode == FourierSamplingMode.Fixed) return create(requested).GenerateData();
        var steps = new List<FourierValidationStep>();
        var source = PupilSourceIdentity.Capture(Optic)?.Fingerprint;
        var work = 0L;
        var sourceCount = Math.Max(1L, Optic.Fields.Count) * Math.Max(1L, Optic.Wavelengths.Count);
        (double Rms, double Max)? requestedError = null;
        AnalysisData? original = null;
        var originalSampling = 0;
        var supportsDensePupil = false;
        var supportsPhysicalPsfWindow = false;
        var supportsStreamedPsf = false;
        var supportsStreamedProfile = false;
        DiffractionEncircledEnergyAnalysis? streamedEnergy = null;
        var usedDensePupil = false;
        var usedStreamedPupil = false;
        var allowStreamedPupil = options.Mode == FourierSamplingMode.Adaptive && name is "PSF" or "MTF"
            && options.MaximumPupilSampling > AnalysisResourceLimits.MaximumFftGridSize;
        var allowStreamedProfile = options.Mode == FourierSamplingMode.Adaptive && name == "FFT PSF Cross Section"
            && options.MaximumPupilSampling > AnalysisResourceLimits.MaximumFftGridSize;
        var allowStreamedEnergy = options.Mode == FourierSamplingMode.Adaptive && name == "Diffraction Encircled Energy";
        var streamedNodes = 0L;
        var physicalSamplingEstimate = 0d;
        var normalizedPupilEstimate = 0d;
        var retryWithPhysicalWindow = false;
        if (source is null) return Unavailable("The optical source cannot be identified losslessly for Fourier validation.");
        var denseLimit = Math.Min(options.MaximumPupilSampling, AnalysisResourceLimits.MaximumFftGridSize);
        if (allowStreamedEnergy)
        {
            var initial = Scan(requested, requested, densePhysicalPupil: false);
            if (initial is not null) return initial;
            return original is null || streamedEnergy is null ? Unavailable("No original physical energy interval is available.")
                : StreamedPupil(original, energy: streamedEnergy);
        }
        var result = Scan(requested, allowStreamedPupil ? denseLimit : options.MaximumPupilSampling, densePhysicalPupil: false);
        if (result is not null) return result;

        if (options.Mode == FourierSamplingMode.Adaptive && supportsDensePupil && requested <= denseLimit / 4)
        {
            usedDensePupil = true;
            result = Scan(checked(requested * 2), denseLimit, densePhysicalPupil: true);
            var canStream = allowStreamedPupil || allowStreamedProfile && supportsStreamedProfile;
            if (result is not null && (!canStream || result.Outcome != AnalysisOutcome.Unavailable)) return result;
        }
        if (allowStreamedPupil && original is not null && physicalSamplingEstimate > 0 && (name == "MTF" || supportsStreamedPsf))
        {
            result = StreamedPupil(original);
            return retryWithPhysicalWindow ? StreamedPupil(original, forcePhysicalWindow: true) : result;
        }
        if (allowStreamedProfile && supportsStreamedProfile && original is not null)
            return StreamedPupil(original, profile: true);
        if (result is null && options.Mode == FourierSamplingMode.Adaptive && supportsPhysicalPsfWindow
            && original?.Values.GetValueOrDefault("DisplaySize") is int)
        {
            var limit = Math.Min(denseLimit, DiffractionEngine.MaximumPhysicalPsfWindowSampling);
            if (requested <= limit / 4)
            {
                var first = requested;
                while (first <= limit / 4) first *= 2;
                usedDensePupil = true;
                result = Scan(first, limit, densePhysicalPupil: false, physicalPsfWindow: true);
                if (result is not null) return result;
            }
        }
        return Unavailable("The declared pupil-sampling budget did not establish result convergence.");

        AnalysisData StreamedPupil(AnalysisData domain, bool profile = false, bool forcePhysicalWindow = false,
            DiffractionEncircledEnergyAnalysis? energy = null)
        {
            const int increment = 256;
            var displaySize = domain.Values.GetValueOrDefault("DisplaySize") as int?;
            if (name == "PSF" && displaySize is null)
                return Unavailable("Streamed pupil refinement requires a fixed physical image window.");
            var maximumSampling = profile || energy is not null ? DiffractionEngine.MaximumStreamedPhysicalPsfSampling
                : name == "PSF" ? DiffractionEngine.MaximumStreamedPsfSampling : DiffractionEngine.MaximumStreamedMtfSampling;
            var limit = Math.Min(options.MaximumPupilSampling, maximumSampling);
            limit -= limit % increment;
            var symmetricEnergy = limit > 4096 && energy?.UsesSagittalCentroidSymmetry == true;
            if (symmetricEnergy) limit = Math.Min(limit, DiffractionEngine.MaximumSymmetricCentroidEnergySampling);
            // Use the observed unresolved grid step to plan a lower trial,
            // with a margin for phase curvature. Both trials still enforce
            // every actual pupil edge and compare the full physical result.
            var lower = symmetricEnergy ? (7 * limit / 8 / increment) * increment : profile || energy is not null
                ? Math.Max(profile ? AnalysisResourceLimits.MaximumFftGridSize : increment, (3 * limit / 4 / increment) * increment)
                : LowerTrial(physicalSamplingEstimate);
            var physicalWindow = false;
            if ((forcePhysicalWindow || lower >= limit) && name == "PSF" && normalizedPupilEstimate > 0)
            {
                // A narrow image pitch can stretch the source lattice so far
                // that it wastes the nominal budget on empty samples. Read the
                // same physical image frequencies from an unstretched pupil.
                physicalWindow = true;
                limit = Math.Min(limit, DiffractionEngine.MaximumStreamedPhysicalPsfSampling);
                lower = LowerTrial(normalizedPupilEstimate);
            }
            if (lower >= limit || !double.IsFinite(lower))
                return Unavailable("The streamed pupil budget cannot fit two distinct phase-resolved trials.");
            usedStreamedPupil = true;
            AnalysisData? previous = null;
            var candidates = new List<int> { (int)lower, limit };
            for (var trial = 0; trial < candidates.Count; trial++)
            {
                var n = candidates[trial];
                ComputationCancellation.ThrowIfCancellationRequested();
                try
                {
                    var phase = new List<FftPupilSampling>();
                    AnalysisData data;
                    var remaining = options.MaximumNominalPupilCells - work - streamedNodes;
                    using (FftPupilSampling.RequireResolvedResults())
                    using (FftPupilSampling.ObserveForValidation(phase))
                        data = energy is not null ? energy.GenerateStreamedPhysicalPupil(n, remaining) : create(n) switch
                        {
                            PsfAnalysis { SupportsStreamedPhysicalPupil: true } psf when displaySize.HasValue
                                => psf.GenerateStreamedPhysicalPupil(displaySize.Value, remaining, physicalWindow),
                            MtfAnalysis { SupportsStreamedPhysicalPupil: true } mtf
                                => mtf.GenerateStreamedPhysicalPupil(remaining),
                            FftPsfCrossSectionAnalysis { SupportsStreamedPhysicalPupil: true } section when profile
                                => section.GenerateStreamedPhysicalPupil(domain, remaining),
                            _ => Unavailable("Streamed refinement requires one scalar focal field and wavelength with a fixed Fourier interval.")
                        };
                    if (data.Outcome != AnalysisOutcome.Success && (energy is null || phase.Count == 0)) return data;
                    if (PupilSourceIdentity.Capture(Optic)?.Fingerprint != source)
                        return Unavailable("The optical source changed during streamed Fourier validation.");
                    if (data.Values.GetValueOrDefault("StreamedFourierPupil") is not StreamedFourierPupilDiagnostics diagnostics)
                        return Unavailable("Streamed pupil diagnostics are unavailable.");
                    streamedNodes += diagnostics.TracedNodes;
                    var resolved = phase.Count == 1 && phase[0].Status == FftPhaseSamplingStatus.ResolvedAtNodes;
                    var projected = data.Outcome == AnalysisOutcome.Success ? Project(data, domain) : null;
                    var error = previous is null || projected is null ? ((double Rms, double Max)?)null : Difference(previous, projected);
                    steps.Add(new(n, resolved, error?.Rms, error?.Max)
                    {
                        UsesStreamedPhysicalPupil = true,
                        UsesPhysicalPsfWindow = physicalWindow,
                        MaximumPupilPhaseStepWaves = phase.Count == 1 ? phase[0].MaximumStepWaves : null
                    });
                    if (!resolved)
                    {
                        if ((profile || energy is not null) && trial == 0 && phase.Count == 1
                            && phase[0].Status == FftPhaseSamplingStatus.UndersampledPhase)
                        {
                            var refinedLower = LowerTrial(n * phase[0].MaximumStepWaves / FftPupilSampling.MaximumAdjacentStepWaves);
                            if (refinedLower > n && refinedLower < limit)
                            {
                                candidates.Insert(1, (int)refinedLower);
                                continue;
                            }
                        }
                        return Unavailable("Streamed Fourier pupil phase remains unresolved.");
                    }
                    if (projected is null) return data;
                    if (error is { } delta && Accept(delta))
                    {
                        if (originalSampling == requested) requestedError = Difference(domain, projected);
                        return Attach(projected, true, n,
                            name == "MTF"
                                ? "Two streamed phase-resolved pupil densities agree over the requested physical frequency interval."
                                : profile
                                ? "Two streamed phase-resolved pupil densities agree on the requested physical image axis."
                                : energy is not null
                                ? "Two streamed phase-resolved pupils agree on both requested energy curves."
                                : "Two streamed phase-resolved pupil densities agree over every requested physical image node.");
                    }
                    previous = projected;
                }
                catch (InvalidOperationException error) { return Unavailable(error.Message); }
            }
            retryWithPhysicalWindow = !physicalWindow && name == "PSF" && normalizedPupilEstimate > 0;
            return Unavailable("The two phase-resolved streamed pupils did not meet the declared output accuracy.");

            static double LowerTrial(double estimate) => Math.Max(AnalysisResourceLimits.MaximumFftGridSize,
                Math.Ceiling(estimate * (17d / 16) / increment) * increment);
        }

        AnalysisData? Scan(int first, int last, bool densePhysicalPupil, bool physicalPsfWindow = false)
        {
            AnalysisData? previous = null;
            var previousResolved = false;
            for (var n = first; n <= last; n = checked(n * 2))
            {
                // Eligible profiles can use the remaining ray budget directly on
                // the original axis once the bounded dense source is exhausted.
                if (!densePhysicalPupil && allowStreamedProfile && supportsStreamedProfile && n > denseLimit)
                    break;
                ComputationCancellation.ThrowIfCancellationRequested();
                // Conservative nominal-cell accounting includes both actual and
                // ideal scalar pupils and all defined fields/wavelengths. It is
                // explicitly not a claim about the tracer's private interpolation work.
                var nextWork = checked(2L * n * n * sourceCount);
                if (nextWork > options.MaximumNominalPupilCells - work)
                    return Unavailable("The total declared Fourier pupil-cell budget was exhausted.");
                work += nextWork;
                var phase = new List<FftPupilSampling>(); AnalysisData data;
                var canRetryDisconnectedPsf = false;
                try
                {
                    using (FftPupilSampling.RequireResolvedResults())
                    using (FftPupilSampling.ObserveForValidation(phase))
                    {
                        var analysis = create(n);
                        canRetryDisconnectedPsf = options.Mode == FourierSamplingMode.Adaptive
                            && analysis is PsfAnalysis { SupportsDensePhysicalPupil: true };
                        if (physicalPsfWindow && analysis is PsfAnalysis { SupportsPhysicalPsfWindow: true } physical
                            && original?.Values.GetValueOrDefault("DisplaySize") is int displaySize)
                        {
                            data = physical.GeneratePhysicalPsfWindow(displaySize);
                        }
                        else if (densePhysicalPupil)
                        {
                            if (analysis is FftPsfCrossSectionAnalysis { SupportsDensePhysicalPupil: true } section)
                                data = section.GenerateDensePhysicalPupil();
                            else if (analysis is PsfAnalysis { SupportsDensePhysicalPupil: true } psf)
                                data = psf.GenerateDensePhysicalPupil(original?.Values.GetValueOrDefault("DisplaySize") as int?);
                            else
                                return Unavailable("Dense pupil refinement requires an eligible scalar focal FFT analysis.");
                        }
                        else
                        {
                            if (n == requested)
                            {
                                if (allowStreamedEnergy)
                                {
                                    if (analysis is not DiffractionEncircledEnergyAnalysis { SupportsStreamedPhysicalPupil: true } selectedEnergy)
                                        return Unavailable("Adaptive energy requires a scalar focal radial analysis with a selected field and wavelength and a positive maximum radius.");
                                    streamedEnergy = selectedEnergy;
                                }
                                supportsDensePupil = analysis is FftPsfCrossSectionAnalysis { SupportsDensePhysicalPupil: true }
                                    or PsfAnalysis { SupportsDensePhysicalPupil: true };
                                supportsPhysicalPsfWindow = analysis is PsfAnalysis { SupportsPhysicalPsfWindow: true };
                                supportsStreamedPsf = analysis is PsfAnalysis { SupportsStreamedPhysicalPupil: true };
                                supportsStreamedProfile = analysis is FftPsfCrossSectionAnalysis { SupportsStreamedPhysicalPupil: true };
                            }
                            data = analysis is PsfAnalysis directPsf
                                ? directPsf.GenerateSamplingValidationBaseline() : analysis.GenerateData();
                        }
                    }
                }
                catch (InvalidOperationException unavailable) when (options.Mode == FourierSamplingMode.Adaptive
                    && (unavailable is AnalysisDataUnavailableException { CanRetryAtHigherSampling: true }
                        || unavailable.Data[DiffractionEngine.InsufficientFftPupilNodesKey] is true))
                {
                    var reason = unavailable is AnalysisDataUnavailableException dataUnavailable
                        ? dataUnavailable.Reason : unavailable.Message;
                    steps.Add(new(n, false, null, null) { UsesDensePhysicalPupil = densePhysicalPupil,
                        UsesPhysicalPsfWindow = physicalPsfWindow, UnavailableReason = reason });
                    previous = null;
                    previousResolved = false;
                    continue;
                }
                catch (ArgumentException) when (densePhysicalPupil || physicalPsfWindow)
                { return Unavailable("Dense Fourier pupil sampling exceeds its physical coverage or allocation limit."); }
                catch (AnalysisDataUnavailableException unavailable) when (densePhysicalPupil || physicalPsfWindow)
                { return Unavailable(unavailable.Message); }
                catch (ArgumentOutOfRangeException) when (n > requested)
                { return Unavailable("The refined Fourier grid exceeds the existing backend allocation limit."); }
                if (data.Values.TryGetValue("WavelengthNumber", out var selection) && selection is int wave)
                {
                    var fields = data.Values.GetValueOrDefault("FieldCount") is int count ? Math.Max(1, count) : 1;
                    var activeSources = (wave != 0 ? 1L : Math.Max(1, Optic.Wavelengths.Count)) * fields;
                    work -= nextWork; sourceCount = activeSources;
                    work += checked(2L * n * n * sourceCount);
                }
                if (PupilSourceIdentity.Capture(Optic)?.Fingerprint != source)
                    return Unavailable("The optical source changed during Fourier validation.");
                if (data.Outcome == AnalysisOutcome.NotApplicable) return data;
                if (data.Outcome != AnalysisOutcome.Success) return Unavailable(data.OutcomeReason ?? "Fourier result unavailable");
                if (data.Values.GetValueOrDefault("Normalized") is true || data.Values.GetValueOrDefault("UsePolarization") is true
                    || data.PlotSeries.Any(s => s.YUnit is AnalysisAxisUnit.Decibel or AnalysisAxisUnit.Radian || s.ValueUnit == AnalysisAxisUnit.Decibel))
                    return Unavailable("This protocol requires linear scalar intensity or modulation without peak normalization.");
                if (data.Table is not null || data.PlotPanes is { Count: > 0 })
                    return Unavailable("This validation protocol requires a scalar Fourier plot without result tables or panes.");
                if (original is null)
                {
                    original = data;
                    originalSampling = n;
                }
                AnalysisData projected;
                try { projected = Project(data, original); }
                catch (InvalidOperationException e) { return Unavailable(e.Message); }
                var resolved = phase.Count > 0 && phase.All(p => p.Status == FftPhaseSamplingStatus.ResolvedAtNodes);
                if (allowStreamedPupil && !densePhysicalPupil && phase.Count == 1
                    && phase[0].Status == FftPhaseSamplingStatus.UndersampledPhase)
                {
                    physicalSamplingEstimate = n * phase[0].MaximumStepWaves / FftPupilSampling.MaximumAdjacentStepWaves;
                    if (supportsStreamedPsf && data.Values.GetValueOrDefault("PupilGridStretchX") is double[] { Length: 1 } stretchX
                        && data.Values.GetValueOrDefault("PupilGridStretchY") is double[] { Length: 1 } stretchY)
                    {
                        var stretch = Math.Min(stretchX[0], stretchY[0]);
                        normalizedPupilEstimate = double.IsFinite(stretch) && stretch > 1 ? physicalSamplingEstimate / stretch : 0;
                    }
                }
                if (phase.Any(p => p.Status is FftPhaseSamplingStatus.NoConnectedSamples or FftPhaseSamplingStatus.UnverifiedNodeOrder))
                {
                    if (!canRetryDisconnectedPsf || phase.Any(p => p.Status == FftPhaseSamplingStatus.UnverifiedNodeOrder)
                        || data.Values.GetValueOrDefault("DisplaySize") is not int displaySize
                        || data.Values.GetValueOrDefault("RequestedDisplaySize") is not int requestedDisplaySize
                        || displaySize != requestedDisplaySize)
                        return Unavailable("The physical pupil adjacency cannot be verified.");

                    // Keep the full original image coordinates, but exclude a disconnected pupil's intensity.
                    if (n == requested) originalSampling = 0;
                    steps.Add(new(n, false, null, null)
                    {
                        UsesDensePhysicalPupil = densePhysicalPupil,
                        UsesPhysicalPsfWindow = physicalPsfWindow,
                        UnavailableReason = "The sampled pupil has no connected illuminated nodes."
                    });
                    previous = null;
                    previousResolved = false;
                    continue;
                }
                var error = previous is null ? ((double Rms, double Max)?)null : Difference(previous, projected);
                steps.Add(new(n, resolved, error?.Rms, error?.Max) { UsesDensePhysicalPupil = densePhysicalPupil,
                    UsesPhysicalPsfWindow = physicalPsfWindow });
                if (previousResolved && resolved && error is { } delta && Accept(delta))
                {
                    var candidate = options.Mode == FourierSamplingMode.ValidateRequested ? Project(original, original) : projected;
                    var originalError = Difference(candidate, projected);
                    if (originalSampling == requested)
                        requestedError = Difference(Project(original, original), projected);
                    if (!Accept(originalError)) return Unavailable("The requested finite sum differs from the resolved reference beyond the declared accuracy.");
                    return Attach(candidate, true, options.Mode == FourierSamplingMode.ValidateRequested ? requested : n,
                        "Two independently sampled phase-resolved outputs agree over every requested physical node.");
                }
                previous = projected; previousResolved = resolved;
            }
            return null;
        }

        bool Accept((double Rms, double Max) error) => error.Max <= options.AbsoluteAccuracy || error.Rms <= options.RelativeRmsAccuracy;
        AnalysisData Attach(AnalysisData data, bool valid, int actual, string reason)
        {
            var values = new Dictionary<string, object>(data.Values)
            {
                ["FourierSamplingValidation"] = new FourierValidationReport(usedStreamedPupil ? StreamedPupilProtocol
                    : usedDensePupil ? DensePupilProtocol : Protocol, options.Mode, requested, actual,
                    options.MaximumPupilSampling, options.RelativeRmsAccuracy, options.AbsoluteAccuracy, valid, steps.ToArray(), reason)
                {
                    EvaluatedNominalPupilCells = work, MaximumNominalPupilCells = options.MaximumNominalPupilCells,
                    EvaluatedStreamedPupilNodes = streamedNodes,
                    RequestedRelativeRmsDeviation = requestedError?.Rms, RequestedMaximumAbsoluteDeviation = requestedError?.Max
                }
            };
            return data with { Values = values };
        }
        AnalysisData Unavailable(string reason) => Attach(AnalysisData.Unavailable(Name, reason), false, 0, reason);
    }

    internal static AnalysisData Project(AnalysisData source, AnalysisData domain)
    {
        if (source.PlotSeries.Count != domain.PlotSeries.Count || domain.PlotSeries.Count == 0)
            throw new InvalidOperationException("Fourier result series differ or are empty.");
        var series = source.PlotSeries.Zip(domain.PlotSeries, (s, d) =>
        {
            if (s.XUnit == AnalysisAxisUnit.Unspecified || s.YUnit == AnalysisAxisUnit.Unspecified
                || s.XQuantity == AnalysisAxisQuantity.Unspecified || s.YQuantity == AnalysisAxisQuantity.Unspecified
                || s.Points.Any(p => p.Value.HasValue) && (s.ValueUnit == AnalysisAxisUnit.Unspecified || s.ValueQuantity == AnalysisAxisQuantity.Unspecified)
                || s.Kind != d.Kind || s.XUnit != d.XUnit || s.XQuantity != d.XQuantity
                || s.YUnit != d.YUnit || s.YQuantity != d.YQuantity || s.ValueUnit != d.ValueUnit || s.ValueQuantity != d.ValueQuantity
                || s.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.Value is { } v && !double.IsFinite(v)))
                throw new InvalidOperationException("Fourier result units, masks or physical quantities differ.");
            if (d.Points.Any(p => p.Value.HasValue))
            {
                // No implicit image resizing, pixel shifting or renormalization.
                if (s.Points.Count != d.Points.Count || s.Points.Zip(d.Points).Any(p => p.First.X != p.Second.X || p.First.Y != p.Second.Y
                    || p.First.Value.HasValue != p.Second.Value.HasValue))
                    throw new InvalidOperationException("Fourier image sampling changed; specify a fixed positive image spacing.");
                return s;
            }
            var points = s.Points.OrderBy(p => p.X).ToArray();
            if (points.Length < 2 || points.Zip(points.Skip(1)).Any(p => p.First.X >= p.Second.X)
                || d.Points.Any(p => p.X < points[0].X - 1e-10 || p.X > points[^1].X + 1e-10))
                throw new InvalidOperationException("The refined Fourier curve does not cover the complete original physical domain.");
            var at = 0;
            var projected = d.Points.OrderBy(p => p.X).Select(p =>
            {
                while (at + 1 < points.Length - 1 && points[at + 1].X < p.X) at++;
                var a = points[at]; var b = points[at + 1];
                return p with { Y = a.Y + (b.Y - a.Y) * (p.X - a.X) / (b.X - a.X) };
            }).ToArray();
            return s with { Points = projected };
        }).ToArray();
        var values = new Dictionary<string, object>(source.Values);
        if (values.ContainsKey("PeakStrehlRatio")) values["PeakStrehlRatio"] = "Not evaluated outside the validated physical domain";
        if (domain.Values.ContainsKey("DisplayedWindowPeakRatio"))
            values["DisplayedWindowPeakRatio"] = series.SelectMany(s => s.Points).Max(p => p.Value ?? p.Y);
        foreach (var key in new[] { "ImageDeltaMicrometers", "ImageDeltaMilliradians" })
            if (domain.Values.TryGetValue(key, out var requestedSpacing))
            {
                if (values.TryGetValue(key, out var internalSpacing)) values["Integration" + key] = internalSpacing;
                values[key] = requestedSpacing;
            }
        return source with { Values = values, Series = null, SeriesList = series, PlotOptions = domain.PlotOptions };
    }

    internal static (double Rms, double Max) Difference(AnalysisData a, AnalysisData b)
    {
        var maximumRms = 0d; var maximum = 0d;
        foreach (var (s, t) in a.PlotSeries.Zip(b.PlotSeries))
        {
            var pairs = s.Points.Zip(t.Points).Select(p => (A: p.First.Value ?? p.First.Y, B: p.Second.Value ?? p.Second.Y)).ToArray();
            var scale = Math.Max(1e-30, pairs.Max(p => Math.Abs(p.B)));
            maximumRms = Math.Max(maximumRms, Math.Sqrt(pairs.Average(p => (p.A - p.B) * (p.A - p.B))) / scale);
            maximum = Math.Max(maximum, pairs.Max(p => Math.Abs(p.A - p.B)));
        }
        foreach (var key in new[] { "StrehlRatio", "UnaberratedPeakRatio", "WorkingFNumber", "CutoffFrequency", "DisplayedWindowPeakRatio" })
        {
            var hasA = a.Values.TryGetValue(key, out var av); var hasB = b.Values.TryGetValue(key, out var bv);
            if (hasA != hasB) return (double.PositiveInfinity, double.PositiveInfinity);
            if (!hasA) continue;
            if (av is not (double or float or decimal or int or long) || bv is not (double or float or decimal or int or long)) continue;
            var x = Convert.ToDouble(av, System.Globalization.CultureInfo.InvariantCulture);
            var y = Convert.ToDouble(bv, System.Globalization.CultureInfo.InvariantCulture);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            { if (x == y) continue; return (double.PositiveInfinity, double.PositiveInfinity); }
            var error = Math.Abs(x - y);
            maximum = Math.Max(maximum, error);
            maximumRms = Math.Max(maximumRms, error / Math.Max(1e-30, Math.Abs(y)));
        }
        return (maximumRms, maximum);
    }
}
