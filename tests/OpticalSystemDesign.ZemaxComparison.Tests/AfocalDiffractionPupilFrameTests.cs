using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalDiffractionPupilFrameTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RotatingOnlyImageAxesCannotCollapseThePhysicalDiffractionPupil(int configuration)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract/source-evidence.zip"));
        using var stream = zip.GetEntry($"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/003-binocular_system-c{configuration}.json")!.Open();
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
        var method = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!
            .GetMethod("AfocalDiffractionPupilDiameterMillimeters", BindingFlags.Public | BindingFlags.Static)!;
        double Diameter() => (double)method.Invoke(null, [optic])!;
        var original = Diameter();
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with { RotationZDegrees = image.CoordinateSystem.RotationZDegrees + 90 };
        var rotated = Diameter();
        Assert.True(original > 1);
        Assert.InRange(Math.Abs(rotated - original), 0, 1e-8);
    }
}
