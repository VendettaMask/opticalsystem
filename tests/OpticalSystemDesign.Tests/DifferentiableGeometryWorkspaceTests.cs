using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class DifferentiableGeometryWorkspaceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BatchedManufacturingValuesAndAllTangentsExactlyMatchOriginalScalarQueries(bool asphere, bool absolute)
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, asphere, false);
        var parameters = Parameters(asphere, absolute); var workspace = new DifferentiableGeometryWorkspace(optic, parameters);
        using var actual = SequentialTraceMeasurement.Begin();
        foreach (var radial in Enumerable.Range(0, 65).Select(i => 15.0 * i / 64).Concat([0.0, -0.0, -3.0]))
            foreach (var surface in new[] { 1, 2 })
            {
                Same(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, surface, radial), workspace.SurfaceSlope(surface, radial), parameters.Length);
                Same(DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, surface, 3, radial), workspace.AxialSeparation(surface, 3, radial), parameters.Length);
            }
        Assert.Equal(0, actual.RayCount); Assert.True(workspace.ProfileCacheHits > 0);
        Assert.True(workspace.ProfileEvaluations < workspace.Requests * 2);
    }

    [Fact]
    public void MaskRelaxationAndSingularDomainRemainDistinctAndOriginalInputsCannotMutateASavedWorkspace()
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, true, false); var parameters = Parameters(true, false);
        var original = Optic.FromSnapshot(optic.ToSnapshot()); var savedParameters = parameters.ToArray();
        var workspace = new DifferentiableGeometryWorkspace(optic, parameters);
        foreach (var relaxed in new[] { false, true })
            foreach (var mask in new[] { false, true })
                Same(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, 1, 10, relaxed, mask), workspace.SurfaceSlope(1, 10, relaxed, mask), parameters.Length);
        Assert.Throws<ArithmeticException>(() => DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, 1, 80));
        Assert.Throws<ArithmeticException>(() => workspace.SurfaceSlope(1, 80));
        Same(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, 1, 80, maskOutsideProfile: true), workspace.SurfaceSlope(1, 80, maskOutsideProfile: true), parameters.Length);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(30, -.9, [0, 2e-5, -2e-9]);
        optic.SurfaceGroup.Items[1].Thickness = 7; optic.SurfaceGroup.Renumber();
        parameters[0] = parameters[0] with { Scale = .1 };
        Same(DifferentiableOpticalMetrics.AxialSeparation(original, savedParameters, 1, 2, 4), workspace.AxialSeparation(1, 2, 4), savedParameters.Length);
        var next = new DifferentiableGeometryWorkspace(optic, parameters);
        Assert.NotEqual(workspace.AxialSeparation(1, 2, 4).Value, next.AxialSeparation(1, 2, 4).Value);
    }

    [Fact]
    public void EvictionCancellationAndReverseTapeCannotReuseInvalidGeometry()
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, true, false); var parameters = Parameters(true, false);
        using var stop = new CancellationTokenSource(); var workspace = new DifferentiableGeometryWorkspace(optic, parameters, stop.Token);
        for (var i = 0; i < 9000; i++) workspace.SurfaceSlope(1, (i + 1) / 10000.0);
        Same(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, 1, .0001), workspace.SurfaceSlope(1, .0001), parameters.Length);
        using (new DifferentialTape(parameters.Length)) Assert.Throws<NotSupportedException>(() => workspace.SurfaceSlope(1, 1));
        stop.Cancel(); Assert.Throws<OperationCanceledException>(() => workspace.SurfaceSlope(1, 1));
        Assert.Throws<OperationCanceledException>(() => new DifferentiableGeometryWorkspace(optic, parameters, stop.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardSeedAndDispersionReuseMatchesUncachedContextAndNeverLeaksIntoATape(bool relaxed)
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, true, relaxed);
        var parameters = OpticalDesignDerivativeTests.Parameters(true, relaxed);
        var scalar = new OpticalDifferentialContext(optic, parameters);
        var tracing = new OpticalDifferentialContext(optic, parameters, 2, analyticNormals: true);
        for (var pass = 0; pass < 3; pass++)
            foreach (var wave in new[] { 486.1, 587.6, 656.3, 450.25, 745.0 })
                for (var surface = 0; surface < optic.SurfaceGroup.Items.Count; surface++)
                    Same(scalar.Index(surface, wave), tracing.Index(surface, wave), parameters.Length);
        Same(scalar.Parameter(1, OpticalDesignParameterKind.Curvature, .02), tracing.Parameter(1, OpticalDesignParameterKind.Curvature, .02), parameters.Length);
        Same(scalar.Parameter(1, OpticalDesignParameterKind.Curvature, .03), tracing.Parameter(1, OpticalDesignParameterKind.Curvature, .03), parameters.Length);
        using (new DifferentialTape(parameters.Length))
        {
            var tapeValue = tracing.Parameter(1, OpticalDesignParameterKind.Curvature, .02);
            Assert.Throws<InvalidOperationException>(() => tapeValue.Gradient());
            if (relaxed) Assert.Throws<InvalidOperationException>(() => tracing.Index(1, 587.6).Gradient());
        }
        Same(scalar.Index(1, 587.6), tracing.Index(1, 587.6), parameters.Length);
        Same(scalar.Parameter(1, OpticalDesignParameterKind.Curvature, .02), tracing.Parameter(1, OpticalDesignParameterKind.Curvature, .02), parameters.Length);
    }

    [Fact]
    public void LinkedParametersShareTheCorrectSlotWithoutAliasingANewNominalValue()
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, false, true);
        optic.SurfaceGroup.Items[2].Radius = 50;
        optic.SurfaceGroup.Items[2].MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("linked", 1.52, 64);
        OpticalDesignParameter[] parameters = [new(1, OpticalDesignParameterKind.Curvature, .02, LinkedSurfaceIndex: 2),
            new(1, OpticalDesignParameterKind.MaterialIndex, LinkedSurfaceIndex: 2), new(1, OpticalDesignParameterKind.MaterialAbbe, 50, LinkedSurfaceIndex: 2)];
        var scalar = new OpticalDifferentialContext(optic, parameters); var tracing = new OpticalDifferentialContext(optic, parameters, 2);
        foreach (var surface in new[] { 1, 2 })
        {
            Same(scalar.Parameter(surface, OpticalDesignParameterKind.Curvature, .02), tracing.Parameter(surface, OpticalDesignParameterKind.Curvature, .02), parameters.Length);
            Same(scalar.Index(surface, 486.1), tracing.Index(surface, 486.1), parameters.Length);
        }
    }

    private static OpticalDesignParameter[] Parameters(bool asphere, bool absolute)
    {
        var values = new List<OpticalDesignParameter> { new(1, OpticalDesignParameterKind.Curvature, .02), new(2, OpticalDesignParameterKind.Curvature, .02) };
        if (absolute) values.AddRange([new(1, OpticalDesignParameterKind.AxialPosition, .1) { OriginOffset = -1 }, new(2, OpticalDesignParameterKind.AxialPosition, .1)]);
        else values.AddRange([new(1, OpticalDesignParameterKind.Thickness, 5), new(2, OpticalDesignParameterKind.Thickness, 50)]);
        if (asphere) values.AddRange([new(1, OpticalDesignParameterKind.Conic), new(1, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e-5, 1)]);
        return values.ToArray();
    }
    private static void Same(DifferentialScalar a, DifferentialScalar b, int count)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Value), BitConverter.DoubleToInt64Bits(b.Value));
        for (var i = 0; i < count; i++) Assert.Equal(BitConverter.DoubleToInt64Bits(a.Derivative(i)), BitConverter.DoubleToInt64Bits(b.Derivative(i)));
    }
}
