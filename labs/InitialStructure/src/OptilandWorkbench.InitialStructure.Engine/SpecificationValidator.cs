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
        var budget = specification.Budget ?? new SearchBudget();

        if (specification.SchemaVersion != InitialStructureSpecification.CurrentSchemaVersion)
        {
            errors.Add($"unsupported schema version {specification.SchemaVersion}");
        }

        if (!Enum.IsDefined(specification.Conjugate))
        {
            errors.Add("object conjugate mode is invalid");
        }

        if (string.IsNullOrWhiteSpace(specification.Name)
            || specification.Name.Length > InitialStructureLimits.MaximumNameLength)
        {
            errors.Add($"name is required and cannot exceed {InitialStructureLimits.MaximumNameLength} characters");
        }

        Positive(specification.EffectiveFocalLengthMillimeters, "effective focal length", errors);
        Positive(specification.FNumber, "F-number", errors);
        FiniteNonNegative(specification.MaximumFieldAngleDegrees, "maximum field angle", errors);
        if (specification.MaximumFieldAngleDegrees > InitialStructureLimits.MaximumFieldAngleDegrees)
        {
            errors.Add($"maximum field angle cannot exceed {InitialStructureLimits.MaximumFieldAngleDegrees} degrees");
        }
        Positive(specification.MaximumTrackLengthMillimeters, "maximum track length", errors);
        Positive(specification.MinimumCenterThicknessMillimeters, "minimum center thickness", errors);
        Positive(specification.MinimumAirGapMillimeters, "minimum air gap", errors);
        Positive(specification.MinimumBackFocusMillimeters, "minimum back focus", errors);
        Positive(specification.SemiDiameterMarginFactor, "semi-diameter margin factor", errors);
        Positive(specification.MaximumRmsSpotRadiusMillimeters, "maximum RMS spot radius", errors);
        Positive(specification.MaximumSpotRadiusMillimeters, "maximum spot radius", errors);
        if (specification.MaximumSpotRadiusMillimeters < specification.MaximumRmsSpotRadiusMillimeters)
        {
            errors.Add("maximum spot radius cannot be smaller than maximum RMS spot radius");
        }

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
        if (!double.IsFinite(minimumTrack))
        {
            errors.Add("minimum structural track overflows the supported numeric range");
        }
        else if (specification.MaximumTrackLengthMillimeters < minimumTrack)
        {
            errors.Add("maximum track length is smaller than the minimum structural track");
        }

        var apertureRadius = specification.EffectiveFocalLengthMillimeters
            / (2 * specification.FNumber);
        var semiDiameter = apertureRadius * specification.SemiDiameterMarginFactor;
        if (!double.IsFinite(apertureRadius) || !double.IsFinite(semiDiameter))
        {
            errors.Add("focal length, F-number, and semi-diameter margin produce an unsupported aperture size");
        }

        if (specification.Wavelengths is not { Count: > 0 })
        {
            errors.Add("at least one wavelength is required");
        }
        else
        {
            if (specification.Wavelengths.Count > InitialStructureLimits.MaximumWavelengthCount)
            {
                errors.Add($"wavelength count cannot exceed {InitialStructureLimits.MaximumWavelengthCount}");
            }

            var primaryCount = 0;
            for (var index = 0; index < specification.Wavelengths.Count; index++)
            {
                var wavelength = specification.Wavelengths[index];
                if (wavelength is null)
                {
                    errors.Add($"wavelength {index + 1} is null");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(wavelength.Label)
                    || wavelength.Label.Length > InitialStructureLimits.MaximumNameLength)
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

        else if (specification.InitialGlass.Length > InitialStructureLimits.MaximumNameLength)
        {
            errors.Add($"initial glass cannot exceed {InitialStructureLimits.MaximumNameLength} characters");
        }

        if (specification.GlassCatalogs is null
            || specification.GlassCatalogs.Count > InitialStructureLimits.MaximumGlassCatalogCount
            || specification.GlassCatalogs.Any(item => string.IsNullOrWhiteSpace(item)
                || item.Length > InitialStructureLimits.MaximumNameLength))
        {
            errors.Add("glass catalog list is too large or contains an invalid name");
        }

        if (specification.Budget is null)
        {
            errors.Add("search budget is required");
        }

        if (budget.InitialSeedCount is <= 0
            or > InitialStructureLimits.MaximumInitialSeedCount)
        {
            errors.Add($"initial seed count must be between 1 and {InitialStructureLimits.MaximumInitialSeedCount}");
        }

        if (budget.MaximumEvaluations is <= 0
            or > InitialStructureLimits.MaximumEvaluations)
        {
            errors.Add($"maximum evaluations must be between 1 and {InitialStructureLimits.MaximumEvaluations}");
        }

        if (budget.MaximumParallelism is <= 0
            or > InitialStructureLimits.MaximumParallelism)
        {
            errors.Add($"maximum parallelism must be between 1 and {InitialStructureLimits.MaximumParallelism}");
        }

        if (budget.TimeLimit <= TimeSpan.Zero
            || budget.TimeLimit > InitialStructureLimits.MaximumTimeLimit)
        {
            errors.Add($"time limit must be positive and no longer than {InitialStructureLimits.MaximumTimeLimit}");
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
