using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed class FirstOrderSeedGenerator
{
    private readonly FlatRootFactory _flatRootFactory;

    public FirstOrderSeedGenerator(FlatRootFactory? flatRootFactory = null)
    {
        _flatRootFactory = flatRootFactory ?? new FlatRootFactory();
    }

    public CandidateSnapshot Create(
        InitialStructureSpecification specification,
        int elementCount,
        int seedIndex)
    {
        SpecificationValidator.Validate(specification);
        if (seedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seedIndex));
        }

        var stopVariant = seedIndex % 3;
        var root = _flatRootFactory.Create(specification, elementCount, stopVariant);
        var rootFingerprint = ContentFingerprint.Compute(root);
        var optic = Optic.FromSnapshot(root);
        var primary = specification.Wavelengths.Single(wavelength => wavelength.IsPrimary);
        var refractiveIndex = optic.Materials
            .Resolve(specification.InitialGlass)
            .RefractiveIndex(primary.Nanometers);
        if (!double.IsFinite(refractiveIndex) || refractiveIndex <= 1)
        {
            throw new InvalidDataException(
                $"Initial glass '{specification.InitialGlass}' has no usable refractive index at {primary.Nanometers:0.###} nm.");
        }

        ApplyPowerDistribution(
            optic,
            specification,
            elementCount,
            seedIndex,
            refractiveIndex);
        RecoverEffectiveFocalLength(optic, specification.EffectiveFocalLengthMillimeters, elementCount);
        RecoverImagePlane(optic, specification, elementCount);

        var lineage = new CandidateLineage
        {
            RootFingerprint = rootFingerprint,
            Operation = "paraxial-power-expansion",
            Generation = 1,
            ElementCount = elementCount,
            StopVariant = stopVariant,
            SeedIndex = seedIndex
        };
        return CreateEvaluatedCandidate(
            specification,
            root,
            optic,
            lineage,
            "paraxial-expansion/v2",
            rayDensity: 2,
            allowLabAccepted: false);
    }

    internal static CandidateSnapshot CreateEvaluatedCandidate(
        InitialStructureSpecification specification,
        OpticSnapshot flatRoot,
        Optic optic,
        CandidateLineage lineage,
        string algorithm,
        int rayDensity,
        bool allowLabAccepted)
    {
        var evaluation = EvaluateOptic(optic, specification, rayDensity);
        var violations = EvaluateConstraints(optic, specification, evaluation);
        var status = Classify(evaluation, violations, specification, allowLabAccepted);
        var opticSnapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(opticSnapshot);
        var opticFingerprint = ContentFingerprint.Compute(opticSnapshot);
        var candidateIdentity = ContentFingerprint.Compute(new
        {
            opticFingerprint,
            lineage,
            algorithm,
            EvaluationStage = allowLabAccepted ? "independent-dense" : "search"
        });

        return new CandidateSnapshot
        {
            CandidateId = $"candidate-{candidateIdentity[..16]}",
            OpticFingerprint = opticFingerprint,
            Status = status,
            FlatRootOptic = flatRoot,
            Optic = opticSnapshot,
            Lineage = lineage,
            Evaluation = evaluation,
            Violations = violations
        };
    }

    private static void ApplyPowerDistribution(
        Optic optic,
        InitialStructureSpecification specification,
        int elementCount,
        int seedIndex,
        double refractiveIndex)
    {
        var random = new DeterministicRandom(
            specification.Budget.RandomSeed
            + (seedIndex * 1_000_003L)
            + (elementCount * 97L));
        var weights = new double[elementCount];
        for (var index = 0; index < weights.Length; index++)
        {
            weights[index] = 0.75 + (0.5 * random.NextUnitDouble());
        }

        if (elementCount >= 3 && seedIndex % 3 == 1)
        {
            weights[elementCount / 2] *= -0.35;
        }
        else if (elementCount >= 4 && seedIndex % 3 == 2)
        {
            weights[1] *= -0.2;
        }

        var sum = weights.Sum();
        if (Math.Abs(sum) < 1e-9)
        {
            weights[0] += 1;
            sum = weights.Sum();
        }

        for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            var normalizedPower = weights[elementIndex] / sum;
            var lensPower = normalizedPower / specification.EffectiveFocalLengthMillimeters;
            var bend = (random.NextUnitDouble() - 0.5) * 0.7;
            var baseCurvature = lensPower / (2 * (refractiveIndex - 1));
            var frontCurvature = baseCurvature * (1 + bend);
            var backCurvature = -baseCurvature * (1 - bend);
            var front = optic.SurfaceGroup.Items[1 + (elementIndex * 2)];
            var back = optic.SurfaceGroup.Items[2 + (elementIndex * 2)];
            front.Radius = RadiusFromCurvature(frontCurvature);
            back.Radius = RadiusFromCurvature(backCurvature);
        }

        optic.SurfaceGroup.Renumber();
    }

    internal static void RecoverEffectiveFocalLength(Optic optic, double target, int elementCount)
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var effective = optic.Paraxial.EstimateEffectiveFocalLength();
            if (!double.IsFinite(effective) || Math.Abs(effective) < 1e-9)
            {
                break;
            }

            var scale = Math.Abs(effective / target);
            if (!double.IsFinite(scale) || scale is < 0.2 or > 5)
            {
                break;
            }

            if (Math.Abs(scale - 1) < 1e-7)
            {
                break;
            }

            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                var front = optic.SurfaceGroup.Items[1 + (elementIndex * 2)];
                var back = optic.SurfaceGroup.Items[2 + (elementIndex * 2)];
                front.Radius /= scale;
                back.Radius /= scale;
            }

            optic.SurfaceGroup.Renumber();
        }
    }

    internal static void RecoverImagePlane(
        Optic optic,
        InitialStructureSpecification specification,
        int elementCount)
    {
        var finalLensSurface = optic.SurfaceGroup.Items[elementCount * 2];
        var cardinal = optic.Paraxial.EstimateCardinalPoints();
        var requested = cardinal.BackFocalPosition - finalLensSurface.CoordinateSystem.Origin.Z;
        var maximum = Math.Max(
            specification.MinimumBackFocusMillimeters,
            specification.MaximumTrackLengthMillimeters
            - finalLensSurface.CoordinateSystem.Origin.Z);
        finalLensSurface.Thickness = double.IsFinite(requested)
            ? Math.Clamp(requested, specification.MinimumBackFocusMillimeters, maximum)
            : specification.MinimumBackFocusMillimeters;
        optic.SurfaceGroup.Renumber();
    }

    internal static EvaluationVector EvaluateOptic(
        Optic optic,
        InitialStructureSpecification specification,
        int rayDensity)
    {
        double? effectiveFocalLength = null;
        double? fNumber = null;
        try
        {
            effectiveFocalLength = optic.Paraxial.EstimateEffectiveFocalLength();
            fNumber = optic.Paraxial.EstimateFNumber();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArithmeticException)
        {
            // The structured constraint list below reports unavailable paraxial values.
        }

        var normalizedFields = new[] { 0.0, 0.5, 1.0 };
        var normalizedPupils = new[]
        {
            (X: 0.0, Y: 0.0),
            (X: 0.7, Y: 0.0),
            (X: -0.7, Y: 0.0),
            (X: 0.0, Y: 0.7),
            (X: 0.0, Y: -0.7)
        };
        var evaluatedRayCount = 0;
        var validRayCount = 0;
        foreach (var wavelength in specification.Wavelengths.Where(item => item.Weight > 0))
        {
            foreach (var field in normalizedFields)
            {
                foreach (var pupil in normalizedPupils)
                {
                    evaluatedRayCount++;
                    try
                    {
                        var sample = optic.TraceGenericFinalSample(
                            0,
                            field,
                            pupil.X,
                            pupil.Y,
                            wavelength.Nanometers / 1000.0);
                        if (sample is not null
                            && !sample.Vignetted
                            && sample.Intensity > 0
                            && double.IsFinite(sample.Position.X)
                            && double.IsFinite(sample.Position.Y)
                            && double.IsFinite(sample.Position.Z))
                        {
                            validRayCount++;
                        }
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException
                        or ArithmeticException
                        or ArgumentException)
                    {
                        // A failed ray contributes to the explicit valid-ray fraction.
                    }
                }
            }
        }

        double? rmsSpotRadius = null;
        double? maximumSpotRadius = null;
        if (validRayCount > evaluatedRayCount / 2)
        {
            try
            {
                var spot = SpotMetricEvaluator.Evaluate(optic, rayDensity: rayDensity);
                rmsSpotRadius = spot.RmsSpotRadius;
                maximumSpotRadius = spot.MaximumSpotRadius;
            }
            catch (Exception exception) when (
                exception is AnalysisDataUnavailableException
                or InvalidOperationException
                or ArithmeticException)
            {
                // Low-density reachability remains available even when the spot metric cannot be formed.
            }
        }

        return new EvaluationVector
        {
            EffectiveFocalLengthMillimeters = FiniteOrNull(effectiveFocalLength),
            FNumber = FiniteOrNull(fNumber),
            ValidRayFraction = evaluatedRayCount == 0 ? 0 : (double)validRayCount / evaluatedRayCount,
            RmsSpotRadiusMillimeters = FiniteOrNull(rmsSpotRadius),
            MaximumSpotRadiusMillimeters = FiniteOrNull(maximumSpotRadius),
            EvaluatedRayCount = evaluatedRayCount,
            ValidRayCount = validRayCount
        };
    }

    internal static IReadOnlyList<ConstraintViolation> EvaluateConstraints(
        Optic optic,
        InitialStructureSpecification specification,
        EvaluationVector evaluation)
    {
        var violations = new List<ConstraintViolation>();
        if (evaluation.EffectiveFocalLengthMillimeters is not { } effective)
        {
            violations.Add(new ConstraintViolation(
                "paraxial.efl.unavailable",
                ConstraintSeverity.Hard,
                "Effective focal length could not be evaluated."));
        }
        else
        {
            var relativeError = Math.Abs(effective - specification.EffectiveFocalLengthMillimeters)
                / specification.EffectiveFocalLengthMillimeters;
            if (relativeError > 0.05)
            {
                violations.Add(new ConstraintViolation(
                    "paraxial.efl.relative-error",
                    relativeError > 0.15 ? ConstraintSeverity.Hard : ConstraintSeverity.Warning,
                    "Effective focal length is outside the first-order target window.",
                    effective,
                    specification.EffectiveFocalLengthMillimeters));
            }
        }

        if (evaluation.ValidRayFraction < 0.8)
        {
            violations.Add(new ConstraintViolation(
                "trace.valid-ray-fraction",
                evaluation.ValidRayFraction < 0.5 ? ConstraintSeverity.Hard : ConstraintSeverity.Warning,
                "Too few sampled rays reached the image surface.",
                evaluation.ValidRayFraction,
                0.8));
        }

        if (evaluation.FNumber is not { } fNumber)
        {
            violations.Add(new ConstraintViolation(
                "paraxial.f-number.unavailable",
                ConstraintSeverity.Hard,
                "F-number could not be evaluated."));
        }
        else
        {
            var relativeError = Math.Abs(fNumber - specification.FNumber) / specification.FNumber;
            if (relativeError > 0.1)
            {
                violations.Add(new ConstraintViolation(
                    "paraxial.f-number.relative-error",
                    relativeError > 0.3 ? ConstraintSeverity.Hard : ConstraintSeverity.Warning,
                    "F-number is outside the initial-structure target window.",
                    fNumber,
                    specification.FNumber));
            }
        }

        AddSpotConstraint(
            evaluation.RmsSpotRadiusMillimeters,
            specification.MaximumRmsSpotRadiusMillimeters,
            "image.rms-spot-radius",
            "RMS spot radius");
        AddSpotConstraint(
            evaluation.MaximumSpotRadiusMillimeters,
            specification.MaximumSpotRadiusMillimeters,
            "image.maximum-spot-radius",
            "Maximum spot radius");

        var totalTrack = optic.SurfaceGroup.TotalTrack;
        if (!double.IsFinite(totalTrack) || totalTrack > specification.MaximumTrackLengthMillimeters)
        {
            violations.Add(new ConstraintViolation(
                "geometry.maximum-track",
                ConstraintSeverity.Hard,
                "The optical track exceeds the configured limit.",
                totalTrack,
                specification.MaximumTrackLengthMillimeters));
        }

        return violations;

        void AddSpotConstraint(double? actual, double limit, string code, string label)
        {
            if (actual is not { } value)
            {
                violations.Add(new ConstraintViolation(
                    code + ".unavailable",
                    ConstraintSeverity.Hard,
                    $"{label} could not be evaluated."));
                return;
            }
            if (value > limit)
            {
                violations.Add(new ConstraintViolation(
                    code,
                    value > limit * 2 ? ConstraintSeverity.Hard : ConstraintSeverity.Warning,
                    $"{label} exceeds the refinable-candidate limit.",
                    value,
                    limit));
            }
        }
    }

    internal static CandidateStatus Classify(
        EvaluationVector evaluation,
        IReadOnlyList<ConstraintViolation> violations,
        InitialStructureSpecification specification,
        bool allowLabAccepted)
    {
        if (violations.Any(violation => violation.Severity == ConstraintSeverity.Hard))
        {
            return CandidateStatus.Rejected;
        }

        if (evaluation.ValidRayFraction < 0.8)
        {
            return CandidateStatus.Exploratory;
        }

        if (violations.Any(violation => violation.Severity == ConstraintSeverity.Warning))
        {
            return CandidateStatus.TraceValid;
        }

        var relativeError = evaluation.EffectiveFocalLengthMillimeters is { } effective
            ? Math.Abs(effective - specification.EffectiveFocalLengthMillimeters)
                / specification.EffectiveFocalLengthMillimeters
            : double.PositiveInfinity;
        var passesAcceptance = relativeError <= 0.05
            && evaluation.FNumber is { } fNumber
            && Math.Abs(fNumber - specification.FNumber) / specification.FNumber <= 0.1
            && evaluation.RmsSpotRadiusMillimeters is { } rms
            && rms <= specification.MaximumRmsSpotRadiusMillimeters
            && evaluation.MaximumSpotRadiusMillimeters is { } maximum
            && maximum <= specification.MaximumSpotRadiusMillimeters;
        if (!passesAcceptance)
        {
            return CandidateStatus.TraceValid;
        }

        return allowLabAccepted
            ? CandidateStatus.LabAccepted
            : CandidateStatus.Refinable;
    }

    internal static double RadiusFromCurvature(double curvature) =>
        Math.Abs(curvature) < 1e-12 ? 0 : 1 / curvature;

    private static double? FiniteOrNull(double? value) =>
        value is { } finite && double.IsFinite(finite) ? finite : null;
}
