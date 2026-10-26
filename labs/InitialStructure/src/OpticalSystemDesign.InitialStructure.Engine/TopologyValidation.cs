using System.Diagnostics.CodeAnalysis;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Structural contract only. No tracing, paraxial approximation or optical acceptance.</summary>
public static class TopologyValidation
{
    public static void Validate(DesignTopology topology, TopologyConstraints? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        constraints ??= new();
        ValidateConstraints(constraints);
        Require(topology.SchemaVersion == AutonomousTopologyIdentity.SchemaVersion && topology.Algorithm == AutonomousTopologyIdentity.Name,
            TopologyFailureCode.InvalidIdentity, "The topology does not belong to AutonomousTopologyV1 schema 1.");
        Require(topology.Conjugate == ObjectConjugateMode.Infinite, TopologyFailureCode.UnsupportedConjugate, "The first topology contract supports infinite object conjugates only.");
        Require(Identifier(topology.Name, InitialStructureLimits.MaximumNameLength) && topology.Reference is not null && topology.Stop is not null && topology.Image is not null,
            TopologyFailureCode.InvalidInput, "A topology name and independent reference, stop and image planes are required.");
        Require(!topology.Elements.IsDefault && topology.Elements.Length >= constraints.MinimumElementCount && topology.Elements.Length <= constraints.MaximumElementCount,
            TopologyFailureCode.ElementLimit, "The topology element count is outside the requested bounds.");
        Require(double.IsFinite(topology.Reference.ZMillimeters) && double.IsFinite(topology.Stop.ZMillimeters) && double.IsFinite(topology.Image.ZMillimeters)
            && double.IsFinite(topology.MechanicalTrackMillimeters) && topology.MechanicalTrackMillimeters > 0 && topology.MechanicalTrackMillimeters <= constraints.MaximumMechanicalTrackMillimeters,
            TopologyFailureCode.TrackLength, "The mechanical reference and image must define a finite positive bounded track.");
        Require(topology.Stop.ZMillimeters >= topology.Reference.ZMillimeters && topology.Stop.ZMillimeters < topology.Image.ZMillimeters,
            TopologyFailureCode.AxialOrder, "The independent stop must lie between the reference and image planes.");
        Aperture(topology.Stop.ClearSemiDiameterMillimeters, topology.Stop.MechanicalSemiDiameterMillimeters);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        AddId(topology.Reference.Id); AddId(topology.Stop.Id); AddId(topology.Image.Id);
        double? previousBack = null;
        foreach (var element in topology.Elements)
        {
            Require(element is not null && element.Front is not null && element.Back is not null && Identifier(element.Material, InitialStructureLimits.MaximumIdentifierLength),
                TopologyFailureCode.InvalidInput, "Every element requires two faces and a material name.");
            AddId(element.Id); AddId(element.FrontSurfaceId); AddId(element.BackSurfaceId);
            Require(!element.Transition || constraints.AllowTransitionElements, TopologyFailureCode.TransitionNotAllowed, "Transition elements require an explicit exploration constraint.");
            var scoped = element.Transition && constraints.TransitionAllowance?.ElementId == element.Id ? constraints.TransitionAllowance : null;
            Require(constraints.TransitionAllowance is null || !element.Transition || scoped is not null,
                TopologyFailureCode.TransitionNotAllowed, "Only the explicitly named target may use a transition exception.");
            Require(double.IsFinite(element.FrontZMillimeters) && double.IsFinite(element.ThicknessMillimeters) && double.IsFinite(element.BackZMillimeters)
                && element.ThicknessMillimeters > 0 && element.BackZMillimeters > element.FrontZMillimeters
                && (scoped is not null ? element.ThicknessMillimeters >= scoped.MinimumCenterThicknessMillimeters
                    : element.Transition && constraints.TransitionAllowance is null || element.ThicknessMillimeters >= constraints.MinimumCenterThicknessMillimeters),
                TopologyFailureCode.CenterThickness, "An element has nonrepresentable, nonpositive or insufficient center thickness.");
            Require(element.FrontZMillimeters >= topology.Reference.ZMillimeters && element.BackZMillimeters < topology.Image.ZMillimeters,
                TopologyFailureCode.AxialOrder, "Element vertices must lie within the mechanical reference and image interval.");
            if (previousBack.HasValue)
                Require(element.FrontZMillimeters > previousBack.Value && element.FrontZMillimeters - previousBack.Value >= constraints.MinimumAirGapMillimeters,
                    TopologyFailureCode.AirGap, "Elements must already be in nonintersecting axial order with the requested air gap.");
            Require(topology.Stop.ZMillimeters < element.FrontZMillimeters || topology.Stop.ZMillimeters > element.BackZMillimeters,
                TopologyFailureCode.StopInsideElement, "An independent stop cannot lie in or coincide with a glass element.");
            Aperture(element.Front.ClearSemiDiameterMillimeters, element.Front.MechanicalSemiDiameterMillimeters);
            Aperture(element.Back.ClearSemiDiameterMillimeters, element.Back.MechanicalSemiDiameterMillimeters);
            Profile(element.Front.Profile); Profile(element.Back.Profile);
            previousBack = element.BackZMillimeters;
        }
        Require(topology.Image.ZMillimeters - previousBack!.Value >= constraints.MinimumBackFocusMillimeters,
            TopologyFailureCode.BackFocus, "The last real lens must leave the requested back working space.");
        foreach (var interval in AirIntervalsUnchecked(topology))
            if (interval.LeftSurfaceId == topology.Stop.Id || interval.RightSurfaceId == topology.Stop.Id)
                Require(interval.LengthMillimeters == 0 && interval.LeftSurfaceId == topology.Reference.Id
                    || interval.LengthMillimeters >= constraints.MinimumAirGapMillimeters,
                    TopologyFailureCode.AirGap, "The independent stop splits an air region below the requested gap.");
        Require(!topology.Fields.IsDefaultOrEmpty && topology.Fields.Length <= 101
            && topology.Fields.All(field => field is not null && Identifier(field.Label, InitialStructureLimits.MaximumNameLength)
                && double.IsFinite(field.XAngleDegrees) && Math.Abs(field.XAngleDegrees) < 89
                && double.IsFinite(field.YAngleDegrees) && Math.Abs(field.YAngleDegrees) < 89 && double.IsFinite(field.Weight) && field.Weight >= 0)
            && topology.Fields.Any(field => field.Weight > 0), TopologyFailureCode.InvalidInput, "Finite angular fields with an active weight are required.");
        Require(!topology.Wavelengths.IsDefaultOrEmpty && topology.Wavelengths.Length <= InitialStructureLimits.MaximumWavelengthCount
            && topology.Wavelengths.All(wave => wave is not null && Identifier(wave.Label, InitialStructureLimits.MaximumNameLength)
                && double.IsFinite(wave.Nanometers) && wave.Nanometers >= 1 && double.IsFinite(wave.Weight) && wave.Weight >= 0)
            && topology.Wavelengths.Count(wave => wave.IsPrimary && wave.Weight > 0) == 1
            && topology.Wavelengths.Count(wave => wave.IsPrimary) == 1,
            TopologyFailureCode.InvalidInput, "An active primary wavelength and finite spectral data are required.");
        Require(!topology.GlassCatalogs.IsDefault && topology.GlassCatalogs.Length <= InitialStructureLimits.MaximumGlassCatalogCount
            && topology.GlassCatalogs.All(name => Identifier(name, InitialStructureLimits.MaximumIdentifierLength)), TopologyFailureCode.InvalidInput, "Invalid glass catalog list.");
        Require(double.IsFinite(topology.TemperatureCelsius) && double.IsFinite(topology.PressureAtmospheres) && topology.PressureAtmospheres >= 0,
            TopologyFailureCode.InvalidInput, "Environment values must be finite with nonnegative pressure.");

        void AddId(string id)
        {
            Require(Identifier(id, InitialStructureLimits.MaximumIdentifierLength), TopologyFailureCode.InvalidIdentity, "Every topology item requires a bounded stable identifier.");
            Require(ids.Add(id), TopologyFailureCode.DuplicateId, "Topology element and surface identifiers must be unique.");
        }
    }

    public static IReadOnlyList<TopologyAirInterval> AirIntervals(DesignTopology topology, TopologyConstraints? constraints = null)
    {
        Validate(topology, constraints);
        return AirIntervalsUnchecked(topology).AsReadOnly();
    }

    private static List<TopologyAirInterval> AirIntervalsUnchecked(DesignTopology topology)
    {
        var intervals = new List<TopologyAirInterval>();
        var left = topology.Reference.Id;
        var position = topology.Reference.ZMillimeters;
        foreach (var element in topology.Elements)
        {
            Add(element.FrontSurfaceId, element.FrontZMillimeters);
            left = element.BackSurfaceId;
            position = element.BackZMillimeters;
        }
        Add(topology.Image.Id, topology.Image.ZMillimeters);
        return intervals;

        void Add(string right, double end)
        {
            if (topology.Stop.ZMillimeters >= position && topology.Stop.ZMillimeters <= end)
            {
                intervals.Add(new(left, topology.Stop.Id, position, topology.Stop.ZMillimeters));
                intervals.Add(new(topology.Stop.Id, right, topology.Stop.ZMillimeters, end));
            }
            else intervals.Add(new(left, right, position, end));
        }
    }

    private static void Profile(TopologyProfile profile)
    {
        Require(profile is not null && Enum.IsDefined(profile.Kind) && double.IsFinite(profile.CurvaturePerMillimeter)
            && double.IsFinite(profile.Conic) && !profile.Terms.IsDefault && profile.Terms.Length <= 8,
            TopologyFailureCode.InvalidProfile, "A finite supported surface profile is required.");
        Require(profile.CurvaturePerMillimeter == 0 || double.IsFinite(1 / profile.CurvaturePerMillimeter), TopologyFailureCode.InvalidProfile, "Curvature cannot be represented as a Core radius.");
        Require(profile.Kind != TopologyProfileKind.Plane || profile.CurvaturePerMillimeter == 0 && profile.Conic == 0,
            TopologyFailureCode.InvalidProfile, "A plane profile cannot contain curvature or conic terms.");
        if (profile.Kind == TopologyProfileKind.EvenAsphere)
            Require(profile.MaximumEvenPower is >= 4 and <= AutonomousTopologyIdentity.MaximumEvenPower && profile.MaximumEvenPower % 2 == 0,
                TopologyFailureCode.UnsupportedProfile, "Even-asphere order must be explicit and lie between r4 and r18.");
        else Require(profile.MaximumEvenPower == 0 && profile.Terms.IsEmpty, TopologyFailureCode.InvalidProfile, "Polynomial terms require an explicit even-asphere profile.");
        Require(profile.Terms.All(term => term is not null && term.Power >= 4 && term.Power <= profile.MaximumEvenPower && term.Power % 2 == 0 && double.IsFinite(term.Coefficient))
            && profile.Terms.Select(term => term.Power).Distinct().Count() == profile.Terms.Length,
            TopologyFailureCode.InvalidProfile, "Even terms require unique explicit powers; r2 and terms outside the declared order are unsupported.");
    }

    private static void Aperture(double clear, double? mechanical) => Require(
        double.IsFinite(clear) && clear >= AutonomousTopologyIdentity.MinimumPhysicalSemiDiameter
        && (mechanical is null || double.IsFinite(mechanical.Value) && mechanical.Value >= clear),
        TopologyFailureCode.InvalidAperture, "Physical apertures must be representable and fit within their mechanical semi-diameters.");

    private static void ValidateConstraints(TopologyConstraints constraints) => Require(
        constraints.MinimumElementCount >= 1 && constraints.MaximumElementCount >= constraints.MinimumElementCount
        && constraints.MaximumElementCount <= AutonomousTopologyIdentity.MaximumElements
        && Positive(constraints.MinimumCenterThicknessMillimeters) && Positive(constraints.MinimumAirGapMillimeters)
        && Positive(constraints.MinimumBackFocusMillimeters) && Positive(constraints.MaximumMechanicalTrackMillimeters)
        && !constraints.AllowedGlassNames.IsDefault && constraints.AllowedGlassNames.Length <= 4096
        && constraints.AllowedGlassNames.All(name => Identifier(name, InitialStructureLimits.MaximumIdentifierLength))
        && (constraints.TransitionAllowance is null || constraints.AllowTransitionElements
            && Identifier(constraints.TransitionAllowance.ElementId, InitialStructureLimits.MaximumIdentifierLength)
            && Positive(constraints.TransitionAllowance.MinimumCenterThicknessMillimeters)
            && constraints.TransitionAllowance.MinimumCenterThicknessMillimeters <= constraints.MinimumCenterThicknessMillimeters
            && Positive(constraints.TransitionAllowance.MinimumEdgeThicknessMillimeters)),
        TopologyFailureCode.InvalidInput, "Invalid topology constraints or allowed material list.");

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Identifier(string? value, int length) => !string.IsNullOrWhiteSpace(value) && value.Length <= length && !value.Any(char.IsControl);
    internal static void Require([DoesNotReturnIf(false)] bool condition, TopologyFailureCode code, string message)
    {
        if (!condition) throw new TopologyValidationException(code, message);
    }
}
