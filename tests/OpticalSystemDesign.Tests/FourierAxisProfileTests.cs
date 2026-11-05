using OpticalSystemDesign.Validation;
using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Tests;

public sealed class FourierAxisProfileTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ExplicitPhysicalRangeResamplesTheNativeCurveDomain(double range)
    {
        using var fixture = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/fft-profile-physical-range-native-v1.json")));
        var root = fixture.RootElement;
        var optic = OpticalSystemDesign.Core.FileIO.OpticalFormatCatalog.Import(root.GetProperty("source").GetString()!, ".zmx");
        optic.RayAimingEnabled = true;
        var actual = new FftPsfCrossSectionAnalysis(optic, 32, graphScaleMicrometers: range,
            wavelengthNumber: 1, fieldNumber: 1, type: "X-线性").GenerateData();
        var points = Assert.Single(actual.PlotSeries).Points;
        var expected = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("scale").GetDouble() == range).GetProperty("points");
        Assert.Equal(expected.GetArrayLength(), points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var x = expected[i].GetProperty("x").GetDouble(); var y = expected[i].GetProperty("y").GetDouble();
            Assert.InRange(Math.Abs(points[i].X - x), 0, 5.1e-6);
            Assert.InRange(Math.Abs(points[i].Y - y), 0,
                root.GetProperty("absoluteTolerance").GetDouble() + root.GetProperty("relativeTolerance").GetDouble() * Math.Abs(y));
        }
        Assert.Equal(AnalysisAxisUnit.Micrometer, actual.PlotSeries[0].XUnit);
    }

    [Theory]
    [InlineData(5, 16)]
    [InlineData(8, 32)]
    public void AxisReadoutsMatchIndependentComplexDftWithObscuredNonseparablePupil(int size, int transform)
    {
        var pupil = new Complex[size, size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            if ((x + 2 * y) % 4 != 0) pupil[y, x] = Complex.FromPolarCoordinates(.2 + .1 * (x + y), .3 * x * y - .2 * y);
        var actual = DiffractionEngine.FourierAxisProfiles(pupil, transform);
        var padding = (transform - size) / 2; var center = (transform - 1) / 2;
        for (var i = 0; i < transform; i++)
        {
            var xAxis = Complex.Zero; var yAxis = Complex.Zero;
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                xAxis += pupil[y, x] * Complex.FromPolarCoordinates(1, -2 * Math.PI * (i - center) * (x + padding) / transform);
                yAxis += pupil[y, x] * Complex.FromPolarCoordinates(1, -2 * Math.PI * (i - center) * (y + padding) / transform);
            }
            Assert.InRange((actual.Horizontal[i] - xAxis).Magnitude, 0, 5e-12);
            Assert.InRange((actual.Vertical[i] - yAxis).Magnitude, 0, 5e-12);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.FourierAxisProfiles(pupil, 65536));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(8192)]
    public void CompactGridRetainsEveryIlluminatedNodeAndOriginalCoordinateArithmetic(int nominal)
    {
        var source = new PupilGridSpecification(nominal, true, true, Math.Sqrt(nominal / 32d))
        { StretchY = Math.Sqrt(nominal / 32d) * 1.17 };
        var compact = DiffractionEngine.CompactFftPupilGrid(source);
        for (var i = 0; i < nominal; i++)
        {
            if (i >= compact.SourceOffset && i < compact.SourceOffset + compact.SampleCount)
            {
                var j = i - compact.SourceOffset;
                Assert.Equal(source.Coordinate(i), compact.Coordinate(j));
                Assert.Equal(source.YCoordinate(i), compact.YCoordinate(j));
                Assert.Equal(j, compact.ColumnIndex(source.Coordinate(i)));
                Assert.Equal(j, compact.RowIndex(source.YCoordinate(i)));
            }
            else
                Assert.True(Math.Abs(source.Coordinate(i)) > 1 && Math.Abs(source.YCoordinate(i)) > 1);
        }
        Assert.InRange(compact.SampleCount, 32, AnalysisResourceLimits.MaximumFftGridSize);
    }

    [Fact]
    public void SharedPupilProfilesRetainFullImageAbsoluteIntensityPhysicalAxesAndApodization()
    {
        var optic = Optic.CreateBlank(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        optic.Aperture.Value = .4;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 2 },
            new OpticalSurface { Thickness = 2, SemiDiameter = .2, IsStop = true },
            new OpticalSurface { SemiDiameter = 2 }
        ]);
        const int n = 256; const int transform = 512;
        var full = new PsfAnalysis(optic, n, transform, wavelengthNumber: 1, fieldNumber: 1,
            zemaxCompatible: true).GenerateData();
        var actual = DiffractionEngine.ComputeFftAxisProfiles(optic, (0, 0), optic.Wavelengths[0], n, transform, optic.Wavelengths[0]);
        var points = Assert.Single(full.PlotSeries).Points;
        var xAxis = points.Where(p => p.Y == 0).OrderBy(p => p.X).ToArray();
        var yAxis = points.Where(p => p.X == 0).OrderBy(p => p.Y).ToArray();
        Assert.Equal(transform, xAxis.Length); Assert.Equal(transform, yAxis.Length);
        Assert.Equal(Convert.ToDouble(full.Values["ImageDeltaMicrometers"]), actual.Spacing);
        for (var i = 0; i < transform; i++)
        {
            Assert.InRange(Math.Abs(xAxis[i].Value!.Value - actual.Horizontal[i]), 0, 1e-11);
            Assert.InRange(Math.Abs(yAxis[i].Value!.Value - actual.Vertical[i]), 0, 1e-11);
        }
        var high = new FftPsfCrossSectionAnalysis(optic, 2048, wavelengthNumber: 1, type: "X-线性").GenerateData();
        Assert.Equal(true, high.Values["AxisFourierReadout"]);
        Assert.Equal(8193, Assert.Single(high.PlotSeries).Points.Count);
        Assert.IsType<string>(high.Values["PeakStrehlRatio"]);
        Assert.Throws<AnalysisDataUnavailableException>(() => new FftPsfCrossSectionAnalysis(optic, 2048, normalize: true).GenerateData());
    }
}
