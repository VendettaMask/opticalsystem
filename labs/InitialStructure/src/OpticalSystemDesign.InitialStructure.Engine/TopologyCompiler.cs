using System.Collections.Immutable;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

public sealed record TopologyElementMapping(string ElementId, int FrontIndex, int BackIndex);
public sealed record TopologyMaterialProvenance(string ElementId, string RequestedName, string ResolvedName, string Manufacturer, string ComponentFingerprint);

/// <summary>Immutable topology/mapping; mutable Core objects and snapshots are returned as independent copies.</summary>
public sealed class CompiledTopology
{
    private readonly OpticSnapshot _snapshot;
    internal CompiledTopology(DesignTopology topology, TopologyConstraints constraints, Optic optic,
        ImmutableDictionary<string, int> indices, ImmutableArray<TopologyElementMapping> elements,
        ImmutableArray<TopologyMaterialProvenance> materials)
    {
        Topology = topology;
        Constraints = constraints;
        _snapshot = optic.ToSnapshot();
        SurfaceIndices = indices;
        SurfaceIds = indices.ToImmutableDictionary(pair => pair.Value, pair => pair.Key);
        Elements = elements;
        Materials = materials;
        TopologyFingerprint = ContentFingerprint.Compute(topology);
        OpticFingerprint = ContentFingerprint.Compute(_snapshot);
        BindingFingerprint = ContentFingerprint.Compute(new { Identity = AutonomousTopologyIdentity.Name, topology, constraints, OpticFingerprint });
    }

    public DesignTopology Topology { get; }
    public TopologyConstraints Constraints { get; }
    public ImmutableDictionary<string, int> SurfaceIndices { get; }
    public ImmutableDictionary<int, string> SurfaceIds { get; }
    public ImmutableArray<TopologyElementMapping> Elements { get; }
    public ImmutableArray<TopologyMaterialProvenance> Materials { get; }
    public string TopologyFingerprint { get; }
    public string BindingFingerprint { get; }
    public string OpticFingerprint { get; }
    public double GlobalToCoreZOffset => -Topology.Reference.ZMillimeters;
    public OpticSnapshot Snapshot => CreateOptic().ToSnapshot();
    public Optic CreateOptic() => Optic.FromSnapshot(_snapshot);
}

public sealed record TopologyCompilationResult(CompiledTopology? Value, TopologyFailure? Failure)
{
    public bool Succeeded => Value is not null && Failure is null;
}

/// <summary>Only structure/parameter translation; all geometry, media and optical calculations belong to Core.</summary>
public static class TopologyCompiler
{
    private sealed record Node(string Id, double Z, TopologyFace? Face, string Material, bool Stop = false, bool Reference = false, bool Image = false);

    public static CompiledTopology Compile(DesignTopology topology, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        Validate(topology, constraints);
        var optic = new Optic(topology.Name);
        if (topology.Elements.Any(element => element.Material.StartsWith("AutoLensE58:", StringComparison.OrdinalIgnoreCase)))
            AutoLensReferenceMaterials.Register(optic.Materials);
        optic.Materials.SetPreferredGlassCatalogs(topology.GlassCatalogs);
        var allowed = constraints.AllowedGlassNames.Select(Resolve).Select(material => material.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var materialByElement = topology.Elements.ToDictionary(element => element.Id, element => Resolve(element.Material), StringComparer.Ordinal);
        foreach (var material in materialByElement.Values)
            Require(allowed.Count == 0 || allowed.Contains(material.Name), TopologyFailureCode.UnsupportedMaterial, "The element material is outside the allowed catalog set.");

        var nodes = new List<Node>
        {
            new(topology.Reference.Id, topology.Reference.ZMillimeters, null, "Air", Reference: true),
            new(topology.Stop.Id, topology.Stop.ZMillimeters, new() { Profile = new() { Kind = TopologyProfileKind.Plane },
                ClearSemiDiameterMillimeters = topology.Stop.ClearSemiDiameterMillimeters,
                MechanicalSemiDiameterMillimeters = topology.Stop.MechanicalSemiDiameterMillimeters }, "Air", Stop: true),
            new(topology.Image.Id, topology.Image.ZMillimeters, null, "Air", Image: true)
        };
        for (var elementIndex = 0; elementIndex < topology.Elements.Length; elementIndex++)
        {
            var element = topology.Elements[elementIndex];
            var leftContact = topology.CementedInterfaces?.Interfaces.Any(contact => contact.RightElementId == element.Id) == true;
            var rightContact = topology.CementedInterfaces?.Interfaces.Any(contact => contact.LeftElementId == element.Id) == true;
            if (!leftContact) nodes.Add(new(element.FrontSurfaceId, element.FrontZMillimeters, element.Front, materialByElement[element.Id].Name));
            nodes.Add(new(element.BackSurfaceId, element.BackZMillimeters, element.Back,
                rightContact ? materialByElement[topology.Elements[elementIndex + 1].Id].Name : "Air"));
        }
        // Only the non-refracting reference and independent stop may coincide.
        nodes = nodes.OrderBy(node => node.Z).ThenBy(node => node.Reference ? 0 : 1).ToList();
        var surfaces = new List<OpticalSurface> { new() { Label = "Object", Thickness = double.PositiveInfinity, Material = "Air" } };
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var thickness = index + 1 < nodes.Count ? nodes[index + 1].Z - node.Z : 0;
            Require(double.IsFinite(thickness) && thickness >= 0, TopologyFailureCode.AxialOrder, "Axial differences must be representable in Core.");
            surfaces.Add(new()
            {
                Label = node.Id,
                Thickness = thickness,
                Material = node.Material,
                IsStop = node.Stop,
                SemiDiameter = node.Face?.ClearSemiDiameterMillimeters ?? 1
            });
        }
        optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
        var indices = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var surface = optic.SurfaceGroup.Items[index + 1];
            indices.Add(node.Id, index + 1);
            surface.Geometry = node.Face is null ? new PlaneGeometry() : Geometry(node.Face.Profile);
            surface.PhysicalAperture = node.Face is null ? null : new CircularAperture(node.Face.ClearSemiDiameterMillimeters);
            surface.SemiDiameterFixed = true;
            if (node.Face?.MechanicalSemiDiameterMillimeters is { } mechanical) surface.MechanicalSemiDiameter = mechanical;
        }
        optic.SurfaceGroup.Items[0].PhysicalAperture = null;
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        optic.Aperture.Kind = ApertureKind.FloatByStopSize;
        optic.Aperture.Value = 2 * topology.Stop.ClearSemiDiameterMillimeters;
        optic.Solves.KeepImageAtBackFocus = false;
        optic.Fields.Clear();
        foreach (var field in topology.Fields) optic.Fields.Add(new() { Label = field.Label, XAngleDegrees = field.XAngleDegrees, YAngleDegrees = field.YAngleDegrees, Weight = field.Weight });
        optic.Wavelengths.Clear();
        foreach (var wave in topology.Wavelengths) optic.Wavelengths.Add(new() { Label = wave.Label, Nanometers = wave.Nanometers, Weight = wave.Weight, IsPrimary = wave.IsPrimary });
        optic.RayAimingEnabled = topology.RayAimingEnabled;
        optic.Environment.MatchRefractiveIndexData = topology.MatchRefractiveIndexData;
        optic.Environment.TemperatureCelsius = topology.TemperatureCelsius;
        optic.Environment.PressureAtmospheres = topology.PressureAtmospheres;
        optic.InvalidateRayTraceCache();
        var elementMap = topology.Elements.Select(element => new TopologyElementMapping(element.Id, indices[element.FrontSurfaceId], indices[element.BackSurfaceId])).ToImmutableArray();
        var snapshot = optic.ToSnapshot();
        var provenance = topology.Elements.Select(element => new TopologyMaterialProvenance(element.Id, element.Material,
            materialByElement[element.Id].Name, materialByElement[element.Id].Manufacturer,
            ContentFingerprint.Compute(snapshot.Surfaces[indices[element.FrontSurfaceId]].Components!.MaterialAfterComponent!))).ToImmutableArray();
        return new(topology, constraints, optic, indices.ToImmutable(), elementMap, provenance);

        CatalogGlassMaterial Resolve(string name)
        {
            Require(optic.Materials.TryResolve(name, topology.GlassCatalogs, out var material) && material is CatalogGlassMaterial,
                TopologyFailureCode.UnsupportedMaterial, "Topology V1 requires a resolvable real catalog glass: " + name);
            var catalog = (CatalogGlassMaterial)material;
            foreach (var wave in topology.Wavelengths.Where(wave => wave.Weight > 0))
                Require(double.IsFinite(catalog.RefractiveIndex(wave.Nanometers)) && catalog.RefractiveIndex(wave.Nanometers) > 0,
                    TopologyFailureCode.UnsupportedMaterial, "The catalog glass has no finite positive index for an active wavelength.");
            return catalog;
        }
    }

    public static TopologyCompilationResult TryCompile(DesignTopology topology, TopologyConstraints? constraints = null)
    {
        try { return new(Compile(topology, constraints), null); }
        catch (TopologyValidationException error) { return new(null, new(error.Code, error.Message)); }
    }

    /// <summary>Update the same topology from Core parameter results, preserving IDs and protection flags.</summary>
    public static CompiledTopology Update(CompiledTopology source, OpticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(source);
        var optic = SupportedOptic(snapshot);
        var originalSnapshot = source.Snapshot;
        var originalOptic = source.CreateOptic();
        Require(ContentFingerprint.Compute(snapshot with { Surfaces = [] }) == ContentFingerprint.Compute(originalSnapshot with { Surfaces = [] }),
            TopologyFailureCode.IdentityMismatch, "Changing evaluation settings requires a new compiled template.");
        Require(optic.SurfaceGroup.Items.Count == source.SurfaceIndices.Count + 1, TopologyFailureCode.IdentityMismatch, "An edited topology needs a new mapping, not an old optimizer state.");
        foreach (var pair in source.SurfaceIndices)
            Require(optic.SurfaceGroup.Items[pair.Value].Label == pair.Key, TopologyFailureCode.IdentityMismatch, "Stable surface identities changed.");
        var offset = source.GlobalToCoreZOffset;
        var refSurface = optic.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Reference.Id]];
        var stop = optic.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Stop.Id]];
        var image = optic.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Image.Id]];
        var originalStop = originalOptic.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Stop.Id]];
        var originalImage = originalOptic.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Image.Id]];
        var sameImageZ = image.CoordinateSystem.Origin.Z == originalImage.CoordinateSystem.Origin.Z;
        Require(refSurface.Geometry is PlaneGeometry && refSurface.PhysicalAperture is null && refSurface.MaterialAfter is AirMaterial,
            TopologyFailureCode.IdentityMismatch, "The mechanical reference cannot be replaced by a physical interface.");
        Require(optic.SurfaceGroup.Items.Count(surface => surface.IsStop) == 1 && stop.IsStop && stop.MaterialAfter is AirMaterial,
            TopologyFailureCode.IdentityMismatch, "The independent stop identity changed.");
        Require(refSurface.CoordinateSystem.Origin.Z == 0 && stop.CoordinateSystem.Origin.Z == originalStop.CoordinateSystem.Origin.Z,
            TopologyFailureCode.ProtectedElement, "The mechanical frame and independent stop position are fixed.");
        Require(!source.Topology.Image.Protected || sameImageZ,
            TopologyFailureCode.ProtectedElement, "The image plane is protected.");
        Require(PlaneEquivalent(stop.Geometry) && PlaneEquivalent(image.Geometry), TopologyFailureCode.UnsupportedProfile, "Stop and image planes must remain optically planar.");
        Require(Aperture(stop) == source.Topology.Stop.ClearSemiDiameterMillimeters
            && snapshot.Surfaces[source.SurfaceIndices[source.Topology.Stop.Id]].MechanicalSemiDiameter == source.Topology.Stop.MechanicalSemiDiameterMillimeters,
            TopologyFailureCode.ProtectedElement, "The independent stop aperture is fixed.");
        var elements = source.Topology.Elements.Select(element =>
        {
            var map = source.Elements.Single(mapping => mapping.ElementId == element.Id);
            var front = optic.SurfaceGroup.Items[map.FrontIndex];
            var back = optic.SurfaceGroup.Items[map.BackIndex];
            var originalFront = originalOptic.SurfaceGroup.Items[map.FrontIndex];
            var originalBack = originalOptic.SurfaceGroup.Items[map.BackIndex];
            var rightContact = source.Topology.CementedInterfaces?.Interfaces.Any(contact => contact.LeftElementId == element.Id) == true;
            Require(front.MaterialAfter is CatalogGlassMaterial && (rightContact ? back.MaterialAfter is CatalogGlassMaterial : back.MaterialAfter is AirMaterial),
                TopologyFailureCode.IdentityMismatch, "An element's material/air topology changed without rebuilding its mapping.");
            Require(ContentFingerprint.Compute(snapshot.Surfaces[map.FrontIndex].Components!.MaterialAfterComponent!)
                == ContentFingerprint.Compute(originalSnapshot.Surfaces[map.FrontIndex].Components!.MaterialAfterComponent!),
                TopologyFailureCode.IdentityMismatch, "A material branch requires a new compiled template and binding.");
            Require(ContentFingerprint.Compute(snapshot.Surfaces[map.BackIndex].Components!.MaterialAfterComponent!)
                == ContentFingerprint.Compute(originalSnapshot.Surfaces[map.BackIndex].Components!.MaterialAfterComponent!),
                TopologyFailureCode.IdentityMismatch, "The material after the back/shared interface changed without rebuilding its binding.");
            var sameFrontZ = front.CoordinateSystem.Origin.Z == originalFront.CoordinateSystem.Origin.Z;
            var sameBackZ = back.CoordinateSystem.Origin.Z == originalBack.CoordinateSystem.Origin.Z;
            var updated = element with
            {
                FrontZMillimeters = sameFrontZ ? element.FrontZMillimeters : front.CoordinateSystem.Origin.Z - offset,
                ThicknessMillimeters = sameFrontZ && sameBackZ ? element.ThicknessMillimeters : back.CoordinateSystem.Origin.Z - front.CoordinateSystem.Origin.Z,
                Front = UpdatedFace(element.Front, originalFront, front, snapshot.Surfaces[map.FrontIndex]),
                Back = UpdatedFace(element.Back, originalBack, back, snapshot.Surfaces[map.BackIndex])
            };
            if (element.Protected)
                Require(sameFrontZ && sameBackZ && ReferenceEquals(updated.Front, element.Front) && ReferenceEquals(updated.Back, element.Back),
                    TopologyFailureCode.ProtectedElement, "An updated parameter moved or reshaped a protected element.");
            return updated;
        }).ToImmutableArray();
        var model = source.Topology with
        {
            Elements = elements,
            Image = sameImageZ ? source.Topology.Image : source.Topology.Image with { ZMillimeters = image.CoordinateSystem.Origin.Z - offset }
        };
        return Compile(model, source.Constraints);
    }

    /// <summary>Import centered, stop-sized, infinite-conjugate catalog singlets and explicit shared glass interfaces.</summary>
    public static CompiledTopology Import(OpticSnapshot snapshot, TopologyConstraints? constraints = null, double referenceZMillimeters = 0)
    {
        var optic = SupportedOptic(snapshot);
        var surfaces = optic.SurfaceGroup.Items;
        Require(double.IsFinite(referenceZMillimeters), TopologyFailureCode.InvalidInput, "A finite mechanical frame origin is required.");
        var stopIndex = surfaces.ToList().FindIndex(surface => surface.IsStop);
        Require(stopIndex > 0 && stopIndex < surfaces.Count - 1 && surfaces.Count(surface => surface.IsStop) == 1,
            TopologyFailureCode.UnsupportedFeature, "Exactly one independent circular stop is required.");
        var stop = surfaces[stopIndex];
        Require(PlaneEquivalent(stop.Geometry) && stop.MaterialBefore is AirMaterial && stop.MaterialAfter is AirMaterial,
            TopologyFailureCode.UnsupportedFeature, "The stop must be a planar independent interface in air.");
        var elements = ImmutableArray.CreateBuilder<LensElement>();
        var contacts = ImmutableArray.CreateBuilder<TopologyCementedInterface>();
        var continuedGlass = false;
        var importedMaterials = new List<string>();
        var normalizedSnapshot = optic.ToSnapshot();
        for (var index = 1; index + 1 < surfaces.Count; index++)
        {
            var front = surfaces[index];
            if (index == stopIndex) continue;
            if (index == 1 && PlaneEquivalent(front.Geometry) && front.PhysicalAperture is null && front.MaterialAfter is AirMaterial) continue;
            Require((continuedGlass ? front.MaterialBefore is CatalogGlassMaterial : front.MaterialBefore is AirMaterial)
                && front.MaterialAfter is CatalogGlassMaterial && index + 2 < surfaces.Count,
                TopologyFailureCode.UnsupportedFeature, "Only unambiguous catalog glass regions and shared cemented interfaces can be imported.");
            var back = surfaces[index + 1];
            Require(!back.IsStop && back.MaterialAfter is AirMaterial or CatalogGlassMaterial && back.MaterialBefore.Name == front.MaterialAfter.Name,
                TopologyFailureCode.UnsupportedFeature, "Interrupted or incorrectly connected glass regions are not supported.");
            var id = "element-" + (elements.Count + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            var frontId = continuedGlass ? elements[^1].BackSurfaceId : id + ":front";
            if (continuedGlass) contacts.Add(new(elements[^1].Id, id, frontId));
            elements.Add(new()
            {
                Id = id,
                FrontSurfaceId = frontId,
                BackSurfaceId = id + ":back",
                FrontZMillimeters = front.CoordinateSystem.Origin.Z + referenceZMillimeters,
                ThicknessMillimeters = back.CoordinateSystem.Origin.Z - front.CoordinateSystem.Origin.Z,
                Material = front.MaterialAfter.Name,
                Front = Face(front, snapshot.Surfaces[index]),
                Back = Face(back, snapshot.Surfaces[index + 1])
            });
            importedMaterials.Add(ContentFingerprint.Compute(normalizedSnapshot.Surfaces[index].Components!.MaterialAfterComponent!));
            continuedGlass = back.MaterialAfter is CatalogGlassMaterial;
            if (!continuedGlass) index++;
        }
        var model = new DesignTopology
        {
            Name = snapshot.Name,
            Reference = new("reference", referenceZMillimeters),
            Stop = new("stop", stop.CoordinateSystem.Origin.Z + referenceZMillimeters, Aperture(stop), snapshot.Surfaces[stopIndex].MechanicalSemiDiameter),
            Image = new("image", surfaces[^1].CoordinateSystem.Origin.Z + referenceZMillimeters),
            Elements = elements.ToImmutable(),
            CementedInterfaces = contacts.Count == 0 ? null : new() { Interfaces = contacts.ToImmutable() },
            Fields = optic.Fields.Select(field => new TopologyField(field.Label, field.XAngleDegrees, field.YAngleDegrees, field.Weight)).ToImmutableArray(),
            Wavelengths = optic.Wavelengths.Select(wave => new WavelengthSpecification { Label = wave.Label, Nanometers = wave.Nanometers, Weight = wave.Weight, IsPrimary = wave.IsPrimary }).ToImmutableArray(),
            GlassCatalogs = optic.GlassCatalogs.ToImmutableArray(),
            RayAimingEnabled = optic.RayAimingEnabled,
            MatchRefractiveIndexData = optic.Environment.MatchRefractiveIndexData,
            TemperatureCelsius = optic.Environment.TemperatureCelsius,
            PressureAtmospheres = optic.Environment.PressureAtmospheres
        };
        var compiled = Compile(model, constraints);
        Require(compiled.Materials.Select(material => material.ComponentFingerprint).SequenceEqual(importedMaterials),
            TopologyFailureCode.UnsupportedMaterial, "The imported material data differ from the installed catalog; a name-only substitution is not allowed.");
        return compiled;
    }

    private static Optic SupportedOptic(OpticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Require(snapshot.Surfaces is { Count: >= 4 } && snapshot.Fields is not null && snapshot.Wavelengths is not null,
            TopologyFailureCode.InvalidInput, "A complete Core snapshot is required.");
        Require(double.IsPositiveInfinity(snapshot.Surfaces[0].Thickness), TopologyFailureCode.UnsupportedConjugate, "Finite-conjugate imports require a separate adapter.");
        Require(snapshot.FieldDefinition == "Angle" && !snapshot.ImageSpaceAfocal && !snapshot.ObjectSpaceTelecentric && !snapshot.FieldGroupTelecentric
            && snapshot.Apodization is null && snapshot.ReferenceOpd is null
            && snapshot.RadiusPickups is not { Count: > 0 } && snapshot.ThicknessPickups is not { Count: > 0 } && snapshot.SemiDiameterPickups is not { Count: > 0 }
            && snapshot.MeritOperands is not { Count: > 0 } && (snapshot.Polarization is null || snapshot.Polarization.Unpolarized)
            && snapshot.SolveSettings?.KeepImageAtBackFocus == false && snapshot.GlobalReferenceSurfaceNumber == 1
            && snapshot.Aperture is { ObjectSpaceTelecentric: false }
            && snapshot.Surfaces.All(surface => !surface.RadiusVariable && !surface.ThicknessVariable),
            TopologyFailureCode.UnsupportedFeature, "This topology adapter cannot discard field, pupil, polarization, operand or pickup features.");
        Require(snapshot.Fields.All(field => field.VignetteFactorX == 0 && field.VignetteFactorY == 0 && field.VignetteDecenterX == 0 && field.VignetteDecenterY == 0 && field.VignetteAngleDegrees == 0),
            TopologyFailureCode.UnsupportedFeature, "Field vignetting metadata requires a separate adapter.");
        var optic = Optic.FromSnapshot(snapshot);
        Require(optic.Aperture.Kind == ApertureKind.FloatByStopSize, TopologyFailureCode.UnsupportedFeature, "The first topology contract requires an actual stop-sized pupil.");
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            var coordinates = surface.CoordinateSystem;
            Require(coordinates.Origin.X == 0 && coordinates.Origin.Y == 0 && coordinates.RotationXDegrees == 0 && coordinates.RotationYDegrees == 0 && coordinates.RotationZDegrees == 0,
                TopologyFailureCode.UnsupportedFeature, "Decentered or tilted surfaces require a separate topology adapter.");
            Require(surface.Geometry is PlaneGeometry or StandardGeometry or EvenAsphereGeometry
                && surface.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: false }
                && surface.CoatingModel is NoneCoatingModel && surface.ScatteringModel is null && surface.ChipZone == 0
                && (surface.ThermalExpansionPpmPerC is null or 0) && surface.MechanicalSemiDiameterSolveCode == 0,
                TopologyFailureCode.UnsupportedFeature, "The first adapter supports plain centered refractive surfaces without unmodeled solves, coatings or scattering.");
        }
        Require(PlaneEquivalent(optic.SurfaceGroup.Items[0].Geometry) && optic.SurfaceGroup.Items[0].PhysicalAperture is null
            && optic.SurfaceGroup.Items[0].MaterialAfter is AirMaterial && PlaneEquivalent(optic.SurfaceGroup.Items[^1].Geometry)
            && optic.SurfaceGroup.Items[^1].PhysicalAperture is null && optic.SurfaceGroup.Items[^1].MaterialAfter is AirMaterial,
            TopologyFailureCode.UnsupportedFeature, "Object and image planes must remain non-clipping reference planes in air.");
        return optic;
    }

    private static IGeometry Geometry(TopologyProfile profile)
    {
        var radius = profile.CurvaturePerMillimeter == 0 ? 0 : 1 / profile.CurvaturePerMillimeter;
        IGeometry result = profile.Kind switch
        {
            TopologyProfileKind.Plane => new PlaneGeometry(),
            TopologyProfileKind.Standard => new StandardGeometry(radius, profile.Conic),
            _ => new EvenAsphereGeometry(radius, profile.Conic, Coefficients(profile))
        };
        var actual = OpticalDesignParameters.Profile(result).Curvature;
        Require(actual == profile.CurvaturePerMillimeter || Math.Abs(actual - profile.CurvaturePerMillimeter) <= 8e-15 * Math.Abs(profile.CurvaturePerMillimeter),
            TopologyFailureCode.InvalidProfile, "The requested curvature loses its meaning in the Core radius representation.");
        return result;
    }

    private static double[] Coefficients(TopologyProfile profile)
    {
        var result = new double[profile.MaximumEvenPower / 2];
        foreach (var term in profile.Terms) result[term.Power / 2 - 1] = term.Coefficient;
        return result;
    }

    private static TopologyFace Face(OpticalSurface surface, SurfaceSnapshot snapshot)
    {
        var profile = OpticalDesignParameters.Profile(surface.Geometry);
        Require(profile.Coefficients.Length <= 9 && (profile.Coefficients.Length == 0 || profile.Coefficients[0] == 0),
            TopologyFailureCode.UnsupportedProfile, "The first adapter supports even terms r4 through r18 with zero r2 only.");
        var even = surface.Geometry is EvenAsphereGeometry;
        Require(!even || profile.Coefficients.Length >= 2, TopologyFailureCode.UnsupportedProfile, "An even-asphere requires an explicit supported order.");
        return new()
        {
            ClearSemiDiameterMillimeters = Aperture(surface),
            MechanicalSemiDiameterMillimeters = snapshot.MechanicalSemiDiameter,
            Profile = new()
            {
                Kind = surface.Geometry is PlaneGeometry ? TopologyProfileKind.Plane : even ? TopologyProfileKind.EvenAsphere : TopologyProfileKind.Standard,
                CurvaturePerMillimeter = profile.Curvature,
                Conic = profile.Conic,
                MaximumEvenPower = even ? 2 * profile.Coefficients.Length : 0,
                Terms = even ? profile.Coefficients.Select((value, index) => new TopologyAsphereTerm(2 * (index + 1), value)).Skip(1).ToImmutableArray() : []
            }
        };
    }

    private static double Aperture(OpticalSurface surface)
    {
        Require(surface.PhysicalAperture is CircularAperture, TopologyFailureCode.InvalidAperture, "Each optical face and independent stop requires an explicit circular physical aperture.");
        var radius = ((CircularAperture)surface.PhysicalAperture).Radius;
        Require(surface.SemiDiameter == radius, TopologyFailureCode.InvalidAperture, "Physical and declared clear semi-diameters must agree in this contract.");
        return radius;
    }

    private static bool PlaneEquivalent(IGeometry geometry)
    {
        var profile = OpticalDesignParameters.Profile(geometry);
        return geometry is PlaneGeometry || profile.Curvature == 0 && profile.Conic == 0 && profile.Coefficients.All(value => value == 0);
    }

    private static TopologyFace UpdatedFace(TopologyFace original, OpticalSurface before, OpticalSurface after, SurfaceSnapshot snapshot)
    {
        var oldProfile = OpticalDesignParameters.Profile(before.Geometry);
        var newProfile = OpticalDesignParameters.Profile(after.Geometry);
        var sameShape = oldProfile.Curvature == newProfile.Curvature && oldProfile.Conic == newProfile.Conic
            && oldProfile.Coefficients.SequenceEqual(newProfile.Coefficients);
        var updated = Face(after, snapshot);
        // A Core axial-only update may represent a flat plane as Standard(0). Preserve the declared profile branch.
        Require(updated.Profile.Kind == original.Profile.Kind || original.Profile.Kind == TopologyProfileKind.Plane && PlaneEquivalent(after.Geometry),
            TopologyFailureCode.IdentityMismatch, "Changing profile type requires a new template and binding.");
        Require(updated.Profile.MaximumEvenPower == original.Profile.MaximumEvenPower,
            TopologyFailureCode.IdentityMismatch, "Changing polynomial order requires a new template and binding.");
        if (sameShape && original.ClearSemiDiameterMillimeters == updated.ClearSemiDiameterMillimeters
            && original.MechanicalSemiDiameterMillimeters == updated.MechanicalSemiDiameterMillimeters) return original;
        return updated with
        {
            Profile = sameShape ? original.Profile : updated.Profile with
            {
                Terms = updated.Profile.Terms.Where(term => term.Coefficient != 0 || original.Profile.Terms.Any(old => old.Power == term.Power)).ToImmutableArray()
            }
        };
    }
}
