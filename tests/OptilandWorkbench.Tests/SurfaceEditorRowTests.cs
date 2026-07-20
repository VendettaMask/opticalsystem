using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceEditorRowTests
{
    [Fact]
    public void PlaneRadiusDisplaysAsInfinityAndAcceptsInfinityText()
    {
        var row = new SurfaceEditorRow(CreateSurface(radius: 0, thickness: 12));

        Assert.Equal("无限", row.RadiusDisplay);

        row.RadiusDisplay = "25.5";
        Assert.Equal(25.5, row.Radius, precision: 12);

        row.RadiusDisplay = "∞";
        Assert.Equal(0, row.Radius, precision: 12);
        Assert.Equal("无限", row.RadiusDisplay);
    }

    [Fact]
    public void ImageSurfaceThicknessDisplaysDashAndCannotBeEdited()
    {
        var row = new SurfaceEditorRow(CreateSurface(radius: 0, thickness: 0), isLastSurface: true);

        Assert.Equal("-", row.ThicknessDisplay);

        row.ThicknessDisplay = "10";
        Assert.Equal(0, row.Thickness, precision: 12);
        Assert.Equal("-", row.ThicknessDisplay);
    }

    private static SurfaceRowDto CreateSurface(double radius, double thickness) => new(
        Number: 1,
        Label: "Surface 1",
        Radius: radius,
        Thickness: thickness,
        Material: "Air",
        Coating: "None",
        SemiDiameter: 10,
        Conic: 0,
        IsStop: false,
        GeometryKind: "标准球面/圆锥",
        CoatingKind: "None",
        InteractionKind: "折射/反射",
        ApertureKind: "圆形",
        GratingOrder: 1,
        GratingPeriodMicrometers: double.PositiveInfinity,
        GrooveOrientationAngleDegrees: 0,
        ThinLensFocalLength: double.PositiveInfinity,
        RadiusVariable: false,
        ThicknessVariable: false);
}
