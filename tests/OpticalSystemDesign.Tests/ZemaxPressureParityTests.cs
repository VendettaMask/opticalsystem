using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Validation;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxPressureParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VacuumCellMatchesEveryFrozenSurface(bool batched)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples",
            "Miscellaneous", "vacuum cell.zmx");
        Assert.Equal("FD2A3063B4ED4AAB4F50AEE4786FAA4B498F5731FB6746985B81066AAE869911",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
        var optic = new ZemaxZmxImporter().Import(File.ReadAllText(source));
        using var archive = ZipFile.OpenRead(EvidenceFiles.Resolve(Path.Combine(root, "validation", "repairs",
            "2026-10-21", "sequential-corpus-full-evidence.zip")));
        using var nativeStream = archive.GetEntry("run/rays/046-vacuum-cell-c1-w1/native/ray-audit.json")!.Open();
        using var native = JsonDocument.Parse(nativeStream);
        var surfaces = native.RootElement.GetProperty("surfaces").EnumerateArray().ToArray();
        Assert.Equal(optic.SurfaceGroup.Items.Count - 1, surfaces.Length);

        var offset = 0;
        var maximumCoordinateError = 0d;
        var maximumDirectionError = 0d;
        foreach (var field in native.RootElement.GetProperty("inputs").EnumerateArray()
                     .GroupBy(input => input.GetProperty("fieldIndex").GetInt32()))
        {
            var rows = field.ToArray();
            var pupil = rows.Select(row => new PupilSample(Number(row, "px"), Number(row, "py"), Number(row, "weight"))).ToArray();
            var rays = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                Number(rows[0], "hx"), Number(rows[0], "hy"), optic.Wavelengths[0].Micrometers,
                pupil, aimAtStop: optic.RayAimingEnabled);
            using var trace = optic.SequentialRayTracer.Trace(rays,
                TraceRequest.FullHistory(maxDegreeOfParallelism: 1) with { UseBatchedBackend = batched });
            foreach (var surface in surfaces)
            {
                var number = surface.GetProperty("surface").GetInt32();
                var frame = optic.SurfaceGroup.Items[number].CoordinateSystem;
                var references = surface.GetProperty("rays");
                for (var ray = 0; ray < rows.Length; ray++)
                {
                    var expected = references[offset + ray];
                    Assert.Equal(offset + ray + 1, expected.GetProperty("number").GetInt32());
                    Assert.Equal(0, expected.GetProperty("error").GetInt32());
                    Assert.Equal(0, expected.GetProperty("vignette").GetInt32());
                    Assert.True(trace.TryGetSample(ray, number, out var actual));
                    Assert.False(actual.Vignetted);
                    Assert.True(actual.Intensity > 0);
                    var position = frame.ToLocalPoint(actual.Position);
                    var direction = frame.ToLocalDirection(actual.Direction);
                    Compare(position.X, Number(expected, "x"), 1e-6, ref maximumCoordinateError);
                    Compare(position.Y, Number(expected, "y"), 1e-6, ref maximumCoordinateError);
                    Compare(position.Z, Number(expected, "z"), 1e-6, ref maximumCoordinateError);
                    Compare(direction.X, Number(expected, "l"), 1e-8, ref maximumDirectionError);
                    Compare(direction.Y, Number(expected, "m"), 1e-8, ref maximumDirectionError);
                    Compare(direction.Z, Number(expected, "n"), 1e-8, ref maximumDirectionError);
                }
            }
            offset += rows.Length;
        }
        Assert.Equal(144, offset);
        output.WriteLine(FormattableString.Invariant(
            $"Batched={batched}, ray count={offset}, surface comparisons={offset * surfaces.Length}, maximum coordinate error={maximumCoordinateError:G17} mm, maximum direction error={maximumDirectionError:G17}"));
    }

    [Fact]
    public async Task OrderedPressureRowsAreScopedToTheGlassAndConfiguration()
    {
        const string source = """
            MODE SEQ
            ENVD 20 1 1
            ENPD 2
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 5
              DIAM 1 1
            SURF 2
              DISZ 5
              DIAM 1 1
            SURF 3
              DISZ 5
              DIAM 1 1
            SURF 4
              DIAM 1 1
            MNUM 2 1
            PRES 0 1 0
            PRES 0 2 0.5
            GLSS 2 1 ""
            GLSS 2 2 ""
            PRES 0 1 1
            PRES 0 2 1
            GLSS 3 1 ""
            GLSS 3 2 ""
            """;
        var importer = new ZemaxZmxImporter();
        var imported = importer.ImportConfigurationSet(source);
        Assert.Equal(2, imported.Configurations.Count);
        var vacuum = imported.Configurations[0];
        var halfAtmosphere = imported.Configurations[1];
        var full = vacuum.SurfaceGroup.Items[1].MaterialAfter.RefractiveIndex(550);
        var empty = vacuum.SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550);
        var half = halfAtmosphere.SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550);
        Assert.Equal(1, full);
        Assert.InRange(empty, .9997269, .9997271);
        Assert.Equal((full + empty) / 2, half, 14);
        Assert.Equal(empty, vacuum.SurfaceGroup.Items[3].MaterialBefore.RefractiveIndex(550));
        Assert.Equal(1, vacuum.SurfaceGroup.Items[3].MaterialAfter.RefractiveIndex(550));
        Assert.Equal(empty, Optic.FromSnapshot(vacuum.ToSnapshot()).SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + StarOptProjectStore.Extension);
        try
        {
            await StarOptProjectStore.SaveAsync(new(imported.Configurations, 0), path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(empty, restored.Configurations[0].SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550));
            Assert.Equal(half, restored.Configurations[1].SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550));
        }
        finally
        {
            File.Delete(path);
        }
        var roundTrip = importer.Import(new ZemaxZmxExporter().Export(vacuum));
        Assert.Equal(0, Assert.IsType<AirMaterial>(roundTrip.SurfaceGroup.Items[2].MaterialAfter).PressureAtmospheres);
        Assert.Equal(1, roundTrip.SurfaceGroup.Items[3].MaterialAfter.RefractiveIndex(550));
        Assert.Throws<NotSupportedException>(() => new CodeVSeqExporter().Export(vacuum));
        Assert.Equal(1, importer.Import(source.Replace("ENVD 20 1 1", "ENVD 20 1 0"))
            .SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550));
        Assert.Throws<InvalidDataException>(() => importer.Import(source.Replace("PRES 0 1 0", "PRES 0 1 -1")));
        Assert.Throws<NotSupportedException>(() => importer.Import(source.Replace("PRES 0 1 1", "PRES 0 1 0.5")));
    }

    private static double Number(JsonElement value, string key) => value.GetProperty(key).GetDouble();

    private static void Compare(double actual, double expected, double absolute, ref double maximumError)
    {
        var error = Math.Abs(actual - expected);
        Assert.InRange(error, 0, absolute + 1e-6 * Math.Abs(expected));
        maximumError = Math.Max(error, maximumError);
    }
}
