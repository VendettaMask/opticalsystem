using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class RadiusSolveTests
{
    [Theory]
    [InlineData(2, 0.5, 4)] // Ansys curvature pickup example, not radius multiplication.
    [InlineData(20, -1, -20)]
    [InlineData(0, 0.5, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    public void CurvatureFactorUsesReciprocalRadiusAndRoundTrips(double radius, double factor, double expected)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Radius = radius;
        optic.Pickups.SetCurvaturePickup(1, 2, factor);
        optic.Pickups.ApplyAll();
        Assert.Equal(expected, optic.SurfaceGroup.Items[2].Radius);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        restored.Pickups.ApplyAll();
        Assert.Equal(expected, restored.SurfaceGroup.Items[2].Radius);
        Assert.Equal(factor, new WorkbenchRuntime(restored).GetRadiusSolve(2).ScaleFactor);
    }

    [Fact]
    public void ChainedPickupsResolveInDependencyOrderAndCyclesDoNotPartiallyApply()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Radius = 20;
        optic.Pickups.SetCurvaturePickup(2, 3, -1);
        optic.Pickups.SetCurvaturePickup(1, 2, 0.5);
        optic.Pickups.ApplyAll();
        Assert.Equal(40, optic.SurfaceGroup.Items[2].Radius);
        Assert.Equal(-40, optic.SurfaceGroup.Items[3].Radius);
        var before = optic.SurfaceGroup.Items.Select(surface => surface.Radius).ToArray();
        optic.Pickups.LinkRadius(3, 1, 1);
        Assert.Throws<InvalidOperationException>(optic.Pickups.ApplyAll);
        Assert.Equal(before, optic.SurfaceGroup.Items.Select(surface => surface.Radius));
    }

    [Fact]
    public async Task SolveTransitionsFollowSourceAreUndoableAndSaveToNativeFile()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Radius = 20 });
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        app.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Pickup, 1, 0.5));
        Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(40, app.Prescription.GetSurfaces()[2].Radius);
        Assert.False(app.Prescription.GetSurfaces()[2].RadiusVariable);
        Assert.True(app.Documents.Undo());
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.True(app.Documents.Redo());
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Radius = 30 });
        Assert.Equal(60, app.Prescription.GetSurfaces()[2].Radius);

        var path = Path.Combine(Path.GetTempPath(), $"radius-solve-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var restored = WorkbenchApplication.Create();
            await restored.Documents.OpenAsync(path);
            Assert.Equal(new RadiusSolveDto(RadiusSolveKind.Pickup, 1, 0.5), restored.Prescription.GetSurfaces()[2].RadiusSolve);
            restored.Prescription.UpdateSurface(restored.Prescription.GetSurfaces()[1] with { Radius = 25 });
            Assert.Equal(50, restored.Prescription.GetSurfaces()[2].Radius);
            restored.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Variable));
            Assert.True(restored.Prescription.GetSurfaces()[2].RadiusVariable);
            restored.Prescription.UpdateSurface(restored.Prescription.GetSurfaces()[1] with { Radius = 35 });
            Assert.Equal(50, restored.Prescription.GetSurfaces()[2].Radius);
            restored.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Fixed));
            Assert.False(restored.Prescription.GetSurfaces()[2].RadiusVariable);
            Assert.Equal(RadiusSolveKind.Fixed, restored.Prescription.GetSurfaces()[2].RadiusSolve!.Kind);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(2, -1, 1)]
    [InlineData(2, 1, double.NaN)]
    [InlineData(2, 1, double.PositiveInfinity)]
    public void InvalidSolveChangesDoNotMutateDocumentOrHistory(int target, int source, double factor)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Prescription.SetRadiusSolve(target, new(RadiusSolveKind.Pickup, source, factor)));
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.Equal(revision, app.Events.Revision);
        Assert.False(app.Documents.Undo());
    }

    [Fact]
    public void StaleDialogCannotChangeRenumberedSurfaceAndBulkVariablesCannotOverridePickups()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Pickup, 1, -1));
        app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.SetAllRadii);
        Assert.False(app.Prescription.GetSurfaces()[2].RadiusVariable);
        var revision = app.Events.Revision;
        app.Prescription.InsertSurface(1, after: false);
        var current = app.Events.Revision;
        Assert.Throws<InvalidOperationException>(() => app.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Variable), revision));
        Assert.Equal(current, app.Events.Revision);
        Assert.Equal(new RadiusSolveDto(RadiusSolveKind.Pickup, 2, -1), app.Prescription.GetSurfaces()[3].RadiusSolve);
    }

    [Fact]
    public void ConfigurationsKeepTheirSolveAndFollowPropagatedSources()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
        runtime.SetRadiusSolve(2, new(RadiusSolveKind.Pickup, 1, -1));
        var config = runtime.AddMultiConfiguration();
        runtime.Surfaces[1].Radius = 40;
        runtime.CommitSurfaceEdit(runtime.Surfaces[1], "Radius");
        runtime.ActivateMultiConfiguration(config);
        Assert.Equal(-40, runtime.Surfaces[2].Radius);
        Assert.Equal(new RadiusSolveDto(RadiusSolveKind.Pickup, 1, -1), runtime.GetRadiusSolve(2));
    }

    [Fact]
    public void BasePickupValuesPropagateWithoutInventingSolvesInExistingConfigurations()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
        var config = runtime.AddMultiConfiguration();
        runtime.SetRadiusSolve(2, new(RadiusSolveKind.Pickup, 1, -1));
        runtime.Surfaces[1].Radius = 40;
        runtime.CommitSurfaceEdit(runtime.Surfaces[1], "Radius");
        runtime.ActivateMultiConfiguration(config);
        Assert.Equal(-40, runtime.Surfaces[2].Radius);
        Assert.Equal(RadiusSolveKind.Fixed, runtime.GetRadiusSolve(2).Kind);
    }

    [Theory]
    [InlineData("Orthogonal Descent")]
    [InlineData("Least Squares")]
    public async Task OptimizerEvaluatesDependentRadiusInLiveAndIndependentPaths(string optimizer)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.SetRadiusSolve(1, new(RadiusSolveKind.Variable));
        app.Prescription.SetRadiusSolve(2, new(RadiusSolveKind.Pickup, 1, 0.5));
        var target = app.Prescription.GetSurfaces()[2].Radius * 1.15;
        app.Optimization.SetMeritFunction(new[]
        {
            new MeritOperandRowDto(1, true, "RADI", 2, 0, 0, 0, 0, 0, 0, target, 1, 0, 0, "拾取面目标")
        });
        var result = await app.Optimization.OptimizeVariablesAsync(optimizer, 5);
        Assert.Single(result.Variables);
        Assert.Equal(1, result.Variables[0].SurfaceNumber);
        Assert.True(result.FinalMerit < result.InitialMerit);
        var rows = app.Prescription.GetSurfaces();
        Assert.Equal(rows[1].Radius * 2, rows[2].Radius, 10);
    }

    [Fact]
    public void LegacyRadiusOffsetIsPreservedWithoutPretendingItIsACurvatureFactor()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Pickups.LinkRadius(1, 2, -0.5, 3);
        var runtime = new WorkbenchRuntime(optic);
        Assert.False(runtime.GetRadiusSolve(2).PickupEditable);
        optic.Pickups.ApplyAll();
        Assert.Equal(optic.SurfaceGroup.Items[1].Radius * -0.5 + 3, optic.SurfaceGroup.Items[2].Radius);
    }
}
