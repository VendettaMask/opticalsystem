using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class GeometricIlluminationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IlluminationSamplingKind.AdaptivePupil, true, false)]
    [InlineData(IlluminationSamplingKind.AdaptivePupil, false, true)]
    [InlineData(IlluminationSamplingKind.AdaptivePupil, true, true)]
    [InlineData(IlluminationSamplingKind.UniformImageCosine, true, false)]
    [InlineData(IlluminationSamplingKind.UniformImageCosine, false, true)]
    [InlineData(IlluminationSamplingKind.UniformImageCosine, true, true)]
    [InlineData(IlluminationSamplingKind.FiniteImageCosine, true, false)]
    [InlineData(IlluminationSamplingKind.FiniteImageCosine, false, true)]
    [InlineData(IlluminationSamplingKind.FiniteImageCosine, true, true)]
    public void GeometricModeRetainsRefractionApodizationAndAperturesButNotTransmission(
        IlluminationSamplingKind sampling, bool absorbing, bool coated)
    {
        var optic = Plate();
        optic.Apodization = new GaussianApodization(0.6);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(0.9, 0.2);
        var reference = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, sampling: sampling);
        SetLoss(optic, absorbing ? 1e-6 : 0, coated ? 0.25 : 1);
        var geometric = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, sampling: sampling,
            includeScalarTransmission: false);
        var physical = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, sampling: sampling);
        Assert.Equal(reference.ProjectedCosineArea, geometric.ProjectedCosineArea, 12);
        Assert.Equal(reference.ValidRays, geometric.ValidRays);
        Assert.False(geometric.IncludesScalarTransmission);
        Assert.True(physical.IncludesScalarTransmission);
        Assert.True(physical.ProjectedCosineArea < reference.ProjectedCosineArea * 0.99);
        Assert.Equal(absorbing ? 1e-6 : 0, optic.SurfaceGroup.Items[1].MaterialAfter.ExtinctionCoefficient(550));
        Assert.IsType<AnnularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.IsType<GaussianApodization>(optic.Apodization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    public void PolZeroOperandIgnoresCoatingWhilePolOneRetainsItsPower(double transmission)
    {
        var optic = Plate();
        var geometric = Operand(optic, 0);
        var polarized = Operand(optic, 1);
        SetLoss(optic, 0, transmission);
        Assert.Equal(geometric.Value, Operand(optic, 0).Value, 12);
        var power = Operand(optic, 1);
        if (transmission == 0)
        {
            Assert.NotEmpty(power.Error);
            Assert.True(double.IsNaN(power.Value));
        }
        else
        {
            Assert.Empty(power.Error);
            Assert.Equal(polarized.Value / Math.Sqrt(transmission), power.Value, 10);
        }
    }

    [Fact]
    public void GeometricPolicyReachesParallelAndAxisReferenceCalls()
    {
        var optic = Plate();
        optic.Fields[0].Y = 8;
        var expected = IlluminationMetrics.RelativeToAxis(optic, (0, 1), 0.55,
            sampling: IlluminationSamplingKind.UniformImageCosine, includeScalarTransmission: false);
        SetLoss(optic, 1e-5, 0);
        var fields = IlluminationMetrics.EvaluateFields(optic, [(0, 0), (0, 1)], 0.55,
            sampling: IlluminationSamplingKind.UniformImageCosine, includeScalarTransmission: false);
        Assert.All(fields, item => Assert.False(item.IncludesScalarTransmission));
        Assert.Equal(expected, fields[1].ProjectedCosineArea / fields[0].ProjectedCosineArea, 12);
        Assert.Equal(expected, IlluminationMetrics.RelativeToAxis(optic, (0, 1), 0.55,
            sampling: IlluminationSamplingKind.UniformImageCosine, includeScalarTransmission: false), 12);
    }

    [Fact]
    public void ApplicationPolZeroSelectsGeometricWhileCorePowerDefaultIsPreserved()
    {
        var optic = Plate();
        SetLoss(optic, 1e-6, 0.25);
        var physical = new RelativeIlluminationAnalysis(optic, fieldDensity: 2).GenerateData();
        var geometric = new RelativeIlluminationAnalysis(optic, fieldDensity: 2,
            includeScalarTransmission: false, sampling: IlluminationSamplingKind.FiniteImageCosine).GenerateData();
        var product = new WorkbenchRuntime(optic).BuildAnalysisData("Relative Illumination",
            new Dictionary<string, string> { ["FieldDensity"] = "2" });
        Assert.Equal(true, physical.Values["IncludesScalarTransmission"]);
        Assert.Equal(false, product.Values["IncludesScalarTransmission"]);
        Assert.Equal("AdaptivePupil", physical.Values["IlluminationSampling"]);
        Assert.Equal("FiniteImageCosine", product.Values["IlluminationSampling"]);
        Assert.Equal(geometric.Values["EffectiveFNumbers"], product.Values["EffectiveFNumbers"]);
        Assert.True(Assert.IsType<double[]>(physical.Values["EffectiveFNumbers"])[0]
            > Assert.IsType<double[]>(product.Values["EffectiveFNumbers"])[0]);
    }

    [Fact]
    public void GeometricPolicyCannotSilentlyDisablePolarizedTransmission()
    {
        var optic = Plate();
        Assert.Throws<ArgumentException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55,
            usePolarization: true, includeScalarTransmission: false));
        Assert.Throws<ArgumentException>(() => new RelativeIlluminationAnalysis(optic,
            usePolarization: true, includeScalarTransmission: false));
        var source = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(0, 0, 0.55, aimAtStop: true)(0, 0);
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.DiagnoseApertures(source,
            usePolarization: true, ignoreScalarTransmission: true));
    }

    [Fact]
    public void ProductPolZeroCurveRetainsOriginalCapturedRelativeIlluminationGate()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixtures, "zemax-123456.ZMX")), ".zmx");
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "zemax-123456-relative-illumination.json")));
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Relative Illumination", new Dictionary<string, string>
        {
            ["RayDensity"] = "10",
            ["FieldDensity"] = "21",
            ["WavelengthNumber"] = "0",
            ["ScanDirection"] = "+y",
            ["RemoveVignettingFactors"] = "true",
            ["UsePolarization"] = "false"
        });
        var points = Assert.Single(data.PlotSeries).Points;
        var reference = native.RootElement.GetProperty("dataSeries")[0];
        var errors = points.Select((point, index) =>
        {
            Assert.Equal(reference.GetProperty("x")[index].GetDouble(), point.X, 12);
            return point.Y - reference.GetProperty("y")[index][0].GetDouble();
        }).ToArray();
        var rmse = Math.Sqrt(errors.Average(error => error * error));
        var maximum = errors.Max(Math.Abs);
        // Existing RI gate only: EFNO has no established native acceptance target.
        Assert.True(rmse <= 0.002 && maximum <= 0.004, $"RMSE={rmse:R}, maximum={maximum:R}");
        Assert.Equal(0.44, Assert.IsType<double>(data.Values["WavelengthMicrometers"]), 12);
        Assert.Equal(false, data.Values["IncludesScalarTransmission"]);
        output.WriteLine("ProductCapturedPolZero=" + JsonSerializer.Serialize(new
        {
            Rmse = rmse,
            MaximumAbsoluteError = maximum,
            EffectiveFNumbers = data.Values["EffectiveFNumbers"],
            NativeEffectiveFNumbers = reference.GetProperty("y").EnumerateArray().Select(row => row[1].GetDouble())
        }));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("double-gauss-28-degree-field")]
    [InlineData("relay-lens")]
    [InlineData("even-asphere")]
    public void GeometricRayPowerMatchesExistingNativeUnpolarizedRayControls(string name)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "validation/zemax/2026-r1/fcgt-efno-2026-10-14/manifest.json")));
        var item = manifest.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == name);
        var sourcePath = Path.Combine(root, item.GetProperty("Source").GetString()!);
        Assert.Equal(item.GetProperty("SourceSha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(sourcePath), ".zmx");
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            item.GetProperty("Capture").GetString()!, "rays.json")));
        var wave = optic.Wavelengths[native.RootElement.GetProperty("wave").GetInt32() - 1].Micrometers;
        var inputs = native.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
        foreach (var row in native.RootElement.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal(0, row.GetProperty("error").GetInt32());
            var input = inputs[row.GetProperty("number").GetInt32() - 1];
            var ray = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(0,
                input.GetProperty("hy").GetDouble(), wave, aimAtStop: true)(0, input.GetProperty("py").GetDouble());
            var physical = optic.SequentialRayTracer.DiagnoseApertures(ray, stopAtIncidentImage: true);
            var geometric = optic.SequentialRayTracer.DiagnoseApertures(ray, stopAtIncidentImage: true,
                ignoreScalarTransmission: true);
            Assert.NotNull(geometric.UnclippedImage);
            Assert.Equal(row.GetProperty("intensity").GetDouble(), geometric.UnclippedImage.Intensity, 12);
            Assert.Equal(physical.UnclippedImage!.Position, geometric.UnclippedImage.Position);
            Assert.Equal(physical.UnclippedImage.Direction, geometric.UnclippedImage.Direction);
            Assert.Equal(physical.FirstBlockedSurfaceIndex, geometric.FirstBlockedSurfaceIndex);
        }
    }

    private static MeritOperandEvaluation Operand(Optic optic, int pol) => MeritFunctionCatalog.Evaluate(optic, new()
    {
        Type = "EFNO",
        ZemaxIntegerParameters = [10, 1],
        ZemaxDataParameters = [1, pol, 0, 0]
    });

    private static Optic Plate()
    {
        var optic = new Optic();
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, Thickness = 2, SemiDiameter = 1 },
            new OpticalSurface { Thickness = 3 },
            new OpticalSurface()
        ]);
        SetLoss(optic, 0, 1);
        return optic;
    }

    private static void SetLoss(Optic optic, double extinction, double transmission)
    {
        var material = new ConstantIndexMaterial("illumination-test-glass", 1.5, extinction);
        optic.SurfaceGroup.Items[1].MaterialAfter = material;
        optic.SurfaceGroup.Items[2].MaterialBefore = material;
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
    }
}
