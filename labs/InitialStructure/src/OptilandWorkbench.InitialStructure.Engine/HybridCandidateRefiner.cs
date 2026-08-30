using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal sealed record CandidateRefinementResult(
    IReadOnlyList<CandidateSnapshot> Candidates,
    IReadOnlyList<SearchDiagnostic> Diagnostics,
    int EvaluationCount);

internal sealed class HybridCandidateRefiner
{
    private const int MinimumFamilyBudget = 4;
    private const double DifferentialWeight = 0.7;
    private const double CrossoverProbability = 0.8;

    public CandidateRefinementResult Refine(
        InitialStructureSpecification specification,
        IReadOnlyList<CandidateSnapshot> seeds,
        int evaluationBudget,
        IProgress<SearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (evaluationBudget < MinimumFamilyBudget || seeds.Count == 0)
        {
            return new CandidateRefinementResult([], [], 0);
        }

        var families = seeds
            .GroupBy(candidate => (
                candidate.Lineage.ElementCount,
                candidate.Lineage.StopVariant))
            .Select(group => group
                .OrderBy(candidate => Score(
                    specification,
                    candidate.Evaluation,
                    candidate.Violations))
                .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => candidate.Lineage.ElementCount)
            .ThenBy(candidate => candidate.Lineage.StopVariant)
            .ToArray();
        var selectedFamilyCount = Math.Min(
            families.Length,
            Math.Max(1, evaluationBudget / MinimumFamilyBudget));
        var candidates = new List<CandidateSnapshot>(selectedFamilyCount);
        var diagnostics = new List<SearchDiagnostic>();
        var consumed = 0;

        for (var familyIndex = 0; familyIndex < selectedFamilyCount; familyIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var familiesRemaining = selectedFamilyCount - familyIndex;
            var familyBudget = (evaluationBudget - consumed) / familiesRemaining;
            var family = RefineFamily(
                specification,
                families[familyIndex],
                familyBudget,
                cancellationToken);
            consumed += family.EvaluationCount;
            if (family.Candidate is not null
                && candidates.All(candidate =>
                    !StringComparer.Ordinal.Equals(
                        candidate.OpticFingerprint,
                        family.Candidate.OpticFingerprint)))
            {
                candidates.Add(family.Candidate);
                progress?.Report(new SearchProgress(
                    consumed,
                    evaluationBudget,
                    "Dense validation",
                    family.Candidate.CandidateId));
            }

            if (family.Diagnostic is not null)
            {
                diagnostics.Add(family.Diagnostic);
            }
        }

        return new CandidateRefinementResult(candidates, diagnostics, consumed);
    }

    private static FamilyRefinementResult RefineFamily(
        InitialStructureSpecification specification,
        CandidateSnapshot parent,
        int evaluationBudget,
        CancellationToken cancellationToken)
    {
        var parameterization = new CandidateParameterization(specification, parent);
        var dimension = parameterization.Dimension;
        var requestedPopulationSize = Math.Clamp(dimension + 2, 4, 12);
        var populationSize = Math.Min(requestedPopulationSize, evaluationBudget);
        if (populationSize < 4)
        {
            return new FamilyRefinementResult(null, null, 0);
        }

        var random = new DeterministicRandom(
            specification.Budget.RandomSeed
            + (parent.Lineage.ElementCount * 1_000_003L)
            + (parent.Lineage.StopVariant * 97L)
            + parent.Lineage.SeedIndex);
        var parentVector = parameterization.ReadParentVector();
        var population = new double[populationSize][];
        var scores = new double[populationSize];
        population[0] = parentVector;
        scores[0] = Score(specification, parent.Evaluation, parent.Violations);
        var evaluations = 0;

        for (var index = 1; index < populationSize && CanSpendSearch(1); index++)
        {
            population[index] = Jitter(parentVector, ref random);
            scores[index] = Evaluate(
                parameterization,
                specification,
                population[index],
                rayDensity: 1,
                cancellationToken);
            evaluations++;
        }

        var initializedCount = population.Count(vector => vector is not null);
        if (initializedCount < 4)
        {
            return new FamilyRefinementResult(null, null, evaluations);
        }

        var usedDifferentialEvolution = false;
        while (CanSpendSearch(initializedCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextPopulation = population.Select(vector => (double[])vector.Clone()).ToArray();
            var nextScores = (double[])scores.Clone();
            for (var targetIndex = 0; targetIndex < initializedCount; targetIndex++)
            {
                var indices = SelectDistinctIndices(
                    initializedCount,
                    targetIndex,
                    ref random);
                var forcedDimension = random.NextInt32(dimension);
                var trial = new double[dimension];
                for (var parameterIndex = 0; parameterIndex < dimension; parameterIndex++)
                {
                    var useMutant = parameterIndex == forcedDimension
                        || random.NextUnitDouble() < CrossoverProbability;
                    trial[parameterIndex] = useMutant
                        ? CandidateParameterization.Clamp(
                            population[indices.A][parameterIndex]
                            + (DifferentialWeight
                                * (population[indices.B][parameterIndex]
                                    - population[indices.C][parameterIndex])))
                        : population[targetIndex][parameterIndex];
                }

                var trialScore = Evaluate(
                    parameterization,
                    specification,
                    trial,
                    rayDensity: 1,
                    cancellationToken);
                evaluations++;
                if (trialScore < scores[targetIndex])
                {
                    nextPopulation[targetIndex] = trial;
                    nextScores[targetIndex] = trialScore;
                }
            }

            population = nextPopulation;
            scores = nextScores;
            usedDifferentialEvolution = true;
        }

        var bestIndex = Array.IndexOf(scores, scores.Min());
        var bestVector = population[bestIndex];
        var bestScore = scores[bestIndex];
        var usedDampedLeastSquares = false;
        var damping = 0.05;
        while (CanSpendSearch(dimension + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseResidual = Math.Sqrt(Math.Max(0, bestScore));
            var jacobian = new double[dimension];
            const double finiteDifferenceStep = 0.01;
            for (var parameterIndex = 0; parameterIndex < dimension; parameterIndex++)
            {
                var perturbed = (double[])bestVector.Clone();
                var actualStep = Math.Min(
                    finiteDifferenceStep,
                    1 - perturbed[parameterIndex]);
                if (actualStep <= 1e-9)
                {
                    actualStep = -Math.Min(
                        finiteDifferenceStep,
                        perturbed[parameterIndex] + 1);
                }

                perturbed[parameterIndex] = CandidateParameterization.Clamp(
                    perturbed[parameterIndex] + actualStep);
                var perturbedScore = Evaluate(
                    parameterization,
                    specification,
                    perturbed,
                    rayDensity: 2,
                    cancellationToken);
                evaluations++;
                jacobian[parameterIndex] = actualStep == 0
                    ? 0
                    : (Math.Sqrt(Math.Max(0, perturbedScore)) - baseResidual) / actualStep;
            }

            var denominator = damping + jacobian.Sum(value => value * value);
            var trial = new double[dimension];
            for (var parameterIndex = 0; parameterIndex < dimension; parameterIndex++)
            {
                var step = denominator <= 0
                    ? 0
                    : -(baseResidual * jacobian[parameterIndex]) / denominator;
                trial[parameterIndex] = CandidateParameterization.Clamp(
                    bestVector[parameterIndex] + Math.Clamp(step, -0.25, 0.25));
            }

            var trialScore = Evaluate(
                parameterization,
                specification,
                trial,
                rayDensity: 2,
                cancellationToken);
            evaluations++;
            usedDampedLeastSquares = true;
            if (trialScore < bestScore)
            {
                bestVector = trial;
                bestScore = trialScore;
                damping = Math.Max(1e-6, damping / 2);
            }
            else
            {
                damping = Math.Min(1e6, damping * 4);
            }
        }

        if (evaluations + 1 > evaluationBudget)
        {
            return new FamilyRefinementResult(
                null,
                new SearchDiagnostic(
                    "refinement.validation-budget",
                    "The family budget was exhausted before independent dense validation.",
                    parent.CandidateId),
                evaluations);
        }

        var finalOptic = parameterization.CreateOptic(bestVector);
        var operation = (usedDifferentialEvolution, usedDampedLeastSquares) switch
        {
            (true, true) => "bounded-differential-evolution+damped-least-squares+dense-validation",
            (true, false) => "bounded-differential-evolution+dense-validation",
            _ => "bounded-population+dense-validation"
        };
        var lineage = parent.Lineage with
        {
            ParentCandidateId = parent.CandidateId,
            Operation = operation,
            Generation = parent.Lineage.Generation + 1
        };
        var candidate = FirstOrderSeedGenerator.CreateEvaluatedCandidate(
            specification,
            parent.FlatRootOptic,
            finalOptic,
            lineage,
            "flat-to-usable-hybrid/v1",
            rayDensity: 4,
            allowLabAccepted: true);
        evaluations++;
        return new FamilyRefinementResult(candidate, null, evaluations);

        bool CanSpendSearch(int count) => evaluations + count + 1 <= evaluationBudget;
    }

    private static double Evaluate(
        CandidateParameterization parameterization,
        InitialStructureSpecification specification,
        IReadOnlyList<double> vector,
        int rayDensity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var optic = parameterization.CreateOptic(vector);
            var evaluation = FirstOrderSeedGenerator.EvaluateOptic(
                optic,
                specification,
                rayDensity);
            var violations = FirstOrderSeedGenerator.EvaluateConstraints(
                optic,
                specification,
                evaluation);
            return Score(specification, evaluation, violations);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentException
            or ArithmeticException
            or KeyNotFoundException)
        {
            return 1e12;
        }
    }

    private static double Score(
        InitialStructureSpecification specification,
        EvaluationVector evaluation,
        IReadOnlyList<ConstraintViolation> violations)
    {
        static double RelativeSquared(double? actual, double target) =>
            actual is { } value && double.IsFinite(value)
                ? Math.Pow((value - target) / target, 2)
                : 100;
        static double LimitSquared(double? actual, double limit) =>
            actual is { } value && double.IsFinite(value)
                ? Math.Pow(value / limit, 2)
                : 100;

        var score = 8 * RelativeSquared(
            evaluation.EffectiveFocalLengthMillimeters,
            specification.EffectiveFocalLengthMillimeters);
        score += 4 * RelativeSquared(evaluation.FNumber, specification.FNumber);
        score += 20 * Math.Pow(1 - Math.Clamp(evaluation.ValidRayFraction, 0, 1), 2);
        score += LimitSquared(
            evaluation.RmsSpotRadiusMillimeters,
            specification.MaximumRmsSpotRadiusMillimeters);
        score += 0.25 * LimitSquared(
            evaluation.MaximumSpotRadiusMillimeters,
            specification.MaximumSpotRadiusMillimeters);
        score += 100 * violations.Count(item => item.Severity == ConstraintSeverity.Hard);
        score += 10 * violations.Count(item => item.Severity == ConstraintSeverity.Warning);
        return double.IsFinite(score) ? score : 1e12;
    }

    private static double[] Jitter(IReadOnlyList<double> parent, ref DeterministicRandom random)
    {
        var result = new double[parent.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var span = index < (parent.Count + 1) / 2 ? 0.4 : 0.7;
            result[index] = CandidateParameterization.Clamp(
                parent[index] + ((random.NextUnitDouble() - 0.5) * 2 * span));
        }

        return result;
    }

    private static (int A, int B, int C) SelectDistinctIndices(
        int count,
        int excluded,
        ref DeterministicRandom random)
    {
        var a = NextDistinctIndex(count, excluded, -1, -1, ref random);
        var b = NextDistinctIndex(count, excluded, a, -1, ref random);
        var c = NextDistinctIndex(count, excluded, a, b, ref random);
        return (a, b, c);
    }

    private static int NextDistinctIndex(
        int count,
        int excluded,
        int firstSelected,
        int secondSelected,
        ref DeterministicRandom random)
    {
        int value;
        do
        {
            value = random.NextInt32(count);
        }
        while (value == excluded || value == firstSelected || value == secondSelected);

        return value;
    }

    private sealed record FamilyRefinementResult(
        CandidateSnapshot? Candidate,
        SearchDiagnostic? Diagnostic,
        int EvaluationCount);
}
