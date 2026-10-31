using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class PupilEnvelopeTests
{
    [Fact]
    public void FixedStopEnvelopeMatchesCoreNominalParaxialTraceAndEverySelectedDerivative()
    {
        var (plan, _) = StructureEvolutionTests.Specification();
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var variables = source.Topology.Elements.SelectMany(e => new[] { e.FrontSurfaceId, e.BackSurfaceId })
            .Select(id => new TopologyVariableRequest(id, OpticalDesignParameterKind.Curvature, -.2, .2, .01)).ToArray();
        var binding = TopologyParameterBinder.Bind(source, variables);
        var optic = source.CreateOptic(); var input = source.SurfaceIndices[source.Topology.Reference.Id];
        var wavelength = optic.Paraxial.GetTransferMatrix(input, source.SurfaceIndices[source.Topology.Image.Id]).WavelengthNanometers;
        using var measurement = SequentialTraceMeasurement.Begin();
        var result = DifferentiablePupilEnvelopes.Evaluate(optic, binding.CoreParameters, input, [0, .5, 1], wavelength);
        Assert.Equal(2 + result.Samples.Select(s => s.SurfaceIndex).Distinct().Count(), result.MatrixEvaluations);
        Assert.NotEmpty(result.Samples);
        foreach (var field in new[] { 0.0, .5, 1.0 })
        {
            var nominal = optic.Paraxial.TraceNormalizedPupil(field, [-1, 1], wavelength / 1000);
            foreach (var group in result.Samples.Where(s => s.NormalizedField == field).GroupBy(s => s.SurfaceIndex))
            {
                var expected = nominal.Heights[group.Key].Order().ToArray();
                var actual = group.Select(s => s.HeightMillimeters.Value).Order().ToArray();
                Assert.Equal(expected.Length, actual.Length);
                for (var j = 0; j < actual.Length; j++) Assert.Equal(expected[j], actual[j], 8);
            }
        }
        const double h = 1e-5;
        for (var j = 0; j < binding.Slots.Length; j++)
        {
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[j] += h; minus[j] -= h;
            var a = DifferentiablePupilEnvelopes.Evaluate(TopologyParameterBinder.Apply(source, binding, plus).CreateOptic(),
                binding.CoreParameters, input, [0, .5, 1], wavelength);
            var b = DifferentiablePupilEnvelopes.Evaluate(TopologyParameterBinder.Apply(source, binding, minus).CreateOptic(),
                binding.CoreParameters, input, [0, .5, 1], wavelength);
            for (var i = 0; i < result.Samples.Count; i++)
            {
                var fd = (a.Samples[i].HeightMillimeters.Value - b.Samples[i].HeightMillimeters.Value) / (2 * h);
                Assert.InRange(Math.Abs(fd - result.Samples[i].HeightMillimeters.Derivative(j)), 0, 1e-5 * Math.Max(1, Math.Abs(fd)));
            }
        }
        Assert.Equal(0, measurement.RayCount);
    }

    [Fact]
    public void EnvelopeRecoveryIsChargedAndKeepsTheOriginalPhysicalFrameWithoutCertifyingTransmission()
    {
        var (plan, _) = StructureEvolutionTests.Specification();
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var requests = source.Topology.Elements.SelectMany(e => new[] { e.FrontSurfaceId, e.BackSurfaceId })
            .Select(id => new TopologyVariableRequest(id, OpticalDesignParameterKind.Curvature, -.2, .2, .01)).ToArray();
        var binding = TopologyParameterBinder.Bind(source, requests, exactMechanicalFrame: true);
        var flat = TopologyParameterBinder.Apply(source, binding, new double[requests.Length]);
        using var measurement = SequentialTraceMeasurement.Begin();
        var result = ParaxialRestorer.Run(source, flat, requests, plan.Targets, new()
        {
            FixedStopEnvelopeVersion = 1, UseAutomaticMatrixDerivatives = true, RestorePupilMagnification = true,
            UseGeometryConstraintTangents = true, MaximumAcceptedSteps = 4, MaximumFunctionEvaluations = 8,
            TimeLimit = TimeSpan.FromSeconds(10), ProfileRadialSamples = 64
        });
        Assert.NotNull(result.InitialEnvelopeDeficitMillimeters); Assert.NotNull(result.EndpointEnvelopeDeficitMillimeters);
        Assert.InRange(result.Work.FunctionEvaluations, 2, 8); Assert.True(result.Work.JacobianBuilds > 0);
        Assert.True(result.Work.MatrixEvaluations > result.Work.JacobianBuilds); Assert.True(result.Work.GeometryProbes > 0);
        Assert.Equal(source.Topology.Reference, result.Topology.Reference); Assert.Equal(source.Topology.Stop, result.Topology.Stop);
        Assert.Equal(source.Topology.Image, result.Topology.Image);
        Assert.Equal(source.Topology.Elements.Select(e => (e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters)),
            result.Topology.Elements.Select(e => (e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters)));
        Assert.Equal(0, measurement.RayCount); // Construction alone provides no real-ray certificate.
    }

    [Fact]
    public void EnvelopeIsExplicitAndBoundedAndDoesNotChangeHistoricalRecoveryJson()
    {
        Assert.DoesNotContain("FixedStopEnvelopeVersion", System.Text.Json.JsonSerializer.Serialize(new ParaxialRecoveryOptions()));
        var (plan, _) = StructureEvolutionTests.Specification(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var optic = source.CreateOptic(); var input = source.SurfaceIndices[source.Topology.Reference.Id];
        var binding = TopologyParameterBinder.Bind(source, [new(source.Topology.Elements[0].FrontSurfaceId,
            OpticalDesignParameterKind.Curvature, -.2, .2, .01)]);
        Assert.Throws<ArgumentException>(() => DifferentiablePupilEnvelopes.Evaluate(optic, binding.CoreParameters, input, [1.01]));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DifferentiablePupilEnvelopes.Evaluate(optic, [], input, [0, 1], cancellationToken: cancelled.Token));
    }
}
