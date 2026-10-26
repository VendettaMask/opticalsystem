using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class ExtendedImageAnalysisTests
{
    [Fact]
    public void CatalogExposesEveryExtendedSceneAnalysis()
    {
        var catalog = Optic.CreateCookeTriplet().Analyses;
        var names = new[]
        {
            "Image Simulation",
            "Geometric Image Analysis",
            "Geometric Bitmap Image Analysis",
            "Light Source Analysis",
            "Partially Coherent Image Analysis",
            "Extended Diffraction Image Analysis",
            "Relative Illumination"
        };

        Assert.All(names, name => Assert.Contains(name, catalog.Names));
        Assert.IsType<GeometricImageAnalysis>(catalog.Create("Geometric Image Analysis"));
        Assert.IsType<GeometricBitmapImageAnalysis>(catalog.Create("Geometric Bitmap Image Analysis"));
        Assert.IsType<LightSourceAnalysis>(catalog.Create("Light Source Analysis"));
        Assert.IsType<PartiallyCoherentImageAnalysis>(catalog.Create("Partially Coherent Image Analysis"));
        Assert.IsType<ExtendedDiffractionImageAnalysis>(catalog.Create("Extended Diffraction Image Analysis"));
    }

    [Fact]
    public void ExtendedSceneAnalysesProduceRenderableResults()
    {
        var optic = Optic.CreateCookeTriplet();
        var imageAnalyses = new BaseAnalysis[]
        {
            new GeometricImageAnalysis(optic, imageSize: 16, numRays: 4, relativeIllumination: false),
            new GeometricBitmapImageAnalysis(optic, imageSize: 16, raysPerPixel: 4, relativeIllumination: false),
            new PartiallyCoherentImageAnalysis(optic, imageSize: 16, pupilSampling: 4, coherence: 0, relativeIllumination: false),
            new ExtendedDiffractionImageAnalysis(optic, imageSize: 16, pupilSampling: 4, fieldGrid: 2, relativeIllumination: false)
        };

        Assert.All(imageAnalyses, analysis =>
        {
            var data = analysis.GenerateData();
            Assert.Equal(2, data.PlotPanes?.Count);
            Assert.All(data.PlotPanes!, pane =>
                Assert.Contains(pane.Series, series => series.Kind == AnalysisSeriesKind.Raster));
        });

        var source = new LightSourceAnalysis(optic, resolution: 9, numRays: 32).GenerateData();
        Assert.Contains(source.PlotSeries, series => series.Kind == AnalysisSeriesKind.Heatmap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalCoarseGeometricImageInputsRemainExplicitlyUnavailable(bool bitmap)
    {
        var optic = Optic.CreateCookeTriplet();
        BaseAnalysis analysis = bitmap
            ? new GeometricBitmapImageAnalysis(optic, imageSize: 16, raysPerPixel: 2)
            : new GeometricImageAnalysis(optic, imageSize: 16, numRays: 2);
        var data = analysis.GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
        Assert.Contains("no positive PSF power was sampled", data.OutcomeReason);
        Assert.Null(data.PlotPanes);
    }

    [Fact]
    public void ConnectorProvidesSettingsForWiredExtendedSceneAnalyses()
    {
        var connector = new WorkbenchRuntime(Optic.CreateCookeTriplet());

        Assert.Contains(
            connector.GetAnalysisParameters("Geometric Image Analysis"),
            parameter => parameter.Key == "SourceImage");
        Assert.Contains(
            connector.GetAnalysisParameters("Geometric Bitmap Image Analysis"),
            parameter => parameter.Key == "RaysPerPixel");
        Assert.Contains(
            connector.GetAnalysisParameters("Light Source Analysis"),
            parameter => parameter.Key == "Resolution");
        Assert.Contains(
            connector.GetAnalysisParameters("Partially Coherent Image Analysis"),
            parameter => parameter.Key == "Coherence");
        Assert.Contains(
            connector.GetAnalysisParameters("Extended Diffraction Image Analysis"),
            parameter => parameter.Key == "FieldGrid");
    }

    [Fact]
    public void ZemaxImageReaderSupportsTextAndBinaryImaAndBim()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opticalsystemdesign-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var textIma = Path.Combine(directory, "letter.ima");
            File.WriteAllText(textIma, "3\r\n090\r\n999\r\n900\r\n");
            var text = ZemaxImageFile.Read(textIma);
            Assert.Equal((3, 3, 1), (text.Width, text.Height, text.Channels));
            Assert.Equal(9, text.Value(0, 0, 1));

            var binaryIma = Path.Combine(directory, "color.ima");
            using (var writer = new BinaryWriter(File.Create(binaryIma)))
            {
                writer.Write((short)0);
                writer.Write((short)2);
                writer.Write((short)3);
                writer.Write(Enumerable.Range(0, 12).Select(index => (byte)index).ToArray());
            }

            var binary = ZemaxImageFile.Read(binaryIma);
            Assert.Equal((2, 2, 3), (binary.Width, binary.Height, binary.Channels));
            Assert.Equal(11, binary.Value(2, 1, 1));

            var bimPath = Path.Combine(directory, "energy.bim");
            using (var writer = new BinaryWriter(File.Create(bimPath)))
            {
                writer.Write(2);
                writer.Write(2);
                writer.Write(new[] { 0.0, 0.25, 0.5, 1.0 }.SelectMany(BitConverter.GetBytes).ToArray());
            }

            var bim = ZemaxImageFile.Read(bimPath);
            Assert.Equal((2, 2, 1), (bim.Width, bim.Height, bim.Channels));
            Assert.True(bim.BottomUp);
            Assert.Equal(1, bim.Value(0, 1, 1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
