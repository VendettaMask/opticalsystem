using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Multiconfig;

namespace OpticalSystemDesign.Tests;

public sealed class MultiConfigurationThicknessPrecisionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void EditingBaseThicknessKeepsLinkedActivePrescriptionAndSavedConfigurationsIdentical(int active, bool operandRow)
    {
        var runtime = new WorkbenchRuntime(Lens());
        runtime.AddMultiConfiguration();
        runtime.AddMultiConfiguration();
        if (operandRow) runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Thickness, 2)]);
        runtime.ActivateMultiConfiguration(active);
        const double thickness = 3.123456789012345;
        var expected = Lens();
        expected.SurfaceGroup.Items[2].Thickness = thickness;
        expected.SurfaceGroup.Renumber();

        runtime.SetMultiConfigurationThickness(0, 2, thickness);

        Assert.Equal(thickness, runtime.CurrentOptic.SurfaceGroup.Items[2].Thickness);
        Assert.Equal(expected.Paraxial.EstimateEffectiveFocalLength(), runtime.CurrentOptic.Paraxial.EstimateEffectiveFocalLength());
        Assert.All(runtime.CreateMeritConfigurationContext().Configurations,
            optic => Assert.Equal(thickness, optic.SurfaceGroup.Items[2].Thickness));
        Assert.All(runtime.CaptureDocument().Configurations,
            optic => Assert.Equal(thickness, optic.SurfaceGroup.Items[2].Thickness));
        runtime.ActivateMultiConfiguration((active + 1) % 3);
        Assert.Equal(thickness, runtime.CurrentOptic.SurfaceGroup.Items[2].Thickness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ThicknessSourceEditResolvesSurfacePickupsInEveryLinkedConfiguration(int active)
    {
        var optic = Lens();
        optic.Pickups.SetThicknessPickup(2, 4, 2, .125);
        optic.Pickups.ApplyAll();
        var runtime = new WorkbenchRuntime(optic);
        runtime.AddMultiConfiguration();
        runtime.ActivateMultiConfiguration(active);

        runtime.SetMultiConfigurationThickness(0, 2, 4.123456789012345);

        var expected = 2 * 4.123456789012345 + .125;
        Assert.All(runtime.CreateMeritConfigurationContext().Configurations,
            configuration => Assert.Equal(expected, configuration.SurfaceGroup.Items[4].Thickness));
        Assert.Equal(expected, runtime.CurrentOptic.SurfaceGroup.Items[4].Thickness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ThicknessPickupTargetCannotBeOverwrittenByTheMatrix(int configuration)
    {
        var optic = Lens();
        optic.Pickups.SetThicknessPickup(2, 4, 2, .125);
        optic.Pickups.ApplyAll();
        var runtime = new WorkbenchRuntime(optic);
        runtime.AddMultiConfiguration();
        var before = runtime.CaptureDocument();

        Assert.Throws<InvalidOperationException>(() => runtime.SetMultiConfigurationThickness(configuration, 4, 9));

        Assert.Equal(before.BrokenLinks, runtime.CaptureDocument().BrokenLinks);
        Assert.All(runtime.CreateMeritConfigurationContext().Configurations,
            value => Assert.Equal(6.125, value.SurfaceGroup.Items[4].Thickness));
    }

    [Fact]
    public async Task BaseEditPreservesIndependentValuesAndPickupDependenciesThroughUndoRedoAndSave()
    {
        var runtime = new WorkbenchRuntime(Lens());
        runtime.AddMultiConfiguration();
        runtime.AddMultiConfiguration();
        runtime.ReplaceMultiConfigurationOperands([
            new(MultiConfigurationOperandKind.Thickness, 2),
            new(MultiConfigurationOperandKind.Thickness, 4)]);
        runtime.SetMultiConfigurationOperandValue(1, 1, 8.23456789012345);
        runtime.SetMultiConfigurationOperandPickup(2, 2, 1, 0, 2, .125);
        runtime.ActivateMultiConfiguration(2);
        var before = runtime.CaptureDocument();
        const double thickness = 4.123456789012345;

        runtime.SetMultiConfigurationThickness(0, 2, thickness);

        Verify();
        Assert.True(runtime.Undo());
        Assert.Equal(before.ActiveOptic.SurfaceGroup.Items[2].Thickness, runtime.CurrentOptic.SurfaceGroup.Items[2].Thickness);
        Assert.Equal(before.ActiveOptic.SurfaceGroup.Items[4].Thickness, runtime.CurrentOptic.SurfaceGroup.Items[4].Thickness);
        Assert.True(runtime.Redo());
        Verify();
        var path = Path.Combine(Path.GetTempPath(), $"configuration-precision-{Guid.NewGuid():N}.staropt");
        try
        {
            await runtime.SaveAsync(path);
            runtime.ApplyLoadedDocument(await WorkbenchRuntime.ReadDocumentAsync(path), path);
            Verify();
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        void Verify()
        {
            var context = runtime.CreateMeritConfigurationContext();
            Assert.Equal(thickness, context.Configurations[0].SurfaceGroup.Items[2].Thickness);
            Assert.Equal(8.23456789012345, context.Configurations[1].SurfaceGroup.Items[2].Thickness);
            Assert.Equal(thickness, runtime.CurrentOptic.SurfaceGroup.Items[2].Thickness);
            Assert.Equal(2 * thickness + .125, runtime.CurrentOptic.SurfaceGroup.Items[4].Thickness);
        }
    }

    private static Optic Lens() => OpticalFormatCatalog.Import("""
        MODE SEQ
        ENPD 2
        WAVM 1 .55 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          CURV .02
          DISZ 2
          GLAS N-BK7
          DIAM 4
        SURF 2
          CURV -.02
          DISZ 3
          DIAM 4
        SURF 3
          CURV .03
          DISZ 2
          GLAS N-BK7
          DIAM 4
        SURF 4
          CURV -.03
          DISZ 30
          DIAM 4
        SURF 5
          DISZ 0
        """, ".zmx");
}
