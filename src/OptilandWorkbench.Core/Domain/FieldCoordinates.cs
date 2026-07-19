namespace OptilandWorkbench.Core.Domain;

public static class FieldCoordinates
{
    public static double MaximumRadius(IEnumerable<FieldPoint> fields)
    {
        return fields
            .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();
    }

    public static (double X, double Y) Normalize(
        IEnumerable<FieldPoint> fields,
        double fieldX,
        double fieldY)
    {
        var maximumRadius = MaximumRadius(fields);
        return maximumRadius <= 1e-15
            ? (0, 0)
            : (fieldX / maximumRadius, fieldY / maximumRadius);
    }

    public static (double X, double Y) Denormalize(
        IEnumerable<FieldPoint> fields,
        double normalizedFieldX,
        double normalizedFieldY)
    {
        var maximumRadius = MaximumRadius(fields);
        return (normalizedFieldX * maximumRadius, normalizedFieldY * maximumRadius);
    }
}
