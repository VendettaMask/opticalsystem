using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisGpuTests(ITestOutputHelper log)
{
    [Fact]
    public void ExistingSettingsEnableAutomaticHardwareAndCpuChoiceSurvivesReload()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(settings.UseGpuAcceleration);
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        settings.UseGpuAcceleration = false;
        settings.Save(path);
        Assert.False(AppSettings.Load(path).UseGpuAcceleration);
        settings.ResetDisplaySettings();
        Assert.False(settings.UseGpuAcceleration);
    }

    [CudaFact]
    public void DenseFourierMatchesCpuAndInvertsOnHardware()
    {
        var image = new Complex[512, 512];
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
            image[y, x] = new(Math.Sin(x * .73 + y * .11), Math.Cos(y * .13 - x * .37));
        var reference = (Complex[,])image.Clone();
        var actual = (Complex[,])image.Clone();
        using var gpu = new AnalysisGpuSession(true);
        using (var cpu = new AnalysisGpuSession(false))
        {
            DiffractionEngine.FourierTransform2D(reference);
            Assert.Null(cpu.UsedDevice);
        }
        DiffractionEngine.FourierTransform2D(actual);
        Assert.NotNull(gpu.UsedDevice);
        var forwardError = actual.Cast<Complex>().Zip(reference.Cast<Complex>(), (a, b) => (a - b).Magnitude).Max();
        DiffractionEngine.FourierTransform2D(actual, inverse: true);
        var roundTripError = actual.Cast<Complex>().Zip(image.Cast<Complex>(), (a, b) => (a - b).Magnitude).Max();
        log.WriteLine($"GPU: {gpu.UsedDevice}, FFT peak absolute error {forwardError:G5}, round-trip {roundTripError:G5}");
        Assert.InRange(forwardError, 0, 1e-7);
        Assert.InRange(roundTripError, 0, 1e-11);
    }

    [CudaFact]
    public void ConvolutionPreservesEnergyCorrectionAndFiniteImageEdgesOnHardware()
    {
        const int height = 192, width = 256, size = 19;
        var source = new double[height, width];
        var maps = new double[1, height, width];
        var mean = new double[size, size];
        var component = new double[size, size];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            source[y,x] = (x + y) % 5 == 0 ? 0 : .2 + (x % 17) / 17d;
            maps[0,y,x] = (y % 23 - 11) / 11d;
        }
        mean[6,6] = 1;
        component[1,2] = -.08;
        component[5,8] = .08;
        var timer = Stopwatch.StartNew();
        var cpu = ImageSimulationEngine.ConvolvePsfIntensity(source, [component], maps, mean);
        var cpuMs = timer.Elapsed.TotalMilliseconds;
        using var gpu = new AnalysisGpuSession(true);
        Assert.NotNull(AnalysisGpuSession.AvailableDevice);
        timer.Restart();
        var result = ImageSimulationEngine.ConvolvePsfIntensity(source, [component], maps, mean);
        var gpuMs = timer.Elapsed.TotalMilliseconds;
        Assert.NotNull(gpu.UsedDevice);
        Assert.Equal(cpu.CorrectedKernelCount, result.CorrectedKernelCount);
        Assert.Equal(cpu.MaximumNegativeMass, result.MaximumNegativeMass, 12);
        var error = result.Values.Cast<double>().Zip(cpu.Values.Cast<double>(), (a, b) => Math.Abs(a - b)).Max();
        log.WriteLine($"GPU: {gpu.UsedDevice}, convolution max error {error:G5}, CPU {cpuMs:F2} ms, GPU {gpuMs:F2} ms");
        Assert.InRange(error, 0, 1e-12);
        source[3, 4] = double.NaN;
        Assert.Throws<ArgumentException>(() => ImageSimulationEngine.ConvolvePsfIntensity(source, [component], maps, mean));
    }

    [CudaTheory]
    [InlineData("PSF")]
    [InlineData("MTF")]
    public async Task WorkbenchReportsOnlyExecutedGpuWork(string analysis)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var settings = app.Analyses.MergeSettings(analysis, analysis == "MTF" ? null : new Dictionary<string, string>
        {
            ["Sampling"] = "64 x 64", ["Display"] = "128 x 128"
        });
        var request = new AnalysisRequestDto(Guid.NewGuid(), 1, analysis, settings);
        var cpu = await app.Analyses.RunAsync(request);
        var gpu = await app.Analyses.RunAsync(request with { UseGpuAcceleration = true });
        Assert.True(gpu.View.Outcome == OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, gpu.View.OutcomeReason);
        Assert.Null(cpu.Provenance.GpuDeviceUsed);
        Assert.NotNull(gpu.Provenance.GpuDeviceUsed);
        var a = cpu.View.Series.SelectMany(series => series.Points).ToArray();
        var b = gpu.View.Series.SelectMany(series => series.Points).ToArray();
        Assert.NotEmpty(a);
        Assert.Equal(a.Length, b.Length);
        var error = a.Zip(b, (p, q) => Math.Max(Math.Abs(p.Y - q.Y), Math.Abs((p.Value ?? 0) - (q.Value ?? 0)))).Max();
        log.WriteLine($"Workbench {analysis}: {gpu.Provenance.GpuDeviceUsed}, max error {error:G5}");
        Assert.InRange(error, 0, 1e-10);
    }

    [Fact]
    public void AutomaticModeCompletesWithTheAvailableBackend()
    {
        var image = new Complex[256, 256];
        image[0, 0] = 2;
        using var session = new AnalysisGpuSession(true);
        DiffractionEngine.FourierTransform2D(image);
        Assert.All(image.Cast<Complex>(), value => Assert.Equal(new Complex(2, 0), value));
        Assert.Equal(AnalysisGpuSession.AvailableDevice, session.UsedDevice);
        log.WriteLine("Executed on " + (session.UsedDevice ?? "CPU"));
    }
}
