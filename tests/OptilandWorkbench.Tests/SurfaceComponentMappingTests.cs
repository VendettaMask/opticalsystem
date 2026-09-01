using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceComponentMappingTests
{
    [Fact]
    public void ComponentEditorAcceptsImportedStandardSurfaceWithoutRecreatingIt()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var surface = optic.SurfaceGroup.Items[1];
        var before = Assert.IsType<StandardGeometry>(surface.Geometry);

        runtime.ApplySurfaceComponents(surface, "标准球面/圆锥", "无");

        Assert.Same(before, surface.Geometry);
        Assert.Equal(before.Radius, surface.Radius, precision: 12);
        Assert.Equal(before.Conic, surface.Conic, precision: 12);
    }

    [Fact]
    public void ComponentEditorPreservesSpecialGeometryAndApertureParametersWhenKindIsUnchanged()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new EvenAsphereGeometry(42, -0.7, new[] { 1.25e-5, -3.5e-8, 9e-11 });
        surface.PhysicalAperture = new AnnularAperture(6.4, 1.7);
        var before = Assert.IsType<EvenAsphereGeometry>(surface.Geometry);

        runtime.ApplySurfaceComponents(surface, "偶次非球面", "环形");

        var after = Assert.IsType<EvenAsphereGeometry>(surface.Geometry);
        Assert.Same(before, after);
        Assert.Equal(new[] { 1.25e-5, -3.5e-8, 9e-11 }, after.Coefficients);
        var annular = Assert.IsType<AnnularAperture>(surface.PhysicalAperture);
        Assert.Equal(6.4, annular.OuterRadius, precision: 12);
        Assert.Equal(1.7, annular.InnerRadius, precision: 12);
    }

    [Fact]
    public void SurfaceEditorRowLabelsOrdinarySurfacesAndPropagatesUnsupportedGeometryState()
    {
        var row = new SurfaceEditorRow(new SurfaceRowDto(
            Number: 2,
            Label: "Imported special",
            Radius: 10,
            Thickness: 1,
            Material: "Air",
            Coating: "None",
            SemiDiameter: 5,
            Conic: 0,
            IsStop: false,
            GeometryKind: "不支持：Zemax TYPE BINARY_2",
            CoatingKind: "无镀膜",
            InteractionKind: "折射",
            ApertureKind: "无",
            GratingOrder: 1,
            GratingPeriodMicrometers: 1,
            GrooveOrientationAngleDegrees: 0,
            ThinLensFocalLength: 50,
            RadiusVariable: false,
            ThicknessVariable: false,
            GeometryComputable: false));

        Assert.Equal("普通面", row.SurfaceRole);
        Assert.False(row.GeometryComputable);
        Assert.Equal("不支持：Zemax TYPE BINARY_2", row.SurfaceType);
    }
}
