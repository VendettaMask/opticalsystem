namespace OpticalSystemDesign.Core.NonSequential;

internal static class NonSequentialFieldValidation
{
    internal static void Validate(NonSequentialIncidentField field)
    {
        if (!double.IsFinite(field.PowerWatts) || field.PowerWatts < 0 || !double.IsFinite(field.RefractiveIndex) || field.RefractiveIndex <= 0
            || field.State is null || field.State.First is null || field.State.Second is null
            || string.IsNullOrWhiteSpace(field.State.CoherenceGroup) || field.State.CoherenceGroup.Length > 128 || field.State.CoherenceGroup.Any(char.IsControl))
            throw new InvalidDataException("Invalid stored Jones state or flux.");
        var power = field.State.Power;
        if (!double.IsFinite(power) || Math.Abs(power - 1) > 2e-10)
            throw new InvalidDataException("Stored Jones modes must have unit total flux.");
    }
}
