using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class MtfMaximumFrequencyTests
{
    [Fact]
    public void MtfCurvesUseDeclaredFieldNamesAndCoordinates()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new MtfAnalysis(
            optic,
            numRays: 16,
            gridSize: 32,
            maximumFrequency: 20).GenerateData();

        Assert.Equal(new[]
        {
            "On axis (Y=0 °), Tangential",
            "On axis (Y=0 °), Sagittal",
            "14 deg (Y=14 °), Tangential",
            "14 deg (Y=14 °), Sagittal",
            "20 deg (Y=20 °), Tangential",
            "20 deg (Y=20 °), Sagittal"
        }, data.PlotSeries.Select(series => series.Name));
        for (var fieldIndex = 0; fieldIndex < optic.Fields.Count; fieldIndex++)
        {
            var tangential = data.PlotSeries[fieldIndex * 2];
            var sagittal = data.PlotSeries[(fieldIndex * 2) + 1];
            Assert.Equal(fieldIndex, tangential.ColorIndex);
            Assert.Equal(fieldIndex, sagittal.ColorIndex);
            Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
            Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
        }
    }

    [Fact]
    public void RealImageHeightMtfLegendUsesMillimetersInsteadOfNormalizedFields()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "轴上视场", Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "视场 2", Y = 1.125 });
        optic.Fields.Add(new FieldPoint { Label = "视场 3", Y = 2.25 });
        optic.Fields.Add(new FieldPoint { Label = "视场 4", Y = 3.375 });
        optic.Fields.Add(new FieldPoint { Label = "最大Y视场", Y = 4.5 });

        var data = new MtfAnalysis(
            optic,
            numRays: 16,
            gridSize: 32,
            maximumFrequency: 20).GenerateData();

        Assert.Equal(10, data.PlotSeries.Count);
        Assert.Equal("轴上视场 (Y=0 mm), Tangential", data.PlotSeries[0].Name);
        Assert.Equal("视场 2 (Y=1.125 mm), Tangential", data.PlotSeries[2].Name);
        Assert.Equal("视场 3 (Y=2.25 mm), Tangential", data.PlotSeries[4].Name);
        Assert.Equal("视场 4 (Y=3.375 mm), Tangential", data.PlotSeries[6].Name);
        Assert.Equal("最大Y视场 (Y=4.5 mm), Tangential", data.PlotSeries[8].Name);
        Assert.DoesNotContain(data.PlotSeries, series =>
            series.Name?.Contains("Hx:", StringComparison.Ordinal) == true
            || series.Name?.Contains("Hy:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ThroughFocusMtfLegendsUseDeclaredRealImageHeightFields()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Field 1", Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "Field 2", Y = 4.5 });
        optic.Fields.Add(new FieldPoint { Label = "Field 3", Y = 3.375 });
        optic.Fields.Add(new FieldPoint { Label = "Field 4", Y = 2.25 });
        optic.Fields.Add(new FieldPoint { Label = "Field 5", Y = 1.125 });

        var settings = new MtfComputationSettings(
            PupilSampling: 8,
            ImageSize: 16,
            GeometricRayCount: 8);
        var methodSpecific = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Fourier,
            spatialFrequency: 20,
            focusPlaneCount: 1,
            settings: settings).GenerateData();
        var sampled = new ThroughFocusMtfAnalysis(
            optic,
            spatialFrequency: 20,
            numSteps: 1,
            pupilSampling: 8).GenerateData();

        foreach (var data in new[] { methodSpecific, sampled })
        {
            Assert.Equal(10, data.PlotSeries.Count);
            Assert.Equal("Field 1 (Y=0 mm), Tangential", data.PlotSeries[0].Name);
            Assert.Equal("Field 2 (Y=4.5 mm), Tangential", data.PlotSeries[2].Name);
            Assert.Equal("Field 3 (Y=3.375 mm), Tangential", data.PlotSeries[4].Name);
            Assert.Equal("Field 4 (Y=2.25 mm), Tangential", data.PlotSeries[6].Name);
            Assert.Equal("Field 5 (Y=1.125 mm), Tangential", data.PlotSeries[8].Name);
            Assert.DoesNotContain(data.PlotSeries, series =>
                series.Name?.Contains("Hx", StringComparison.Ordinal) == true
                || series.Name?.Contains("Hy", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void FourierThroughFocusMtfUsesCapturedZemaxUiFallbackAndSmoothPlotSampling()
    {
        var optic = Optic.CreateCookeTriplet();
        var settings = new MtfComputationSettings(
            PupilSampling: 8,
            ImageSize: 16,
            ZemaxCompatible: true);

        var data = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Fourier,
            spatialFrequency: 0,
            deltaFocus: 0.1,
            focusPlaneCount: 5,
            settings: settings,
            type: "调制").GenerateData();

        Assert.Equal(0.0, data.Values["FrequencyInput"]);
        Assert.Equal(50.0, data.Values["SpatialFrequency"]);
        Assert.Equal(5, data.Values["NumberOfSteps"]);
        Assert.Equal("Modulation", data.Values["Type"]);
        Assert.Equal(true, data.Values["ZemaxCompatible"]);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(300, series.Points.Count);
            Assert.Equal(-0.1, series.Points[0].X, 12);
            Assert.Equal(0.1, series.Points[^1].X, 12);
        });
    }

    [Fact]
    public void ThroughFocusMtfUsesConfiguredZemaxDeltaFocusStepsAndPolychromaticSelection()
    {
        var optic = Optic.CreateCookeTriplet();
        var settings = new MtfComputationSettings(
            PupilSampling: 8,
            ImageSize: 16,
            GeometricRayCount: 8);

        var data = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Fourier,
            spatialFrequency: 50,
            deltaFocus: 0.1,
            focusPlaneCount: 5,
            settings: settings,
            wavelengthNumber: 0,
            fieldNumber: 0).GenerateData();

        Assert.Equal(0.1, data.Values["DeltaFocus"]);
        Assert.Equal(5, data.Values["Steps"]);
        Assert.Equal(0, data.Values["WavelengthNumber"]);
        Assert.Equal(optic.Wavelengths.Count, Assert.IsType<double[]>(data.Values["WavelengthsMicrometers"]).Length);
        var rawTangential = Assert.IsType<double[][]>(data.Values["RawTangential"]);
        Assert.Equal(optic.Fields.Count, rawTangential.Length);
        Assert.All(rawTangential, field => Assert.Equal(5, field.Length));
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(101, series.Points.Count);
            Assert.Equal(-0.1, series.Points[0].X, 12);
            Assert.Equal(0.1, series.Points[^1].X, 12);
        });
    }

    [Fact]
    public void HuygensThroughFocusUsesZemaxImageDeltaFormulaAndOutputSampling()
    {
        var optic = Optic.CreateCookeTriplet();
        var settings = new MtfComputationSettings(
            PupilSampling: 8,
            ImageSize: 8,
            PixelPitchMillimeters: 0,
            ZemaxCompatible: true,
            UseZemaxHuygensSemantics: true);
        var wavelengths = optic.Wavelengths.ToArray();
        var longest = wavelengths.MaxBy(item => item.Micrometers)!;
        var field = SpotAnalysisEngine.DefinedFields(optic)[0];
        var expectedDeltaMicrometers = longest.Micrometers
            * DiffractionEngine.WorkingFNumber(optic, field, longest)
            / Math.Sqrt(settings.PupilSampling);

        var data = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Huygens,
            spatialFrequency: 20,
            deltaFocus: 0.1,
            focusPlaneCount: 2,
            settings: settings,
            fieldNumber: 1).GenerateData();

        Assert.Equal(8, data.Values["PupilSampling"]);
        Assert.Equal(8, data.Values["ImageSampling"]);
        Assert.Equal(0.0, data.Values["ImageDeltaMicrometers"]);
        var resolved = Assert.IsType<double[]>(data.Values["ResolvedImageDeltaMicrometers"]);
        Assert.Equal(expectedDeltaMicrometers, Assert.Single(resolved), 12);
        Assert.All(data.PlotSeries, series => Assert.Equal(101, series.Points.Count));
    }

    [Fact]
    public void ThroughFocusMtfCanSelectOneDeclaredFieldAndWavelengthByZemaxNumber()
    {
        var optic = Optic.CreateCookeTriplet();
        var settings = new MtfComputationSettings(PupilSampling: 8, ImageSize: 16);

        var data = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Fourier,
            spatialFrequency: 20,
            focusPlaneCount: 1,
            settings: settings,
            wavelengthNumber: 2,
            fieldNumber: 2).GenerateData();

        Assert.Equal(2, data.PlotSeries.Count);
        Assert.StartsWith("14 deg (Y=14 °)", data.PlotSeries[0].Name);
        Assert.Equal(
            new[] { optic.Wavelengths[1].Micrometers },
            Assert.IsType<double[]>(data.Values["WavelengthsMicrometers"]));
    }

    [Fact]
    public void FourierMtfHonorsRequestedMaximumFrequency()
    {
        const double maximumFrequency = 20;
        var data = new MtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 16,
            gridSize: 32,
            maximumFrequency: maximumFrequency,
            zemaxCompatible: true).GenerateData();

        Assert.Equal(maximumFrequency, data.PlotOptions!.XMaximum);
        Assert.Equal(maximumFrequency, Convert.ToDouble(data.Values["MaximumFrequency"]));
        Assert.All(data.PlotSeries.SelectMany(series => series.Points), point =>
            Assert.InRange(point.X, 0, maximumFrequency));
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(300, series.Points.Count);
            Assert.Equal(maximumFrequency, series.Points[^1].X);
        });
    }

    [Fact]
    public void FourierMtfAcceptsTheSameSettingsAsZemax()
    {
        const double maximumFrequency = 20;
        var data = new MtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 8,
            gridSize: 16,
            maximumFrequency: maximumFrequency,
            wavelengthNumber: 1,
            fieldNumber: 1,
            surfaceNumber: 0,
            type: "实部",
            showDiffractionLimit: true,
            usePolarization: false,
            useDashes: false,
            zemaxCompatible: true).GenerateData();

        Assert.Equal(3, data.PlotSeries.Count);
        Assert.Equal("Real", data.Values["Type"]);
        Assert.Equal(0, data.Values["SurfaceNumber"]);
        Assert.Equal(true, data.Values["ShowDiffractionLimit"]);
        Assert.Equal(false, data.Values["UsePolarization"]);
        Assert.Equal(false, data.Values["UseDashes"]);
        Assert.Equal(300, data.Values["PlotPointCount"]);
        Assert.Equal(AnalysisLineStyle.Solid, data.PlotSeries[0].LineStyle);
        Assert.Equal(AnalysisLineStyle.Dashed, data.PlotSeries[1].LineStyle);
        Assert.Equal("Diffraction Limit", data.PlotSeries[2].Name);
        Assert.All(data.PlotSeries, series => Assert.Equal(300, series.Points.Count));
        Assert.Equal(-1, data.PlotOptions!.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
    }

    [Theory]
    [InlineData("调制", "Modulation", 0.0, 1.05)]
    [InlineData("实部", "Real", -1.0, 1.0)]
    [InlineData("虚部", "Imaginary", -1.0, 1.0)]
    [InlineData("相位", "Phase", -3.141592653589793, 3.141592653589793)]
    [InlineData("方波", "SquareWave", 0.0, 1.05)]
    public void FourierMtfTypeControlsTheReturnedData(
        string type,
        string expectedType,
        double expectedMinimum,
        double expectedMaximum)
    {
        var data = new MtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 8,
            gridSize: 16,
            maximumFrequency: 20,
            wavelengthNumber: 1,
            fieldNumber: 1,
            type: type,
            zemaxCompatible: true).GenerateData();

        Assert.Equal(expectedType, data.Values["Type"]);
        Assert.Equal(expectedMinimum, data.PlotOptions!.YMinimum);
        Assert.Equal(expectedMaximum, data.PlotOptions.YMaximum);
        Assert.All(data.PlotSeries, series => Assert.Equal(300, series.Points.Count));
    }

    [Fact]
    public void HuygensMtfHonorsRequestedMaximumFrequency()
    {
        const double maximumFrequency = 20;
        var data = new HuygensMtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 5,
            imageSize: 32,
            pixelPitchMillimeters: 0.005,
            fields: new[] { (0.0, 0.0) },
            maximumFrequency: maximumFrequency).GenerateData();

        Assert.Equal(maximumFrequency, data.PlotOptions!.XMaximum);
        Assert.Equal(maximumFrequency, Convert.ToDouble(data.Values["MaximumFrequency"]));
        Assert.All(data.PlotSeries.SelectMany(series => series.Points), point =>
            Assert.InRange(point.X, 0, maximumFrequency));
        Assert.All(data.PlotSeries, series => Assert.Equal(maximumFrequency, series.Points[^1].X));
    }
}
