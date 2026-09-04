using System.Diagnostics.CodeAnalysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;

namespace OptilandWorkbench.Core.Serialization;

public static class OpticSnapshotValidator
{
    public const int MinimumSupportedSchemaVersion = 1;
    public const int CurrentSchemaVersion = 4;

    private const int MaximumTopLevelItemCount = 100_000;
    private const int MaximumComponentNumberCount = 1_000_000;
    private const int MaximumComponentTextCount = 100_000;
    private const int MaximumComponentDepth = 32;
    private const int MaximumEncodedCollectionCount = 1_000_000;

    private static readonly IReadOnlySet<string> ApodizationKinds = Kinds(
        "zemax_pupil",
        "uniform",
        "gaussian",
        "cosine_squared",
        "hann",
        "polynomial",
        "super_gaussian",
        "tukey");

    private static readonly IReadOnlySet<string> GeometryKinds = Kinds(
        "plane",
        "plane_grating",
        "standard_grating",
        "standard",
        "even_asphere",
        "odd_asphere",
        "biconic",
        "separable_biconic",
        "toroidal",
        "polynomial",
        "chebyshev",
        "zernike",
        "forbes_q");

    private static readonly IReadOnlySet<string> MaterialKinds = Kinds(
        "unresolved",
        "air",
        "constant",
        "cauchy",
        "sellmeier",
        "polynomial_dispersion",
        "abbe",
        "catalog");

    private static readonly IReadOnlySet<string> CoatingKinds = Kinds(
        "none",
        "simple",
        "thin_film_stack",
        "approximate_transmission_ripple");

    private static readonly IReadOnlySet<string> InteractionKinds = Kinds(
        "reflective",
        "refractive",
        "thin_lens",
        "diffractive",
        "phase");

    private static readonly IReadOnlySet<string> ApertureKinds = Kinds(
        "circular",
        "annular",
        "offset_radial",
        "rectangular",
        "elliptical",
        "polygon",
        "file",
        "union",
        "intersection",
        "difference");

    private static readonly IReadOnlySet<string> ScatteringKinds = Kinds(
        "lambertian",
        "measured_bsdf",
        "main_ray_scatter_loss_approximation",
        "mean_measured_scatter_loss");

    private static readonly IReadOnlySet<string> PhaseProfileKinds = Kinds(
        "constant",
        "linear_grating",
        "radial",
        "grid",
        "polynomial_phase");

    private static readonly IReadOnlySet<string> PupilSamplingKinds = Kinds(
        "hexapolar",
        "gaussian_quad",
        "uniform");

    public static void Validate(OpticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.SchemaVersion is < MinimumSupportedSchemaVersion or > CurrentSchemaVersion)
        {
            Invalid(
                "$.schemaVersion",
                $"schema version {snapshot.SchemaVersion} is not supported; expected "
                + $"{MinimumSupportedSchemaVersion} through {CurrentSchemaVersion}");
        }

        RequireText(snapshot.Name, "$.name");
        ValidateSystemAperture(snapshot.Aperture);
        ValidateGlassCatalogs(snapshot.GlassCatalogs);

        if (!Enum.TryParse<FieldDefinitionKind>(
                snapshot.FieldDefinition,
                ignoreCase: false,
                out _))
        {
            Invalid("$.fieldDefinition", $"'{snapshot.FieldDefinition}' is not a supported field definition");
        }

        ValidateEnvironment(snapshot.Environment);
        ValidateComponent(snapshot.Apodization, ComponentRole.Apodization, "$.apodization", 0);

        if (snapshot.Fields is not { Count: > 0 })
        {
            Invalid("$.fields", "at least one field is required");
        }

        if (snapshot.Fields.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.fields", "the field table is too large");
        }

        for (var index = 0; index < snapshot.Fields.Count; index++)
        {
            ValidateField(snapshot.Fields[index], index);
        }

        if (snapshot.Wavelengths is not { Count: > 0 })
        {
            Invalid("$.wavelengths", "at least one wavelength is required");
        }

        if (snapshot.Wavelengths.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.wavelengths", "the wavelength table is too large");
        }

        var primaryCount = 0;
        for (var index = 0; index < snapshot.Wavelengths.Count; index++)
        {
            var wavelength = snapshot.Wavelengths[index];
            ValidateWavelength(wavelength, index);
            if (wavelength.IsPrimary)
            {
                primaryCount++;
            }
        }

        if (primaryCount != 1)
        {
            Invalid("$.wavelengths", "exactly one primary wavelength is required");
        }

        if (snapshot.Surfaces is not { Count: > 0 })
        {
            Invalid("$.surfaces", "at least one surface is required");
        }

        if (snapshot.Surfaces.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.surfaces", "the surface table is too large");
        }

        var surfaceNumbers = new HashSet<int>();
        for (var index = 0; index < snapshot.Surfaces.Count; index++)
        {
            var surface = snapshot.Surfaces[index];
            ValidateSurface(surface, index);
            if (!surfaceNumbers.Add(surface.Number))
            {
                Invalid($"$.surfaces[{index}].number", $"surface number {surface.Number} is duplicated");
            }

            if (surface.Number != index)
            {
                Invalid(
                    $"$.surfaces[{index}].number",
                    $"surface numbers must be contiguous and ordered from 0; expected {index}");
            }
        }

        ValidatePickups(snapshot.RadiusPickups, surfaceNumbers);
        ValidateSolveSettings(snapshot.SolveSettings);
        ValidateMeritOperands(
            snapshot.MeritOperands,
            surfaceNumbers,
            snapshot.Fields.Count,
            snapshot.Wavelengths.Count);
    }

    private static void ValidateSystemAperture(ApertureSnapshot? aperture)
    {
        if (aperture is null)
        {
            return;
        }

        if (!Enum.TryParse<ApertureKind>(aperture.Kind, ignoreCase: false, out _))
        {
            Invalid("$.aperture.kind", $"'{aperture.Kind}' is not a supported aperture kind");
        }

        RequireFinitePositive(aperture.Value, "$.aperture.value");
    }

    private static void ValidateGlassCatalogs(IReadOnlyList<string>? catalogs)
    {
        if (catalogs is null)
        {
            return;
        }

        if (catalogs.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.glassCatalogs", "the glass-catalog list is too large");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < catalogs.Count; index++)
        {
            RequireText(catalogs[index], $"$.glassCatalogs[{index}]");
            if (!names.Add(catalogs[index]))
            {
                Invalid(
                    $"$.glassCatalogs[{index}]",
                    $"glass catalog '{catalogs[index]}' is duplicated");
            }
        }
    }

    private static void ValidateEnvironment(EnvironmentSnapshot? environment)
    {
        if (environment is null)
        {
            return;
        }

        RequireFinite(environment.TemperatureCelsius, "$.environment.temperatureCelsius");
        RequireFinitePositive(
            environment.PressureAtmospheres,
            "$.environment.pressureAtmospheres");
    }

    private static void ValidateField(FieldPointSnapshot? field, int index)
    {
        var path = $"$.fields[{index}]";
        if (field is null)
        {
            Invalid(path, "field entries cannot be null");
        }

        RequireText(field.Label, $"{path}.label");
        RequireFinite(field.XAngleDegrees, $"{path}.xAngleDegrees");
        RequireFinite(field.YAngleDegrees, $"{path}.yAngleDegrees");
        RequireFiniteNonNegative(field.Weight, $"{path}.weight");
        RequireFinite(field.VignetteFactorX, $"{path}.vignetteFactorX");
        RequireFinite(field.VignetteFactorY, $"{path}.vignetteFactorY");
    }

    private static void ValidateWavelength(WavelengthSnapshot? wavelength, int index)
    {
        var path = $"$.wavelengths[{index}]";
        if (wavelength is null)
        {
            Invalid(path, "wavelength entries cannot be null");
        }

        RequireText(wavelength.Label, $"{path}.label");
        RequireFinitePositive(wavelength.Nanometers, $"{path}.nanometers");
        RequireFiniteNonNegative(wavelength.Weight, $"{path}.weight");
    }

    private static void ValidateSurface(SurfaceSnapshot? surface, int index)
    {
        var path = $"$.surfaces[{index}]";
        if (surface is null)
        {
            Invalid(path, "surface entries cannot be null");
        }

        RequireText(surface.Label, $"{path}.label");
        if (double.IsNaN(surface.Radius))
        {
            Invalid($"{path}.radius", "radius cannot be NaN");
        }

        if (index != 0 || !double.IsPositiveInfinity(surface.Thickness))
        {
            RequireFinite(surface.Thickness, $"{path}.thickness");
        }
        RequireText(surface.Material, $"{path}.material");
        RequireText(surface.Coating, $"{path}.coating");
        RequireFiniteNonNegative(surface.SemiDiameter, $"{path}.semiDiameter");
        RequireFinite(surface.Conic, $"{path}.conic");

        if (surface.CoordinateSystem is { } coordinate)
        {
            RequireFinite(coordinate.OriginX, $"{path}.coordinateSystem.originX");
            RequireFinite(coordinate.OriginY, $"{path}.coordinateSystem.originY");
            RequireFinite(coordinate.OriginZ, $"{path}.coordinateSystem.originZ");
            RequireFinite(
                coordinate.RotationXDegrees,
                $"{path}.coordinateSystem.rotationXDegrees");
            RequireFinite(
                coordinate.RotationYDegrees,
                $"{path}.coordinateSystem.rotationYDegrees");
            RequireFinite(
                coordinate.RotationZDegrees,
                $"{path}.coordinateSystem.rotationZDegrees");
        }

        if (surface.Components is { } components)
        {
            ValidateSurfaceComponents(components, $"{path}.components");
            ValidateSurfaceComponentConsistency(surface, path);
        }
    }

    private static void ValidateSurfaceComponents(
        SurfaceComponentSnapshot components,
        string path)
    {
        RequireText(components.GeometryKind, $"{path}.geometryKind");
        if (!GeometryKinds.Contains(components.GeometryKind)
            && components.Geometry is null)
        {
            Invalid(
                $"{path}.geometry",
                "unknown geometry kinds must include an opaque component payload");
        }
        RequireText(components.MaterialBefore, $"{path}.materialBefore");
        RequireText(components.MaterialAfter, $"{path}.materialAfter");
        RequireKnownKind(components.CoatingKind, CoatingKinds, $"{path}.coatingKind");
        RequireKnownKind(
            components.InteractionKind,
            InteractionKinds,
            $"{path}.interactionKind");
        RequireOptionalKnownKind(
            components.PhysicalApertureKind,
            ApertureKinds,
            $"{path}.physicalApertureKind");
        RequireOptionalKnownKind(
            components.ScatteringKind,
            ScatteringKinds,
            $"{path}.scatteringKind");

        ValidateComponent(components.Geometry, ComponentRole.Geometry, $"{path}.geometry", 0);
        ValidateComponent(
            components.MaterialBeforeComponent,
            ComponentRole.Material,
            $"{path}.materialBeforeComponent",
            0);
        ValidateComponent(
            components.MaterialAfterComponent,
            ComponentRole.Material,
            $"{path}.materialAfterComponent",
            0);
        ValidateComponent(components.Coating, ComponentRole.Coating, $"{path}.coating", 0);
        ValidateComponent(
            components.Interaction,
            ComponentRole.Interaction,
            $"{path}.interaction",
            0);
        ValidateComponent(
            components.PhysicalAperture,
            ComponentRole.Aperture,
            $"{path}.physicalAperture",
            0);
        ValidateComponent(
            components.Scattering,
            ComponentRole.Scattering,
            $"{path}.scattering",
            0);

        if (components.PhysicalAperture is null
            && components.PhysicalApertureKind is "union" or "intersection" or "difference")
        {
            Invalid(
                $"{path}.physicalAperture",
                "boolean apertures must include their left and right components");
        }
    }

    private static void ValidateSurfaceComponentConsistency(
        SurfaceSnapshot surface,
        string path)
    {
        var components = surface.Components!;
        var normalized = SurfaceSnapshotCompatibility.NormalizeLegacyFromComponents(surface);
        var normalizedComponents = normalized.Components!;

        RequireSameKind(
            components.GeometryKind,
            components.Geometry?.Kind,
            $"{path}.components.geometryKind");
        RequireSameKind(
            components.CoatingKind,
            components.Coating?.Kind,
            $"{path}.components.coatingKind");
        RequireSameKind(
            components.InteractionKind,
            components.Interaction?.Kind,
            $"{path}.components.interactionKind");
        RequireSameKind(
            components.PhysicalApertureKind,
            components.PhysicalAperture?.Kind,
            $"{path}.components.physicalApertureKind");
        RequireSameKind(
            components.ScatteringKind,
            components.Scattering?.Kind,
            $"{path}.components.scatteringKind");
        RequireSameText(
            components.MaterialBefore,
            normalizedComponents.MaterialBefore,
            $"{path}.components.materialBefore");
        RequireSameText(
            components.MaterialAfter,
            normalizedComponents.MaterialAfter,
            $"{path}.components.materialAfter");

        if (SurfaceSnapshotCompatibility.LegacyRadiusConic(
                components.Geometry,
                out _,
                out _))
        {
            RequireSameNumber(surface.Radius, normalized.Radius, $"{path}.radius");
            RequireSameNumber(surface.Conic, normalized.Conic, $"{path}.conic");
        }

        if (surface.IsReflective != normalized.IsReflective)
        {
            Invalid(
                $"{path}.isReflective",
                "the legacy reflective flag contradicts the interaction component");
        }

        RequireSameText(surface.Material, normalized.Material, $"{path}.material");
        RequireSameText(surface.Coating, normalized.Coating, $"{path}.coating");
    }

    private static void ValidatePickups(
        IReadOnlyList<RadiusPickupSnapshot>? pickups,
        IReadOnlySet<int> surfaceNumbers)
    {
        if (pickups is null)
        {
            return;
        }

        if (pickups.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.radiusPickups", "the pickup table is too large");
        }

        for (var index = 0; index < pickups.Count; index++)
        {
            var pickup = pickups[index];
            var path = $"$.radiusPickups[{index}]";
            if (pickup is null)
            {
                Invalid(path, "pickup entries cannot be null");
            }

            RequireSurfaceReference(
                pickup.SourceSurface,
                surfaceNumbers,
                $"{path}.sourceSurface");
            RequireSurfaceReference(
                pickup.TargetSurface,
                surfaceNumbers,
                $"{path}.targetSurface");
            RequireFinite(pickup.Scale, $"{path}.scale");
            RequireFinite(pickup.Offset, $"{path}.offset");
        }
    }

    private static void ValidateSolveSettings(SolveSettingsSnapshot? solveSettings)
    {
        if (solveSettings is not null)
        {
            RequireFinite(
                solveSettings.DesiredBackFocus,
                "$.solveSettings.desiredBackFocus");
        }
    }

    private static void ValidateMeritOperands(
        IReadOnlyList<MeritOperandSnapshot>? operands,
        IReadOnlySet<int> surfaceNumbers,
        int fieldCount,
        int wavelengthCount)
    {
        if (operands is null)
        {
            return;
        }

        if (operands.Count > MaximumTopLevelItemCount)
        {
            Invalid("$.meritOperands", "the merit operand table is too large");
        }

        for (var index = 0; index < operands.Count; index++)
        {
            var operand = operands[index];
            var path = $"$.meritOperands[{index}]";
            if (operand is null)
            {
                Invalid(path, "merit operand entries cannot be null");
            }

            var canonicalType = (operand.Type ?? string.Empty).Trim().ToUpperInvariant();
            if (canonicalType.Length == 0
                || !MeritFunctionCatalog.Types.Any(type => type.Code == canonicalType))
            {
                Invalid($"{path}.type", $"'{operand.Type}' is not a supported merit operand type");
            }

            if (!operand.CompatibilityOnly
                && !MeritFunctionCatalog.HasOpaqueZemaxParameters(canonicalType))
            {
                var descriptor = ZemaxOperandRegistry.TryGet(canonicalType, out var zemaxDescriptor)
                    ? zemaxDescriptor
                    : null;
                var surfaceSlotIsReference = descriptor is null
                    || descriptor.UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Surface);
                var fieldSlotIsReference = descriptor is null
                    || descriptor.UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field);
                var wavelengthSlotIsReference = descriptor is null
                    || descriptor.UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength);
                var secondSlotIsEndSurface = descriptor?.UsesSlotAs(
                    "Int2",
                    ZemaxOperandParameterValueKind.EndSurface) == true;

                if (surfaceSlotIsReference
                    && (operand.Surface < 0
                        || (operand.Surface > 0 && !surfaceNumbers.Contains(operand.Surface))))
                {
                    Invalid(
                        $"{path}.surface",
                        $"surface reference {operand.Surface} does not exist");
                }

                if (canonicalType is "RADI" or "THIC"
                    && !surfaceNumbers.Contains(operand.Surface))
                {
                    Invalid(
                        $"{path}.surface",
                        $"operand {canonicalType} requires an existing surface");
                }

                if (secondSlotIsEndSurface
                    && (operand.Wavelength < 0
                        || (operand.Wavelength > 0 && !surfaceNumbers.Contains(operand.Wavelength))))
                {
                    Invalid(
                        $"{path}.wavelength",
                        $"end-surface reference {operand.Wavelength} does not exist");
                }

                if (fieldSlotIsReference)
                {
                    RequireOneBasedReferenceOrDefault(operand.Field, fieldCount, $"{path}.field");
                }

                if (wavelengthSlotIsReference)
                {
                    RequireOneBasedReferenceOrDefault(
                        operand.Wavelength,
                        wavelengthCount,
                        $"{path}.wavelength");
                }
            }
            RequireFinite(operand.Hx, $"{path}.hx");
            RequireFinite(operand.Hy, $"{path}.hy");
            RequireFinite(operand.Px, $"{path}.px");
            RequireFinite(operand.Py, $"{path}.py");
            RequireFinite(operand.Target, $"{path}.target");
            RequireFinite(operand.Weight, $"{path}.weight");
            RequireText(operand.Comment, $"{path}.comment");
            if (operand.ZemaxIntegerParameters is { Count: > 16 })
            {
                Invalid(
                    $"{path}.zemaxIntegerParameters",
                    "no more than 16 raw integer parameters are allowed");
            }
            if (operand.ZemaxDataParameters is { Count: > 16 })
            {
                Invalid(
                    $"{path}.zemaxDataParameters",
                    "no more than 16 raw data parameters are allowed");
            }
            for (var parameterIndex = 0;
                 parameterIndex < (operand.ZemaxDataParameters?.Count ?? 0);
                 parameterIndex++)
            {
                RequireFinite(
                    operand.ZemaxDataParameters![parameterIndex],
                    $"{path}.zemaxDataParameters[{parameterIndex}]");
            }

            if (operand.PupilRings is < 1 or > 20)
            {
                Invalid($"{path}.pupilRings", "pupil ring count must be in [1, 20]");
            }

            if (operand.PupilArms is < 3 or > 36)
            {
                Invalid($"{path}.pupilArms", "pupil arm count must be in [3, 36]");
            }

            RequireFinite(operand.PupilObscuration, $"{path}.pupilObscuration");
            if (operand.PupilObscuration is < 0 or > 0.95)
            {
                Invalid(
                    $"{path}.pupilObscuration",
                    "pupil obscuration must be in [0, 0.95]");
            }

            RequireKnownKind(
                operand.PupilSampling,
                PupilSamplingKinds,
                $"{path}.pupilSampling",
                ignoreCase: true);
            RequireFiniteNonNegative(operand.SpatialFrequency, $"{path}.spatialFrequency");
        }
    }

    private static void ValidateComponent(
        ComponentSnapshot? component,
        ComponentRole role,
        string path,
        int depth)
    {
        if (component is null)
        {
            return;
        }

        if (depth > MaximumComponentDepth)
        {
            Invalid(path, "component nesting is too deep");
        }

        var opaqueGeometry = role == ComponentRole.Geometry
            && !GeometryKinds.Contains(component.Kind);
        if (opaqueGeometry)
        {
            RequireText(component.Kind, $"{path}.kind");
        }
        else
        {
            RequireKnownKind(component.Kind, AllowedKinds(role), $"{path}.kind");
        }
        if (component.Numbers is null)
        {
            Invalid($"{path}.numbers", "the numeric component table cannot be null");
        }

        if (component.Text is null)
        {
            Invalid($"{path}.text", "the text component table cannot be null");
        }

        if (component.Numbers.Count > MaximumComponentNumberCount)
        {
            Invalid($"{path}.numbers", "the numeric component table is too large");
        }

        if (component.Text.Count > MaximumComponentTextCount)
        {
            Invalid($"{path}.text", "the text component table is too large");
        }

        foreach (var item in component.Numbers)
        {
            if (double.IsNaN(item.Value)
                || (double.IsInfinity(item.Value)
                    && !AllowsInfinity(role, component.Kind, item.Key, item.Value)))
            {
                Invalid(
                    $"{path}.numbers['{item.Key}']",
                    "the component value is not finite");
            }
        }

        foreach (var item in component.Text)
        {
            if (item.Value is null)
            {
                Invalid($"{path}.text['{item.Key}']", "component text values cannot be null");
            }
        }

        if (opaqueGeometry)
        {
            ValidateOpaqueChildren(component.Children, path, depth);
            return;
        }

        ValidateEncodedCollectionSizes(component, role, path);
        if (role == ComponentRole.Apodization && component.Kind == "zemax_pupil")
        {
            RequireNumberKey(component, "type", path);
            RequireNumberKey(component, "factor", path);
            if (component.Numbers["type"] is not (0 or 1 or 2) || component.Numbers["factor"] < 0)
            {
                Invalid(path, "Zemax apodization requires type 0, 1 or 2 and a non-negative factor");
            }
        }
        ValidateComponentChildren(component, role, path, depth);
    }

    private static void ValidateOpaqueChildren(
        Dictionary<string, ComponentSnapshot>? children,
        string path,
        int depth)
    {
        if (children is null)
        {
            return;
        }

        if (children.Count > MaximumComponentTextCount)
        {
            Invalid($"{path}.children", "the opaque child component table is too large");
        }

        foreach (var item in children)
        {
            RequireText(item.Key, $"{path}.children key");
            if (item.Value is null)
            {
                Invalid($"{path}.children['{item.Key}']", "opaque child components cannot be null");
            }

            ValidateOpaqueComponent(item.Value, $"{path}.children['{item.Key}']", depth + 1);
        }
    }

    private static void ValidateOpaqueComponent(
        ComponentSnapshot component,
        string path,
        int depth)
    {
        if (depth > MaximumComponentDepth)
        {
            Invalid(path, "component nesting is too deep");
        }

        RequireText(component.Kind, $"{path}.kind");
        if (component.Numbers is null || component.Numbers.Count > MaximumComponentNumberCount)
        {
            Invalid($"{path}.numbers", "the opaque numeric component table is null or too large");
        }

        if (component.Text is null || component.Text.Count > MaximumComponentTextCount)
        {
            Invalid($"{path}.text", "the opaque text component table is null or too large");
        }

        foreach (var item in component.Numbers)
        {
            if (!double.IsFinite(item.Value))
            {
                Invalid($"{path}.numbers['{item.Key}']", "opaque numeric values must be finite");
            }
        }

        foreach (var item in component.Text)
        {
            if (item.Value is null)
            {
                Invalid($"{path}.text['{item.Key}']", "opaque text values cannot be null");
            }
        }

        ValidateOpaqueChildren(component.Children, path, depth);
    }

    private static void ValidateComponentChildren(
        ComponentSnapshot component,
        ComponentRole role,
        string path,
        int depth)
    {
        var children = component.Children;
        if (role == ComponentRole.Interaction && component.Kind == "phase")
        {
            if (children is null || children.Count != 1)
            {
                Invalid($"{path}.children", "phase interactions require one profile component");
            }

            if (!children.TryGetValue("profile", out var profile) || profile is null)
            {
                Invalid($"{path}.children", "phase interactions require one profile component");
            }

            ValidateComponent(
                profile,
                ComponentRole.PhaseProfile,
                $"{path}.children.profile",
                depth + 1);
            return;
        }

        if (role == ComponentRole.Aperture
            && component.Kind is "union" or "intersection" or "difference")
        {
            if (children is null || children.Count != 2)
            {
                Invalid(
                    $"{path}.children",
                    "boolean apertures require exactly left and right components");
            }

            if (!children.TryGetValue("left", out var left) || left is null)
            {
                Invalid(
                    $"{path}.children",
                    "boolean apertures require exactly left and right components");
            }

            if (!children.TryGetValue("right", out var right) || right is null)
            {
                Invalid(
                    $"{path}.children",
                    "boolean apertures require exactly left and right components");
            }

            ValidateComponent(left, ComponentRole.Aperture, $"{path}.children.left", depth + 1);
            ValidateComponent(right, ComponentRole.Aperture, $"{path}.children.right", depth + 1);
            return;
        }

        if (children is { Count: > 0 })
        {
            Invalid($"{path}.children", "this component kind does not accept child components");
        }
    }

    private static void ValidateEncodedCollectionSizes(
        ComponentSnapshot component,
        ComponentRole role,
        string path)
    {
        if (role == ComponentRole.Coating
            && component.Kind is "thin_film_stack" or "approximate_transmission_ripple")
        {
            var count = RequireEncodedCount(
                component,
                "count",
                0,
                MaximumEncodedCollectionCount,
                path);
            for (var index = 0; index < count; index++)
            {
                RequireNumberKey(component, $"thickness_{index}", path);
                RequireTextKey(component, $"material_{index}", path);
            }
        }

        if (role == ComponentRole.Aperture && component.Kind is "polygon" or "file")
        {
            var count = RequireEncodedCount(
                component,
                "vertexCount",
                0,
                MaximumEncodedCollectionCount,
                path);
            for (var index = 0; index < count; index++)
            {
                RequireNumberKey(component, $"x{index}", path);
                RequireNumberKey(component, $"y{index}", path);
            }
        }

        if (role == ComponentRole.Aperture && component.Kind == "file")
        {
            RequireEncodedCount(
                component,
                "skipHeader",
                0,
                MaximumEncodedCollectionCount,
                path);
        }

        if (role == ComponentRole.Scattering
            && component.Kind is "measured_bsdf" or "mean_measured_scatter_loss")
        {
            var count = RequireEncodedCount(
                component,
                "sampleCount",
                0,
                MaximumEncodedCollectionCount,
                path);
            for (var index = 0; index < count; index++)
            {
                RequireNumberKey(component, $"angle{index}", path);
                RequireNumberKey(component, $"value{index}", path);
            }
        }

        if (role == ComponentRole.PhaseProfile && component.Kind == "grid")
        {
            var xCount = RequireEncodedCount(
                component,
                "xCount",
                4,
                PhaseProfileLimits.MaximumGridAxisCount,
                path);
            var yCount = RequireEncodedCount(
                component,
                "yCount",
                4,
                PhaseProfileLimits.MaximumGridAxisCount,
                path);
            if ((long)xCount * yCount > PhaseProfileLimits.MaximumGridCellCount)
            {
                Invalid($"{path}.numbers", "the encoded phase grid is too large");
            }

            for (var x = 0; x < xCount; x++)
            {
                RequireNumberKey(component, $"x{x}", path);
            }

            for (var y = 0; y < yCount; y++)
            {
                RequireNumberKey(component, $"y{y}", path);
                for (var x = 0; x < xCount; x++)
                {
                    RequireNumberKey(component, $"g{y}_{x}", path);
                }
            }
        }

        if (role == ComponentRole.Geometry
            && component.Kind is "plane_grating" or "standard_grating")
        {
            RequireOptionalInteger(component, "order", path);
        }

        if (role == ComponentRole.Interaction && component.Kind == "diffractive")
        {
            RequireOptionalInteger(component, "order", path);
        }

        if (role == ComponentRole.PhaseProfile && component.Kind == "linear_grating")
        {
            RequireOptionalInteger(component, "order", path);
        }
    }

    private static int RequireEncodedCount(
        ComponentSnapshot component,
        string key,
        int fallback,
        int maximum,
        string path)
    {
        var value = component.Numbers.TryGetValue(key, out var stored) ? stored : fallback;
        if (!double.IsFinite(value)
            || value < 0
            || value > maximum
            || value != Math.Truncate(value))
        {
            Invalid(
                $"{path}.numbers['{key}']",
                $"encoded collection counts must be integers in [0, {maximum}]");
        }

        return (int)value;
    }

    private static void RequireOptionalInteger(
        ComponentSnapshot component,
        string key,
        string path)
    {
        if (!component.Numbers.TryGetValue(key, out var value))
        {
            return;
        }

        if (!double.IsFinite(value)
            || value < int.MinValue
            || value > int.MaxValue
            || value != Math.Truncate(value))
        {
            Invalid($"{path}.numbers['{key}']", "the component value must be a 32-bit integer");
        }
    }

    private static void RequireNumberKey(
        ComponentSnapshot component,
        string key,
        string path)
    {
        if (!component.Numbers.ContainsKey(key))
        {
            Invalid(
                $"{path}.numbers['{key}']",
                "the encoded collection entry is missing");
        }
    }

    private static void RequireTextKey(
        ComponentSnapshot component,
        string key,
        string path)
    {
        if (!component.Text.ContainsKey(key))
        {
            Invalid(
                $"{path}.text['{key}']",
                "the encoded collection entry is missing");
        }
    }

    private static bool AllowsInfinity(
        ComponentRole role,
        string kind,
        string key,
        double value)
    {
        if (role == ComponentRole.Interaction
            && kind == "thin_lens"
            && key == "focalLength")
        {
            return true;
        }

        if (role != ComponentRole.Geometry)
        {
            return false;
        }

        if (key is "radius" or "radiusX" or "radiusY"
            or "tangentialRadius" or "sagittalRadius")
        {
            return true;
        }

        return kind is "plane_grating" or "standard_grating"
            && key == "periodMicrometers"
            && double.IsPositiveInfinity(value);
    }

    private static IReadOnlySet<string> AllowedKinds(ComponentRole role)
    {
        return role switch
        {
            ComponentRole.Apodization => ApodizationKinds,
            ComponentRole.Geometry => GeometryKinds,
            ComponentRole.Material => MaterialKinds,
            ComponentRole.Coating => CoatingKinds,
            ComponentRole.Interaction => InteractionKinds,
            ComponentRole.Aperture => ApertureKinds,
            ComponentRole.Scattering => ScatteringKinds,
            ComponentRole.PhaseProfile => PhaseProfileKinds,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
    }

    private static void RequireSurfaceReference(
        int surfaceNumber,
        IReadOnlySet<int> surfaceNumbers,
        string path)
    {
        if (!surfaceNumbers.Contains(surfaceNumber))
        {
            Invalid(path, $"surface reference {surfaceNumber} does not exist");
        }
    }

    private static void RequireOneBasedReferenceOrDefault(
        int value,
        int count,
        string path)
    {
        if (value < 0 || value > count)
        {
            Invalid(path, $"reference {value} is outside the valid range 0 through {count}");
        }
    }

    private static void RequireFinite(double value, string path)
    {
        if (!double.IsFinite(value))
        {
            Invalid(path, "the value must be finite");
        }
    }

    private static void RequireFinitePositive(double value, string path)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            Invalid(path, "the value must be finite and greater than zero");
        }
    }

    private static void RequireFiniteNonNegative(double value, string path)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            Invalid(path, "the value must be finite and non-negative");
        }
    }

    private static void RequireSameKind(string? stored, string? authoritative, string path)
    {
        if (authoritative is not null && stored != authoritative)
        {
            Invalid(path, "the summary kind contradicts the nested component kind");
        }
    }

    private static void RequireSameText(string stored, string authoritative, string path)
    {
        if (!string.Equals(stored, authoritative, StringComparison.OrdinalIgnoreCase))
        {
            Invalid(path, "the legacy value contradicts the component value");
        }
    }

    private static void RequireSameNumber(double stored, double authoritative, string path)
    {
        if (double.IsNaN(stored) || double.IsNaN(authoritative))
        {
            Invalid(path, "the legacy value contradicts the component value");
        }

        if (double.IsInfinity(stored) || double.IsInfinity(authoritative))
        {
            if (stored.Equals(authoritative))
            {
                return;
            }

            Invalid(path, "the legacy value contradicts the component value");
        }

        var tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(stored), Math.Abs(authoritative)));
        if (Math.Abs(stored - authoritative) > tolerance)
        {
            Invalid(path, "the legacy value contradicts the component value");
        }
    }

    private static void RequireText(string? value, string path)
    {
        if (value is null)
        {
            Invalid(path, "the value cannot be null");
        }
    }

    private static void RequireKnownKind(
        string? value,
        IReadOnlySet<string> allowed,
        string path,
        bool ignoreCase = false)
    {
        if (value is null)
        {
            Invalid(path, "the component kind cannot be null");
        }

        var isKnown = ignoreCase
            ? allowed.Any(kind => string.Equals(kind, value, StringComparison.OrdinalIgnoreCase))
            : allowed.Contains(value);
        if (!isKnown)
        {
            Invalid(path, $"'{value}' is not a supported component kind");
        }
    }

    private static void RequireOptionalKnownKind(
        string? value,
        IReadOnlySet<string> allowed,
        string path)
    {
        if (value is not null)
        {
            RequireKnownKind(value, allowed, path);
        }
    }

    private static IReadOnlySet<string> Kinds(params string[] values)
    {
        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    [DoesNotReturn]
    private static void Invalid(string path, string message)
    {
        throw new InvalidDataException($"Invalid optic snapshot at {path}: {message}.");
    }

    private enum ComponentRole
    {
        Apodization,
        Geometry,
        Material,
        Coating,
        Interaction,
        Aperture,
        Scattering,
        PhaseProfile
    }
}
