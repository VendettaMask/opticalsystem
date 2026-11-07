using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class ParabasalPupilTests
{
    [Theory]
    [InlineData(1, -5.617265543393678, -161.01700441553783)]
    [InlineData(3, -3.236674218330823, -90.39089669143986)]
    public void SkewSpectrometerPencilMeetsNativePointTolerance(int wavelength, double sagittal, double tangential)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Diffractive components/Sample Spectrometer.ZMX");
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
        var result = new OpticalSystemDesign.Application.Runtime.WorkbenchRuntime(optic).BuildAnalysisData("Optical Path Difference",
            new Dictionary<string, string>
            {
                ["NumberOfRays"] = "20", ["FieldNumber"] = "2", ["WavelengthNumber"] = wavelength.ToString(),
                ["VignettedPupil"] = "True", ["CheckApertures"] = "True", ["UseRayAiming"] = "False"
            });
        var panes = result.PlotPanes!;
        Assert.Equal(2, panes.Count);
        VerifyPoint(panes[0].Series.Single().Points.Single(p => p.X == -1).Y, tangential);
        VerifyPoint(panes[1].Series.Single().Points.Single(p => Math.Abs(p.X + .30) < 1e-12).Y, sagittal);

        // Native monochromatic plot values at the original physical pupil knots.
        static void VerifyPoint(double actual, double expected) =>
            Assert.InRange(Math.Abs(actual - expected), 0, 1e-5 + .001 * Math.Abs(expected));
    }

    [Fact]
    public void DiscontinuousMirrorPencilKeepsItsLimitingConjugateAndChiefBranchWidth()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/MEMS DLL Sample.ZMX");
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
        var wavelength = optic.Wavelengths[0];
        foreach (var step in new[] { .001, .0005 })
        {
            var pupil = ParabasalPupilEngine.Compute(optic, wavelength, probeStep: step);
            Assert.InRange(Math.Abs(pupil.ImageAxialDistanceMillimeters - 40), 0, 1e-5);
            Assert.InRange(Math.Abs(pupil.DiameterMillimeters - 5), 0, 1e-9);
        }
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(1.8)]
    public void ConstantGrinSlabPupilAgreesWithPlaneInterfaceApparentDepth(double index)
    {
        var optic = new Optic("Constant GRIN pupil");
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Value = .2;
        var material = new GradientIndexMaterial("Constant spatial index", new Gradient1IndexProfile(index, 0, 0), 10);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 1, MaterialAfter = material, IsStop = true, SemiDiameter = 1 },
            new OpticalSurface { Thickness = .5, MaterialBefore = material, SemiDiameter = 1 },
            new OpticalSurface { SemiDiameter = 1 }
        ]);
        foreach (var xAxis in new[] { false, true })
        {
            var pupil = ParabasalPupilEngine.Compute(optic, optic.Wavelengths[0], xAxis);
            Assert.InRange(Math.Abs(pupil.ImageAxialDistanceMillimeters + .5 + 1 / index), 0, 2e-8);
            Assert.InRange(Math.Abs(pupil.DiameterMillimeters - .2), 0, 2e-8);
        }
    }

    [Fact]
    public void GridGrinProjectedWavefrontMeetsConvergedNativeTolerance()
    {
        using var doc = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grid-gradient-pupil-native-v1.json")));
        var fixture = doc.RootElement;
        var optic = OpticalFormatCatalog.Import(fixture.GetProperty("source").GetString()!, ".zmx");
        var before = optic.RayAimingEnabled;
        var nativePupil = fixture.GetProperty("nativeExitPupil").GetDouble();
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateExitPupilLocation(.55) - nativePupil), 0, 1e-6 + 1e-6 * Math.Abs(nativePupil));
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (0, 0), optic.Wavelengths[1], false);
        Assert.InRange(Math.Abs(sphere.Radius - fixture.GetProperty("nativeSphereRadius").GetDouble()), 0, 1e-6);
        var result = new OpticalSystemDesign.Application.Runtime.WorkbenchRuntime(optic).BuildAnalysisData("Wavefront",
            new Dictionary<string, string> { ["Sampling"] = "64", ["WavelengthNumber"] = "2", ["FieldNumber"] = "1", ["UseExitPupilShape"] = "True" });
        Assert.Equal(true, result.Values["ProjectedExitPupilGrid"]);
        var points = Assert.Single(result.PlotSeries).Points;
        var actual = points.Select(p => ((int)Math.Round(p.X * 31 + 32), (int)Math.Round(p.Y * 31 + 32))).ToHashSet();
        var mask = fixture.GetProperty("nativeMask");
        for (var row = 0; row < 64; row++) for (var column = 0; column < 64; column++)
            Assert.Equal(mask[row][column].GetBoolean(), actual.Contains((column, row)));
        var offset = Convert.ToDouble(result.Values["MeanOpticalPathDifference"]) / .00055 - points.Average(p => p.Value!.Value);
        var tolerances = fixture.GetProperty("tolerances");
        var squared = 0d; var convergenceSquared = 0d; var peak = 0d; var allWithinPointBounds = true;
        foreach (var point in points)
        {
            var column = (int)Math.Round(point.X * 31 + 32); var row = (int)Math.Round(point.Y * 31 + 32);
            var expected = fixture.GetProperty("nativeValues")[row][column].GetDouble();
            var coarse = fixture.GetProperty("coarseNativeValues")[row][column].GetDouble();
            var error = point.Value!.Value + offset - expected;
            squared += error * error; convergenceSquared += (coarse - expected) * (coarse - expected);
            peak = Math.Max(peak, Math.Abs(expected));
            allWithinPointBounds &= Math.Abs(error) <= tolerances.GetProperty("absolute").GetDouble()
                + tolerances.GetProperty("relative").GetDouble() * Math.Abs(expected);
        }
        var bound = tolerances.GetProperty("nrmse").GetDouble();
        Assert.True(Math.Sqrt(convergenceSquared / points.Count) / peak <= bound, "Native integration reference did not converge");
        Assert.True(allWithinPointBounds || Math.Sqrt(squared / points.Count) / peak <= bound, "Original WavefrontError tolerance failed");
        Assert.Equal(1, result.PlotOptions!.SpatialDisplayScaleX);
        Assert.Equal(1, result.PlotOptions.SpatialDisplayScaleY);
        Assert.Equal(before, optic.RayAimingEnabled);
    }
}
