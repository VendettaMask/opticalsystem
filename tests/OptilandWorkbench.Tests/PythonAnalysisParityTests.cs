using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class PythonAnalysisParityTests
{
    public static IEnumerable<object[]> OfficialSamples()
    {
        yield return new object[] { "cooke", (Func<Optic>)Optic.CreateCookeTriplet };
        yield return new object[] { "tessar", (Func<Optic>)Optic.CreateTessarLens };
    }

    public static IEnumerable<object[]> ReferenceSphereWavefrontCases()
    {
        yield return new object[]
        {
            "cooke",
            (Func<Optic>)Optic.CreateCookeTriplet,
            "centroid_sphere_wavefront",
            ReferenceSphereStrategy.CentroidSphere,
            "centroid_sphere",
            "Centroid Sphere Wavefront"
        };
        yield return new object[]
        {
            "cooke",
            (Func<Optic>)Optic.CreateCookeTriplet,
            "best_fit_sphere_wavefront",
            ReferenceSphereStrategy.BestFitSphere,
            "best_fit_sphere",
            "Best Fit Sphere Wavefront"
        };
        yield return new object[]
        {
            "tessar",
            (Func<Optic>)Optic.CreateTessarLens,
            "centroid_sphere_wavefront",
            ReferenceSphereStrategy.CentroidSphere,
            "centroid_sphere",
            "Centroid Sphere Wavefront"
        };
        yield return new object[]
        {
            "tessar",
            (Func<Optic>)Optic.CreateTessarLens,
            "best_fit_sphere_wavefront",
            ReferenceSphereStrategy.BestFitSphere,
            "best_fit_sphere",
            "Best Fit Sphere Wavefront"
        };
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void DistortionMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("distortion");
        var data = new DistortionAnalysis(createOptic(), numPoints: 17).GenerateData();
        Assert.Equal(expected.GetProperty("series").GetArrayLength(), data.PlotSeries.Count);

        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            var expectedValues = expected.GetProperty("series")[wavelength];
            var expectedField = expected.GetProperty("field");
            var actual = data.PlotSeries[wavelength];
            Assert.Equal(expectedValues.GetArrayLength(), actual.Points.Count);
            for (var index = 0; index < actual.Points.Count; index++)
            {
                AssertClose(expectedValues[index].GetDouble(), actual.Points[index].X);
                AssertClose(expectedField[index].GetDouble(), actual.Points[index].Y);
            }
        }

        Assert.Equal("Distortion (%)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Field Angle (deg)", data.PlotSeries[0].YAxisLabel);
        Assert.True(data.PlotOptions?.SymmetricX);
        Assert.True(data.PlotOptions?.ShowVerticalZeroLine);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FThetaDistortionMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("distortion_f_theta");
        var data = new DistortionAnalysis(createOptic(), numPoints: 17, distortionType: "f-theta").GenerateData();

        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            var expectedValues = expected.GetProperty("series")[wavelength];
            for (var index = 0; index < data.PlotSeries[wavelength].Points.Count; index++)
            {
                AssertClose(expectedValues[index].GetDouble(), data.PlotSeries[wavelength].Points[index].X);
            }
        }
    }

    [Fact]
    public void RealImageHeightUsesEquivalentAngleForDistortionAndMillimetersForFieldCurvature()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        for (var index = 0; index < optic.Fields.Count; index++)
        {
            optic.Fields[index].X = 0;
            optic.Fields[index].Y = 4.5 * index / (optic.Fields.Count - 1.0);
        }

        var distortion = new DistortionAnalysis(optic, numPoints: 5).GenerateData();
        var gridDistortion = new GridDistortionAnalysis(optic, numPoints: 3).GenerateData();
        var fieldCurvature = new FieldCurvatureAnalysis(optic, numPoints: 5).GenerateData();

        Assert.True(Convert.ToDouble(distortion.Values["MaxFieldDegrees"]) > 0);
        Assert.False(distortion.Values.ContainsKey("MaxRealImageHeightMillimeters"));
        Assert.Equal("f-tan", distortion.Values["DistortionType"]);
        Assert.Equal("Field Angle (deg)", distortion.PlotSeries[0].YAxisLabel);
        Assert.Equal("f-tan", gridDistortion.Values["DistortionType"]);
        Assert.Equal(4.5, fieldCurvature.Values["MaxRealImageHeightMillimeters"]);
        Assert.Equal("Real Image Height (mm)", fieldCurvature.PlotSeries[0].YAxisLabel);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void GridDistortionMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("grid_distortion");
        var data = new GridDistortionAnalysis(createOptic(), numPoints: 10).GenerateData();
        Assert.Equal(21, data.PlotSeries.Count);

        for (var row = 0; row < 10; row++)
        {
            AssertGridLine(expected, "xr", "yr", row, data.PlotSeries[10 + row]);
        }

        AssertGridPoints(expected, "xp", "yp", data.PlotSeries[^1]);

        AssertClose(
            expected.GetProperty("max_distortion").GetDouble(),
            Convert.ToDouble(data.Values["MaximumDistortionPercent"]));
        Assert.Equal("畸变网格", data.PlotSeries[0].Name);
        Assert.All(data.PlotSeries.Take(20), item => Assert.Equal(AnalysisLineStyle.Solid, item.LineStyle));
        Assert.Equal(AnalysisSeriesKind.Scatter, data.PlotSeries[^1].Kind);
        Assert.Equal(AnalysisMarkerStyle.Cross, data.PlotSeries[^1].MarkerStyle);
        Assert.True(data.PlotOptions?.EqualAspect);
        Assert.True(data.PlotOptions?.HideAxes);
        Assert.False(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FThetaGridDistortionMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("grid_distortion_f_theta");
        var data = new GridDistortionAnalysis(createOptic(), numPoints: 10, distortionType: "f-theta").GenerateData();

        for (var row = 0; row < 10; row++)
        {
            AssertGridLine(expected, "xr", "yr", row, data.PlotSeries[10 + row]);
        }

        AssertGridPoints(expected, "xp", "yp", data.PlotSeries[^1]);

        AssertClose(
            expected.GetProperty("max_distortion").GetDouble(),
            Convert.ToDouble(data.Values["MaximumDistortionPercent"]));
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FieldCurvatureMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("field_curvature");
        var data = new FieldCurvatureAnalysis(createOptic(), numPoints: 17).GenerateData();
        var wavelengthCount = expected.GetProperty("wavelengths").GetArrayLength();
        Assert.Equal(wavelengthCount * 2, data.PlotSeries.Count);

        for (var wavelength = 0; wavelength < wavelengthCount; wavelength++)
        {
            AssertCurvatureSeries(expected, "tangential", wavelength, data.PlotSeries[wavelength * 2]);
            AssertCurvatureSeries(expected, "sagittal", wavelength, data.PlotSeries[(wavelength * 2) + 1]);
            Assert.Equal(AnalysisLineStyle.Solid, data.PlotSeries[wavelength * 2].LineStyle);
            Assert.Equal(AnalysisLineStyle.Dashed, data.PlotSeries[(wavelength * 2) + 1].LineStyle);
            Assert.Equal(data.PlotSeries[wavelength * 2].ColorIndex, data.PlotSeries[(wavelength * 2) + 1].ColorIndex);
        }

        Assert.Equal("Field Curvature", data.PlotOptions?.Title);
        Assert.True(data.PlotOptions?.SymmetricX);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void SpotDiagramMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("spot_diagram");
        var data = new SpotDiagramAnalysis(createOptic()).GenerateData();
        Assert.NotNull(data.PlotPanes);
        Assert.Equal(expected.GetProperty("fields").GetArrayLength(), data.PlotPanes.Count);

        for (var field = 0; field < data.PlotPanes.Count; field++)
        {
            var pane = data.PlotPanes[field];
            Assert.Equal(expected.GetProperty("panes")[field].GetProperty("title").GetString(), pane.Title);
            for (var wavelength = 0; wavelength < pane.Series.Count; wavelength++)
            {
                var expectedX = expected.GetProperty("x")[field][wavelength];
                var expectedY = expected.GetProperty("y")[field][wavelength];
                Assert.Equal(expectedX.GetArrayLength(), pane.Series[wavelength].Points.Count);
                for (var index = 0; index < pane.Series[wavelength].Points.Count; index++)
                {
                    AssertClose(expectedX[index].GetDouble(), pane.Series[wavelength].Points[index].X);
                    AssertClose(expectedY[index].GetDouble(), pane.Series[wavelength].Points[index].Y);
                }
            }

            AssertClose(expected.GetProperty("panes")[field].GetProperty("x_lim")[0].GetDouble(), pane.PlotOptions.XMinimum!.Value);
            AssertClose(expected.GetProperty("panes")[field].GetProperty("x_lim")[1].GetDouble(), pane.PlotOptions.XMaximum!.Value);
            Assert.True(pane.PlotOptions.EqualAspect);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void EncircledEnergyMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("encircled_energy");
        var data = new EncircledEnergyAnalysis(
            createOptic(),
            numRays: 3,
            distribution: "hexapolar",
            numPoints: 33).GenerateData();
        Assert.Equal(expected.GetProperty("energy").GetArrayLength(), data.PlotSeries.Count);

        for (var field = 0; field < data.PlotSeries.Count; field++)
        {
            var expectedRadius = expected.GetProperty("radius")[field];
            var expectedEnergy = expected.GetProperty("energy")[field];
            for (var index = 0; index < data.PlotSeries[field].Points.Count; index++)
            {
                AssertClose(expectedRadius[index].GetDouble(), data.PlotSeries[field].Points[index].X);
                AssertClose(expectedEnergy[index].GetDouble(), data.PlotSeries[field].Points[index].Y);
            }
        }

        Assert.Equal(expected.GetProperty("presentation").GetProperty("title").GetString(), data.PlotOptions?.Title);
        Assert.Equal("Radius (mm)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Encircled Energy (-)", data.PlotSeries[0].YAxisLabel);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void RmsVsFieldMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("rms_vs_field");
        var data = new RmsVsFieldAnalysis(createOptic(), numFields: 9, numRings: 3).GenerateData();
        var expectedSpotSize = expected.GetProperty("spot_size");
        Assert.Equal(expected.GetProperty("wavelengths").GetArrayLength(), data.PlotSeries.Count);

        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            for (var field = 0; field < data.PlotSeries[wavelength].Points.Count; field++)
            {
                AssertClose(expected.GetProperty("field")[field].GetDouble(), data.PlotSeries[wavelength].Points[field].X);
                AssertClose(expectedSpotSize[field][wavelength].GetDouble(), data.PlotSeries[wavelength].Points[field].Y);
            }
        }

        Assert.Equal("Normalized Y Field Coordinate", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("RMS Spot Size (mm)", data.PlotSeries[0].YAxisLabel);
        Assert.Equal(0, data.PlotOptions?.XMinimum);
        Assert.Equal(1, data.PlotOptions?.XMaximum);
        Assert.Equal(0, data.PlotOptions?.YMinimum);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void RmsWavefrontVsFieldMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("rms_wavefront_vs_field");
        var data = new RmsWavefrontVsFieldAnalysis(createOptic(), numFields: 9, numRings: 5).GenerateData();
        Assert.Equal(expected.GetProperty("wavelengths").GetArrayLength(), data.PlotSeries.Count);
        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            for (var field = 0; field < data.PlotSeries[wavelength].Points.Count; field++)
            {
                AssertClose(expected.GetProperty("field")[field].GetDouble(), data.PlotSeries[wavelength].Points[field].X);
                AssertClose(
                    expected.GetProperty("wavefront_error")[field][wavelength].GetDouble(),
                    data.PlotSeries[wavelength].Points[field].Y);
            }
        }

        Assert.Equal("Normalized Y Field Coordinate", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("RMS Wavefront Error (waves)", data.PlotSeries[0].YAxisLabel);
        Assert.Equal(0, data.PlotOptions?.XMinimum);
        Assert.Equal(1, data.PlotOptions?.XMaximum);
        Assert.Equal(0, data.PlotOptions?.YMinimum);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void IncidentAngleVsHeightMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        foreach (var item in new[]
        {
            (Mode: AngleScanMode.ThroughPupil, Key: "angle_vs_height_pupil", Fixed: "Field"),
            (Mode: AngleScanMode.ThroughField, Key: "angle_vs_height_field", Fixed: "Pupil")
        })
        {
            var expected = reference.RootElement.GetProperty(sampleName).GetProperty(item.Key);
            var data = new IncidentAngleVsHeightAnalysis(createOptic(), item.Mode, numPoints: 17).GenerateData();
            var series = Assert.Single(data.PlotSeries);
            Assert.Equal(AnalysisSeriesKind.ColoredLine, series.Kind);
            Assert.Equal(expected.GetProperty("height").GetArrayLength(), series.Points.Count);
            Assert.Equal(item.Fixed, expected.GetProperty("fixed_coordinates").GetString());
            for (var index = 0; index < series.Points.Count; index++)
            {
                AssertClose(expected.GetProperty("height")[index].GetDouble(), series.Points[index].X);
                AssertClose(
                    expected.GetProperty("angle_radians")[index].GetDouble(),
                    series.Points[index].Y * Math.PI / 180);
                AssertClose(expected.GetProperty("scan_range")[index].GetDouble(), series.Points[index].Value!.Value);
            }

            Assert.Equal("Image Height in Millimeters", series.XAxisLabel);
            Assert.Equal("Incident Angle in Degrees", series.YAxisLabel);
            Assert.Contains("Normalized", series.ValueLabel);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void IncoherentIrradianceMatchesPythonOptilandPixelForPixel(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("incoherent_irradiance");
        var optic = createOptic();
        var detectorHalfWidth = expected.GetProperty("detector_half_width").GetDouble();
        optic.SurfaceGroup.Items[^1].PhysicalAperture = new RectangularAperture(detectorHalfWidth, detectorHalfWidth);
        var data = new IncoherentIrradianceAnalysis(
            optic,
            numRays: 3,
            resolutionX: 15,
            resolutionY: 13,
            distribution: "hexapolar").GenerateData();

        Assert.NotNull(data.PlotPanes);
        Assert.Equal(9, data.PlotPanes.Count);
        Assert.Equal(3, data.PlotPaneColumns);
        Assert.Equal(AnalysisColorMap.Inferno, data.PlotPanes[0].Series.Single().ColorMap);
        Assert.Equal("Normalized Irradiance", data.PlotPanes[0].Series.Single().ValueLabel);
        var xEdges = expected.GetProperty("x_edges");
        var yEdges = expected.GetProperty("y_edges");
        var expectedMaps = expected.GetProperty("normalized");
        for (var field = 0; field < 3; field++)
        {
            for (var wavelength = 0; wavelength < 3; wavelength++)
            {
                var pane = data.PlotPanes[(field * 3) + wavelength];
                var points = pane.Series.Single().Points;
                Assert.Equal(15 * 13, points.Count);
                Assert.True(pane.PlotOptions.EqualAspect);
                for (var x = 0; x < 15; x++)
                {
                    for (var y = 0; y < 13; y++)
                    {
                        var point = points[(x * 13) + y];
                        AssertClose((xEdges[x].GetDouble() + xEdges[x + 1].GetDouble()) / 2, point.X);
                        AssertClose((yEdges[y].GetDouble() + yEdges[y + 1].GetDouble()) / 2, point.Y);
                        var expectedValue = expectedMaps[field][wavelength][x][y].GetDouble();
                        var tolerance = 2e-8 * Math.Max(1, Math.Abs(expectedValue));
                        Assert.True(
                            Math.Abs(expectedValue - point.Value!.Value) <= tolerance,
                            $"Field {field}, wavelength {wavelength}, pixel ({x}, {y}): expected {expectedValue:R}, actual {point.Value.Value:R}.");
                    }
                }
            }
        }

        var expectedPeak = expected.GetProperty("peaks").EnumerateArray()
            .SelectMany(field => field.EnumerateArray())
            .Max(value => value.GetDouble());
        AssertClose(expectedPeak, Convert.ToDouble(data.Values["PeakIrradiance"]));
    }

    [Fact]
    public void IncoherentIrradianceReportsPythonDetectorApertureRequirement()
    {
        var data = new IncoherentIrradianceAnalysis(Optic.CreateCookeTriplet()).GenerateData();
        Assert.Empty(data.PlotSeries);
        Assert.Contains("physical aperture", data.Values["Status"].ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Set a physical aperture on the detector surface", data.Values["PythonRequirement"]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void RadiantIntensityMatchesPythonOptilandPixelForPixel(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("radiant_intensity");
        var data = new RadiantIntensityAnalysis(
            createOptic(),
            binsX: 15,
            binsY: 13,
            angleXMinimum: -30,
            angleXMaximum: 30,
            angleYMinimum: -30,
            angleYMaximum: 30,
            useAbsoluteUnits: true,
            numRays: 3,
            distribution: "hexapolar").GenerateData();

        Assert.NotNull(data.PlotPanes);
        Assert.Equal(18, data.PlotPanes.Count);
        Assert.Equal(6, data.PlotPaneColumns);
        var xCenters = expected.GetProperty("x_centers");
        var yCenters = expected.GetProperty("y_centers");
        var expectedMaps = expected.GetProperty("intensity");
        var expectedPeak = expected.GetProperty("peaks").EnumerateArray()
            .SelectMany(field => field.EnumerateArray())
            .Max(value => value.GetDouble());
        for (var field = 0; field < 3; field++)
        {
            for (var wavelength = 0; wavelength < 3; wavelength++)
            {
                var paneIndex = ((field * 3) + wavelength) * 2;
                var map = data.PlotPanes[paneIndex].Series.Single();
                var crossSection = data.PlotPanes[paneIndex + 1].Series.Single();
                Assert.Equal(AnalysisSeriesKind.Heatmap, map.Kind);
                Assert.Equal(AnalysisColorMap.Jet, map.ColorMap);
                Assert.Equal(0, map.ValueMinimum);
                AssertClose(expectedPeak, map.ValueMaximum!.Value);
                Assert.Equal("Radiant Intensity (W/sr)", map.ValueLabel);
                Assert.Equal(15 * 13, map.Points.Count);
                for (var x = 0; x < 15; x++)
                {
                    for (var y = 0; y < 13; y++)
                    {
                        var point = map.Points[(x * 13) + y];
                        AssertClose(xCenters[x].GetDouble(), point.X);
                        AssertClose(yCenters[y].GetDouble(), point.Y);
                        AssertClose(expectedMaps[field][wavelength][x][y].GetDouble(), point.Value!.Value);
                    }

                    AssertClose(xCenters[x].GetDouble(), crossSection.Points[x].X);
                    AssertClose(expectedMaps[field][wavelength][x][6].GetDouble(), crossSection.Points[x].Y);
                }

                Assert.Equal("Central Cross-Section", data.PlotPanes[paneIndex + 1].Title);
                Assert.True(data.PlotPanes[paneIndex].PlotOptions.DottedGrid);
            }
        }

        AssertClose(expectedPeak, Convert.ToDouble(data.Values["PeakRadiantIntensity"]));
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void GeometricMtfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("geometric_mtf");
        var data = new GeometricMtfAnalysis(
            createOptic(),
            numRays: 9,
            distribution: "uniform",
            numPoints: 33).GenerateData();

        Assert.Equal(6, data.PlotSeries.Count);
        for (var field = 0; field < 3; field++)
        {
            var tangential = data.PlotSeries[field * 2];
            var sagittal = data.PlotSeries[(field * 2) + 1];
            Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
            Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
            Assert.Equal(tangential.ColorIndex, sagittal.ColorIndex);
            Assert.Equal(expected.GetProperty("frequency").GetArrayLength(), tangential.Points.Count);
            for (var index = 0; index < tangential.Points.Count; index++)
            {
                AssertClose(expected.GetProperty("frequency")[index].GetDouble(), tangential.Points[index].X);
                AssertClose(expected.GetProperty("tangential")[field][index].GetDouble(), tangential.Points[index].Y);
                AssertClose(expected.GetProperty("sagittal")[field][index].GetDouble(), sagittal.Points[index].Y);
            }
        }

        AssertClose(expected.GetProperty("max_frequency").GetDouble(), data.PlotOptions!.XMaximum!.Value);
        Assert.Equal(0, data.PlotOptions.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
        Assert.True(data.PlotOptions.ShowLegend);
        Assert.Equal("Geometric", data.Values["Method"]);
    }

    [Fact]
    public void GeometricMtfMaximumFrequencyChangesTheCalculatedFrequencyRange()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var diffractionCutoff = 1 / (
            wavelength.Micrometers
            * 1e-3
            * Math.Abs(optic.Paraxial.EstimateFNumber()));
        var lowerMaximum = diffractionCutoff / 2;

        var lowerRange = new GeometricMtfAnalysis(
            optic,
            numRays: 9,
            distribution: "uniform",
            numPoints: 3,
            maximumFrequency: lowerMaximum).GenerateData();
        var fullRange = new GeometricMtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 9,
            distribution: "uniform",
            numPoints: 3,
            maximumFrequency: diffractionCutoff).GenerateData();

        AssertClose(diffractionCutoff, Convert.ToDouble(lowerRange.Values["CutoffFrequency"]));
        AssertClose(lowerMaximum, Convert.ToDouble(lowerRange.Values["MaximumFrequency"]));
        AssertClose(lowerMaximum, lowerRange.PlotOptions!.XMaximum!.Value);
        AssertClose(lowerMaximum, lowerRange.PlotSeries[0].Points[^1].X);
        AssertClose(
            fullRange.PlotSeries[0].Points[1].Y,
            lowerRange.PlotSeries[0].Points[^1].Y);
        Assert.True(lowerRange.PlotSeries[0].Points[^1].Y > 0);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void RayFanMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("ray_fan");
        var data = new RayFanAnalysis(createOptic(), numPoints: 17).GenerateData();
        Assert.NotNull(data.PlotPanes);
        var fieldCount = expected.GetProperty("fields").GetArrayLength();
        var wavelengthCount = expected.GetProperty("wavelengths").GetArrayLength();
        Assert.Equal(fieldCount * 2, data.PlotPanes.Count);
        Assert.Equal(2, data.PlotPaneColumns);

        for (var field = 0; field < fieldCount; field++)
        {
            var yPane = data.PlotPanes[field * 2];
            var xPane = data.PlotPanes[(field * 2) + 1];
            for (var wavelength = 0; wavelength < wavelengthCount; wavelength++)
            {
                var expectedX = expected.GetProperty("x")[field][wavelength];
                var expectedY = expected.GetProperty("y")[field][wavelength];
                for (var index = 0; index < expectedX.GetArrayLength(); index++)
                {
                    AssertClose(expected.GetProperty("px")[index].GetDouble(), xPane.Series[wavelength].Points[index].X);
                    AssertClose(expected.GetProperty("py")[index].GetDouble(), yPane.Series[wavelength].Points[index].X);
                    AssertClose(expectedX[index].GetDouble(), xPane.Series[wavelength].Points[index].Y);
                    AssertClose(expectedY[index].GetDouble(), yPane.Series[wavelength].Points[index].Y);
                }
            }

            var expectedYLimits = expected.GetProperty("panes")[field * 2].GetProperty("y_lim");
            AssertClose(expectedYLimits[0].GetDouble(), yPane.PlotOptions.YMinimum!.Value);
            AssertClose(expectedYLimits[1].GetDouble(), yPane.PlotOptions.YMaximum!.Value);
            Assert.True(yPane.PlotOptions.ShowVerticalZeroLine);
            Assert.True(yPane.PlotOptions.ShowHorizontalZeroLine);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void BestFitRayFanMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("best_fit_ray_fan");
        var optic = createOptic();
        var maxField = optic.Fields.Select(field => Math.Sqrt(
                (field.XAngleDegrees * field.XAngleDegrees)
                + (field.YAngleDegrees * field.YAngleDegrees)))
            .DefaultIfEmpty(0)
            .Max();
        var fields = optic.Fields.Select(field => (
            Hx: maxField <= 1e-12 ? 0 : field.XAngleDegrees / maxField,
            Hy: maxField <= 1e-12 ? 0 : field.YAngleDegrees / maxField)).ToArray();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        for (var field = 0; field < fields.Length; field++)
        {
            var sphere = BestFitSphereEngine.Calculate(optic, fields[field], wavelength, numRings: 5);
            AssertClose(expected.GetProperty("centers")[field][0].GetDouble(), sphere.CenterX);
            AssertClose(expected.GetProperty("centers")[field][1].GetDouble(), sphere.CenterY);
            AssertClose(expected.GetProperty("centers")[field][2].GetDouble(), sphere.CenterZ);
            AssertClose(expected.GetProperty("radii")[field].GetDouble(), sphere.Radius);
        }

        var data = new BestFitRayFanAnalysis(optic, numPoints: 17, numRingsForFit: 5).GenerateData();
        Assert.NotNull(data.PlotPanes);
        Assert.Equal(6, data.PlotPanes.Count);
        Assert.Equal(2, data.PlotPaneColumns);
        for (var field = 0; field < fields.Length; field++)
        {
            var yPane = data.PlotPanes[field * 2];
            var xPane = data.PlotPanes[(field * 2) + 1];
            for (var wave = 0; wave < optic.Wavelengths.Count; wave++)
            {
                for (var index = 0; index < 17; index++)
                {
                    AssertClose(expected.GetProperty("px")[index].GetDouble(), xPane.Series[wave].Points[index].X);
                    AssertClose(expected.GetProperty("py")[index].GetDouble(), yPane.Series[wave].Points[index].X);
                    var expectedX = expected.GetProperty("x")[field][wave][index].GetDouble();
                    var expectedY = expected.GetProperty("y")[field][wave][index].GetDouble();
                    var validX = expected.GetProperty("intensity_x")[field][wave][index].GetDouble() > 0;
                    var validY = expected.GetProperty("intensity_y")[field][wave][index].GetDouble() > 0;
                    if (validX)
                    {
                        AssertClose(expectedX, xPane.Series[wave].Points[index].Y);
                    }
                    else
                    {
                        Assert.True(double.IsNaN(xPane.Series[wave].Points[index].Y));
                    }

                    if (validY)
                    {
                        AssertClose(expectedY, yPane.Series[wave].Points[index].Y);
                    }
                    else
                    {
                        Assert.True(double.IsNaN(yPane.Series[wave].Points[index].Y));
                    }
                }
            }

            AssertClose(expected.GetProperty("panes")[field * 2].GetProperty("y_lim")[0].GetDouble(), yPane.PlotOptions.YMinimum!.Value);
            AssertClose(expected.GetProperty("panes")[field * 2].GetProperty("y_lim")[1].GetDouble(), yPane.PlotOptions.YMaximum!.Value);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void SampledMtfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("sampled_mtf");
        var data = new SampledMtfAnalysis(
            createOptic(),
            pupilSampling: 16,
            zernikeTerms: 37,
            numPoints: 33).GenerateData();

        Assert.Equal(6, data.PlotSeries.Count);
        for (var field = 0; field < 3; field++)
        {
            var tangential = data.PlotSeries[field * 2];
            var sagittal = data.PlotSeries[(field * 2) + 1];
            Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
            Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
            for (var index = 0; index < 33; index++)
            {
                AssertClose(expected.GetProperty("frequency")[index].GetDouble(), tangential.Points[index].X);
                AssertClose(expected.GetProperty("tangential")[field][index].GetDouble(), tangential.Points[index].Y);
                AssertClose(expected.GetProperty("sagittal")[field][index].GetDouble(), sagittal.Points[index].Y);
            }
        }

        AssertClose(expected.GetProperty("frequency")[32].GetDouble(), data.PlotOptions!.XMaximum!.Value);
        Assert.Equal(0, data.PlotOptions.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
        Assert.Equal("Sampled", data.Values["Method"]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ThroughFocusSpotMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("through_focus_spot");
        var data = new ThroughFocusAnalysis(
            createOptic(),
            deltaFocus: 0.1,
            numSteps: 3,
            numRings: 3).GenerateData();
        Assert.NotNull(data.PlotPanes);
        var fieldCount = expected.GetProperty("fields").GetArrayLength();
        var stepCount = expected.GetProperty("defocus").GetArrayLength();
        var wavelengthCount = expected.GetProperty("wavelengths").GetArrayLength();
        Assert.Equal(fieldCount * stepCount, data.PlotPanes.Count);
        Assert.Equal(stepCount, data.PlotPaneColumns);

        for (var field = 0; field < fieldCount; field++)
        {
            for (var step = 0; step < stepCount; step++)
            {
                var paneIndex = (field * stepCount) + step;
                var pane = data.PlotPanes[paneIndex];
                Assert.Equal(expected.GetProperty("panes")[paneIndex].GetProperty("title").GetString(), pane.Title);
                for (var wavelength = 0; wavelength < wavelengthCount; wavelength++)
                {
                    var expectedX = expected.GetProperty("x")[step][field][wavelength];
                    var expectedY = expected.GetProperty("y")[step][field][wavelength];
                    for (var ray = 0; ray < expectedX.GetArrayLength(); ray++)
                    {
                        AssertClose(expectedX[ray].GetDouble(), pane.Series[wavelength].Points[ray].X);
                        AssertClose(expectedY[ray].GetDouble(), pane.Series[wavelength].Points[ray].Y);
                    }
                }

                var expectedLimits = expected.GetProperty("panes")[paneIndex].GetProperty("x_lim");
                AssertClose(expectedLimits[0].GetDouble(), pane.PlotOptions.XMinimum!.Value);
                AssertClose(expectedLimits[1].GetDouble(), pane.PlotOptions.XMaximum!.Value);
                Assert.True(pane.PlotOptions.EqualAspect);
            }
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ThroughFocusMtfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("through_focus_mtf");
        var data = new ThroughFocusMtfAnalysis(
            createOptic(),
            spatialFrequency: 20,
            deltaFocus: 0.1,
            numSteps: 5,
            pupilSampling: 16).GenerateData();
        var tangential = Assert.IsType<double[][]>(data.Values["RawTangential"]);
        var sagittal = Assert.IsType<double[][]>(data.Values["RawSagittal"]);
        Assert.Equal(expected.GetProperty("fields").GetArrayLength() * 2, data.PlotSeries.Count);
        for (var field = 0; field < tangential.Length; field++)
        {
            for (var step = 0; step < tangential[field].Length; step++)
            {
                AssertClose(expected.GetProperty("tangential")[field][step].GetDouble(), tangential[field][step]);
                AssertClose(expected.GetProperty("sagittal")[field][step].GetDouble(), sagittal[field][step]);
            }
        }

        for (var series = 0; series < data.PlotSeries.Count; series++)
        {
            var expectedX = expected.GetProperty("series_x")[series];
            var expectedY = expected.GetProperty("series_y")[series];
            Assert.Equal(256, data.PlotSeries[series].Points.Count);
            Assert.Equal(expected.GetProperty("line_labels")[series].GetString(), data.PlotSeries[series].Name);
            for (var index = 0; index < data.PlotSeries[series].Points.Count; index++)
            {
                AssertClose(expectedX[index].GetDouble(), data.PlotSeries[series].Points[index].X);
                AssertClose(expectedY[index].GetDouble(), data.PlotSeries[series].Points[index].Y);
            }
        }

        Assert.Equal(AnalysisLineStyle.Solid, data.PlotSeries[0].LineStyle);
        Assert.Equal(AnalysisLineStyle.Dashed, data.PlotSeries[1].LineStyle);
        Assert.Equal(0, data.PlotOptions?.YMinimum);
        Assert.Equal(1.05, data.PlotOptions?.YMaximum);
        Assert.True(data.PlotOptions?.ShowLegend);
        Assert.True(data.PlotOptions?.DottedGrid);
    }

    [Theory]
    [InlineData("cooke", -50.961347703805274, 10.233729452318345, 0.8782847343828784)]
    [InlineData("tessar", -3.9168450744779424, 0.8740223235625226, 0.9410197321397179)]
    public void ExitPupilGeometryMatchesPythonOptiland(string sampleName, double expectedLocation, double expectedDiameter, double expectedMtf)
    {
        var optic = sampleName == "cooke" ? Optic.CreateCookeTriplet() : Optic.CreateTessarLens();
        AssertClose(expectedLocation, optic.Paraxial.EstimateExitPupilLocation());
        AssertClose(expectedDiameter, optic.Paraxial.EstimateExitPupilDiameter());
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        AssertClose(expectedMtf, SampledMtfEngine.Calculate(optic, (0, 0), wavelength, 20, 0, 16));
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void PupilAberrationMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("pupil_aberration");
        var data = new PupilAberrationAnalysis(createOptic(), numPoints: 17).GenerateData();
        Assert.NotNull(data.PlotPanes);
        var fieldCount = expected.GetProperty("fields").GetArrayLength();
        var wavelengthCount = expected.GetProperty("wavelengths").GetArrayLength();
        Assert.Equal(fieldCount * 2, data.PlotPanes.Count);

        for (var field = 0; field < fieldCount; field++)
        {
            var yPane = data.PlotPanes[field * 2];
            var xPane = data.PlotPanes[(field * 2) + 1];
            for (var wavelength = 0; wavelength < wavelengthCount; wavelength++)
            {
                var expectedX = expected.GetProperty("x")[field][wavelength];
                var expectedY = expected.GetProperty("y")[field][wavelength];
                for (var index = 0; index < expectedX.GetArrayLength(); index++)
                {
                    AssertClose(expectedX[index].GetDouble(), xPane.Series[wavelength].Points[index].Y);
                    AssertClose(expectedY[index].GetDouble(), yPane.Series[wavelength].Points[index].Y);
                }
            }

            var expectedLimits = expected.GetProperty("panes")[field * 2].GetProperty("y_lim");
            AssertClose(expectedLimits[0].GetDouble(), yPane.PlotOptions.YMinimum!.Value);
            AssertClose(expectedLimits[1].GetDouble(), yPane.PlotOptions.YMaximum!.Value);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void YYbarMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("yybar");
        var data = new YYbarAnalysis(createOptic()).GenerateData();
        var expectedMarginal = expected.GetProperty("ya");
        var expectedChief = expected.GetProperty("yb");
        Assert.Equal(expectedMarginal.GetArrayLength() - 1, data.PlotSeries.Count);

        for (var segment = 0; segment < data.PlotSeries.Count; segment++)
        {
            AssertClose(expectedChief[segment].GetDouble(), data.PlotSeries[segment].Points[0].X);
            AssertClose(expectedMarginal[segment].GetDouble(), data.PlotSeries[segment].Points[0].Y);
            AssertClose(expectedChief[segment + 1].GetDouble(), data.PlotSeries[segment].Points[1].X);
            AssertClose(expectedMarginal[segment + 1].GetDouble(), data.PlotSeries[segment].Points[1].Y);
        }

        Assert.Equal("Chief Ray Height (mm)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Marginal Ray Height (mm)", data.PlotSeries[0].YAxisLabel);
        Assert.True(data.PlotOptions?.ShowVerticalZeroLine);
        Assert.True(data.PlotOptions?.ShowHorizontalZeroLine);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ChiefRayWavefrontMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("wavefront");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = WavefrontEngine.GenerateChiefRay(optic, (0, 1), wavelength, numRings: 5);
        Assert.Equal(expected.GetProperty("opd").GetArrayLength(), actual.Samples.Count);

        for (var index = 0; index < actual.Samples.Count; index++)
        {
            var sample = actual.Samples[index];
            AssertClose(expected.GetProperty("normalized_pupil_x")[index].GetDouble(), sample.NormalizedPupilX);
            AssertClose(expected.GetProperty("normalized_pupil_y")[index].GetDouble(), sample.NormalizedPupilY);
            AssertClose(expected.GetProperty("pupil_x")[index].GetDouble(), sample.PupilX);
            AssertClose(expected.GetProperty("pupil_y")[index].GetDouble(), sample.PupilY);
            AssertClose(expected.GetProperty("pupil_z")[index].GetDouble(), sample.PupilZ);
            AssertClose(expected.GetProperty("opd")[index].GetDouble(), sample.OpdWaves);
            AssertClose(expected.GetProperty("intensity")[index].GetDouble(), sample.Intensity);
        }

        AssertClose(expected.GetProperty("radius").GetDouble(), actual.Radius);
        AssertClose(expected.GetProperty("rms").GetDouble(), actual.Rms);
    }

    [Theory]
    [MemberData(nameof(ReferenceSphereWavefrontCases))]
    public void ReferenceSphereWavefrontMatchesPythonOptilandRayForRay(
        string sampleName,
        Func<Optic> createOptic,
        string referenceKey,
        ReferenceSphereStrategy strategy,
        string referenceName,
        string analysisName)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty(referenceKey);
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = ReferenceSphereWavefrontEngine.Generate(optic, (0, 1), wavelength, numRings: 5, strategy);
        Assert.Equal(expected.GetProperty("opd").GetArrayLength(), actual.Samples.Count);
        Assert.Equal(strategy == ReferenceSphereStrategy.CentroidSphere ? "centroid_sphere" : "best_fit_sphere", referenceName);
        Assert.Equal(strategy == ReferenceSphereStrategy.CentroidSphere ? "Centroid Sphere Wavefront" : "Best Fit Sphere Wavefront", analysisName);

        AssertClose(expected.GetProperty("field")[0].GetDouble(), 0);
        AssertClose(expected.GetProperty("field")[1].GetDouble(), 1);
        AssertClose(expected.GetProperty("wavelength").GetDouble(), wavelength.Micrometers);
        AssertClose(expected.GetProperty("center")[0].GetDouble(), actual.CenterX);
        AssertClose(expected.GetProperty("center")[1].GetDouble(), actual.CenterY);
        AssertClose(expected.GetProperty("center")[2].GetDouble(), actual.CenterZ);
        AssertClose(expected.GetProperty("radius").GetDouble(), actual.Radius);
        AssertClose(expected.GetProperty("rms").GetDouble(), actual.Rms);

        for (var index = 0; index < actual.Samples.Count; index++)
        {
            var sample = actual.Samples[index];
            AssertClose(expected.GetProperty("normalized_pupil_x")[index].GetDouble(), sample.NormalizedPupilX);
            AssertClose(expected.GetProperty("normalized_pupil_y")[index].GetDouble(), sample.NormalizedPupilY);
            AssertClose(expected.GetProperty("pupil_x")[index].GetDouble(), sample.PupilX);
            AssertClose(expected.GetProperty("pupil_y")[index].GetDouble(), sample.PupilY);
            AssertClose(expected.GetProperty("pupil_z")[index].GetDouble(), sample.PupilZ);
            AssertClose(expected.GetProperty("opd")[index].GetDouble(), sample.OpdWaves);
            AssertClose(expected.GetProperty("intensity")[index].GetDouble(), sample.Intensity);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FringeZernikeFitMatchesPythonOptiland(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("zernike");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var wavefront = WavefrontEngine.GenerateChiefRay(optic, (0, 1), wavelength, numRings: 5);
        var actual = ZernikeFitEngine.FitFringe(wavefront.Samples, numTerms: 15);
        Assert.Equal(expected.GetProperty("coefficients").GetArrayLength(), actual.Count);

        for (var index = 0; index < actual.Count; index++)
        {
            Assert.Equal(expected.GetProperty("indices")[index][0].GetInt32(), actual[index].RadialOrder);
            Assert.Equal(expected.GetProperty("indices")[index][1].GetInt32(), actual[index].AzimuthalOrder);
            AssertClose(expected.GetProperty("coefficients")[index].GetDouble(), actual[index].Value);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FftPsfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_psf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = DiffractionEngine.ComputeFftPsf(optic, (0, 1), wavelength, 16, 32);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), actual.WorkingFNumber);
        AssertClose(expected.GetProperty("strehl").GetDouble(), actual.StrehlRatio);
        var expectedPsf = expected.GetProperty("psf");
        for (var row = 0; row < actual.GridSize; row++)
        {
            for (var column = 0; column < actual.GridSize; column++)
            {
                AssertClose(expectedPsf[row][column].GetDouble(), actual.Values[row, column]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FftMtfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_mtf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 1), wavelength, 16, 32);
        var actual = DiffractionEngine.ComputeFftMtf(psf, optic, wavelength);
        AssertClose(expected.GetProperty("cutoff").GetDouble(), actual.CutoffFrequency);
        for (var index = 0; index < actual.Frequency.Count; index++)
        {
            AssertClose(expected.GetProperty("frequency")[index].GetDouble(), actual.Frequency[index]);
            AssertClose(expected.GetProperty("tangential")[index].GetDouble(), actual.Tangential[index]);
            AssertClose(expected.GetProperty("sagittal")[index].GetDouble(), actual.Sagittal[index]);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MmdftPsfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("mmdft_psf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = DiffractionEngine.ComputeMmdftPsf(
            optic,
            (0, 1),
            wavelength,
            expected.GetProperty("num_rays").GetInt32(),
            expected.GetProperty("image_size").GetInt32());
        AssertClose(expected.GetProperty("wavelength").GetDouble(), wavelength.Micrometers);
        AssertClose(expected.GetProperty("pixel_pitch").GetDouble(), actual.SampleSpacingMicrometers);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), actual.WorkingFNumber);
        AssertClose(expected.GetProperty("strehl").GetDouble(), actual.PeakStrehlRatio);
        AssertPsfValues(expected.GetProperty("psf"), actual);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensPsfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("huygens_psf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = DiffractionEngine.ComputeHuygensPsf(
            optic,
            (0, 1),
            wavelength,
            expected.GetProperty("num_rays").GetInt32(),
            expected.GetProperty("image_size").GetInt32(),
            expected.GetProperty("pixel_pitch").GetDouble());
        AssertClose(expected.GetProperty("wavelength").GetDouble(), wavelength.Micrometers);
        AssertClose(expected.GetProperty("pixel_pitch").GetDouble(), actual.SampleSpacingMicrometers / 1000.0);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), actual.WorkingFNumber);
        AssertClose(expected.GetProperty("strehl").GetDouble(), actual.StrehlRatio);
        AssertPsfValues(expected.GetProperty("psf"), actual);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensMtfMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("huygens_mtf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var psf = DiffractionEngine.ComputeHuygensPsf(
            optic,
            (0, 1),
            wavelength,
            5,
            expected.GetProperty("image_size").GetInt32(),
            expected.GetProperty("pixel_pitch").GetDouble());
        var actual = DiffractionEngine.ComputePsfMtf(psf);
        for (var index = 0; index < actual.Frequency.Count; index++)
        {
            AssertClose(expected.GetProperty("frequency")[index].GetDouble(), actual.Frequency[index]);
            AssertClose(expected.GetProperty("tangential")[index].GetDouble(), actual.Tangential[index]);
            AssertClose(expected.GetProperty("sagittal")[index].GetDouble(), actual.Sagittal[index]);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void WavefrontAnalysisUsesPythonOpdMapContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("wavefront");
        var data = new WavefrontAnalysis(createOptic(), numRings: 5, mapSize: 33).GenerateData();
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Pupil X", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Pupil Y", data.PlotSeries[0].YAxisLabel);
        Assert.Equal("OPD (waves)", data.PlotSeries[0].ValueLabel);
        AssertClose(expected.GetProperty("rms").GetDouble(), Convert.ToDouble(data.Values["RmsWaves"]));
        Assert.Equal($"OPD Map: RMS={expected.GetProperty("rms").GetDouble():0.000} waves", data.PlotOptions?.Title);
    }

    [Theory]
    [MemberData(nameof(ReferenceSphereWavefrontCases))]
    public void ReferenceSphereWavefrontAnalysisUsesPythonOpdMapContract(
        string sampleName,
        Func<Optic> createOptic,
        string referenceKey,
        ReferenceSphereStrategy strategy,
        string referenceName,
        string analysisName)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty(referenceKey);
        var data = new ReferenceSphereWavefrontAnalysis(createOptic(), strategy, numRings: 5, mapSize: 33).GenerateData();
        Assert.Equal(analysisName, data.Name);
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Pupil X", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Pupil Y", data.PlotSeries[0].YAxisLabel);
        Assert.Equal("OPD (waves)", data.PlotSeries[0].ValueLabel);
        Assert.Equal(referenceName, data.Values["Reference"]);
        Assert.Equal(expected.GetProperty("opd").GetArrayLength(), Convert.ToInt32(data.Values["RayCount"]));
        Assert.Equal(0, Convert.ToInt32(data.Values["VignettedRayCount"]));
        AssertClose(expected.GetProperty("rms").GetDouble(), Convert.ToDouble(data.Values["RmsWaves"]));
        AssertClose(expected.GetProperty("radius").GetDouble(), Convert.ToDouble(data.Values["ReferenceSphereRadius"]));
        AssertClose(expected.GetProperty("field")[0].GetDouble(), Convert.ToDouble(data.Values["FieldHx"]));
        AssertClose(expected.GetProperty("field")[1].GetDouble(), Convert.ToDouble(data.Values["FieldHy"]));
        AssertClose(expected.GetProperty("wavelength").GetDouble(), Convert.ToDouble(data.Values["WavelengthMicrometers"]));
        Assert.Equal($"OPD Map: RMS={expected.GetProperty("rms").GetDouble():0.000} waves", data.PlotOptions?.Title);
        Assert.True(data.PlotOptions?.EqualAspect);
        Assert.Equal(-1, data.PlotOptions?.XMinimum);
        Assert.Equal(1, data.PlotOptions?.XMaximum);
        Assert.Equal(-1, data.PlotOptions?.YMinimum);
        Assert.Equal(1, data.PlotOptions?.YMaximum);
        Assert.StartsWith("(", Assert.IsType<string>(data.Values["ReferenceSphereCenter"]));
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ZernikeAnalysisUsesPythonFringeHeatmapContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("zernike");
        var data = new ZernikeAnalysis(createOptic(), numRings: 5, numTerms: 15, mapSize: 33).GenerateData();
        Assert.Equal(AnalysisSeriesKind.Bar, data.Series?.Kind);
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Zernike Fringe Fit", data.PlotOptions?.Title);
        Assert.Equal("OPD (waves)", data.PlotSeries[0].ValueLabel);
        Assert.Equal(expected.GetProperty("coefficients").GetArrayLength(), data.Series?.Points.Count);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void PsfAnalysisUsesPythonFftHeatmapContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_psf");
        var data = new PsfAnalysis(createOptic(), numRays: 16, gridSize: 32).GenerateData();
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Relative Intensity (%)", data.PlotSeries[0].ValueLabel);
        Assert.Equal("FFT PSF", data.PlotOptions?.Title);
        AssertClose(expected.GetProperty("strehl").GetDouble(), Convert.ToDouble(data.Values["StrehlRatio"]));
        Assert.Equal("FFT", data.Values["Method"]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MtfAnalysisUsesPythonFftSeriesContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_mtf");
        var data = new MtfAnalysis(createOptic(), numRays: 16, gridSize: 32).GenerateData();
        Assert.Equal(6, data.PlotSeries.Count);
        var tangential = data.PlotSeries[^2];
        var sagittal = data.PlotSeries[^1];
        Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
        Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
        Assert.Equal(tangential.ColorIndex, sagittal.ColorIndex);
        for (var index = 0; index < tangential.Points.Count; index++)
        {
            AssertClose(expected.GetProperty("frequency")[index].GetDouble(), tangential.Points[index].X);
            AssertClose(expected.GetProperty("tangential")[index].GetDouble(), tangential.Points[index].Y);
            AssertClose(expected.GetProperty("sagittal")[index].GetDouble(), sagittal.Points[index].Y);
        }

        AssertClose(expected.GetProperty("cutoff").GetDouble(), data.PlotOptions!.XMaximum!.Value);
        Assert.Equal(0, data.PlotOptions.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MmdftPsfAnalysisUsesPythonHeatmapContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("mmdft_psf");
        var data = new MmdftPsfAnalysis(
            createOptic(),
            numRays: expected.GetProperty("num_rays").GetInt32(),
            imageSize: expected.GetProperty("image_size").GetInt32()).GenerateData();
        Assert.Equal("MMDFT PSF", data.Name);
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Relative Intensity (%)", data.PlotSeries[0].ValueLabel);
        Assert.Equal("MMDFT PSF", data.PlotOptions?.Title);
        Assert.Equal("MMDFT", data.Values["Method"]);
        AssertClose(expected.GetProperty("pixel_pitch").GetDouble(), Convert.ToDouble(data.Values["PixelPitchMicrometers"]));
        AssertClose(expected.GetProperty("working_fno").GetDouble(), Convert.ToDouble(data.Values["WorkingFNumber"]));
        AssertClose(expected.GetProperty("strehl").GetDouble(), Convert.ToDouble(data.Values["StrehlRatio"]));
        AssertPsfSeriesValues(expected.GetProperty("psf"), data.PlotSeries[0]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensPsfAnalysisUsesPythonHeatmapContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("huygens_psf");
        var data = new HuygensPsfAnalysis(
            createOptic(),
            numRays: expected.GetProperty("num_rays").GetInt32(),
            imageSize: expected.GetProperty("image_size").GetInt32(),
            pixelPitchMillimeters: expected.GetProperty("pixel_pitch").GetDouble()).GenerateData();
        Assert.Equal("Huygens PSF", data.Name);
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Relative Intensity (%)", data.PlotSeries[0].ValueLabel);
        Assert.Equal("Huygens PSF", data.PlotOptions?.Title);
        Assert.Equal("Huygens-Fresnel", data.Values["Method"]);
        AssertClose(expected.GetProperty("pixel_pitch").GetDouble(), Convert.ToDouble(data.Values["PixelPitchMicrometers"]) / 1000.0);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), Convert.ToDouble(data.Values["WorkingFNumber"]));
        AssertClose(expected.GetProperty("strehl").GetDouble(), Convert.ToDouble(data.Values["StrehlRatio"]));
        AssertPsfSeriesValues(expected.GetProperty("psf"), data.PlotSeries[0]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensMtfAnalysisUsesPythonSeriesContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("huygens_mtf");
        var data = new HuygensMtfAnalysis(
            createOptic(),
            numRays: 5,
            imageSize: expected.GetProperty("image_size").GetInt32(),
            pixelPitchMillimeters: expected.GetProperty("pixel_pitch").GetDouble(),
            fields: new[] { (0.0, 1.0) }).GenerateData();
        Assert.Equal("Huygens MTF", data.Name);
        Assert.Equal("Huygens MTF", data.PlotOptions?.Title);
        Assert.Equal("Huygens-Fresnel", data.Values["Method"]);
        Assert.Equal(2, data.PlotSeries.Count);
        var tangential = data.PlotSeries[0];
        var sagittal = data.PlotSeries[1];
        Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
        Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
        for (var index = 0; index < tangential.Points.Count; index++)
        {
            AssertClose(expected.GetProperty("frequency")[index].GetDouble(), tangential.Points[index].X);
            AssertClose(expected.GetProperty("tangential")[index].GetDouble(), tangential.Points[index].Y);
            AssertClose(expected.GetProperty("sagittal")[index].GetDouble(), sagittal.Points[index].Y);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void JonesPupilMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("jones_pupil");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = JonesPupilEngine.Generate(optic, (0, 0), wavelength, gridSize: 9, useFresnelCoatings: true);
        Assert.Equal(expected.GetProperty("grid_size").GetInt32(), actual.GridSize);
        Assert.Equal(expected.GetProperty("valid").GetArrayLength(), actual.Samples.Count);

        var components = new (string Real, string Imag, Func<JonesPupilSample, System.Numerics.Complex> Select)[]
        {
            ("jxx_real", "jxx_imag", sample => sample.Jxx),
            ("jxy_real", "jxy_imag", sample => sample.Jxy),
            ("jyx_real", "jyx_imag", sample => sample.Jyx),
            ("jyy_real", "jyy_imag", sample => sample.Jyy)
        };
        for (var index = 0; index < actual.Samples.Count; index++)
        {
            var sample = actual.Samples[index];
            var isValid = expected.GetProperty("valid")[index].GetBoolean();
            Assert.Equal(isValid, sample.IsValid);
            AssertClose(expected.GetProperty("px")[index].GetDouble(), sample.Px);
            AssertClose(expected.GetProperty("py")[index].GetDouble(), sample.Py);
            if (!isValid)
            {
                continue;
            }

            foreach (var component in components)
            {
                var value = component.Select(sample);
                AssertClose(expected.GetProperty(component.Real)[index].GetDouble(), value.Real);
                AssertClose(expected.GetProperty(component.Imag)[index].GetDouble(), value.Imaginary);
            }
        }
    }

    [Fact]
    public void JonesPupilAnalysisUsesPythonTwoByFourHeatmapContract()
    {
        var data = new JonesPupilAnalysis(Optic.CreateCookeTriplet(), gridSize: 9).GenerateData();
        Assert.NotNull(data.PlotPanes);
        Assert.Equal(8, data.PlotPanes.Count);
        Assert.Equal(4, data.PlotPaneColumns);
        Assert.Equal(
            new[] { "Re(Jxx)", "Re(Jxy)", "Re(Jyx)", "Re(Jyy)", "Im(Jxx)", "Im(Jxy)", "Im(Jyx)", "Im(Jyy)" },
            data.PlotPanes.Select(pane => pane.Title));
        Assert.All(data.PlotPanes, pane => Assert.Equal(AnalysisSeriesKind.Heatmap, pane.Series.Single().Kind));
        Assert.Equal("Fresnel", data.Values["CoatingMode"]);
    }

    [Fact]
    public void SpatiallyVariableConvolutionMatchesPythonScipyFftConvolve()
    {
        var source = new double[,]
        {
            { 0.0, 0.1, 0.2, 0.3 },
            { 0.4, 0.5, 0.6, 0.7 },
            { 0.8, 0.9, 1.0, 1.1 }
        };
        var mean = new double[,] { { 0.1, 0.2 }, { 0.3, 0.4 } };
        var eigen = new[] { new double[,] { { 0.2, -0.1 }, { 0.05, 0.15 } } };
        var coefficient = new double[1, 3, 4];
        for (var index = 0; index < 12; index++)
        {
            coefficient[0, index / 4, index % 4] = -0.3 + (0.7 * index / 11.0);
        }

        var expected = new double[,]
        {
            { 5.551115123125783e-18, 0.005272727272727251, 0.03545454545454544, 0.0669090909090909 },
            { 0.036363636363636376, 0.1624545454545455, 0.2636363636363637, 0.36863636363636365 },
            { 0.23254545454545447, 0.590090909090909, 0.7065454545454546, 0.8268181818181819 }
        };
        var actual = ImageSimulationEngine.SpatiallyVariableConvolution(source, eigen, coefficient, mean);
        for (var row = 0; row < expected.GetLength(0); row++)
        {
            for (var column = 0; column < expected.GetLength(1); column++)
            {
                AssertClose(expected[row, column], actual[row, column]);
            }
        }
    }

    [Fact]
    public void ImageSimulationAnalysisUsesPythonSideBySideRgbContract()
    {
        var config = new ImageSimulationConfig
        {
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 16,
            NumRays = 8,
            Components = 2,
            Padding = 2,
            DistortionGridSize = 7,
            DistortionPolynomialDegree = 3
        };
        var data = new ImageSimulationAnalysis(Optic.CreateCookeTriplet(), config).GenerateData();
        Assert.NotNull(data.PlotPanes);
        Assert.Equal(2, data.PlotPanes.Count);
        Assert.Equal(2, data.PlotPaneColumns);
        Assert.Equal("Original Image [0]", data.PlotPanes[0].Title);
        Assert.Equal("Simulated Image [0]", data.PlotPanes[1].Title);
        Assert.All(data.PlotPanes, pane =>
        {
            Assert.True(pane.PlotOptions.HideAxes);
            Assert.Equal(AnalysisSeriesKind.Raster, pane.Series.Single().Kind);
            Assert.Equal(64 * 48, pane.Series.Single().Points.Count);
        });
        Assert.DoesNotContain(data.Values.Keys, key => key.Contains("Proxy", StringComparison.OrdinalIgnoreCase));
        Assert.True(Convert.ToDouble(data.Values["MeanAbsoluteChange"]) > 0);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ImageSimulationPipelineMatchesPythonOptilandPixelForPixel(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("image_simulation");
        var config = new ImageSimulationConfig
        {
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 16,
            NumRays = 8,
            Components = 3,
            Padding = 2,
            DistortionGridSize = 7,
            DistortionPolynomialDegree = 3
        };
        var actual = ImageSimulationEngine.Simulate(
            createOptic(),
            ImageSimulationEngine.CreateTestChart(16, 16),
            config);
        Assert.Equal(expected.GetProperty("shape")[0].GetInt32(), actual.Simulated.Channels);
        Assert.Equal(expected.GetProperty("shape")[1].GetInt32(), actual.Simulated.Height);
        Assert.Equal(expected.GetProperty("shape")[2].GetInt32(), actual.Simulated.Width);
        AssertImageClose(expected.GetProperty("maximum").GetDouble(), actual.MaximumValue);
        AssertImageClose(expected.GetProperty("mean_absolute_change").GetDouble(), actual.MeanAbsoluteChange);
        for (var channel = 0; channel < actual.Simulated.Channels; channel++)
        {
            for (var row = 0; row < actual.Simulated.Height; row++)
            {
                for (var column = 0; column < actual.Simulated.Width; column++)
                {
                    AssertImageClose(
                        expected.GetProperty("simulated")[channel][row][column].GetDouble(),
                        actual.Simulated.Values[channel, row, column]);
                }
            }
        }

    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void ImageSimulationBlurAndDistortionStagesMatchPythonOptiland(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("image_simulation");
        var optic = createOptic();
        var source = ImageSimulationEngine.CreateTestChart(16, 16);
        for (var channel = 0; channel < 3; channel++)
        {
            for (var row = 0; row < 16; row++)
            {
                for (var column = 0; column < 16; column++)
                {
                    AssertClose(expected.GetProperty("source")[channel][row][column].GetDouble(), source.Values[channel, row, column]);
                }
            }
        }
        var padded = ImageSimulationEngine.ReflectPad(source.Values, 2);
        var config = new ImageSimulationConfig
        {
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 16,
            NumRays = 8,
            Components = 3,
            Padding = 2,
            DistortionGridSize = 7,
            DistortionPolynomialDegree = 3
        };
        var wavelengths = new[] { 0.65, 0.55, 0.45 };
        for (var channel = 0; channel < wavelengths.Length; channel++)
        {
            var wavelength = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = wavelengths[channel] * 1000 };
            var basis = ImageSimulationEngine.GenerateBasis(optic, wavelength, config);
            var maps = ImageSimulationEngine.ResizeCoefficientMaps(basis.CoefficientGrid, 20, 20);
            var sourceChannel = new double[20, 20];
            for (var row = 0; row < 20; row++)
            {
                for (var column = 0; column < 20; column++)
                {
                    sourceChannel[row, column] = padded[channel, row, column];
                }
            }

            var blurred = ImageSimulationEngine.SpatiallyVariableConvolution(sourceChannel, basis.EigenPsfs, maps, basis.MeanPsf);
            var grid = ImageSimulationEngine.GenerateDistortionGrid(optic, wavelength, 20, 20, 7, 3);
            for (var row = 0; row < 20; row++)
            {
                for (var column = 0; column < 20; column++)
                {
                    AssertImageClose(expected.GetProperty("blurred")[channel][row][column].GetDouble(), blurred[row, column]);
                    AssertClose(expected.GetProperty("distortion_grids")[channel][row][column][0].GetDouble(), grid[row, column].X);
                    AssertClose(expected.GetProperty("distortion_grids")[channel][row][column][1].GetDouble(), grid[row, column].Y);
                }
            }
        }
    }

    [Fact]
    public void ImageSimulationDistortionGridMatchesPythonReferenceCoordinates()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 650 };
        var grid = ImageSimulationEngine.GenerateDistortionGrid(optic, wavelength, 20, 20, 7, 3);
        AssertClose(-1.00178581, grid[0, 0].X);
        AssertClose(-1.00178581, grid[0, 0].Y);
        AssertClose(-0.80251784, grid[2, 2].X);
        AssertClose(-0.80251784, grid[2, 2].Y);
        AssertClose(0.05477868, grid[10, 10].X);
        AssertClose(0.05477868, grid[10, 10].Y);
    }

    [Fact]
    public void ImageSimulationPsfBasisPreservesUnitMeanEnergy()
    {
        var config = new ImageSimulationConfig
        {
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 16,
            NumRays = 8,
            Components = 3
        };
        var optic = Optic.CreateCookeTriplet();
        var wavelength = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 650 };
        var centerPsf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wavelength, 8, 16);
        var cornerWavefront = WavefrontEngine.GenerateChiefRayUniform(optic, (-1, -1), wavelength, 8);
        var cornerPsf = DiffractionEngine.ComputeFftPsf(optic, (-1, -1), wavelength, 8, 16);
        Assert.True(centerPsf.Values.Cast<double>().Sum() > 0);
        Assert.Contains(cornerWavefront.Samples, sample => sample.Intensity > 0);
        Assert.True(cornerWavefront.Samples.All(sample => double.IsFinite(sample.OpdWaves)));
        Assert.True(cornerPsf.Values.Cast<double>().Sum() > 0, $"Corner PSF sum: {cornerPsf.Values.Cast<double>().Sum():R}");
        var cornerSum = cornerPsf.Values.Cast<double>().Sum();
        AssertClose(0.0025213610975391913, cornerPsf.Values[0, 0] / cornerSum);
        AssertClose(7.51274450291732e-05, cornerPsf.Values[3, 7] / cornerSum);
        AssertClose(0.00021681217396903035, cornerPsf.Values[8, 8] / cornerSum);
        var basis = ImageSimulationEngine.GenerateBasis(
            optic,
            wavelength,
            config);
        AssertClose(1, basis.MeanPsf.Cast<double>().Sum());
        var fields = new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) };
        for (var field = 0; field < fields.Length; field++)
        {
            var expected = DiffractionEngine.ComputeFftPsf(optic, fields[field], wavelength, 8, 16).Values;
            var sum = expected.Cast<double>().Sum();
            for (var row = 0; row < 16; row++)
            {
                for (var column = 0; column < 16; column++)
                {
                    var reconstructed = basis.MeanPsf[row, column];
                    for (var component = 0; component < basis.EigenPsfs.Length; component++)
                    {
                        reconstructed += basis.CoefficientGrid[component, field / 2, field % 2]
                            * basis.EigenPsfs[component][row, column];
                    }

                    AssertClose(expected[row, column] / sum, reconstructed);
                }
            }
        }
    }

    [Fact]
    public void DiagonalFullFieldChiefRayMatchesPythonOptiland()
    {
        var history = Optic.CreateCookeTriplet().TraceGeneric(-1, -1, 0, 0, 0.65).RayHistories.Single();
        Assert.Equal(8, history.Count);
        AssertClose(-18.08356061, history[^1].Position.X);
        AssertClose(-18.08356061, history[^1].Position.Y);
        AssertClose(60.17675, history[^1].Position.Z);
    }

    private static void AssertGridLine(
        JsonElement expected,
        string xName,
        string yName,
        int row,
        AnalysisSeries actual)
    {
        var expectedX = expected.GetProperty(xName)[row];
        var expectedY = expected.GetProperty(yName)[row];
        Assert.Equal(expectedX.GetArrayLength(), actual.Points.Count);
        for (var column = 0; column < actual.Points.Count; column++)
        {
            AssertClose(expectedX[column].GetDouble(), actual.Points[column].X);
            AssertClose(expectedY[column].GetDouble(), actual.Points[column].Y);
        }
    }

    private static void AssertGridPoints(
        JsonElement expected,
        string xName,
        string yName,
        AnalysisSeries actual)
    {
        var expectedX = expected.GetProperty(xName);
        var expectedY = expected.GetProperty(yName);
        Assert.Equal(expectedX.GetArrayLength() * expectedX[0].GetArrayLength(), actual.Points.Count);
        var index = 0;
        for (var row = 0; row < expectedX.GetArrayLength(); row++)
        {
            for (var column = 0; column < expectedX[row].GetArrayLength(); column++)
            {
                AssertClose(expectedX[row][column].GetDouble(), actual.Points[index].X);
                AssertClose(expectedY[row][column].GetDouble(), actual.Points[index].Y);
                index++;
            }
        }
    }

    private static void AssertCurvatureSeries(
        JsonElement expected,
        string seriesName,
        int wavelength,
        AnalysisSeries actual)
    {
        var expectedValues = expected.GetProperty(seriesName)[wavelength];
        var expectedField = expected.GetProperty("field");
        Assert.Equal(expectedValues.GetArrayLength(), actual.Points.Count);
        for (var index = 0; index < actual.Points.Count; index++)
        {
            AssertClose(expectedValues[index].GetDouble(), actual.Points[index].X);
            AssertClose(expectedField[index].GetDouble(), actual.Points[index].Y);
        }
    }

    private static void AssertPsfValues(JsonElement expectedPsf, PsfResult actual)
    {
        Assert.Equal(expectedPsf.GetArrayLength(), actual.GridSize);
        for (var row = 0; row < actual.GridSize; row++)
        {
            Assert.Equal(expectedPsf[row].GetArrayLength(), actual.GridSize);
            for (var column = 0; column < actual.GridSize; column++)
            {
                AssertClose(expectedPsf[row][column].GetDouble(), actual.Values[row, column]);
            }
        }
    }

    private static void AssertPsfSeriesValues(JsonElement expectedPsf, AnalysisSeries actual)
    {
        var size = expectedPsf.GetArrayLength();
        Assert.Equal(size * size, actual.Points.Count);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var value = actual.Points[(row * size) + column].Value;
                Assert.True(value.HasValue);
                AssertClose(expectedPsf[row][column].GetDouble(), value.Value);
            }
        }
    }

    private static JsonDocument LoadReference()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "optiland-0.5.8-analysis-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void AssertClose(double expected, double actual)
    {
        var tolerance = 2e-8 * Math.Max(1, Math.Abs(expected));
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
    }

    private static void AssertImageClose(double expected, double actual)
    {
        const double tolerance = 5e-5;
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
    }
}
