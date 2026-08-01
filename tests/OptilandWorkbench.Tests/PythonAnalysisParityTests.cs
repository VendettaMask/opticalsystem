using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Geometries;
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
    public void DistortionUsesZemaxChiefRayReferenceDefinition(string sampleName, Func<Optic> createOptic)
    {
        Assert.False(string.IsNullOrWhiteSpace(sampleName));
        var optic = createOptic();
        var data = new DistortionAnalysis(optic, numPoints: 17).GenerateData();
        Assert.Equal(optic.Wavelengths.Count, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, item => Assert.Equal(17, item.Points.Count));
        Assert.All(data.PlotSeries.SelectMany(item => item.Points), point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.All(data.PlotSeries, item => Assert.Equal(0, item.Points[0].X, precision: 9));

        Assert.Equal("Distortion (%)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("Field Angle (deg)", data.PlotSeries[0].YAxisLabel);
        Assert.Equal("f-tan", data.Values["DistortionType"]);
        Assert.Equal(1, data.Values["ReferenceFieldNumber"]);
        Assert.True(data.PlotOptions?.SymmetricX);
        Assert.True(data.PlotOptions?.ShowVerticalZeroLine);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void DistortionSupportsZemaxFThetaAbsoluteAndWavelengthSelection(string sampleName, Func<Optic> createOptic)
    {
        Assert.False(string.IsNullOrWhiteSpace(sampleName));
        var data = new DistortionAnalysis(
            createOptic(),
            numPoints: 17,
            distortionType: "F-Theta",
            wavelengthNumber: 2,
            scanDirection: "-x",
            displayMode: "absolute").GenerateData();

        Assert.Single(data.PlotSeries);
        Assert.Equal("Distortion (mm)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("f-theta", data.Values["DistortionType"]);
        Assert.Equal("-x", data.Values["ScanDirection"]);
        Assert.Equal(2, data.Values["WavelengthNumber"]);
        Assert.Equal(0, data.PlotSeries[0].Points[0].X, precision: 9);
        Assert.All(data.PlotSeries[0].Points, point => Assert.True(double.IsFinite(point.X)));
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
        Assert.Equal("Real Image Height (mm)", distortion.PlotSeries[0].YAxisLabel);
        Assert.Equal(4.5, distortion.PlotSeries[0].Points.Last().Y, precision: 9);
        Assert.False(gridDistortion.Values.ContainsKey("DistortionType"));
        Assert.Equal("cross", gridDistortion.Values["DisplayMode"]);
        Assert.Equal(4.5, fieldCurvature.Values["MaxRealImageHeightMillimeters"]);
        Assert.Equal("Real Image Height (mm)", fieldCurvature.PlotSeries[0].YAxisLabel);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void GridDistortionUsesZemaxStyleIdealGridAndActualImagePoints(string sampleName, Func<Optic> createOptic)
    {
        Assert.False(string.IsNullOrWhiteSpace(sampleName));
        var data = new GridDistortionAnalysis(createOptic(), numPoints: 10).GenerateData();
        Assert.Equal(21, data.PlotSeries.Count);

        Assert.Equal("理想网格", data.PlotSeries[0].Name);
        Assert.All(data.PlotSeries.Take(20), item => Assert.Equal(AnalysisLineStyle.Solid, item.LineStyle));
        Assert.All(data.PlotSeries.Take(20), item => Assert.Equal(10, item.ColorIndex));
        Assert.All(data.PlotSeries.Take(20).SelectMany(item => item.Points), point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.Equal(AnalysisSeriesKind.Scatter, data.PlotSeries[^1].Kind);
        Assert.Equal("实际像点", data.PlotSeries[^1].Name);
        Assert.Equal(AnalysisMarkerStyle.Cross, data.PlotSeries[^1].MarkerStyle);
        Assert.All(data.PlotSeries[^1].Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.True(Math.Abs(Convert.ToDouble(data.Values["MaximumDistortionPercent"])) > 0);
        Assert.True(data.PlotOptions?.EqualAspect);
        Assert.True(data.PlotOptions?.HideAxes);
        Assert.False(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void GridDistortionSupportsZemaxVectorAndScaleSettings(string sampleName, Func<Optic> createOptic)
    {
        Assert.False(string.IsNullOrWhiteSpace(sampleName));
        var data = new GridDistortionAnalysis(
            createOptic(),
            numPoints: 10,
            displayMode: "vector",
            scale: 2,
            heightWidthAspect: 0.75,
            symmetricMagnification: true).GenerateData();

        Assert.Equal(21, data.PlotSeries.Count);
        Assert.Equal("vector", data.Values["DisplayMode"]);
        Assert.Equal(2.0, data.Values["Scale"]);
        Assert.Equal(0.75, data.Values["HeightWidthAspect"]);
        Assert.Equal(true, data.Values["SymmetricMagnification"]);
        Assert.All(data.PlotSeries.Take(20), item => Assert.Equal(AnalysisLineStyle.Solid, item.LineStyle));
        Assert.Equal(AnalysisSeriesKind.Line, data.PlotSeries[^1].Kind);
        Assert.Equal(300, data.PlotSeries[^1].Points.Count);
        Assert.True(Math.Abs(Convert.ToDouble(data.Values["MaximumDistortionPercent"])) > 0);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FieldCurvatureUsesZemaxSelectedHalfFan(string sampleName, Func<Optic> createOptic)
    {
        Assert.False(string.IsNullOrWhiteSpace(sampleName));
        var optic = createOptic();
        var maximumField = FieldCoordinates.MaximumRadius(optic.Fields);
        var data = new FieldCurvatureAnalysis(
            optic,
            numPoints: 17,
            wavelengthNumber: 2,
            scanDirection: "-x").GenerateData();

        Assert.Equal(2, data.PlotSeries.Count);
        foreach (var item in data.PlotSeries)
        {
            Assert.Equal(17, item.Points.Count);
            for (var index = 0; index < item.Points.Count; index++)
            {
                Assert.Equal(-maximumField * index / 16.0, item.Points[index].Y, 12);
                Assert.True(double.IsFinite(item.Points[index].X));
            }
        }

        Assert.Equal("-x", data.Values["ScanDirection"]);
        Assert.Equal(2, data.Values["WavelengthNumber"]);
        Assert.Equal(AnalysisLineStyle.Solid, data.PlotSeries[0].LineStyle);
        Assert.Equal(AnalysisLineStyle.Dashed, data.PlotSeries[1].LineStyle);
        Assert.Equal(data.PlotSeries[0].ColorIndex, data.PlotSeries[1].ColorIndex);
        Assert.Equal("Field Curvature", data.PlotOptions?.Title);
        Assert.True(data.PlotOptions?.SymmetricX);
        Assert.True(data.PlotOptions?.ShowLegend);
        var maximumTangential = Convert.ToDouble(data.Values["MaximumTangentialFieldCurvatureMillimeters"]);
        var maximumSagittal = Convert.ToDouble(data.Values["MaximumSagittalFieldCurvatureMillimeters"]);
        var maximumAbsolute = Convert.ToDouble(data.Values["MaximumAbsoluteImagePlaneDelta"]);
        Assert.True(double.IsFinite(maximumTangential));
        Assert.True(double.IsFinite(maximumSagittal));
        Assert.Equal(
            Math.Max(Math.Abs(maximumTangential), Math.Abs(maximumSagittal)),
            maximumAbsolute,
            12);
    }

    [Fact]
    public void FieldCurvatureIgnoreVignettingFactorsControlsTheTraceWithoutMutatingTheOptic()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var field in optic.Fields)
        {
            field.VignetteFactorX = 1;
            field.VignetteFactorY = 1;
        }

        var ignored = new FieldCurvatureAnalysis(
            optic, numPoints: 5, parabasalDelta: 0.05, ignoreVignettingFactors: true).GenerateData();
        var applied = new FieldCurvatureAnalysis(
            optic, numPoints: 5, parabasalDelta: 0.05, ignoreVignettingFactors: false).GenerateData();

        Assert.False((bool)ignored.Values["VignettingFactorsApplied"]);
        Assert.True((bool)applied.Values["VignettingFactorsApplied"]);
        Assert.All(optic.Fields, field =>
        {
            Assert.Equal(1, field.VignetteFactorX);
            Assert.Equal(1, field.VignetteFactorY);
        });
        Assert.Contains(
            ignored.PlotSeries.SelectMany(series => series.Points),
            point => Math.Abs(point.X) > 1e-10);
        Assert.All(
            applied.PlotSeries.SelectMany(series => series.Points),
            point => Assert.Equal(0, point.X, 12));
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void SpotDiagramMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("spot_diagram");
        var optic = createOptic();
        var data = new SpotDiagramAnalysis(optic).GenerateData();
        Assert.NotNull(data.PlotPanes);
        Assert.Equal(expected.GetProperty("fields").GetArrayLength(), data.PlotPanes.Count);
        Assert.Equal(3, data.PlotPaneColumns);

        for (var field = 0; field < data.PlotPanes.Count; field++)
        {
            var pane = data.PlotPanes[field];
            Assert.Equal(
                $"{optic.Fields[field].Label} (Y={optic.Fields[field].Y:0.###} \u00B0)",
                pane.Title);
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

            Assert.True(pane.PlotOptions.XMinimum <= expected.GetProperty("panes")[field].GetProperty("x_lim")[0].GetDouble());
            Assert.True(pane.PlotOptions.XMaximum >= expected.GetProperty("panes")[field].GetProperty("x_lim")[1].GetDouble());
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
            numPoints: 33,
            optilandCompatibility: true).GenerateData();
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
    public void RmsVsFieldUsesDefinedFields(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("rms_vs_field");
        var optic = createOptic();
        var data = new RmsVsFieldAnalysis(optic, numFields: 9, numRings: 3).GenerateData();
        Assert.Equal(expected.GetProperty("wavelengths").GetArrayLength(), data.PlotSeries.Count);

        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            Assert.Equal(optic.Fields.Count, data.PlotSeries[wavelength].Points.Count);
            Assert.Equal(
                optic.Fields.Select(FieldCoordinate),
                data.PlotSeries[wavelength].Points.Select(point => point.X));
            Assert.Equal(
                optic.Fields.Select(field => field.Label),
                data.PlotSeries[wavelength].Points.Select(point => point.Label));
            Assert.All(data.PlotSeries[wavelength].Points, point => Assert.True(double.IsFinite(point.Y)));
        }

        Assert.Equal("Field Angle (deg)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("RMS Spot Radius (mm)", data.PlotSeries[0].YAxisLabel);
        Assert.Equal(0, data.PlotOptions?.XMinimum);
        Assert.Equal(optic.Fields.Max(FieldCoordinate), data.PlotOptions?.XMaximum);
        Assert.Equal(0, data.PlotOptions?.YMinimum);
        Assert.True(data.PlotOptions?.ShowLegend);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void RmsWavefrontVsFieldUsesDefinedFields(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("rms_wavefront_vs_field");
        var optic = createOptic();
        var data = new RmsWavefrontVsFieldAnalysis(optic, numFields: 9, numRings: 5).GenerateData();
        Assert.Equal(expected.GetProperty("wavelengths").GetArrayLength(), data.PlotSeries.Count);
        for (var wavelength = 0; wavelength < data.PlotSeries.Count; wavelength++)
        {
            Assert.Equal(optic.Fields.Count, data.PlotSeries[wavelength].Points.Count);
            Assert.Equal(
                optic.Fields.Select(FieldCoordinate),
                data.PlotSeries[wavelength].Points.Select(point => point.X));
            Assert.Equal(
                optic.Fields.Select(field => field.Label),
                data.PlotSeries[wavelength].Points.Select(point => point.Label));
            Assert.All(data.PlotSeries[wavelength].Points, point => Assert.True(double.IsFinite(point.Y)));
        }

        Assert.Equal("Field Angle (deg)", data.PlotSeries[0].XAxisLabel);
        Assert.Equal("RMS Wavefront Error (waves)", data.PlotSeries[0].YAxisLabel);
        Assert.Equal(0, data.PlotOptions?.XMinimum);
        Assert.Equal(optic.Fields.Max(FieldCoordinate), data.PlotOptions?.XMaximum);
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
            var optic = createOptic();
            var data = new IncidentAngleVsHeightAnalysis(optic, item.Mode, numPoints: 17).GenerateData();
            var series = Assert.Single(data.PlotSeries);
            Assert.Equal(AnalysisSeriesKind.ColoredLine, series.Kind);
            Assert.Equal(item.Fixed, expected.GetProperty("fixed_coordinates").GetString());
            if (item.Mode == AngleScanMode.ThroughPupil)
            {
                Assert.Equal(expected.GetProperty("height").GetArrayLength(), series.Points.Count);
                for (var index = 0; index < series.Points.Count; index++)
                {
                    AssertClose(expected.GetProperty("height")[index].GetDouble(), series.Points[index].X);
                    AssertClose(
                        expected.GetProperty("angle_radians")[index].GetDouble(),
                        series.Points[index].Y * Math.PI / 180);
                    AssertClose(expected.GetProperty("scan_range")[index].GetDouble(), series.Points[index].Value!.Value);
                }
            }
            else
            {
                Assert.Equal(optic.Fields.Count, series.Points.Count);
                Assert.Equal(
                    optic.Fields.Select(FieldCoordinate),
                    series.Points.Select(point => point.Value!.Value));
                Assert.Equal(
                    optic.Fields.Select(field => field.Label),
                    series.Points.Select(point => point.Label));
                Assert.All(series.Points, point =>
                {
                    Assert.True(double.IsFinite(point.X));
                    Assert.True(double.IsFinite(point.Y));
                });
            }

            Assert.Equal("Image Height in Millimeters", series.XAxisLabel);
            Assert.Equal("Incident Angle in Degrees", series.YAxisLabel);
            if (item.Mode == AngleScanMode.ThroughPupil)
            {
                Assert.Contains("Normalized", series.ValueLabel);
            }
            else
            {
                Assert.Equal("Field Angle (deg)", series.ValueLabel);
            }
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
    public void BestFitSphereMatchesPythonOptilandPointForPoint(string sampleName, Func<Optic> createOptic)
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

    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void SampledMtfUsesZemaxAutocorrelationContract(string sampleName, Func<Optic> createOptic)
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
                Assert.InRange(tangential.Points[index].Y, 0, 1);
                Assert.InRange(sagittal.Points[index].Y, 0, 1);
                if (index == 0)
                {
                    Assert.Equal(1, tangential.Points[index].Y, precision: 12);
                    Assert.Equal(1, sagittal.Points[index].Y, precision: 12);
                }
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
        var optic = createOptic();
        var data = new ThroughFocusAnalysis(
            optic,
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
                Assert.Contains(optic.Fields[field].Label, pane.Title);
                Assert.DoesNotContain("Hx", pane.Title);
                Assert.DoesNotContain("Hy", pane.Title);
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
    public void ThroughFocusMtfUsesSampledZemaxAutocorrelationContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("through_focus_mtf");
        var optic = createOptic();
        var data = new ThroughFocusMtfAnalysis(
            optic,
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
                Assert.InRange(tangential[field][step], 0, 1);
                Assert.InRange(sagittal[field][step], 0, 1);
            }
        }

        for (var series = 0; series < data.PlotSeries.Count; series++)
        {
            var expectedX = expected.GetProperty("series_x")[series];
            var expectedY = expected.GetProperty("series_y")[series];
            Assert.Equal(256, data.PlotSeries[series].Points.Count);
            Assert.Contains(optic.Fields[series / 2].Label, data.PlotSeries[series].Name);
            Assert.DoesNotContain("Hx", data.PlotSeries[series].Name);
            Assert.DoesNotContain("Hy", data.PlotSeries[series].Name);
            for (var index = 0; index < data.PlotSeries[series].Points.Count; index++)
            {
                AssertClose(expectedX[index].GetDouble(), data.PlotSeries[series].Points[index].X);
                Assert.InRange(data.PlotSeries[series].Points[index].Y, 0, 1);
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
        var data = new YYbarAnalysis(createOptic(), zemaxCompatible: false).GenerateData();
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
    public void FringeZernikeLowerTermsRemainCompatibleWithPythonOptiland(string sampleName, Func<Optic> createOptic)
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
    public void FftPsfRetainsPythonReferenceGridWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_psf");
        var optic = createOptic();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var actual = DiffractionEngine.ComputeFftPsf(optic, (0, 1), wavelength, 16, 32);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), actual.WorkingFNumber);
        AssertImageClose(expected.GetProperty("strehl").GetDouble(), actual.StrehlRatio);
        var expectedPsf = expected.GetProperty("psf");
        for (var row = 0; row < actual.GridSize; row++)
        {
            for (var column = 0; column < actual.GridSize; column++)
            {
                AssertDiffractionReferenceClose(expectedPsf[row][column].GetDouble(), actual.Values[row, column]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void FftMtfRetainsPythonReferenceFrequencyGridWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
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
            AssertMtfClose(expected.GetProperty("tangential")[index].GetDouble(), actual.Tangential[index]);
            AssertMtfClose(expected.GetProperty("sagittal")[index].GetDouble(), actual.Sagittal[index]);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MmdftPsfRetainsPythonReferenceGridWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
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
        AssertImageClose(expected.GetProperty("strehl").GetDouble(), actual.PeakStrehlRatio);
        AssertPsfValues(expected.GetProperty("psf"), actual, AssertDiffractionReferenceClose);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensPsfUsesZemaxImageSurfaceTangentPlaneGrid(string sampleName, Func<Optic> createOptic)
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
        Assert.True(double.IsFinite(actual.StrehlRatio));
        Assert.True(actual.StrehlRatio >= 0);
        Assert.All(actual.Values.Cast<double>(), value =>
        {
            Assert.True(double.IsFinite(value));
            Assert.True(value >= 0);
        });
        Assert.True(actual.Values.Cast<double>().Max() > 0);
    }
    [Fact]
    public void HuygensImageGridUsesTiltedImageSurfaceNormalAndExactImageDelta()
    {
        var optic = Optic.CreateCookeTriplet();
        var imageSurface = optic.SurfaceGroup.Items[^1];
        imageSurface.CoordinateSystem = imageSurface.CoordinateSystem with
        {
            RotationYDegrees = 15
        };
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        const double imageDeltaMillimeters = 0.007;
        var grid = CreateHuygensGrid(optic, (0, 0), wavelength, 5, imageDeltaMillimeters);
        var chief = optic.TraceGenericFinalSample(0, 0, 0, 0, wavelength.Micrometers)
            ?? throw new InvalidOperationException("Chief ray did not reach the tilted image surface.");
        var expectedNormal = Unit(imageSurface.CoordinateSystem.ToGlobalDirection(
            imageSurface.Geometry.SurfaceNormal(
                imageSurface.CoordinateSystem.ToLocalPoint(chief.Position))));
        var gridX = grid[2, 3] - grid[2, 2];
        var gridY = grid[3, 2] - grid[2, 2];
        var gridNormal = Unit(Cross(gridX, gridY));

        Assert.Equal(imageDeltaMillimeters, gridX.Length, 12);
        Assert.Equal(imageDeltaMillimeters, gridY.Length, 12);
        AssertVectorClose(chief.Position, grid[2, 2], 1e-12);
        Assert.True(Math.Abs(Dot(expectedNormal, gridNormal)) > 1 - 1e-12);
        Assert.True(Math.Abs(Dot(expectedNormal, Unit(chief.Direction))) < 0.999);
    }

    [Fact]
    public void HuygensImageGridUsesCurvedDetectorNormalAtChiefIntercept()
    {
        var optic = Optic.CreateCookeTriplet();
        var imageSurface = optic.SurfaceGroup.Items[^1];
        imageSurface.Radius = 75;
        imageSurface.Geometry = new StandardGeometry(75);
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var field = (Hx: 0.0, Hy: 0.7);
        var grid = CreateHuygensGrid(optic, field, wavelength, 3, 0.005);
        var chief = optic.TraceGenericFinalSample(field.Hx, field.Hy, 0, 0, wavelength.Micrometers)
            ?? throw new InvalidOperationException("Chief ray did not reach the curved image surface.");
        var expectedNormal = Unit(imageSurface.CoordinateSystem.ToGlobalDirection(
            imageSurface.Geometry.SurfaceNormal(
                imageSurface.CoordinateSystem.ToLocalPoint(chief.Position))));
        var gridNormal = Unit(Cross(grid[1, 2] - grid[1, 1], grid[2, 1] - grid[1, 1]));

        Assert.True(Math.Abs(Dot(expectedNormal, gridNormal)) > 1 - 1e-12);
        Assert.Equal(0.005, (grid[1, 2] - grid[1, 1]).Length, 12);
        Assert.Equal(0.005, (grid[2, 1] - grid[1, 1]).Length, 12);
    }


    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensMtfUsesZemaxImageSurfaceTangentPlaneGrid(string sampleName, Func<Optic> createOptic)
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
            Assert.InRange(actual.Tangential[index], 0, 1);
            Assert.InRange(actual.Sagittal[index], 0, 1);
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
        var data = new ReferenceSphereWavefrontAnalysis(
            createOptic(),
            strategy,
            numRings: 5,
            mapSize: 33,
            fieldNumber: 0).GenerateData();
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
    public void ZernikeAnalysisPreservesPythonHeatmapShapeContract(string sampleName, Func<Optic> createOptic)
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
    public void PsfAnalysisUsesFftHeatmapContractWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("fft_psf");
        var data = new PsfAnalysis(createOptic(), numRays: 16, gridSize: 32).GenerateData();
        Assert.Equal(AnalysisSeriesKind.Heatmap, data.PlotSeries[0].Kind);
        Assert.Equal("Relative Intensity", data.PlotSeries[0].ValueLabel);
        Assert.Equal("FFT PSF", data.PlotOptions?.Title);
        AssertDiffractionReferenceClose(expected.GetProperty("strehl").GetDouble(), Convert.ToDouble(data.Values["StrehlRatio"]));
        Assert.Equal("FFT", data.Values["Method"]);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MtfAnalysisUsesFftSeriesContractWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
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
            AssertMtfClose(expected.GetProperty("tangential")[index].GetDouble(), tangential.Points[index].Y);
            AssertMtfClose(expected.GetProperty("sagittal")[index].GetDouble(), sagittal.Points[index].Y);
        }

        AssertClose(expected.GetProperty("cutoff").GetDouble(), data.PlotOptions!.XMaximum!.Value);
        Assert.Equal(0, data.PlotOptions.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void MmdftPsfAnalysisUsesHeatmapContractWithPowerAmplitude(string sampleName, Func<Optic> createOptic)
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
        AssertDiffractionReferenceClose(expected.GetProperty("strehl").GetDouble(), Convert.ToDouble(data.Values["StrehlRatio"]));
        AssertPsfSeriesValues(expected.GetProperty("psf"), data.PlotSeries[0], AssertDiffractionReferenceClose);
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensPsfAnalysisUsesZemaxHeatmapContract(string sampleName, Func<Optic> createOptic)
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
        Assert.Equal("Relative Intensity", data.PlotSeries[0].ValueLabel);
        Assert.Equal("惠更斯PSF", data.PlotOptions?.Title);
        Assert.Equal("Huygens-Fresnel", data.Values["Method"]);
        AssertClose(expected.GetProperty("pixel_pitch").GetDouble(), Convert.ToDouble(data.Values["PixelPitchMicrometers"]) / 1000.0);
        AssertClose(expected.GetProperty("working_fno").GetDouble(), Convert.ToDouble(data.Values["WorkingFNumber"]));
        Assert.Equal("Chief ray tangent plane", data.Values["ImagePlane"]);
        Assert.True(Convert.ToDouble(data.Values["StrehlRatio"]) >= 0);
        Assert.All(data.PlotSeries[0].Points, point =>
        {
            Assert.True(point.Value.HasValue);
            Assert.True(double.IsFinite(point.Value.Value));
            Assert.True(point.Value.Value >= 0);
        });
    }

    [Theory]
    [MemberData(nameof(OfficialSamples))]
    public void HuygensMtfAnalysisUsesZemaxSeriesContract(string sampleName, Func<Optic> createOptic)
    {
        using var reference = LoadReference();
        var expected = reference.RootElement.GetProperty(sampleName).GetProperty("huygens_mtf");
        var data = new HuygensMtfAnalysis(
            createOptic(),
            numRays: 5,
            imageSize: expected.GetProperty("image_size").GetInt32(),
            pixelPitchMillimeters: expected.GetProperty("pixel_pitch").GetDouble(),
            fields: new[] { (0.0, 1.0) },
            zemaxCompatible: false).GenerateData();
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
            Assert.InRange(tangential.Points[index].Y, 0, 1);
            Assert.InRange(sagittal.Points[index].Y, 0, 1);
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
            Assert.Equal((64 + 4) * (48 + 4), pane.Series.Single().Points.Count);
        });
        Assert.DoesNotContain(data.Values.Keys, key => key.Contains("Proxy", StringComparison.OrdinalIgnoreCase));
        Assert.True(Convert.ToDouble(data.Values["MeanAbsoluteChange"]) > 0);
        Assert.Matches("^(Diffraction|Geometric)( \\+ (Diffraction|Geometric))?$", data.Values["EffectiveAberrationMode"]?.ToString()
            ?? throw new InvalidOperationException("Missing effective image-simulation mode."));
        Assert.Equal(0.0, data.PlotPanes[0].Series.Single().Points[0].Red);
        Assert.Equal(0.0, data.PlotPanes[0].Series.Single().Points[0].Green);
    }

    [Theory]
    [InlineData(ImageSimulationSourcePattern.ColorChart)]
    [InlineData(ImageSimulationSourcePattern.ResolutionTarget)]
    [InlineData(ImageSimulationSourcePattern.DistortionGrid)]
    [InlineData(ImageSimulationSourcePattern.SiemensStar)]
    public void ImageSimulationSourcePatternsProduceValidRgbImages(ImageSimulationSourcePattern pattern)
    {
        var image = ImageSimulationEngine.CreateSourceImage(pattern, 64, 48);

        Assert.Equal(3, image.Channels);
        Assert.Equal(48, image.Height);
        Assert.Equal(64, image.Width);
        Assert.All(image.Values.Cast<double>(), value => Assert.InRange(value, 0, 1));
        Assert.True(image.Values.Cast<double>().Distinct().Count() > 2);
    }

    [Fact]
    public void ImageSimulationPipelineUsesBlackGuardBandAndSelectedMode()
    {
        var config = new ImageSimulationConfig
        {
            AberrationMode = "None",
            UseRelativeIllumination = false,
            Oversampling = 2,
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 8,
            NumRays = 5,
            Components = 2,
            Padding = 2,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 2
        };
        var actual = ImageSimulationEngine.Simulate(
            Optic.CreateCookeTriplet(),
            ImageSimulationEngine.CreateTestChart(16, 16),
            config);

        Assert.Equal(36, actual.Source.Width);
        Assert.Equal(36, actual.Source.Height);
        Assert.Equal(actual.Source.Width, actual.Simulated.Width);
        Assert.Equal(actual.Source.Height, actual.Simulated.Height);
        Assert.Equal("None", actual.EffectiveAberrationMode);
        Assert.Equal(0, actual.GeometricFallbackCount);
        Assert.All(
            Enumerable.Range(0, actual.Source.Channels),
            channel => Assert.Equal(0, actual.Source.Values[channel, 0, 0]));
        Assert.All(actual.Simulated.Values.Cast<double>(), value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void ImageSimulationGuardBandIsBlackInsteadOfReflected()
    {
        var source = ImageSimulationEngine.CreateTestChart(16, 16);
        var padded = ImageSimulationEngine.ZeroPad(source.Values, 2);

        Assert.Equal(20, padded.GetLength(1));
        Assert.Equal(20, padded.GetLength(2));
        Assert.All(padded.Cast<double>().Take(20), value => Assert.Equal(0, value));
        for (var channel = 0; channel < source.Channels; channel++)
        {
            Assert.Equal(source.Values[channel, 0, 0], padded[channel, 2, 2]);
            Assert.Equal(source.Values[channel, 15, 15], padded[channel, 17, 17]);
        }
    }

    [Fact]
    public void ImageSimulationPsfModeIsHonored()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 587.6 };
        var none = ImageSimulationEngine.GenerateBasis(optic, wavelength, new ImageSimulationConfig
        {
            AberrationMode = "None",
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 8,
            NumRays = 5,
            Components = 2
        });
        var geometric = ImageSimulationEngine.GenerateBasis(optic, wavelength, new ImageSimulationConfig
        {
            AberrationMode = "Geometric",
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 8,
            NumRays = 5,
            Components = 2
        });

        Assert.All(none.EffectiveModes!, mode => Assert.Equal("None", mode));
        Assert.All(geometric.EffectiveModes!, mode => Assert.Equal("Geometric", mode));
        Assert.Equal(1, none.MeanPsf.Cast<double>().Sum(), 10);
        Assert.Equal(1, geometric.MeanPsf.Cast<double>().Sum(), 10);
    }

    [Fact]
    public void ImageSimulationDistortionGridKeepsLateralColorAgainstPrimaryReference()
    {
        var optic = Optic.CreateCookeTriplet();
        var red = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 650 };
        var primary = new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 587.6 };
        var primaryGrid = ImageSimulationEngine.GenerateDistortionGrid(
            optic, primary, 20, 20, 7, 3, referenceWavelength: primary);
        var redGrid = ImageSimulationEngine.GenerateDistortionGrid(
            optic, red, 20, 20, 7, 3, referenceWavelength: primary);

        Assert.All(primaryGrid.Cast<(double X, double Y)>(), point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.Contains(
            redGrid.Cast<(double X, double Y)>().Zip(primaryGrid.Cast<(double X, double Y)>()),
            pair => Math.Abs(pair.First.X - pair.Second.X) > 1e-8
                || Math.Abs(pair.First.Y - pair.Second.Y) > 1e-8);
    }

    [Fact]
    public void ImageSimulationDiffractionBasisPreservesUnitEnergyAndReportsFallback()
    {
        var config = new ImageSimulationConfig
        {
            AberrationMode = "Diffraction",
            PsfGridRows = 2,
            PsfGridColumns = 2,
            PsfSize = 8,
            NumRays = 5,
            Components = 3
        };
        var basis = ImageSimulationEngine.GenerateBasis(
            Optic.CreateCookeTriplet(),
            new OptilandWorkbench.Core.Domain.Wavelength { Nanometers = 650 },
            config);

        AssertClose(1, basis.MeanPsf.Cast<double>().Sum());
        Assert.Equal(4, basis.EffectiveModes!.Count);
        Assert.All(basis.EffectiveModes, mode => Assert.Contains(mode, new[] { "Diffraction", "Geometric" }));
        Assert.Equal(basis.EffectiveModes.Count(mode => mode == "Geometric"), basis.GeometricFallbackCount);
        for (var field = 0; field < 4; field++)
        {
            var sum = basis.MeanPsf.Cast<double>().Sum();
            for (var component = 0; component < basis.EigenPsfs.Length; component++)
            {
                sum += basis.CoefficientGrid[component, field / 2, field % 2]
                    * basis.EigenPsfs[component].Cast<double>().Sum();
            }

            AssertClose(1, sum);
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

    private static Vector3D[,] CreateHuygensGrid(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int imageSize,
        double imageDeltaMillimeters)
    {
        var method = typeof(DiffractionEngine).GetMethod(
            "CreateHuygensImageCoordinates",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(DiffractionEngine), "CreateHuygensImageCoordinates");
        return (Vector3D[,])(method.Invoke(
            null,
            new object[] { optic, field, wavelength, imageSize, imageDeltaMillimeters })
            ?? throw new InvalidOperationException("Huygens image-grid construction returned null."));
    }

    private static Vector3D Unit(Vector3D value) => value / value.Length;

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static void AssertVectorClose(Vector3D expected, Vector3D actual, double tolerance)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, tolerance);
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


    private static void AssertPsfValues(
        JsonElement expectedPsf,
        PsfResult actual,
        Action<double, double>? assertClose = null)
    {
        assertClose ??= AssertClose;
        Assert.Equal(expectedPsf.GetArrayLength(), actual.GridSize);
        for (var row = 0; row < actual.GridSize; row++)
        {
            Assert.Equal(expectedPsf[row].GetArrayLength(), actual.GridSize);
            for (var column = 0; column < actual.GridSize; column++)
            {
                assertClose(expectedPsf[row][column].GetDouble(), actual.Values[row, column]);
            }
        }
    }

    private static void AssertPsfSeriesValues(
        JsonElement expectedPsf,
        AnalysisSeries actual,
        Action<double, double>? assertClose = null,
        double scale = 1)
    {
        assertClose ??= AssertClose;
        var size = expectedPsf.GetArrayLength();
        Assert.Equal(size * size, actual.Points.Count);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var value = actual.Points[(row * size) + column].Value;
                Assert.True(value.HasValue);
                assertClose(expectedPsf[row][column].GetDouble() * scale, value.Value);
            }
        }
    }

    private static double FieldCoordinate(FieldPoint field)
    {
        if (Math.Abs(field.X) <= 1e-12)
        {
            return field.Y;
        }

        if (Math.Abs(field.Y) <= 1e-12)
        {
            return field.X;
        }

        return Math.Sqrt((field.X * field.X) + (field.Y * field.Y));
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

    private static void AssertMtfClose(double expected, double actual)
    {
        var tolerance = 1e-3 * Math.Max(1, Math.Abs(expected));
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}. Zemax Fringe term 37 is active.");
    }

    private static void AssertDiffractionReferenceClose(double expected, double actual)
    {
        var tolerance = 2e-4 * Math.Max(1, Math.Abs(expected));
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}. Power is converted to field amplitude with sqrt(Intensity). ");
    }
    private static void AssertImageClose(double expected, double actual)
    {
        const double tolerance = 5e-5;
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
    }
}
