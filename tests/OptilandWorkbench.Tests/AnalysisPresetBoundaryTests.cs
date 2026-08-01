using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisPresetBoundaryTests
{
    [Fact]
    public void CoreConstructorDefaultsRemainGeneralPurposeRatherThanCaptured123456Settings()
    {
        AssertDefault<RmsVsFieldAnalysis>("data", "spot");
        AssertDefault<RmsVsFieldAnalysis>("reference", "centroid");
        AssertDefault<RmsVsFieldAnalysis>("fieldDensity", 0);
        AssertDefault<RmsWavefrontVsFieldAnalysis>("fieldDensity", 0);

        AssertDefault<RmsVsFocusAnalysis>("focusDensity", 21);
        AssertDefault<RmsVsFocusAnalysis>("minimumFocus", -1.0);
        AssertDefault<RmsVsFocusAnalysis>("maximumFocus", 1.0);
        AssertDefault<RmsVsFocusAnalysis>("data", "spot");
        AssertDefault<RmsVsFocusAnalysis>("reference", "centroid");

        AssertDefault<DiffractionEncircledEnergyAnalysis>("numPoints", 256);
        AssertDefault<PupilAberrationAnalysis>("numPoints", 256);
        AssertDefault<HuygensPsfCrossSectionAnalysis>("numRays", 9);
        AssertDefault<HuygensPsfCrossSectionAnalysis>("pixelPitchMillimeters", 0.005);
        AssertDefault<HuygensPsfCrossSectionAnalysis>("wavelengthNumber", -1);
        AssertDefault<HuygensPsfCrossSectionAnalysis>("fieldNumber", 0);
        AssertDefault<HuygensPsfCrossSectionAnalysis>("profileType", "Both");
        AssertDefault<HuygensMtfAnalysis>("numRays", 9);
        AssertDefault<HuygensMtfAnalysis>("pixelPitchMillimeters", 0.005);
        AssertDefault<HuygensMtfAnalysis>("wavelengthNumber", -1);
        AssertDefault<HuygensMtfAnalysis>("zemaxCompatible", false);
        AssertDefault<ContrastLossMapAnalysis>("sampling", 32);
        AssertDefault<ContrastLossMapAnalysis>("frequency", 0.0);
        AssertDefault<ContrastLossMapAnalysis>("wavelengthNumber", 1);
    }

    private static void AssertDefault<TAnalysis>(string parameterName, object expected)
    {
        var constructor = Assert.Single(typeof(TAnalysis).GetConstructors());
        var parameter = Assert.Single(
            constructor.GetParameters(), item => item.Name == parameterName);
        Assert.Equal(expected, parameter.DefaultValue);
    }
}
