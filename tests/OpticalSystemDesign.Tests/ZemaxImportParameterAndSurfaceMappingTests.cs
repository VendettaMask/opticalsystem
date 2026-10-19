using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxImportParameterAndSurfaceMappingTests
{
    [Theory]
    [InlineData("BIRE__IN", 15)]
    [InlineData("BINARY_2", 1)]
    [InlineData("NONSEQCO", 0)]
    [InlineData("GRID_SAG", 0)]
    [InlineData("USERSURF", 0)]
    [InlineData("FZERNSAG", 0)]
    public void SpecialSurfaceParameterZeroSurvivesSnapshotWithoutShiftingOtherParameters(string type, double value)
    {
        var optic = Import($"""
            MODE SEQ
            UNIT CM
            ENPD 1
            SURF 0
              DISZ INFINITY
            SURF 1
              TYPE {type}
              PARM 0 {value}
              PARM 1 2.5
              PARM 2 -3.5
            SURF 2
            """);
        var geometry = Assert.IsType<OpaqueGeometryPayload>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(value, geometry.Payload.Numbers["parm0"]);
        Assert.Equal(2.5, geometry.Payload.Numbers["parm1"]);
        Assert.Equal(-3.5, geometry.Payload.Numbers["parm2"]);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var saved = Assert.IsType<OpaqueGeometryPayload>(restored.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(value, saved.Payload.Numbers["parm0"]);
        Assert.Equal(geometry.OriginalType, saved.OriginalType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeParameterNumbersRemainInvalid(int number) =>
        Assert.Throws<InvalidDataException>(() => Import($"MODE SEQ\nENPD 2\nSURF 0\nSURF 1\nPARM {number} 1"));

    [Fact]
    public void ToroidalDefaultParameterZeroDoesNotChangeTheGeometryOrLengthScaling()
    {
        const string source = """
            MODE SEQ
            UNIT CM
            ENPD 1
            SURF 0
              DISZ INFINITY
            SURF 1
              TYPE TOROIDAL
              CURV 0.1
              PARM 0 0
              PARM 1 0
              PARM 2 20
            SURF 2
            """;
        var withZero = Import(source).ToSnapshot();
        var withoutZero = Import(source.Replace("PARM 0 0", "")).ToSnapshot();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(withoutZero, options), System.Text.Json.JsonSerializer.Serialize(withZero, options));
        Assert.Equal(100, withZero.Surfaces[1].Radius);
    }

    [Fact]
    public void UnsupportedNonzeroParameterZeroCannotBeSilentlyIgnoredByExecutableGeometry() =>
        Assert.Throws<NotSupportedException>(() => Import("MODE SEQ\nENPD 2\nSURF 0\nSURF 1\nTYPE TOROIDAL\nPARM 0 1"));

    [Theory]
    [InlineData("REAX")]
    [InlineData("REAY")]
    [InlineData("REAR")]
    [InlineData("RANG")]
    [InlineData("CENX")]
    [InlineData("CENY")]
    [InlineData("GLCX")]
    [InlineData("RAGZ")]
    [InlineData("CVVA")]
    public void SurfaceSlotsRemapToPhysicalRowsAndSurviveSnapshot(string code)
    {
        var optic = Import(Frames + $"\n{code} 4 1 0 0 0 0 0 1 0 0");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.Equal(2, row.Surface);
        Assert.Equal(new[] { 2, 1 }, row.ZemaxIntegerParameters);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(2, Assert.Single(restored.MeritFunctionOperands).Surface);
    }

    [Fact]
    public void RemappedGlobalCoordinateUsesTheIntendedPhysicalSurface()
    {
        var optic = Import(Frames + "\nGLCY 4 1 0 0 0 0 0 1 0 0");
        var row = Assert.Single(optic.MeritFunctionOperands);
        var evaluation = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(evaluation.Error), evaluation.Error);
        Assert.Equal(3, evaluation.Value, 10);
    }

    [Theory]
    [InlineData("CENX")]
    [InlineData("GLCZ")]
    public void ReferenceToFoldedCoordinateBreakRemainsExplicitlyRejected(string code) =>
        Assert.Throws<NotSupportedException>(() => Import(Frames + $"\n{code} 2 1 0 0 0 0 0 1 0 0"));

    [Theory]
    [InlineData("REAX")]
    [InlineData("REAY")]
    public void RealRayCoordinateBreakReferenceRetainsItsOwnTransparentPlane(string code)
    {
        var optic = Import(Frames.Replace("SURF 4", "SURF 4\n  CURV 0.02\n  GLAS MIRROR")
            + $"\n{code} 2 1 0 0 0 0 0 1 0 0\nREAY 4 1 0 0 0 0 0 1 0 0");
        var plane = optic.SurfaceGroup.Items[2];
        Assert.IsType<PlaneGeometry>(plane.Geometry);
        Assert.Equal(3, plane.CoordinateSystem.Origin.Y);
        Assert.Null(plane.PhysicalAperture);
        Assert.False(plane.IsReflective);
        Assert.Equal(plane.MaterialBefore.Name, plane.MaterialAfter.Name);
        Assert.Equal(2, optic.MeritFunctionOperands[0].Surface);
        Assert.Equal(3, optic.MeritFunctionOperands[1].Surface);
        Assert.Equal(50, optic.SurfaceGroup.Items[3].Radius);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.IsType<PlaneGeometry>(restored.SurfaceGroup.Items[2].Geometry);
        Assert.Equal(2, restored.MeritFunctionOperands[0].ZemaxIntegerParameters[0]);
    }

    [Fact]
    public void CoordinateBreakReferencePlanePrecedesItsThicknessTranslation()
    {
        var optic = Import(Frames.Replace("PARM 2 3", "PARM 2 3\n  DISZ 7")
            + "\nREAY 2 1 0 0 0 0 0 1 0 0");
        Assert.Equal(10, optic.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z);
        Assert.Equal(7, optic.SurfaceGroup.Items[2].Thickness);
        Assert.Equal(17, optic.SurfaceGroup.Items[3].CoordinateSystem.Origin.Z);
    }

    [Theory]
    [InlineData("REAX", 0)]
    [InlineData("REAY", 0)]
    [InlineData("REAR", 0)]
    [InlineData("REAX", 4)]
    [InlineData("REAY", 4)]
    [InlineData("REAR", 4)]
    public void RealRayCoordinatesUseTargetLocalAxesAndKeepZeroObjectConvention(string code, int sourceSurface)
    {
        var optic = Import(Frames.Replace("PARM 2 3", "PARM 1 2\n  PARM 2 3")
            + $"\n{code} {sourceSurface} 1 0 0 0 0 0 1 0 0");
        var row = Assert.Single(optic.MeritFunctionOperands);
        var expected = sourceSurface == 0 ? 0 : code switch { "REAX" => -2, "REAY" => -3, _ => Math.Sqrt(13) };
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.Empty(result.Error);
        Assert.Equal(expected, result.Value, 10);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(expected, MeritFunctionCatalog.Evaluate(restored, restored.MeritFunctionOperands[0]).Value, 10);
    }

    [Fact]
    public void RangeEndpointsRemapWhileSamplingAndRowReferencesStayUnchanged()
    {
        var optic = Import(Frames + """

            TTHI 1 4 0 0 0 0 0 1 0 0
            RSCE 4 1 0 0 0 0 0 1 0 0
            CONS 4 0 0 0 0 0 0 1 0 0
            REAY 0 1 0 0 0 0 0 1 0 0
            """);
        Assert.Equal(new[] { 1, 2 }, optic.MeritFunctionOperands[0].ZemaxIntegerParameters);
        Assert.Equal(2, optic.MeritFunctionOperands[0].Wavelength);
        Assert.Equal(new[] { 4, 1 }, optic.MeritFunctionOperands[1].ZemaxIntegerParameters);
        Assert.Equal(new[] { 4, 0 }, optic.MeritFunctionOperands[2].ZemaxIntegerParameters);
        Assert.Equal(0, optic.MeritFunctionOperands[3].Surface);
        Optic.FromSnapshot(optic.ToSnapshot());
    }

    private static Optic Import(string text) => OpticalFormatCatalog.Import(text, ".zmx");

    private const string Frames = """
        MODE SEQ
        ENPD 2
        WAVM 1 0.55 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          DISZ 10
        SURF 2
          TYPE COORDBRK
          PARM 2 3
        SURF 3
          TYPE COORDBRK
        SURF 4
        """;
}
