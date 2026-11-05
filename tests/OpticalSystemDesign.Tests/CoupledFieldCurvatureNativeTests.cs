using OpticalSystemDesign.Validation;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CoupledFieldCurvatureNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using var fixture=JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","field-curvature-coupled-native-v1.json")));
        foreach(var c in fixture.RootElement.GetProperty("cases").EnumerateArray())
        foreach(var v in c.GetProperty("values").EnumerateArray())
            yield return [c.GetProperty("rotation").GetDouble(),c.GetProperty("sourceSha256").GetString()!,v.GetProperty("code").GetString()!,v.GetProperty("hy").GetDouble(),v.GetProperty("value").GetDouble()];
    }

    [Theory] [MemberData(nameof(NativeCases))]
    public void EachPupilPencilUsesBothTransverseOutputComponents(double rotation,string sourceHash,string code,double hy,double expected)
    {
        var source=Path.Combine(AppContext.BaseDirectory,"Fixtures","reference-closure","Toroid.zmx");
        var text=EvidenceFiles.ReadAllText(source);
        if(rotation!=45) text=text.Replace("PARM 5 45","PARM 5 "+rotation.ToString("R",CultureInfo.InvariantCulture))
            .Replace("PARM 5 -45","PARM 5 "+(-rotation).ToString("R",CultureInfo.InvariantCulture))
            .Replace("PPAR 5 1 -1.000000000000E+00 0.000000000000E+00 0","");
        var bytes=rotation==45?EvidenceFiles.ReadAllBytes(source):Encoding.UTF8.GetBytes(text);
        Assert.Equal(sourceHash,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var optic=OpticalFormatCatalog.Import(text,".zmx");
        var actual=FieldCurvatureMetrics.Evaluate(optic,.5,0,hy,code=="FCGS");
        // The unpowered near-cylindrical axis has a billion-mm focus and is
        // ill-conditioned; keep its relative check separate from finite axes.
        Assert.InRange(Math.Abs(actual-expected),0,Math.Max(1e-6,Math.Abs(expected)*1e-6));
        var image=optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem=image.CoordinateSystem with {RotationZDegrees=image.CoordinateSystem.RotationZDegrees+90};
        var rolled=FieldCurvatureMetrics.Evaluate(optic,.5,0,hy,code=="FCGS");
        Assert.InRange(Math.Abs(rolled-actual),0,Math.Max(1e-6,Math.Abs(actual)*1e-6));
    }

    [Theory] [InlineData(false,-.6039420307306994)] [InlineData(true,-.611276801001489)]
    public void FoldedImageRetainsBothTransverseComponents(bool sagittal,double native)
    {
        // Original native corpus index 2168, selected primary wavelength,
        // on-axis Twyman-Green field. The image is perpendicular to world Y.
        var optic=OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","optical-testing","Twyman Green Interferometer.zmx")),".zmx");
        var wave=optic.Wavelengths.ToList().FindIndex(w=>w.IsPrimary)+1;
        var actual=new FieldCurvatureAnalysis(optic,2,parabasalDelta:.001,wavelengthNumber:wave,
            parabasalRayPair:ParabasalRayPair.ChiefAndPositive).GenerateData().PlotSeries[sagittal?1:0].Points[0].X;
        Assert.InRange(Math.Abs(actual-native),0,1e-5);
        Assert.Throws<NotSupportedException>(()=>FieldCurvatureMetrics.Evaluate(optic,optic.Wavelengths[wave-1].Micrometers,0,0,sagittal));
    }
}
