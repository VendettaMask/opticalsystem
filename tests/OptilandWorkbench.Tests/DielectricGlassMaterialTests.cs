using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.Tests;

public sealed class DielectricGlassMaterialTests
{
    [Fact]
    public void DielectricSampleUsesIorFresnelAndThicknessAttenuation()
    {
        var normal = DielectricGlassMaterial.Sample(1.5, 1, 0, 0, 2, isSideWall: false);
        var grazing = DielectricGlassMaterial.Sample(1.5, 0.1, 0, 0, 2, isSideWall: false);
        var highIndex = DielectricGlassMaterial.Sample(1.8, 1, 0, 0, 2, isSideWall: false);
        var thick = DielectricGlassMaterial.Sample(1.5, 1, 0, 0, 20, isSideWall: false);

        Assert.InRange(normal.Reflectance, 0.0399, 0.0401);
        Assert.True(grazing.Reflectance > normal.Reflectance);
        Assert.True(highIndex.Reflectance > normal.Reflectance);
        Assert.True(thick.Transmission < normal.Transmission);
        Assert.True(thick.Color.A > normal.Color.A);
    }
}
