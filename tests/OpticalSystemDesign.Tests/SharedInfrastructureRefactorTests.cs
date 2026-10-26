using System.Globalization;
using System.Text;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using NsReason = OpticalSystemDesign.Core.Raytrace.NonSequentialTerminationReason;

namespace OpticalSystemDesign.Tests;

public sealed class SharedInfrastructureRefactorTests
{
    [Theory]
    [InlineData(486.1)]
    [InlineData(656.3)]
    public void LiveAndReconstructedDetectorsAgreeOnSpatialAndAngularPowerAndHits(double wavelength)
    {
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new(0, 0, 5)),
            Parameters = new DetectorRectangleParameters(WidthMillimeters: 10, HeightMillimeters: 10, PixelsX: 8, PixelsY: 8)
        };
        var document = new NonSequentialDocument("shared", [new("red", 656.3, 1, true), new("blue", 486.1, 1, false)], [detector]);
        var trace = new NonSequentialDocumentTracer().Trace(document, new MaterialRegistry(),
            new(DirectRay: new RealRay(new(0, 0, 0), new(.2, -.1, 1), wavelength, Intensity: .75)));
        var live = Assert.Single(trace.Detectors);
        var replay = Assert.Single(NonSequentialDetectorReconstruction.Reconstruct(document, trace.Branches));
        var number = wavelength == 656.3 ? 1 : 2;
        Assert.Equal(.75, live.TotalPowerWatts, 12);
        Assert.Equal(live.TotalPowerWatts, replay.TotalPowerWatts);
        Assert.Equal(live.PowerByWavelength[number], replay.PowerByWavelength[number]);
        Assert.Equal(live.HitCountByWavelength![number], replay.HitCountByWavelength![number]);
        Assert.Equal(live.AngularPowerByWavelength![number], replay.AngularPowerByWavelength![number]);
        Assert.Equal(live.AngularHitCountByWavelength![number], replay.AngularHitCountByWavelength![number]);
        Assert.Equal(1, replay.HitCountByWavelength[number].Sum());
        Assert.Equal(1, replay.AngularHitCountByWavelength[number].Sum());
        Assert.Throws<NotSupportedException>(() => ((IList<double>)replay.PowerByWavelength[number])[0] = 10);
    }

    [Fact]
    public void WavelengthLookupPreservesDocumentOrderUnknownValuesAndSnapshotIdentity()
    {
        var entries = new List<NonSequentialWavelength> { new("red", 650, 1, true), new("blue", 450, 1, false) };
        var index = new NonSequentialWavelengthIndex(entries);
        entries.Reverse();
        Assert.Equal(1, index.FindNumber(650));
        Assert.Equal(2, index.FindNearestNumber(450 + 5e-10));
        Assert.Equal(0, index.FindNumber(550));
        Assert.Equal(-1, index.FindIndex(double.NaN));
        Assert.Equal(0, index.FindNearestNumber(double.PositiveInfinity));
        Assert.Equal(0, new NonSequentialWavelengthIndex([]).FindNumber(450));
        var branch = new NonSequentialRayBranch(1, null, 0, null, [], NsReason.Escaped, 1, 650);
        Assert.Equal(1, index.FindNumber(branch));
    }

    [Fact]
    public void WavelengthLookupRetainsFirstAndNearestRulesAtToleranceBoundaries()
    {
        var boundary = NonSequentialWavelengthIndex.ToleranceNanometers;
        var index = new NonSequentialWavelengthIndex([new("boundary", boundary, 1, true)]);
        Assert.Equal(1, index.FindNumber(0));
        Assert.Equal(-1, index.FindIndex(0, includeToleranceBoundary: false));
        Assert.Equal(1, index.FindNearestNumber(0));
        var nearby = new NonSequentialWavelengthIndex([new("first", 1.5e-9, 1, true), new("nearest", 1e-9, 1, false)]);
        Assert.Equal(1, nearby.FindNumber(9e-10));
        Assert.Equal(2, nearby.FindNearestNumber(9e-10));
    }

    [Fact]
    public void AsciiStlByteAndStreamingAdaptersProduceIdenticalScaledMeshes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-stl-{Guid.NewGuid():N}.stl");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("solid triangle\r\n facet normal 0 0 1\n outer loop\r\n vertex 0 0 0\n vertex 1 0 0\n VeRtEx 0 1 0\n endloop\n endfacet\nendsolid");
            File.WriteAllBytes(path, bytes);
            var memory = NonSequentialStlImporter.Import(bytes, Path.GetFileName(path), NonSequentialMeshUnit.Inch);
            var streamed = NonSequentialStlImporter.Import(path, NonSequentialMeshUnit.Inch);
            Assert.Equal(memory.Sha256, streamed.Sha256);
            Assert.Equal(memory.GetGeometry().Vertices, streamed.GetGeometry().Vertices);
            Assert.Equal(memory.GetGeometry().Triangles, streamed.GetGeometry().Triangles);
            Assert.Contains(memory.GetGeometry().Vertices, value => value.X == 25.4);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("solid empty\nendsolid")]
    [InlineData("vertex 0 0 0\nvertex 1 0 0")]
    [InlineData("vertex 0 invalid 0")]
    [InlineData("vertex 0 0 Infinity")]
    public void AsciiStlByteAndStreamingAdaptersRejectMalformedInputWithSameDiagnostics(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-stl-{Guid.NewGuid():N}.stl");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text); File.WriteAllBytes(path, bytes);
            var memory = Assert.Throws<InvalidDataException>(() => NonSequentialStlImporter.Import(bytes, Path.GetFileName(path)));
            var streamed = Assert.Throws<InvalidDataException>(() => NonSequentialStlImporter.Import(path));
            Assert.Equal(memory.Message, streamed.Message);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("qe", "wavelength_nm,qe_r,qe_g,qe_b", "450,.1,.2,bad")]
    [InlineData("light", "wavelength_nm,irradiance_w_m2_nm", "450,bad")]
    [InlineData("rho", "wavelength_nm,reflectance", "450,bad")]
    public async Task SharedSpectralCsvParsingRetainsPhysicalLineNumbers(string kind, string header, string invalidRow)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-csv-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "\uFEFF# comment\r\n\r\n" + header + "\r\n" + invalidRow);
            using var app = WorkbenchApplication.Create();
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                if (kind == "qe") await app.CameraImaging.ImportResponseAsync(path);
                else if (kind == "light") await app.CameraImaging.ImportIlluminantAsync(path);
                else await app.CameraImaging.ImportReflectanceAsync(path);
            });
            Assert.Contains("第 4 行", error.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CommercialSubsetTextKeepsDialectPrecisionAndCurrentCultureIndependence(bool codeV)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var optic = new SequentialLensDocument("subset", [
                new(0, "Object", 0, 100, "Air", "None", 3.5, 0, false, false),
                new(1, "Stop mirror", 25.123456789123, 2.25, "Air", "None", 3.5, -.25, true, false),
                new(2, "Image", 0, 0, "Air", "None", 3.5, 0, false, false)]).ToOptic();
            IOpticalFormatExporter writer = codeV ? new CodeVSeqExporter() : new OsloLenExporter();
            var plainText = writer.Export(optic);
            optic.SurfaceGroup.Items[1].IsReflective = true;
            var text = writer.Export(optic);
            var expected = codeV
                ? "S 1\n  COM Stop mirror\n  RDY 25.123456789123001\n  THI 2.25\n  GLA MIRROR\n  SDIA 3.5\n  CON -0.25\n  STO\n  REFL"
                : "SRF 1\n  NOTE Stop mirror\n  RD 25.123456789123001\n  TH 2.25\n  GLA MIRROR\n  AP 3.5\n  CC -0.25\n  STO\n  REFL";
            Assert.Contains(expected.Replace("\n", Environment.NewLine), text, StringComparison.Ordinal);
            IOpticalFormatImporter reader = codeV ? new CodeVSeqImporter() : new OsloLenImporter();
            var restored = reader.Import(plainText);
            Assert.Equal(optic.SurfaceGroup.Items.Count, restored.SurfaceGroup.Items.Count);
            Assert.True(restored.SurfaceGroup.Items[1].IsStop);
            Assert.False(restored.SurfaceGroup.Items[1].IsReflective);
            Assert.Equal(optic.SurfaceGroup.Items[1].Radius, restored.SurfaceGroup.Items[1].Radius);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }
}
