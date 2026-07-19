using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

internal static class RealImageFieldConversion
{
    public static Optic ForDistortion(Optic optic)
    {
        if (optic.FieldDefinition != FieldDefinitionKind.RealImageHeight)
        {
            return optic;
        }

        var converted = Optic.FromSnapshot(optic.ToSnapshot());
        var launchFields = optic.Fields
            .Select(field => optic.SequentialRayTracer.RayGenerator.ResolveRealImageFieldCoordinates(field.X, field.Y))
            .ToArray();
        converted.FieldDefinition = IsObjectAtInfinity(converted)
            ? FieldDefinitionKind.Angle
            : FieldDefinitionKind.ObjectHeight;
        for (var index = 0; index < converted.Fields.Count; index++)
        {
            converted.Fields[index].X = launchFields[index].X;
            converted.Fields[index].Y = launchFields[index].Y;
        }

        return converted;
    }

    public static Optic ForImageSimulation(Optic optic)
    {
        if (optic.FieldDefinition != FieldDefinitionKind.RealImageHeight)
        {
            return optic;
        }

        var converted = Optic.FromSnapshot(optic.ToSnapshot());
        converted.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        return converted;
    }

    private static bool IsObjectAtInfinity(Optic optic)
    {
        var objectSurface = optic.SurfaceGroup.Items.FirstOrDefault();
        return objectSurface is null
            || double.IsInfinity(objectSurface.CoordinateSystem.Origin.Z)
            || Math.Abs(objectSurface.Thickness) <= 1e-12;
    }
}
