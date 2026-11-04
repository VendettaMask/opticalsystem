using System.Globalization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxCrossColumnPickupTests
{
    [Theory]
    [InlineData("MM",1)] [InlineData("CM",10)] [InlineData("IN",25.4)]
    public void ObjectThicknessPickupProducesTheSamePhysicalCollimatorAsExplicitFocalLength(string unit,double scale)
    {
        var focal=(100/scale).ToString("R",CultureInfo.InvariantCulture);
        var source=$"""
            VERS 160527
            MODE SEQ
            UNIT {unit}
            ENPD 10
            FTYP 1 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 .5876 1
            PWAV 1
            SURF 0
             TYPE STANDARD
             CURV 0
             DISZ {focal}
            SURF 1
             TYPE PARAXIAL
             PARM 1 987
             PARM 2 1
             PPAR 1 0 1 0 2
             DISZ 10
            SURF 2
             TYPE STANDARD
             CURV 0
             DISZ 0
            """;
        var pickup=OpticalFormatCatalog.Import(source,".zmx");
        var explicitLens=OpticalFormatCatalog.Import(source.Replace("PARM 1 987","PARM 1 "+focal).Replace("PPAR 1 0 1 0 2",""),".zmx");
        var interaction=Assert.IsType<ParaxialLensInteractionModel>(pickup.SurfaceGroup.Items[1].InteractionModel);
        Assert.Equal(.01,interaction.PowerY,13);
        foreach(var py in new[]{-.8,-.25,0,.4,.8})
        {
            var a=pickup.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,0,py,.5876).Rays.Single();
            var b=explicitLens.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,0,py,.5876).Rays.Single();
            var actual=pickup.SequentialRayTracer.TraceToSurface(a,1)!;
            var expected=explicitLens.SequentialRayTracer.TraceToSurface(b,1)!;
            Assert.Equal(expected.Position,actual.Position);
            Assert.InRange(Math.Abs(actual.Direction.Y),0,1e-12);
            Assert.InRange(Math.Abs(actual.Direction.Z-1),0,1e-12);
            Assert.Equal(expected.PhaseInclusiveOpticalPathLength,actual.PhaseInclusiveOpticalPathLength);
            var restored=OpticalSystemDesign.Core.Optic.FromSnapshot(pickup.ToSnapshot());
            var savedRay=restored.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,0,py,.5876).Rays.Single();
            var recomputed=restored.SequentialRayTracer.TraceToSurface(savedRay,1)!;
            Assert.Equal(actual.Position,recomputed.Position);
            Assert.Equal(actual.Direction,recomputed.Direction);
            Assert.Equal(actual.PhaseInclusiveOpticalPathLength,recomputed.PhaseInclusiveOpticalPathLength);
        }
    }

    [Theory]
    [InlineData(1,4)] [InlineData(2,20)] [InlineData(3,-3)] [InlineData(4,2.5)]
    [InlineData(6,30)] [InlineData(7,1)] [InlineData(0,30)]
    public void PickupColumnUsesOfficialNumericColumnIdentityAndFileUnits(int column,double sourceValue)
    {
        // Independent column table: radius, thickness, conic, semi-diameter,
        // parameter 1, parameter 2, or the same target parameter respectively.
        var source=$"""
            VERS 160527
            MODE SEQ
            UNIT IN
            ENPD 1
            WAVM 1 .5876 1
            PWAV 1
            SURF 0
             TYPE STANDARD
             DISZ 20
            SURF 1
             TYPE PARAXIAL
             CURV .25
             CONI -3
             DIAM 2.5 1
             PARM 1 30
             PARM 2 1
             DISZ 20
            SURF 2
             TYPE PARAXIAL
             PARM 1 999
             PARM 2 1
             PPAR 1 1 2 7 {column}
             DISZ 10
            SURF 3
             TYPE STANDARD
             DISZ 0
            """;
        var optic=OpticalFormatCatalog.Import(source,".zmx");
        var lens=Assert.IsType<ParaxialLensInteractionModel>(optic.SurfaceGroup.Items[2].InteractionModel);
        Assert.Equal(1/((2*sourceValue+7)*25.4),lens.PowerY,13);
    }

    [Fact]
    public void PickupOfAnUnsupportedColumnIsRejectedInsteadOfSilentlyReadingZero()
    {
        var source="VERS 160527\nMODE SEQ\nENPD 10\nSURF 0\nTYPE STANDARD\nDISZ 100\nSURF 1\nTYPE PARAXIAL\nPARM 1 100\nPARM 2 1\nPPAR 1 0 1 0 99\nDISZ 100\nSURF 2\nTYPE STANDARD\nDISZ 0";
        Assert.Throws<NotSupportedException>(()=>OpticalFormatCatalog.Import(source,".zmx"));
    }
}
