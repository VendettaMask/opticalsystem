namespace OptilandWorkbench.Core.Serialization;

internal static class OpticSnapshotMigration
{
    public static OpticSnapshot Upgrade(OpticSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < OpticSnapshotValidator.MinimumSupportedSchemaVersion
            or >= OpticSnapshotValidator.CurrentSchemaVersion)
        {
            return snapshot;
        }

        var surfaces = snapshot.Surfaces?.Select((surface, index) =>
        {
            if (surface is null)
            {
                return null!;
            }

            var migrated = surface;
            if (!double.IsFinite(migrated.SemiDiameter) || migrated.SemiDiameter < 0)
            {
                migrated = migrated with { SemiDiameter = 10 };
            }

            if (index == 0
                && migrated.CoordinateSystem is { } coordinate
                && double.IsNegativeInfinity(coordinate.OriginZ))
            {
                migrated = migrated with
                {
                    CoordinateSystem = coordinate with { OriginZ = 0 }
                };
            }

            return migrated;
        }).ToList();

        var surfaceNumbers = surfaces?
            .Where(surface => surface is not null)
            .Select(surface => surface.Number)
            .ToHashSet() ?? new HashSet<int>();
        var fieldCount = snapshot.Fields?.Count ?? 0;
        var wavelengthCount = snapshot.Wavelengths?.Count ?? 0;
        var radiusPickups = snapshot.RadiusPickups?
            .Where(pickup =>
                pickup is not null
                && surfaceNumbers.Contains(pickup.SourceSurface)
                && surfaceNumbers.Contains(pickup.TargetSurface))
            .ToList();
        var meritOperands = snapshot.MeritOperands?
            .Where(operand =>
                operand is not null
                && operand.Surface >= 0
                && (operand.Surface == 0 || surfaceNumbers.Contains(operand.Surface))
                && operand.Field >= 0
                && operand.Field <= fieldCount
                && operand.Wavelength >= 0
                && operand.Wavelength <= wavelengthCount
                && (operand.Type is not ("RADI" or "THIC")
                    || surfaceNumbers.Contains(operand.Surface)))
            .ToList();

        return snapshot with
        {
            SchemaVersion = OpticSnapshotValidator.CurrentSchemaVersion,
            Surfaces = surfaces!,
            RadiusPickups = radiusPickups,
            MeritOperands = meritOperands
        };
    }
}
