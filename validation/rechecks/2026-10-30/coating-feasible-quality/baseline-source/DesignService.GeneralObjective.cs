using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record GeneralCoatingWork(long SpectrumEvaluations, long SensitivityEvaluations, long AnalyticJacobians,
    int ConstraintPasses, int AdaptiveRefinements, bool AdaptiveLimitReached)
{
    public int DenseValidations { get; init; }
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
    // Strict interior guidance at the square-root floating-point precision scale; formal limits stay exact.
    private static readonly double GeneralConstraintInteriorGuard = Math.Sqrt(Math.BitIncrement(1.0) - 1.0);
    private sealed class GeneralCounter
    {
        internal long Spectrum, Sensitivity, Jacobians; internal int Passes, Refinements, Dense; internal bool Limit;
        internal void Add(GeneralCoatingWork? w)
        {
            if (w is null) return; Spectrum += w.SpectrumEvaluations; Sensitivity += w.SensitivityEvaluations; Jacobians += w.AnalyticJacobians;
            Passes += w.ConstraintPasses; Refinements += w.AdaptiveRefinements; Dense += w.DenseValidations; Limit |= w.AdaptiveLimitReached;
        }
        internal GeneralCoatingWork Snapshot() => new(Spectrum, Sensitivity, Jacobians, Passes, Refinements, Limit) { DenseValidations = Dense };
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
        return initial.Concat(anchors.Select(w => Math.Clamp(w, t.MinimumNm, t.MaximumNm)))
            .Concat(t.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 8))).Distinct().Order().ToArray();
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
        var centerIndex = At(t.CenterNm); var center = points[centerIndex].Power.Transmittance;
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
        var peakScale = t.PeakTransmittance;
        Row(new(0, t.CenterNm), (peakScale - center) / peakScale, (centerIndex, -1 / peakScale, false));
        foreach (var side in new[] { -1, 1 })
        {
            var inner = At(t.CenterNm + side * Math.Max(0, t.FwhmNm - t.FwhmToleranceNm) / 2);
            var outer = At(t.CenterNm + side * (t.FwhmNm + t.FwhmToleranceNm) / 2);
            // Both allowed edges are signed inequalities; no measured grid-peak/FWHM derivative is taken.
            Row(new(side < 0 ? 1 : 2, grid[inner]), (center / 2 - points[inner].Power.Transmittance) / peakScale, (centerIndex, .5 / peakScale, false), (inner, -1 / peakScale, false));
            Row(new(side < 0 ? 3 : 4, grid[outer]), (points[outer].Power.Transmittance - center / 2) / peakScale, (outer, 1 / peakScale, false), (centerIndex, -.5 / peakScale, false));
        }
        var envelope = Enumerable.Range(0, points.Length).Where(i => Math.Abs(grid[i] - t.CenterNm) > t.CenterToleranceNm
            && Math.Abs(grid[i] - t.CenterNm) <= 2 * t.FwhmNm).ToArray();
        var envelopeScale = peakScale * Math.Sqrt(Math.Max(1, envelope.Length));
        foreach (var i in envelope)
            Row(new(5, grid[i]), (points[i].Power.Transmittance - center) / envelopeScale, (i, 1 / envelopeScale, false), (centerIndex, -1 / envelopeScale, false));
        for (var bandIndex = 0; bandIndex < t.StopBands!.Length; bandIndex++)
        {
            var band = t.StopBands[bandIndex];
            var indices = Enumerable.Range(0, points.Length).Where(i => grid[i] >= band.MinimumNm && grid[i] <= band.MaximumNm).ToArray();
            var scale = Math.Max(1, t.BlockingOd) * Math.Sqrt(indices.Length);
            foreach (var i in indices) Row(new(6 + bandIndex, grid[i]), (t.BlockingOd + points[i].Power.LogTransmittance / Math.Log(10)) / scale,
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
        IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var work = new GeneralCounter(); var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var grid = GeneralGrid(d, initialGrid); var referenceGrid = grid;
        var original = GeneralConstraintSystem(d, Solver(d, materials), referenceGrid, false, work);
        var bestAssessment = AssessGeneral(d, original.Samples); var best = d.Layers;
        if (indices.Length == 0 || d.Settings.MinimumThicknessNm == d.Settings.MaximumThicknessNm) return (best, bestAssessment.Merit,
            new("无变量", GeneralCoatingProtocol, "全部膜层固定", 1, 0, null) { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() });
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
                cached = GeneralConstraintSystem(current, Solver(current, materials), grid, analytic, work); cachedPhase = phase.ToArray(); return cached;
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
            var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, allowance);
            evaluations += report.FunctionEvaluations; iterations += report.Iterations; remaining -= Math.Max(1, report.Iterations);
            work.Jacobians += problem.AnalyticJacobianEvaluations; reason = report.StopReason;
            var result = Layers(); var assessmentSystem = GeneralConstraintSystem(d with { Layers = result }, Solver(d, materials, result), referenceGrid, false, work);
            var assessment = AssessGeneral(d, assessmentSystem.Samples);
            if (PreferGeneral(assessment, bestAssessment)) { best = result; bestAssessment = assessment; }
            progress?.Report(new("通用约束膜厚优化", evaluations, assessment.Merit));
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
            { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() });
    }
}
