using System.Text;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Phase;

namespace OptilandWorkbench.Tests;

public sealed class ReliabilityHardeningTests
{
    [Fact]
    public async Task BoundedFileRejectsOversizedInputBeforeReadingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bounded-{Guid.NewGuid():N}.json");
        try
        {
            await using (var stream = File.Create(path))
            {
                stream.SetLength(BoundedFile.MaximumSettingsBytes + 1);
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => BoundedFile.ReadAllTextAsync(
                path,
                BoundedFile.MaximumSettingsBytes,
                "Settings"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task AtomicBoundedWritePreservesExistingFileWhenOutputIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atomic-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "original");

            await Assert.ThrowsAsync<InvalidDataException>(() => BoundedFile.WriteAllTextAtomicAsync(
                path,
                "replacement",
                4,
                "Test document"));

            Assert.Equal("original", await File.ReadAllTextAsync(path));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedFile.WriteAllTextAtomicAsync(
                path,
                "replacement",
                64,
                "Test document",
                cancellation.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(path));

            await BoundedFile.WriteAllTextAtomicAsync(path, "replacement", 64, "Test document");
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));

            await Assert.ThrowsAsync<InvalidDataException>(() => BoundedFile.WriteAtomicAsync(
                path,
                4,
                "Streamed document",
                async (stream, token) =>
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("too large"), token)));
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));

            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NumericModelCollectionsDoNotExposeMutableBackingStorage()
    {
        var coefficients = new[] { 0.1, 0.2 };
        var material = new CatalogGlassMaterial(
            "TEST",
            "TEST",
            "formula 1",
            400,
            700,
            coefficients: coefficients);
        coefficients[0] = 99;

        Assert.Equal(0.1, material.Coefficients[0], precision: 12);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)material.Coefficients)[0] = 1);

        var geometry = new EvenAsphereGeometry(10, 0, coefficients);
        coefficients[0] = 123;
        Assert.Equal(99, geometry.Coefficients[0], precision: 12);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)geometry.Coefficients).Clear());

        var phaseGrid = new double[4, 4];
        phaseGrid[0, 0] = 0.25;
        var phase = new GridPhaseProfile(
            new[] { 0d, 1d, 2d, 3d },
            new[] { 0d, 1d, 2d, 3d },
            phaseGrid);
        phaseGrid[0, 0] = 7;
        var exposed = phase.PhaseGrid;
        exposed[0, 0] = 8;

        Assert.Equal(0.25, phase.PhaseGrid[0, 0], precision: 12);
    }

    [Fact]
    public void DiffractionAndDirectPsfRejectWorkAboveSafetyBudgets()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);

        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeFftPsf(
            optic,
            (0, 0),
            wavelength,
            2_048,
            4_096));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic,
            (0, 0),
            wavelength,
            128,
            256,
            0.005));
    }

    [Fact]
    public void CoreAnalysisConstructorsRejectUnsafeResourceRequests()
    {
        var optic = Optic.CreateDemo();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IncoherentIrradianceAnalysis(optic, resolutionX: 1_025));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RadiantIntensityAnalysis(optic, binsY: 1_025));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GridDistortionAnalysis(optic, numPoints: 513));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WavefrontAnalysis(optic, mapSize: 513));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ZernikeAnalysis(
                optic,
                ZernikeAnalysisKind.Standard,
                numTerms: ZernikeFitEngine.MaximumStandardTerm + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ZernikeAnalysis(
                optic,
                ZernikeAnalysisKind.Annular,
                numTerms: ZernikeFitEngine.MaximumStandardTerm + 1));
    }

    [Fact]
    public void ZernikeDispatchUsesTypedKindInsteadOfPresentationName()
    {
        var optic = Optic.CreateDemo();
        var typed = new ZernikeAnalysis(
            optic,
            ZernikeAnalysisKind.Standard,
            numRings: 5,
            numTerms: 5,
            mapSize: 17,
            name: "任意显示标题").GenerateData();
        var compatibilityName = new ZernikeAnalysis(
            optic,
            numRings: 5,
            numTerms: 5,
            mapSize: 17,
            name: "Zernike Standard").GenerateData();

        Assert.Equal("standard", typed.Values["ZernikeType"]);
        Assert.Equal("fringe", compatibilityName.Values["ZernikeType"]);
    }

    [Fact]
    public void BitmapViewerRejectsUnsafeDecodedDimensionsBeforeAllocation()
    {
        Assert.Throws<InvalidDataException>(() =>
            ImageFileViewerWindow.ValidateBitmapDimensions(32_768, 32_768));
        ImageFileViewerWindow.ValidateBitmapDimensions(4_096, 4_096);
    }

    [Fact]
    public void SmiaTvDoesNotReportZeroWhenMetricIsUndefined()
    {
        var optic = Optic.CreateBlank();

        Assert.Throws<AnalysisDataUnavailableException>(() =>
            new DistortionAnalysis(optic, distortionType: "smia-tv").GenerateData());
    }

    [Fact]
    public void ZmfRejectsElementCountThatCannotBeRepresentedSafely()
    {
        var path = Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid():N}.zmf");
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: false))
            {
                writer.Write(1001u);
                writer.Write(new byte[100]);
                writer.Write(1u);
                writer.Write(uint.MaxValue);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(50.0);
                writer.Write(10.0);
            }

            Assert.Throws<InvalidDataException>(() => ZemaxStockCatalogReader.ReadFile(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
