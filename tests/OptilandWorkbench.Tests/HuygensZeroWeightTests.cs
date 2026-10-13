using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Tests;

public sealed class HuygensZeroWeightTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroWeightAbsorbingColorDoesNotBlockTheCoherentRuntimePsf(bool centroid)
    {
        var optic = Prescription(absorbingSecondary: true);
        var runtime = new WorkbenchRuntime(optic);
        var parameters = new Dictionary<string, string>
        {
            ["PupilSampling"] = "8", ["ImageSampling"] = "8", ["ImageDeltaMicrometers"] = "0.25",
            ["FieldNumber"] = "1", ["WavelengthNumber"] = "1", ["UsePolarization"] = "true",
            ["UseCentroid"] = centroid.ToString()
        };
        var expected = runtime.BuildAnalysisData("Huygens PSF", parameters);
        parameters["WavelengthNumber"] = "0";
        var actual = runtime.BuildAnalysisData("Huygens PSF", parameters);
        actual = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(actual))!;
        Assert.Equal(AnalysisOutcome.Success, actual.Outcome);
        var a = Assert.Single(actual.PlotSeries).Points;
        var b = Assert.Single(expected.PlotSeries).Points;
        Assert.Equal(64, a.Count);
        Assert.True(a.Zip(b, (x, y) => Math.Abs(x.Value!.Value - y.Value!.Value)).Max() < 1e-12);
    }

    [Fact]
    public void AllZeroAnalysisWeightsKeepTheDocumentedEqualWeightFallback()
    {
        var zero = Prescription(absorbingSecondary: false);
        zero.Wavelengths[0].Weight = 0;
        var equal = Optic.FromSnapshot(zero.ToSnapshot());
        foreach (var wavelength in equal.Wavelengths) wavelength.Weight = 1;
        var a = HuygensPsfSynthesis.Compute(zero, (0, 0), zero.Wavelengths.ToArray(), 8, 8, .00025, true);
        var b = HuygensPsfSynthesis.Compute(equal, (0, 0), equal.Wavelengths.ToArray(), 8, 8, .00025, true);
        Assert.Equal(b.IdealPeakWeight, a.IdealPeakWeight);
        Assert.Equal(b.Psf.Values.Cast<double>(), a.Psf.Values.Cast<double>());
    }

    [Fact]
    public void ExplicitZeroWeightMonochromaticSelectionStillComputesItsRequestedColor()
    {
        var optic = Prescription(absorbingSecondary: false);
        var wavelength = optic.Wavelengths[1];
        var actual = HuygensPsfSynthesis.Compute(optic, (0, 0), [wavelength], 8, 8, .00025, true);
        var expected = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wavelength, 8, 8, .00025, true,
            referenceWavelength: wavelength);
        Assert.Equal(1, actual.IdealPeakWeight);
        Assert.Equal(expected.Values.Cast<double>(), actual.Psf.Values.Cast<double>());
    }

    [Fact]
    public void PositiveWeightAbsorbingColorRemainsExplicitlyUnsupported()
    {
        var optic = Prescription(absorbingSecondary: true);
        optic.Wavelengths[1].Weight = 1;
        Assert.Throws<NotSupportedException>(() => HuygensPsfSynthesis.Compute(
            optic, (0, 0), optic.Wavelengths.ToArray(), 8, 8, .00025, true));
    }

    private static Optic Prescription(bool absorbingSecondary)
    {
        var optic = new Optic("Synthetic spectral weight control");
        optic.Fields.Add(new FieldPoint { Weight = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, Weight = 1, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 450, Weight = 0, IsPrimary = false });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Radius = 50, Thickness = 3, IsStop = true, SemiDiameter = 3,
                SemiDiameterFixed = true, MaterialAfter = new PolynomialDispersionMaterial("Synthetic dispersion",
                    [2.25], extinctionWavelengthsNanometers: [450, 550],
                    extinctionCoefficients: [absorbingSecondary ? .001 : 0, 0]) },
            new OpticalSurface { Radius = -50, Thickness = 47, SemiDiameter = 3, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 0, SemiDiameter = 4, SemiDiameterFixed = true }
        ]);
        optic.Polarization = new(false, 1, 0);
        optic.RayAimingEnabled = true;
        return optic;
    }
}
