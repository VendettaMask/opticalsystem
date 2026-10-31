using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record GeneralCoatingWork(long SpectrumEvaluations, long SensitivityEvaluations, long AnalyticJacobians,
    int ConstraintPasses, int AdaptiveRefinements, bool AdaptiveLimitReached)
{
    public int DenseValidations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int QualityPasses { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int QualityIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QualityProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long BandMetricEvaluations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long BandSensitivityBatches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassbandProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetProtectionProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetProtectionPasses { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ProtectedTrialRejections { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DenseProtectionRejections { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int QualityRestarts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetDirectionRestarts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int InequalitySubproblems { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int InequalityActiveSetIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int InequalitySubproblemLimits { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetRepairIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetRepairAcceptedCandidates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long TangentTrialEvaluations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InequalityStepProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRepairProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetComparisonRuns { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetComparisonIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetRepairContinuationIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetParetoPolishIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetParetoAcceptedCandidates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReflectancePreconditionerIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReflectanceMetricIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SearchPeakReferenceBatches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchSamplingProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PassbandQualityIterations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PassbandQualityAcceptedCandidates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassbandQualityProtocol { get; init; }
}
public sealed record CoatingConstraintAssessment(double[] Violations)
{
    public double PeakTransmittance { get; init; }
    public bool CoarselyFeasible => Violations.All(v => v == 0);
    public double MaximumViolation => Violations.Max();
    public double Merit => Violations.Sum(v => v * v);
}

public sealed partial class DesignService
{
    public const string GeneralCoatingProtocol = "general-constraint-coating/4";
    public const string PassbandConstraintProtocol = "measured-passband-constraints/1";
    // Strict interior guidance at the square-root floating-point precision scale; formal limits stay exact.
    private static readonly double GeneralConstraintInteriorGuard = Math.Sqrt(Math.BitIncrement(1.0) - 1.0);
    private sealed class GeneralCounter
    {
        internal long Spectrum, Sensitivity, Jacobians, BandMetrics, BandSensitivities, ProtectedRejections;
        internal int Passes, Refinements, Dense, QualityPasses, QualityIterations, ProtectionPasses, DenseRejections, QualityRestarts, ProtectionRestarts; internal bool Limit;
        internal int InequalitySteps, InequalityIterations, InequalityLimits;
        internal int RepairIterations, RepairAccepted, ComparisonRuns, ComparisonIterations; internal long TangentTrials;
        internal int ContinuationIterations, ParetoIterations, ParetoAccepted;
        internal int ReflectancePreconditionerIterations;
        internal int MetricIterations;
        internal int PeakReferenceBatches;
        internal int PassbandQualityIterations, PassbandQualityAccepted;
        internal void Add(GeneralCoatingWork? w)
        {
            if (w is null) return; Spectrum += w.SpectrumEvaluations; Sensitivity += w.SensitivityEvaluations; Jacobians += w.AnalyticJacobians;
            Passes += w.ConstraintPasses; Refinements += w.AdaptiveRefinements; Dense += w.DenseValidations; Limit |= w.AdaptiveLimitReached;
            QualityPasses += w.QualityPasses; QualityIterations += w.QualityIterations;
            BandMetrics += w.BandMetricEvaluations; BandSensitivities += w.BandSensitivityBatches;
            ProtectionPasses += w.TargetProtectionPasses; ProtectedRejections += w.ProtectedTrialRejections;
            DenseRejections += w.DenseProtectionRejections; QualityRestarts += w.QualityRestarts;
            ProtectionRestarts += w.TargetDirectionRestarts;
            InequalitySteps += w.InequalitySubproblems; InequalityIterations += w.InequalityActiveSetIterations;
            InequalityLimits += w.InequalitySubproblemLimits;
            RepairIterations += w.TargetRepairIterations; RepairAccepted += w.TargetRepairAcceptedCandidates;
            TangentTrials += w.TangentTrialEvaluations;
            ComparisonRuns += w.TargetComparisonRuns; ComparisonIterations += w.TargetComparisonIterations;
            ContinuationIterations += w.TargetRepairContinuationIterations; ParetoIterations += w.TargetParetoPolishIterations; ParetoAccepted += w.TargetParetoAcceptedCandidates;
            ReflectancePreconditionerIterations += w.ReflectancePreconditionerIterations;
            MetricIterations += w.ReflectanceMetricIterations;
            PeakReferenceBatches += w.SearchPeakReferenceBatches;
            PassbandQualityIterations += w.PassbandQualityIterations; PassbandQualityAccepted += w.PassbandQualityAcceptedCandidates;
        }
        internal GeneralCoatingWork Snapshot() => new(Spectrum, Sensitivity, Jacobians, Passes, Refinements, Limit)
        {
            DenseValidations = Dense,
            QualityPasses = QualityPasses,
            QualityIterations = QualityIterations,
            QualityProtocol = QualityPasses > 0 ? ReflectanceQualityProtocol : null,
            BandMetricEvaluations = BandMetrics,
            BandSensitivityBatches = BandSensitivities,
            PassbandProtocol = BandMetrics > 0 ? PassbandConstraintProtocol : null,
            TargetProtectionProtocol = ProtectionPasses > 0 ? DesignService.TargetProtectionProtocol : null,
            TargetProtectionPasses = ProtectionPasses,
            ProtectedTrialRejections = ProtectedRejections,
            DenseProtectionRejections = DenseRejections,
            QualityRestarts = QualityRestarts,
            TargetDirectionRestarts = ProtectionRestarts,
            InequalitySubproblems = InequalitySteps, InequalityActiveSetIterations = InequalityIterations, InequalitySubproblemLimits = InequalityLimits,
            TargetRepairIterations = RepairIterations, TargetRepairAcceptedCandidates = RepairAccepted,
            TangentTrialEvaluations = TangentTrials,
            InequalityStepProtocol = InequalitySteps > 0 ? InequalityLeastSquaresStep.Version : null,
            TargetRepairProtocol = RepairIterations > 0 ? DesignService.TargetRepairProtocol : null,
            TargetComparisonRuns = ComparisonRuns, TargetComparisonIterations = ComparisonIterations,
            TargetRepairContinuationIterations = ContinuationIterations, TargetParetoPolishIterations = ParetoIterations, TargetParetoAcceptedCandidates = ParetoAccepted,
            ReflectancePreconditionerIterations = ReflectancePreconditionerIterations, ReflectanceMetricIterations = MetricIterations,
            SearchPeakReferenceBatches = PeakReferenceBatches, SearchSamplingProtocol = PeakReferenceBatches > 0 ? DesignService.SearchSamplingProtocol : null,
            PassbandQualityIterations = PassbandQualityIterations, PassbandQualityAcceptedCandidates = PassbandQualityAccepted,
            PassbandQualityProtocol = PassbandQualityIterations > 0 ? DesignService.PassbandQualityProtocol : null
        };
    }
    private static GeneralCoatingWork SumGeneralWork(IEnumerable<RunRecord> records)
    { var work = new GeneralCounter(); foreach (var record in records) work.Add(record.GeneralWork); return work.Snapshot(); }

    private static double GeneralReflectanceRank(Experiment d, IReadOnlyDictionary<string, IMaterial> materials,
        FilmLayer[] layers, double[] grid, GeneralCounter work)
    {
        var samples = GeneralConstraintSystem(d with { Layers = layers }, Solver(d, materials, layers), grid, false, work).Samples;
        // Once violations are zero, retain the best actual reflectance instead of an arbitrary layer-count tie.
        return d.Target.Kind == DesignKind.Antireflection ? samples.Max(s => s.Power.Reflectance) : -samples.Min(s => s.Power.Reflectance);
    }
    private readonly record struct GeneralConstraintKey(int Group, double Wave);
    private sealed record GeneralSystem(double[] Values, double[,]? Derivatives, ThinFilmSample[] Samples, GeneralConstraintKey[] Keys);

    private static double[] GeneralGrid(Experiment d, IEnumerable<double> initial)
    {
        var t = d.Target;
        if (t.Kind != DesignKind.NarrowBand) return initial.Append(t.CenterNm).Distinct().Order().ToArray();
        var anchors = new[] { t.CenterNm, t.CenterNm - t.CenterToleranceNm, t.CenterNm + t.CenterToleranceNm,
            t.CenterNm - (t.FwhmNm + t.FwhmToleranceNm) / 2, t.CenterNm + (t.FwhmNm + t.FwhmToleranceNm) / 2,
            t.CenterNm - Math.Max(0, t.FwhmNm - t.FwhmToleranceNm) / 2, t.CenterNm + Math.Max(0, t.FwhmNm - t.FwhmToleranceNm) / 2 };
        var declared = anchors.Select(w => Math.Clamp(w, t.MinimumNm, t.MaximumNm))
            .Concat(t.StopBands!.SelectMany(b => new[] { b.MinimumNm, b.MaximumNm })).Append(t.MinimumNm).Append(t.MaximumNm).ToHashSet();
        var ordered = initial.Concat(declared)
            .Concat(ThinFilmSpectrum.Grid(Math.Max(t.MinimumNm, t.CenterNm - 2 * t.FwhmNm), Math.Min(t.MaximumNm, t.CenterNm + 2 * t.FwhmNm), 256))
            .Concat(t.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 8))).Distinct().Order().ToArray();
        var merged = new List<double>();
        foreach (var wave in ordered)
        {
            // Four arithmetic operations generate a uniform grid node. Coalesce roundoff duplicates,
            // retaining exact declared anchors and never merging two distinct requested anchors.
            var roundoff = merged.Count == 0 ? 0 : Math.Min(4 * (Math.BitIncrement(1.0) - 1) * Math.Max(wave, merged[^1]),
                Math.Min(t.CenterToleranceNm, t.FwhmToleranceNm) * GeneralConstraintInteriorGuard);
            if (merged.Count == 0 || wave - merged[^1] > roundoff || declared.Contains(wave) && declared.Contains(merged[^1])) merged.Add(wave);
            else if (declared.Contains(wave)) merged[^1] = wave;
        }
        return merged.ToArray();
    }

    private static GeneralSystem GeneralConstraintSystem(Experiment d, CoherentThinFilmSolver solver, double[] grid,
        bool derivatives, GeneralCounter? work = null)
    {
        var t = d.Target; var points = new ThinFilmSample[grid.Length];
        var sensitivity = derivatives ? new ThinFilmThicknessSensitivity[grid.Length] : null;
        for (var i = 0; i < grid.Length; i++)
        {
            if (derivatives)
            { sensitivity![i] = solver.EvaluateThicknessSensitivity(grid[i], t.AngleDegrees, t.Polarization); points[i] = new(grid[i], sensitivity[i].Power); if (work is not null) work.Sensitivity++; }
            else { points[i] = new(grid[i], solver.Evaluate(grid[i], t.AngleDegrees, t.Polarization)); if (work is not null) work.Spectrum++; }
        }
        if (t.Kind != DesignKind.NarrowBand)
        {
            var sign = t.Kind == DesignKind.Antireflection ? 1.0 : -1.0;
            var scale = (t.Kind == DesignKind.Antireflection ? t.Reflectance : 1 - t.Reflectance);
            var interior = scale == 0 ? 0 : GeneralConstraintInteriorGuard / Math.Sqrt(grid.Length);
            if (scale == 0) scale = 1;
            scale *= Math.Sqrt(grid.Length);
            var rValues = points.Select(s => sign * (s.Power.Reflectance - t.Reflectance) / scale
                + interior).ToArray();
            double[,]? rJac = derivatives ? new double[grid.Length, d.Layers.Length] : null;
            if (derivatives) for (var i = 0; i < grid.Length; i++) for (var j = 0; j < d.Layers.Length; j++)
                rJac![i, j] = sign * sensitivity![i].Layers[j].Reflectance / scale;
            return new(rValues, rJac, points, grid.Select(w => new GeneralConstraintKey(-1, w)).ToArray());
        }
        int At(double w) => Array.BinarySearch(grid, Math.Clamp(w, t.MinimumNm, t.MaximumNm)) is var index && index >= 0
            ? index : throw new InvalidOperationException("约束光谱缺少目标锚点。");
        var centerIndex = Enumerable.Range(0, grid.Length).Where(i => Math.Abs(grid[i] - t.CenterNm) <= 2 * t.FwhmNm)
            .MaxBy(i => points[i].Power.LogTransmittance);
        var center = points[centerIndex].Power.Transmittance;
        var values = new List<double>(); var rows = new List<double[]>(); var keys = new List<GeneralConstraintKey>();
        void Row(GeneralConstraintKey key, double value, params (int Point, double Factor, bool Log)[] terms)
        {
            values.Add(value + (key.Group == 0 && t.PeakTransmittance == 1 ? 0 : GeneralConstraintInteriorGuard)); keys.Add(key);
            if (!derivatives) return;
            var row = new double[d.Layers.Length];
            foreach (var term in terms)
                for (var j = 0; j < row.Length; j++) row[j] += term.Factor * (term.Log
                    ? sensitivity![term.Point].Layers[j].LogTransmittance : sensitivity![term.Point].Layers[j].Transmittance);
            rows.Add(row);
        }
        void Inactive(GeneralConstraintKey key)
        { values.Add(double.NegativeInfinity); keys.Add(key); if (derivatives) rows.Add(new double[d.Layers.Length]); }
        var peakScale = t.PeakTransmittance;
        Row(new(0, t.CenterNm), (peakScale - center) / peakScale, (centerIndex, -1 / peakScale, false));
        var bandIndices = Enumerable.Range(0, grid.Length).Where(i => Math.Abs(grid[i] - t.CenterNm) <= 2 * t.FwhmNm).ToArray();
        var bandSensitivity = derivatives ? ThinFilmSpectrum.MeasureThicknessSensitivity(bandIndices.Select(i => grid[i]).ToArray(),
            bandIndices.Select(i => sensitivity![i]).ToArray()) : null;
        var bandMetrics = bandSensitivity?.Metrics ?? ThinFilmSpectrum.Measure(bandIndices.Select(i => points[i]).ToArray());
        if (work is not null) { work.BandMetrics++; if (derivatives) work.BandSensitivities++; }
        var peakEstimate = derivatives ? bandSensitivity!.PeakWavelengthEstimateNanometers
            : ThinFilmSpectrum.EstimatePeakWavelength(bandIndices.Select(i => points[i]).ToArray());
        if (peakEstimate is { } location && (!derivatives || bandSensitivity!.PeakWavelengthNanometers is not null))
            foreach (var sign in new[] { -1, 1 })
            {
                values.Add((sign * (location - t.CenterNm) - t.CenterToleranceNm) / t.FwhmNm + GeneralConstraintInteriorGuard);
                keys.Add(new(sign < 0 ? -4 : -5, t.CenterNm));
                if (derivatives) rows.Add(bandSensitivity!.PeakWavelengthNanometers!.Select(v => sign * v / t.FwhmNm).ToArray());
            }
        else { Inactive(new(-4, t.CenterNm)); Inactive(new(-5, t.CenterNm)); }
        if (bandMetrics.FwhmNanometers is { } width && (!derivatives || bandSensitivity!.FwhmNanometers is not null))
        {
            // Differentiate the same sampled, log-scaled half-height crossings used by formal metrics.
            // Bracket/peak switches are piecewise smooth; the guide is re-evaluated at every trial.
            foreach (var sign in new[] { -1, 1 })
            {
                values.Add((sign * (width - t.FwhmNm) - t.FwhmToleranceNm) / t.FwhmNm + GeneralConstraintInteriorGuard);
                keys.Add(new(sign < 0 ? -2 : -3, t.CenterNm));
                if (derivatives) rows.Add(bandSensitivity!.FwhmNanometers!.Select(v => sign * v / t.FwhmNm).ToArray());
            }
        }
        else { Inactive(new(-2, t.CenterNm)); Inactive(new(-3, t.CenterNm)); }
        var needsShapeGuide = bandMetrics.FwhmNanometers is not { } measuredWidth || Math.Abs(measuredWidth - t.FwhmNm) > t.FwhmToleranceNm
            || peakEstimate is not { } measuredCenter || Math.Abs(measuredCenter - t.CenterNm) > t.CenterToleranceNm;
        foreach (var side in new[] { -1, 1 })
        {
            // Target-edge guidance supplies a passband shape direction alongside the measured metrics.
            var inner = At(t.CenterNm + side * Math.Max(0, t.FwhmNm - t.FwhmToleranceNm) / 2);
            var outer = At(t.CenterNm + side * (t.FwhmNm + t.FwhmToleranceNm) / 2);
            if (needsShapeGuide)
            {
                Row(new(side < 0 ? 1 : 2, grid[inner]), (center / 2 - points[inner].Power.Transmittance) / peakScale, (centerIndex, .5 / peakScale, false), (inner, -1 / peakScale, false));
                Row(new(side < 0 ? 3 : 4, grid[outer]), (points[outer].Power.Transmittance - center / 2) / peakScale, (outer, 1 / peakScale, false), (centerIndex, -.5 / peakScale, false));
            }
            else { Inactive(new(side < 0 ? 1 : 2, grid[inner])); Inactive(new(side < 0 ? 3 : 4, grid[outer])); }
        }
        var envelope = Enumerable.Range(0, points.Length).Where(i => Math.Abs(grid[i] - t.CenterNm) > t.CenterToleranceNm
            && Math.Abs(grid[i] - t.CenterNm) <= 2 * t.FwhmNm).ToArray();
        if (peakEstimate is null && envelope.Length > 0)
        {
            var i = envelope.MaxBy(i => points[i].Power.LogTransmittance);
            var anchor = At(t.CenterNm);
            Row(new(5, t.CenterNm), (points[i].Power.Transmittance - points[anchor].Power.Transmittance) / peakScale, (i, 1 / peakScale, false), (anchor, -1 / peakScale, false));
        }
        else Inactive(new(5, t.CenterNm));
        for (var bandIndex = 0; bandIndex < t.StopBands!.Length; bandIndex++)
        {
            var band = t.StopBands[bandIndex];
            var indices = Enumerable.Range(0, points.Length).Where(i => grid[i] >= band.MinimumNm && grid[i] <= band.MaximumNm).ToArray();
            var scale = Math.Max(1, t.BlockingOd);
            var i = indices.MaxBy(i => points[i].Power.LogTransmittance);
            Row(new(6 + bandIndex, band.MinimumNm), (t.BlockingOd + points[i].Power.LogTransmittance / Math.Log(10)) / scale,
                (i, sensitivity is not null && !sensitivity[i].HasLogTransmissionDerivative ? 0 : 1 / (Math.Log(10) * scale), true));
        }
        double[,]? jacobian = null;
        if (derivatives)
        { jacobian = new double[rows.Count, d.Layers.Length]; for (var i = 0; i < rows.Count; i++) for (var j = 0; j < d.Layers.Length; j++) jacobian[i, j] = rows[i][j]; }
        return new(values.ToArray(), jacobian, points, keys.ToArray());
    }

    private static CoatingConstraintAssessment AssessGeneral(Experiment d, ThinFilmSample[] samples)
    {
        var t = d.Target; var m = Metrics(d, samples);
        if (t.Kind != DesignKind.NarrowBand)
        {
            var ar = t.Kind == DesignKind.Antireflection; var scale = ar ? t.Reflectance : 1 - t.Reflectance;
            if (scale == 0) scale = 1;
            return new([Math.Max(0, ar ? m.MaximumReflectance - t.Reflectance : t.Reflectance - m.MinimumReflectance) / scale])
            { PeakTransmittance = ar ? 1 - m.MaximumReflectance : m.MinimumReflectance };
        }
        var violations = new List<double>
        {
            Math.Max(0, t.PeakTransmittance - m.PeakTransmittance) / t.PeakTransmittance,
            m.FwhmNanometers is { } w ? Math.Max(0, Math.Abs(w - t.FwhmNm) - t.FwhmToleranceNm) / t.FwhmNm : 1,
            Math.Max(0, Math.Abs(m.PeakWavelengthNanometers - t.CenterNm) - t.CenterToleranceNm) / t.FwhmNm
        };
        violations.AddRange(t.StopBands!.Select(b => Math.Max(0, t.BlockingOd - MinimumOd(samples, b)) / Math.Max(1, t.BlockingOd)));
        return new(violations.ToArray()) { PeakTransmittance = m.PeakTransmittance };
    }

    private static bool PreferGeneral(CoatingConstraintAssessment a, CoatingConstraintAssessment b) =>
        a.CoarselyFeasible != b.CoarselyFeasible ? a.CoarselyFeasible
            : a.MaximumViolation < b.MaximumViolation || a.MaximumViolation == b.MaximumViolation
                && (a.Merit < b.Merit || a.Merit == b.Merit && a.PeakTransmittance > b.PeakTransmittance);

    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineGeneralThickness(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations,
        IProgress<DesignProgress>? progress, CancellationToken token, Experiment? validatedStart = null)
    {
        var work = new GeneralCounter(); var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var grid = GeneralGrid(d, initialGrid); var referenceGrid = grid;
        var original = GeneralConstraintSystem(d, Solver(d, materials), referenceGrid, false, work);
        var bestAssessment = AssessGeneral(d, original.Samples); var best = d.Layers;
        var startingAssessment = bestAssessment;
        if (indices.Length == 0 || d.Settings.MinimumThicknessNm == d.Settings.MaximumThicknessNm) return (best, bestAssessment.Merit,
            new("无变量", GeneralCoatingProtocol, "全部膜层固定", 1, 0, null) { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() });
        // If peak T or a stop band is unfinished, protect its starting violation during the solve.
        // With those quality targets already met, retain the established target-restoration direction
        // for width/centering and apply the same componentwise envelope to every retained candidate.
        var qualityState = validatedStart is null ? bestAssessment : AssessGeneral(validatedStart, validatedStart.Result!.Spectrum);
        if (d.Target.Kind == DesignKind.NarrowBand && (qualityState.Violations[0] > 0 || qualityState.Violations.Skip(3).Any(v => v > 0)))
        {
            if (d.Settings.Optimizer == "Damped Least Squares") return RefineFeasibleGeneralTargets(d, materials, grid, maximumIterations, work, validatedStart, progress, token);
            return RefineProtectedGeneralThickness(d, materials, grid, maximumIterations, work, validatedStart, progress, token);
        }
        if (d.Target.Kind != DesignKind.NarrowBand && bestAssessment.CoarselyFeasible)
            return RefineGeneralReflectanceQuality(d, materials, grid, maximumIterations, work, progress, token, validatedStart is not null);
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId],
            d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, k) => d.Layers[i].ThicknessNm / units[k]).ToArray();
        FilmLayer[] Layers()
        {
            var layers = d.Layers.ToArray(); for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            { ThicknessNm = Math.Clamp(phase[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) }; return layers;
        }
        long evaluations = 0; var iterations = 0; var remaining = maximumIterations; var penalty = 1.0; double[]? multipliers = null;
        var previousViolation = double.PositiveInfinity; var reason = "MaximumIterations";
        var multiplierMap = new Dictionary<GeneralConstraintKey, double>();
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested(); work.Passes++;
            var passStartAssessment = bestAssessment;
            if (d.Target.Kind == DesignKind.NarrowBand) work.ProtectionPasses++;
            var problem = new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
            for (var j = 0; j < phase.Length; j++)
            {
                var k = j; problem.AddVariable(new DelegateVariable("光学相位变量 " + (j + 1), () => phase[k], v => phase[k] = v,
                d.Settings.MinimumThicknessNm / units[k], d.Settings.MaximumThicknessNm / units[k], stepHint: 1));
            }
            GeneralSystem? cached = null; double[]? cachedPhase = null;
            var analytic = d.Settings.Optimizer == "Damped Least Squares";
            GeneralSystem Current()
            {
                token.ThrowIfCancellationRequested();
                if (cachedPhase is not null && cachedPhase.SequenceEqual(phase)) return cached!;
                var current = d with { Layers = Layers() };
                cached = GeneralConstraintSystem(current, Solver(current, materials), grid, analytic, work); cachedPhase = phase.ToArray();
                // The AL merit and the formal feasibility rank are different. Retain physical improvements
                // seen inside a pass as well as its final iterate, using one fixed ranking grid.
                var ranking = ReferenceEquals(grid, referenceGrid) ? cached
                    : GeneralConstraintSystem(current, Solver(current, materials), referenceGrid, false, work);
                var assessment = AssessGeneral(current, ranking.Samples);
                if (WithinGeneralTargetEnvelope(assessment, startingAssessment) && PreferGeneral(assessment, bestAssessment))
                { best = current.Layers; bestAssessment = assessment; }
                else if (!WithinGeneralTargetEnvelope(assessment, startingAssessment)) work.ProtectedRejections++;
                return cached;
            }
            var raw = Current(); multipliers = raw.Keys.Select(key => multiplierMap.GetValueOrDefault(key)).ToArray();
            double Value(int i)
            { var g = Current().Values[i]; return double.IsNegativeInfinity(g) ? 0 : Math.Sqrt(penalty) * Math.Max(0, g + multipliers[i] / penalty); }
            for (var i = 0; i < raw.Values.Length; i++) { var k = i; problem.AddOperand(new Operand("通用约束 " + i, 0, 1, () => Value(k))); }
            if (analytic) problem.SetValueJacobianEvaluator(vector =>
            {
                // DLS requests scaled coordinates; these variables already are dimensionless optical phases.
                var saved = phase.ToArray(); Array.Copy(vector.ToArray(), phase, phase.Length);
                try
                {
                    var system = Current(); var matrix = new double[system.Values.Length, phase.Length];
                    for (var i = 0; i < system.Values.Length; i++)
                        if (!double.IsNegativeInfinity(system.Values[i]) && system.Values[i] + multipliers[i] / penalty > 0)
                            for (var j = 0; j < phase.Length; j++) matrix[i, j] = Math.Sqrt(penalty) * system.Derivatives![i, indices[j]] * units[j];
                    return matrix;
                }
                finally { Array.Copy(saved, phase, phase.Length); }
            });
            var allowance = Math.Min(remaining, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations))));
            if (d.Target.Kind != DesignKind.NarrowBand && analytic)
                allowance = Math.Min(allowance, Math.Max(1, maximumIterations / 2 - iterations));
            var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, allowance);
            evaluations += report.FunctionEvaluations; iterations += report.Iterations; remaining -= Math.Max(1, report.Iterations);
            if (d.Target.Kind != DesignKind.NarrowBand) work.ReflectancePreconditionerIterations += report.Iterations;
            work.Jacobians += problem.AnalyticJacobianEvaluations; reason = report.StopReason;
            var result = Layers(); var assessmentSystem = GeneralConstraintSystem(d with { Layers = result }, Solver(d, materials, result), referenceGrid, false, work);
            var assessment = AssessGeneral(d, assessmentSystem.Samples);
            if (WithinGeneralTargetEnvelope(assessment, startingAssessment) && PreferGeneral(assessment, bestAssessment)) { best = result; bestAssessment = assessment; }
            progress?.Report(new("通用约束膜厚优化", evaluations, assessment.Merit));
            // Keep the target-based feasibility step as a preconditioner (also for short screening).
            // Remaining work minimizes the actual worst reflectance even if feasibility was not reached.
            if (d.Target.Kind != DesignKind.NarrowBand && remaining > 0)
            {
                // Close-to-feasible starts benefit from spending the remaining allowance
                // directly on the worst loss. Only a deficit larger than its declared
                // loss scale warrants extended target fitting, and each pass must halve it.
                var preconditionerContracting = analytic && startingAssessment.MaximumViolation > 1
                    && !bestAssessment.CoarselyFeasible && iterations < maximumIterations / 2
                    && bestAssessment.MaximumViolation <= passStartAssessment.MaximumViolation / 2;
                if (!preconditionerContracting)
                {
                    var polished = RefineGeneralReflectanceQuality(d with { Layers = best }, materials, grid, remaining, work, progress, token, validatedStart is not null);
                    return (polished.Layers, polished.Merit, polished.Run with
                    { Evaluations = evaluations + polished.Run.Evaluations, Iterations = iterations + polished.Run.Iterations });
                }
            }
            var currentSystem = Current(); var violation = currentSystem.Values.Max(v => Math.Max(0, v));
            for (var i = 0; i < multipliers.Length; i++) multipliers[i] = double.IsNegativeInfinity(currentSystem.Values[i]) ? 0
                : Math.Min(1e12, Math.Max(0, multipliers[i] + penalty * currentSystem.Values[i]));
            for (var i = 0; i < multipliers.Length; i++) multiplierMap[currentSystem.Keys[i]] = multipliers[i];
            if (violation == 0 && assessment.CoarselyFeasible) { reason = "ConstraintGuidanceSatisfied"; break; }
            if (violation > previousViolation / 2) penalty = Math.Min(1e12, penalty * 10);
            previousViolation = violation;
            if (remaining > 0)
            {
                var tolerance = d.Target.Kind == DesignKind.NarrowBand
                    ? Math.Min(d.Target.CenterToleranceNm / d.Target.FwhmNm, d.Target.FwhmToleranceNm / d.Target.FwhmNm) / 16
                    : Math.Max(double.Epsilon, (d.Target.Kind == DesignKind.Antireflection ? d.Target.Reflectance : 1 - d.Target.Reflectance) / 16);
                var refined = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, result), grid, d.Target.AngleDegrees, d.Target.Polarization,
                    Math.Max(double.Epsilon, tolerance), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
                work.Spectrum += refined.Evaluations; work.Refinements++; work.Limit |= refined.LimitReached;
                grid = GeneralGrid(d, refined.Samples.Select(s => s.WavelengthNanometers));
            }
        }
        return (best, bestAssessment.Merit,
            new(d.Settings.Optimizer, GeneralCoatingProtocol, reason, evaluations, iterations, d.Settings.RandomSeed)
            {
                DesignProtocol = GeneralCoatingProtocol,
                GeneralWork = work.Snapshot(),
                TargetViolationCeilings = d.Target.Kind != DesignKind.NarrowBand ? null
                    : validatedStart is null ? startingAssessment.Violations : AssessGeneral(validatedStart, validatedStart.Result!.Spectrum).Violations,
                TargetEnvelopeSampling = d.Target.Kind != DesignKind.NarrowBand ? null : validatedStart is null ? "guidance-start" : "dense-start",
                TargetProtectionPhase = d.Target.Kind == DesignKind.NarrowBand ? "shape-restoration" : null
            });
    }
}
