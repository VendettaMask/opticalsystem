using System.Collections.Immutable;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed record TopologyGeometryRow(string Id, int First, int Second, double Radius, double Limit, bool Upper = false, bool Slope = false);

internal static class TopologyEvaluationPlan
{
    internal static void Validate(TopologyTargets targets, TopologyTrainingSampling sampling)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(sampling);
        double[] positive = [targets.EffectiveFocalLengthMillimeters, targets.FNumber, targets.FocalLengthRelativeTolerance,
            targets.FNumberRelativeTolerance, targets.MaximumRmsRadiusMillimeters, targets.MaximumSpotRadiusMillimeters,
            targets.MaximumDistortionFraction, targets.MinimumEdgeThicknessMillimeters, targets.MaximumOpticalTrackMillimeters,
            targets.MaximumAbsoluteSurfaceSlope];
        Require(positive.All(value => double.IsFinite(value) && value > 0) && double.IsFinite(targets.MinimumEdgeAirGapMillimeters)
            && targets.MinimumEdgeAirGapMillimeters >= 0 && double.IsFinite(targets.MinimumValidRayFraction)
            && targets.MinimumValidRayFraction is > 0 and <= 1, TopologyFailureCode.InvalidInput, "Invalid physical targets.");
        Require(sampling.FieldsPerDirection is >= 2 and <= 21 && sampling.PupilRings is >= 1 and <= 10
            && sampling.PupilArms is >= 4 and <= 32 && sampling.ProfileRadialSamples is >= 4 and <= 64,
            TopologyFailureCode.InvalidInput, "The fixed sampling protocol exceeds its supported bounds.");
        Require(sampling.IncludeBoundaryMaximum != true || sampling.IncludeBoundaryClearance == true,
            TopologyFailureCode.InvalidInput, "Common-centroid check maximum requires explicit independent check rays.");
    }

    internal static ImmutableArray<TopologyFieldCoordinate> Fields(CompiledTopology source, int samples)
    {
        var optic = source.CreateOptic();
        var fields = new List<TopologyFieldCoordinate> { new(0, 0) };
        foreach (var defined in optic.Fields.Where(field => field.Weight > 0))
        {
            var point = FieldCoordinates.Normalize(optic.Fields, defined.X, defined.Y);
            for (var index = 1; index < samples; index++)
                fields.Add(new(point.X * index / (samples - 1), point.Y * index / (samples - 1)));
        }
        var result = fields.Distinct().ToImmutableArray();
        Require(result.Length <= 101, TopologyFailureCode.InvalidInput, "This task requires more than 101 explicit validation fields; split the protocol explicitly.");
        return result;
    }

    internal static ImmutableArray<TopologyGeometryRow> Geometry(CompiledTopology source, TopologyTargets targets, int points)
    {
        var rows = ImmutableArray.CreateBuilder<TopologyGeometryRow>();
        var bodies = new Dictionary<int, double>();
        foreach (var element in source.Topology.Elements)
        {
            var mapping = source.Elements.Single(item => item.ElementId == element.Id);
            var radius = Math.Max(element.Front.MechanicalSemiDiameterMillimeters ?? element.Front.ClearSemiDiameterMillimeters,
                element.Back.MechanicalSemiDiameterMillimeters ?? element.Back.ClearSemiDiameterMillimeters);
            bodies[mapping.FrontIndex] = Math.Max(bodies.GetValueOrDefault(mapping.FrontIndex), radius);
            bodies[mapping.BackIndex] = Math.Max(bodies.GetValueOrDefault(mapping.BackIndex), radius);
            var allowance = element.Transition && source.Constraints.TransitionAllowance?.ElementId == element.Id ? source.Constraints.TransitionAllowance : null;
            rows.Add(new(element.Id + ":center", mapping.FrontIndex, mapping.BackIndex, 0,
                allowance?.MinimumCenterThicknessMillimeters ?? source.Constraints.MinimumCenterThicknessMillimeters));
            for (var sample = 1; sample <= points; sample++)
            {
                var radial = radius * sample / points;
                rows.Add(new(element.Id + ":edge:" + sample, mapping.FrontIndex, mapping.BackIndex, radial,
                    allowance?.MinimumEdgeThicknessMillimeters ?? targets.MinimumEdgeThicknessMillimeters));
                var scope = source.Topology.CementedInterfaces is null ? "" : element.Id + ":";
                rows.Add(new(scope + element.FrontSurfaceId + ":slope:" + sample, mapping.FrontIndex, mapping.FrontIndex, radial, targets.MaximumAbsoluteSurfaceSlope, true, true));
                rows.Add(new(scope + element.BackSurfaceId + ":slope:" + sample, mapping.BackIndex, mapping.BackIndex, radial, targets.MaximumAbsoluteSurfaceSlope, true, true));
            }
        }
        var stopIndex = source.SurfaceIndices[source.Topology.Stop.Id];
        bodies[stopIndex] = source.Topology.Stop.MechanicalSemiDiameterMillimeters ?? source.Topology.Stop.ClearSemiDiameterMillimeters;
        var optic = source.CreateOptic();
        var physical = bodies.Keys.Order().ToArray();
        for (var index = 0; index + 1 < physical.Length; index++)
        {
            var first = physical[index]; var second = physical[index + 1];
            if (optic.SurfaceGroup.Items[first].MaterialAfter is not Core.Materials.AirMaterial) continue;
            var radius = Math.Min(bodies[first], bodies[second]);
            for (var sample = 0; sample <= points; sample++)
                rows.Add(new($"air:{first}:{second}:{sample}", first, second, radius * sample / points,
                    sample == 0 ? source.Constraints.MinimumAirGapMillimeters : targets.MinimumEdgeAirGapMillimeters));
        }
        var last = source.Elements[^1].BackIndex;
        var image = source.SurfaceIndices[source.Topology.Image.Id];
        for (var sample = 0; sample <= points; sample++)
            rows.Add(new("back-focus:" + sample, last, image, bodies[last] * sample / points,
                sample == 0 ? source.Constraints.MinimumBackFocusMillimeters : 0));
        rows.Add(new("mechanical-track", source.SurfaceIndices[source.Topology.Reference.Id], image, 0, source.Constraints.MaximumMechanicalTrackMillimeters, true));
        rows.Add(new("optical-track", source.Elements[0].FrontIndex, image, 0, targets.MaximumOpticalTrackMillimeters, true));
        return rows.ToImmutable();
    }

    internal static DifferentialScalar Measure(Core.Optic optic, IReadOnlyList<OpticalDesignParameter> parameters, TopologyGeometryRow row) => row.Slope
        ? DifferentialScalar.Abs(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, row.First, row.Radius))
        : DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, row.First, row.Second, row.Radius);

    internal static double FormalMeasure(Core.Optic optic, TopologyGeometryRow row) => row.Slope
        ? Measure(optic, [], row).Value : AxialSurfaceSeparation.EvaluateBetween(optic, row.First, row.Second, row.Radius, 0);

    internal static bool DomainError(Exception error) => error is ArithmeticException or InvalidOperationException or NotSupportedException or ArgumentException
        || Core.Raytrace.SequentialTraceBudgetExceededException.IsBudgetException(error);

    internal static PupilSample[] Checks(int arms = 32) => [new(0, 0, 1), .. Enumerable.Range(0, arms)
        .Select(index => new PupilSample(Math.Cos(2 * Math.PI * index / arms), Math.Sin(2 * Math.PI * index / arms), 1))];
}
