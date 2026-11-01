using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class TernaryMirrorArrayTests
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AddressModesAndFifteenTritWordBoundaryHaveDefinedPixelSelection(int mode)
    {
        var geometry=new TernaryMirrorArrayGeometry(3,6,3,6,[0,.1,.2],0,mode,[5,2]);
        // Row 1 / column 1 / pixel 1 is the least significant ternary digit: state 2.
        Assert.Equal(-Math.Sin(.2),geometry.SurfaceNormal(new(-1,-2.5,0)).Y,14);
        var atSecondRow=geometry.SurfaceNormal(new(-1,-1.5,0));
        Assert.Equal(-Math.Sin(mode==0?.1:mode==1?.2:0),atSecondRow.Y,14);
        if(mode==2) Assert.Equal(-Math.Sin(.2),geometry.SurfaceNormal(new(-1,2.5,0)).Y,14); // pixel 16, word 2
        Assert.Equal(new Vector3D(0,0,1),geometry.SurfaceNormal(new(1.5,0,0)));
        Assert.Equal(new Vector3D(0,0,1),geometry.SurfaceNormal(new(0,3,0)));
        Assert.Equal(0,geometry.Sag(-1,-2.5));
    }
    [Fact]
    public void UnknownUserDllRetainsItsArgumentsAndRawDataWithoutAcquiringTheMemsContract()
    {
        var optic=OpticalFormatCatalog.Import("MODE SEQ\nUNIT CM\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE USERSURF MY_OTHER_MODEL.DLL\nPARM 3 10\nXDAT 2 5\nDISZ 1\nSURF 2",".zmx");
        Assert.Contains(OpticCapabilityPreflight.Inspect(optic),i=>i.OriginalType.Contains("USERSURF"));
        var geometry=optic.ToSnapshot().Surfaces[1].Components!.Geometry!;
        Assert.Equal("MY_OTHER_MODEL.DLL",geometry.Text["zemax.type.arguments"]);
        Assert.Equal(10,geometry.Numbers["parm3"]);Assert.Equal(5,geometry.Numbers["xdat2"]);
    }
}
