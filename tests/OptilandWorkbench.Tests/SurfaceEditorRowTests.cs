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

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("1e999")]
    public void InvalidNumbersRejectWithoutChangingTheRow(string text)
    {
        var row = new SurfaceEditorRow(CreateSurface(radius: 25.5, thickness: 12));
        var before = row.ToDto();
        Assert.Throws<FormatException>(() => row.RadiusDisplay = text);
        Assert.Throws<FormatException>(() => row.ThicknessDisplay = text);
        Assert.Throws<FormatException>(() => row.SemiDiameterDisplay = text);
        Assert.Equal(before, row.ToDto());
    }

    [Fact]
    public void ThicknessInfinityIsExplicitAndOnlyAllowedForTheObject()
    {
        var row = new SurfaceEditorRow(CreateSurface(radius: 0, thickness: 12) with { Number = 0 });
        foreach (var infinity in new[] { "∞", "无限", "inf", "Infinity" })
        {
            row.ThicknessDisplay = infinity;
            Assert.Equal(double.PositiveInfinity, row.Thickness);
        }
        row.ThicknessDisplay = "0";
        Assert.Equal(0, row.Thickness);
        Assert.Throws<FormatException>(() => row.ThicknessDisplay = "-∞");
        var physical = new SurfaceEditorRow(CreateSurface(radius: 0, thickness: 12));
        Assert.Throws<FormatException>(() => physical.ThicknessDisplay = "∞");
        Assert.Equal(12, physical.Thickness);
    }

    [Fact]
    public void SemiDiameterBelowTheExistingMinimumIsRejectedInsteadOfSilentlyClamped()
    {
        var row = new SurfaceEditorRow(CreateSurface(radius: 0, thickness: 12));
        Assert.Throws<FormatException>(() => row.SemiDiameterDisplay = "0.05");
        Assert.Equal(10, row.SemiDiameter);
        row.SemiDiameterDisplay = "0.1";
        Assert.Equal(0.1, row.SemiDiameter);
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
