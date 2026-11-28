using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

public sealed class BeamSplitterConfigurationRepairTests
{
    internal static string SourcePath => Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
        "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Beam splitter.zmx");

    [Fact]
    public async Task ImportedRowsKeepBothMaterialSwitchesInPrescriptionOrder()
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(SourcePath);
        Assert.Equal(14, imported.OperandRows.Count);
        Assert.Equal(new[] { "GLSS", "THIC", "THIC", "THIC", "THIC", "CRVT", "CRVT", "PRAM", "PRAM", "THIC", "THIC", "GLSS", "SDIA", "SDIA" },
            imported.OperandRows.Select(row => MultiConfigurationOperand.Code(row.Kind)));
        Assert.Equal(new[] { "BK7", "MIRROR" }, imported.Configurations.Select(imported.OperandRows[0].ReadText));
        Assert.Equal(new[] { "BK7", "Air" }, imported.Configurations.Select(imported.OperandRows[11].ReadText));
        Assert.Equal(new double[] { 16, 0 }, imported.Configurations.Select(imported.OperandRows[12].Read));
        Assert.Equal(new double[] { 16, 0 }, imported.Configurations.Select(imported.OperandRows[13].Read));
        Assert.Throws<InvalidOperationException>(() => imported.OperandRows[0].Read(imported.ActiveOptic));
        Assert.All(imported.OperandVariables, variable => Assert.True(variable.Operand.IsNumeric));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task BothBranchesPassAllFieldsAndPupilRays(bool threeDimensional, int configuration)
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(SourcePath);
        var optic = imported.Configurations[configuration];
        var builder = new Layout2DBuilder(optic);
        var options = new LayoutBuildOptions(RayCount: 7, DeleteVignetted: true);
        if (threeDimensional)
        {
            var scene = builder.Build3D(options: options);
            Assert.Equal(21, scene.Rays.Count);
            Assert.All(scene.Rays, ray => { Assert.False(ray.Vignetted); Assert.Equal(12, ray.Segments[^1].TargetSurfaceNumber); });
        }
        else
        {
            var scene = builder.Build(options: options);
            Assert.Equal(21, scene.Rays.Count);
            Assert.All(scene.Rays, ray => { Assert.False(ray.Vignetted); Assert.Equal(12, ray.Segments[^1].TargetSurfaceNumber); });
        }
        if (configuration != 1) return;
        foreach (var surface in optic.SurfaceGroup.Items.Where(s => s.Number is 10 or 11))
        {
            Assert.Equal(0, surface.SemiDiameter);
            Assert.Null(surface.PhysicalAperture);
            Assert.True(surface.SemiDiameterDefinesPhysicalAperture);
        }
        // A saved native single-ray report for this same file uses on-axis Py=.8.
        // The nonzero heights on these two air/air dummy surfaces must not clip.
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .8, .55, false).Rays.Single();
        var ray = optic.SequentialRayTracer.TraceToSurface(source, 12)!;
        Assert.False(ray.Vignetted);
        var local = optic.SurfaceGroup.Items[12].CoordinateSystem.ToLocalPoint(ray.Position);
        Assert.Equal(.01281392995, local.Y, 8);
    }

    [Fact]
    public async Task TiltedPlanarCubeUsesFlatRectangularCapsInBothConfigurations()
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(SourcePath);
        foreach (var optic in imported.Configurations)
        {
            var scene = new Layout2DBuilder(optic).Build3D();
            var prisms = scene.LensElements.Where(element => element.FrontSurfaceNumber is 3 or 6).ToArray();
            Assert.Equal(2, prisms.Length);
            foreach (var prism in prisms)
            {
                Assert.Equal(5, prism.FrontRim.Count);
                Assert.Equal(5, prism.BackRim.Count);
                Assert.All(prism.FrontRim.Concat(prism.BackRim), point =>
                {
                    Assert.InRange(point.X, -20.000000001, 20.000000001);
                    Assert.InRange(point.Y, -20.000000001, 20.000000001);
                    Assert.InRange(point.Z, 14.999999999, 55.000000001);
                });
                Assert.Contains(prism.FrontRim, point => Math.Abs(point.X - 20) < 1e-9);
                Assert.Contains(prism.FrontRim, point => Math.Abs(point.X + 20) < 1e-9);
            }
        }
    }

    [Fact]
    public async Task MaterialAndZeroDiameterEditsAreAtomicUndoableAndSurviveReopen()
    {
        using var app = WorkbenchApplication.Create("blank");
        await app.Documents.OpenAsync(SourcePath);
        Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        var rows = app.MultiConfiguration.GetOperandRows();
        Assert.Equal(14, rows.Count);
        Assert.Equal(new[] { "BK7", "MIRROR" }, rows[0].TextValues);
        Assert.Empty(rows[0].Values);
        Assert.Equal(3, rows[7].ParameterNumber);
        Assert.Equal(2, rows[8].ParameterNumber);
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentException>(() => app.MultiConfiguration.SetOperandTextValue(1, 1, "MISSING-BEAM-GLASS", revision));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal("MIRROR", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
        app.MultiConfiguration.SetOperandTextValue(1, 1, "BK7", revision);
        Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        Assert.Equal("BK7", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
        Assert.True(app.Documents.Undo());
        Assert.Equal("MIRROR", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
        Assert.True(app.Documents.Redo());
        Assert.Equal("BK7", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
        Assert.True(app.Documents.Undo());
        Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetOperandVariable(1, 1, true, app.Events.Revision));
        Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetOperandPickup(2, 1, new(1, 0, 1, 0), app.Events.Revision));
        app.MultiConfiguration.SetOperandTextValue(12, 1, "BK7", app.Events.Revision);
        app.MultiConfiguration.SetOperandTextValue(12, 1, "", app.Events.Revision);
        Assert.Equal("Air", app.MultiConfiguration.GetOperandRows()[11].TextValues![1]);
        app.MultiConfiguration.SetOperandValue(13, 1, 16, app.Events.Revision);
        app.MultiConfiguration.SetOperandValue(13, 1, .1, app.Events.Revision);
        var clipped = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 7, ConfigurationIndex: 1));
        Assert.Single(clipped.ThreeDimensional!.Rays);
        app.MultiConfiguration.SetOperandValue(13, 1, 0, app.Events.Revision);
        var directory = Path.Combine(Path.GetTempPath(), "beamsplitter-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "beam.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            app.Documents.NewBlank();
            await app.Documents.OpenAsync(path);
            Assert.Equal(14, app.MultiConfiguration.GetOperandRows().Count);
            Assert.Equal(new[] { "BK7", "MIRROR" }, app.MultiConfiguration.GetOperandRows()[0].TextValues);
            Assert.Equal(0, app.MultiConfiguration.GetOperandRows()[12].Values[1]);
            var scene = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 7, IncludeAllConfigurations: true));
            Assert.Equal(42, scene.ThreeDimensional!.Rays.Count);
            Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExplicitSurfaceApertureStillClipsWhenDisplayDiameterIsZero()
    {
        var source = "MODE SEQ\nENPD 6\nSURF 0\nDISZ INFINITY\nSURF 1\nDIAM 0 1 0 0 1\nCLAP 0 1 0\nDISZ 10\nSURF 2\nDISZ 0";
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        Assert.Equal(0, optic.SurfaceGroup.Items[1].SemiDiameter);
        Assert.NotNull(optic.SurfaceGroup.Items[1].PhysicalAperture);
        var ray = new OpticalSystemDesign.Core.Rays.RealRay(new(0, 2.4, -1), new(0, 0, 1), 550);
        var trace = optic.SequentialRayTracer.Trace(new OpticalSystemDesign.Core.Rays.RealRayBundle([ray]));
        Assert.Contains(trace.RayHistories[0], sample => sample.SurfaceNumber == 1 && sample.Vignetted);
    }
}
