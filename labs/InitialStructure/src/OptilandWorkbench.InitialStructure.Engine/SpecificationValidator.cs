using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed class InitialStructureSpecificationException : ArgumentException
{
    public InitialStructureSpecificationException(IReadOnlyList<string> errors)
        : base("The initial-structure specification is invalid: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public static class SpecificationValidator
{
    public static void Validate(InitialStructureSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var errors = new List<string>();

        if (specification.SchemaVersion != InitialStructureSpecification.CurrentSchemaVersion)
        {
            errors.Add($"unsupported schema version {specification.SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(specification.Name))
        {
            errors.Add("name is required");
        }

        Positive(specification.EffectiveFocalLengthMillimeters, "effective focal length", errors);
        Positive(specification.FNumber, "F-number", errors);
        FiniteNonNegative(specification.MaximumFieldAngleDegrees, "maximum field angle", errors);
        Positive(specification.MaximumTrackLengthMillimeters, "maximum track length", errors);
        Positive(specification.MinimumCenterThicknessMillimeters, "minimum center thickness", errors);
        Positive(specification.MinimumAirGapMillimeters, "minimum air gap", errors);
        Positive(specification.MinimumBackFocusMillimeters, "minimum back focus", errors);
        Positive(specification.SemiDiameterMarginFactor, "semi-diameter margin factor", errors);

        if (specification.MinimumElementCount is < 3 or > 8)
        {
            errors.Add("minimum element count must be between 3 and 8");
        }

        if (specification.MaximumElementCount is < 3 or > 8)
        {
            errors.Add("maximum element count must be between 3 and 8");
        }

        if (specification.MaximumElementCount < specification.MinimumElementCount)
        {
            errors.Add("maximum element count cannot be smaller than minimum element count");
        }

        var minimumTrack = (specification.MaximumElementCount
                * specification.MinimumCenterThicknessMillimeters)
            + ((specification.MaximumElementCount - 1)
                * specification.MinimumAirGapMillimeters)
            + specification.MinimumBackFocusMillimeters;
        if (double.IsFinite(minimumTrack)
            && specification.MaximumTrackLengthMillimeters < minimumTrack)
        {
            errors.Add("maximum track length is smaller than the minimum structural track");
        }

        if (specification.Wavelengths is not { Count: > 0 })
        {
            errors.Add("at least one wavelength is required");
        }
        else
        {
            var primaryCount = 0;
            for (var index = 0; index < specification.Wavelengths.Count; index++)
            {
                var wavelength = specification.Wavelengths[index];
                if (string.IsNullOrWhiteSpace(wavelength.Label))
                {
                    errors.Add($"wavelength {index + 1} requires a label");
                }

                Positive(wavelength.Nanometers, $"wavelength {index + 1}", errors);
                FiniteNonNegative(wavelength.Weight, $"wavelength {index + 1} weight", errors);
                if (wavelength.IsPrimary)
                {
                    primaryCount++;
                }
            }

            if (primaryCount != 1)
            {
                errors.Add("exactly one primary wavelength is required");
            }
        }

        if (string.IsNullOrWhiteSpace(specification.InitialGlass))
        {
            errors.Add("an initial glass is required");
        }

        if (specification.Budget.InitialSeedCount <= 0)
        {
            errors.Add("initial seed count must be positive");
        }

        if (specification.Budget.MaximumEvaluations < specification.Budget.InitialSeedCount)
        {
            errors.Add("maximum evaluations cannot be smaller than the initial seed count");
        }

        if (specification.Budget.MaximumParallelism <= 0)
        {
            errors.Add("maximum parallelism must be positive");
        }

        if (specification.Budget.TimeLimit <= TimeSpan.Zero)
        {
            errors.Add("time limit must be positive");
        }

        if (errors.Count > 0)
        {
            throw new InitialStructureSpecificationException(errors);
        }
    }

    private static void Positive(double value, string name, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            errors.Add($"{name} must be finite and positive");
        }
    }

    private static void FiniteNonNegative(double value, string name, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            errors.Add($"{name} must be finite and non-negative");
        }
    }
}
