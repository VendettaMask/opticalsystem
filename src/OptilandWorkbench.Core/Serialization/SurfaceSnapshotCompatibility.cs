namespace OptilandWorkbench.Core.Serialization;

internal static class SurfaceSnapshotCompatibility
{
    public static SurfaceSnapshot PrepareForSave(SurfaceSnapshot surface)
    {
        return NormalizeLegacyFromComponents(ReconcileLegacyGeometryEdits(surface));
    }

    public static SurfaceSnapshot NormalizeLegacyFromComponents(SurfaceSnapshot surface)
    {
        if (surface.Components is not { } components)
        {
            return surface;
        }

        var normalizedComponents = NormalizeComponents(components);
        var radius = surface.Radius;
        var conic = surface.Conic;
        if (LegacyRadiusConic(normalizedComponents.Geometry, out var componentRadius, out var componentConic))
        {
            radius = componentRadius;
            conic = componentConic;
        }

        return surface with
        {
            Radius = radius,
            Conic = conic,
            Material = LegacyMaterial(normalizedComponents, surface.Material, surface.IsReflective),
            Coating = LegacyCoating(normalizedComponents, surface.Coating),
            IsReflective = IsReflective(surface.IsReflective, normalizedComponents),
            Components = normalizedComponents
        };
    }

    private static SurfaceSnapshot ReconcileLegacyGeometryEdits(SurfaceSnapshot surface)
    {
        if (surface.Components is not { Geometry: { } geometry } components)
        {
            return surface;
        }

        if (geometry.Kind is not ("standard" or "standard_grating"))
        {
            return surface;
        }

        LegacyRadiusConic(geometry, out var componentRadius, out var componentConic);
        if (SameNumber(surface.Radius, componentRadius)
            && SameNumber(surface.Conic, componentConic))
        {
            return surface;
        }

        var numbers = geometry.Numbers is null
            ? new Dictionary<string, double>()
            : new Dictionary<string, double>(geometry.Numbers);
        numbers["radius"] = surface.Radius;
        numbers["conic"] = surface.Conic;

        var reconciledGeometry = geometry with
        {
            Numbers = numbers
        };

        return surface with
        {
            Components = components with
            {
                GeometryKind = geometry.Kind,
                Geometry = reconciledGeometry
            }
        };
    }

    public static bool LegacyRadiusConic(
        ComponentSnapshot? geometry,
        out double radius,
        out double conic)
    {
        radius = 0;
        conic = 0;
        if (geometry is null)
        {
            return false;
        }

        var numbers = geometry.Numbers ?? new Dictionary<string, double>();
        switch (geometry.Kind)
        {
            case "plane":
            case "plane_grating":
                return true;
            case "standard":
            case "standard_grating":
            case "even_asphere":
            case "odd_asphere":
            case "forbes_q":
                radius = Get(numbers, "radius", 0);
                conic = Get(numbers, "conic", 0);
                return true;
            default:
                return false;
        }
    }

    public static string? MaterialName(ComponentSnapshot? material, string fallback)
    {
        if (material is null)
        {
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }

        if (material.Kind == "air")
        {
            return "Air";
        }

        if (material.Text is not null
            && material.Text.TryGetValue("name", out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    public static bool IsReflective(bool legacyFlag, SurfaceComponentSnapshot components)
    {
        return legacyFlag
            || components.InteractionKind == "reflective"
            || components.Interaction is { Kind: "reflective" }
            || ReflectiveNumber(components.Interaction);
    }

    private static SurfaceComponentSnapshot NormalizeComponents(SurfaceComponentSnapshot components)
    {
        var materialBefore = MaterialName(
            components.MaterialBeforeComponent,
            components.MaterialBefore) ?? components.MaterialBefore;
        var materialAfter = MaterialName(
            components.MaterialAfterComponent,
            components.MaterialAfter) ?? components.MaterialAfter;

        return components with
        {
            GeometryKind = components.Geometry?.Kind ?? components.GeometryKind,
            MaterialBefore = materialBefore,
            MaterialAfter = materialAfter,
            CoatingKind = components.Coating?.Kind ?? components.CoatingKind,
            InteractionKind = components.Interaction?.Kind ?? components.InteractionKind,
            PhysicalApertureKind = components.PhysicalAperture?.Kind ?? components.PhysicalApertureKind,
            ScatteringKind = components.Scattering?.Kind ?? components.ScatteringKind
        };
    }

    private static string LegacyMaterial(
        SurfaceComponentSnapshot components,
        string fallback,
        bool legacyReflective)
    {
        return IsReflective(legacyReflective, components)
            ? "MIRROR"
            : MaterialName(components.MaterialAfterComponent, components.MaterialAfter) ?? fallback;
    }

    private static string LegacyCoating(SurfaceComponentSnapshot components, string fallback)
    {
        var coating = components.Coating;
        if (coating is null)
        {
            return components.CoatingKind == "none" ? "None" : fallback;
        }

        if (coating.Kind == "none")
        {
            return "None";
        }

        if (coating.Kind is "thin_film_stack" or "approximate_transmission_ripple"
            && coating.Numbers is not null
            && coating.Text is not null
            && Get(coating.Numbers, "count", 0) == 1
            && coating.Text.TryGetValue("material_0", out var material)
            && !string.IsNullOrWhiteSpace(material))
        {
            return material;
        }

        return coating.Kind switch
        {
            "thin_film_stack" or "approximate_transmission_ripple" => "Experimental Ripple Approximation",
            "simple" => "Simple",
            _ => string.IsNullOrWhiteSpace(fallback) ? coating.Kind : fallback
        };
    }

    private static bool ReflectiveNumber(ComponentSnapshot? interaction)
    {
        return interaction?.Numbers is not null
            && interaction.Numbers.TryGetValue("isReflective", out var value)
            && double.IsFinite(value)
            && Math.Abs(value) > 0;
    }

    private static double Get(
        IReadOnlyDictionary<string, double> numbers,
        string key,
        double fallback)
    {
        return numbers.TryGetValue(key, out var value) ? value : fallback;
    }

    private static bool SameNumber(double left, double right)
    {
        if (double.IsInfinity(left) || double.IsInfinity(right))
        {
            return left.Equals(right);
        }

        var tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= tolerance;
    }
}
