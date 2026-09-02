using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Serialization;

internal static class OpticSnapshotMigration
{
    public static OpticSnapshot Upgrade(OpticSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < OpticSnapshotValidator.MinimumSupportedSchemaVersion
            or > OpticSnapshotValidator.CurrentSchemaVersion)
        {
            return snapshot;
        }

        var migrateLegacySchema = snapshot.SchemaVersion < OpticSnapshotValidator.CurrentSchemaVersion;
        var surfaces = migrateLegacySchema
            ? snapshot.Surfaces?.Select((surface, index) =>
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

                return SurfaceSnapshotCompatibility.NormalizeLegacyFromComponents(migrated);
            }).ToList()
            : snapshot.Surfaces;

        var surfaceNumbers = surfaces?
            .Where(surface => surface is not null)
            .Select(surface => surface.Number)
            .ToHashSet() ?? new HashSet<int>();
        var fieldCount = snapshot.Fields?.Count ?? 0;
        var wavelengthCount = snapshot.Wavelengths?.Count ?? 0;
        var radiusPickups = migrateLegacySchema
            ? snapshot.RadiusPickups?
                .Where(pickup =>
                    pickup is not null
                    && surfaceNumbers.Contains(pickup.SourceSurface)
                    && surfaceNumbers.Contains(pickup.TargetSurface))
                .ToList()
            : snapshot.RadiusPickups;
        var meritOperands = NormalizeMeritOperands(
            snapshot.MeritOperands,
            surfaceNumbers,
            fieldCount,
            wavelengthCount,
            migrateLegacySchema);

        return snapshot with
        {
            SchemaVersion = OpticSnapshotValidator.CurrentSchemaVersion,
            Surfaces = surfaces!,
            RadiusPickups = radiusPickups,
            MeritOperands = meritOperands
        };
    }

    private static List<MeritOperandSnapshot>? NormalizeMeritOperands(
        List<MeritOperandSnapshot>? operands,
        IReadOnlySet<int> surfaceNumbers,
        int fieldCount,
        int wavelengthCount,
        bool migrateLegacySchema)
    {
        if (operands is null)
        {
            return null;
        }

        var normalized = new List<MeritOperandSnapshot>(operands.Count);
        foreach (var operand in operands)
        {
            if (operand is null)
            {
                continue;
            }

            var migrated = ShouldPreserveAsLegacyZemaxCompatibility(operand, migrateLegacySchema)
                ? operand with { CompatibilityOnly = true }
                : operand;

            if (migrateLegacySchema
                && !IsLegacyMeritOperandValid(migrated, surfaceNumbers, fieldCount, wavelengthCount))
            {
                continue;
            }

            normalized.Add(migrated);
        }

        return normalized;
    }

    private static bool ShouldPreserveAsLegacyZemaxCompatibility(
        MeritOperandSnapshot operand,
        bool migrateLegacySchema)
    {
        if (operand.CompatibilityOnly || operand.Enabled)
        {
            return false;
        }

        var code = (operand.Type ?? string.Empty).Trim().ToUpperInvariant();
        if (!ZemaxOperandRegistry.TryGet(code, out _))
        {
            return false;
        }

        if (MeritFunctionCatalog.HasOpaqueZemaxParameters(code))
        {
            return true;
        }

        return migrateLegacySchema || IsLegacyZemaxReadOnlyComment(operand.Comment);
    }

    private static bool IsLegacyZemaxReadOnlyComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return false;
        }

        return comment.StartsWith("Zemax 只读记录", StringComparison.Ordinal)
            || comment.StartsWith("Zemax read-only record", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyMeritOperandValid(
        MeritOperandSnapshot operand,
        IReadOnlySet<int> surfaceNumbers,
        int fieldCount,
        int wavelengthCount)
    {
        if (operand.CompatibilityOnly || MeritFunctionCatalog.HasOpaqueZemaxParameters(operand.Type))
        {
            return true;
        }

        return operand.Surface >= 0
            && (operand.Surface == 0 || surfaceNumbers.Contains(operand.Surface))
            && operand.Field >= 0
            && operand.Field <= fieldCount
            && operand.Wavelength >= 0
            && operand.Wavelength <= wavelengthCount
            && (operand.Type is not ("RADI" or "THIC")
                || surfaceNumbers.Contains(operand.Surface));
    }
}
