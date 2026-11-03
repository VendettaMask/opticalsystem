using System.Text.Json;
using System.Globalization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class ParaxialLensModelTests
{
    [Theory]
    [InlineData("dual-air-0", 1)] [InlineData("dual-air-1", 1)]
    [InlineData("dual-glass-0", 1)] [InlineData("dual-glass-1", 1)]
    [InlineData("dual-mirror-0", 1)] [InlineData("dual-mirror-1", 1)]
    [InlineData("dual-air-1", 10)] [InlineData("dual-mirror-0", 25.4)]
    [InlineData("xy-infinite-air", 1)] [InlineData("xy-infinite-glass", 1)]
    [InlineData("xy-finite-air", 1)] [InlineData("xy-finite-glass", 1)] [InlineData("xy-strong-air", 1)]
    [InlineData("xy-finite-air", 10)] [InlineData("xy-strong-air", 25.4)]
    [InlineData("bire-tilted-0", 1)] [InlineData("bire-axial-0", 1)] [InlineData("bire-glass-0", 1)]
    [InlineData("bire-tilted-1", 1)] [InlineData("bire-axial-1", 1)] [InlineData("bire-glass-1", 1)]
    [InlineData("bire-tilted-1", 10)] [InlineData("bire-glass-1", 25.4)]
    [InlineData("infinite-mode1", 1)] [InlineData("infinite-mode2", 1)]
    [InlineData("infinite-mode0", 1)] [InlineData("infinite-mode0", 10)]
    [InlineData("finite-mode1", 1)] [InlineData("finite-mode2", 1)]
    [InlineData("aberrated-mode1", 1)] [InlineData("aberrated-mode2", 1)]
    [InlineData("infinite-mode1", 10)] [InlineData("finite-mode1", 10)] [InlineData("aberrated-mode1", 10)]
    [InlineData("plane-air-order-1", 1)] [InlineData("plane-air-order1", 1)]
    [InlineData("plane-glass-order-1", 1)] [InlineData("plane-glass-order1", 1)]
    [InlineData("plane-mirror-order-1", 1)] [InlineData("plane-mirror-order1", 1)]
    [InlineData("curved-air-order-1", 1)] [InlineData("curved-air-order1", 1)]
    [InlineData("curved-mirror-order-1", 1)] [InlineData("curved-mirror-order1", 1)]
    [InlineData("plane-glass-order1", 10)] [InlineData("curved-mirror-order-1", 25.4)]
    [InlineData("binary-plane--1", 1)] [InlineData("binary-plane-1", 1)]
    [InlineData("binary-curved--1", 1)] [InlineData("binary-curved-1", 1)]
    [InlineData("binary-glass--1", 1)] [InlineData("binary-glass-1", 1)]
    [InlineData("binary-mirror--1", 1)] [InlineData("binary-mirror-1", 1)]
    [InlineData("hologram-transmission-0", 1)] [InlineData("hologram-transmission-1", 1)]
    [InlineData("hologram-reflection-0", 1)] [InlineData("hologram-reflection-1", 1)]
    [InlineData("binary-curved-1", 10)] [InlineData("binary-glass--1", 25.4)]
    [InlineData("hologram-transmission-1", 10)] [InlineData("hologram-reflection-1", 25.4)]
    [InlineData("irregular-0", 1)] [InlineData("irregular-1", 1)]
    [InlineData("irregular-1", 10)] [InlineData("irregular-1", 25.4)]
    [InlineData("grid-random", 1)] [InlineData("grid-smooth", 1)]
    [InlineData("grid-linear", 1)] [InlineData("grid-estimated", 1)]
    [InlineData("toroid-50-80", 1)] [InlineData("toroid-50--80", 1)]
    [InlineData("toroid--50-80", 1)] [InlineData("toroid--50--80", 1)]
    [InlineData("toroid-0-80", 1)] [InlineData("toroid-50-0", 1)] [InlineData("toroid-50-100000", 1)]
    [InlineData("toroid-50-80", 10)] [InlineData("toroid-50--80", 10)]
    [InlineData("toroid--50-80", 10)] [InlineData("toroid--50--80", 10)]
    [InlineData("toroid-0-80", 10)] [InlineData("toroid-50-0", 10)] [InlineData("toroid-50-100000", 10)]
    [InlineData("toroid-50-80", 25.4)] [InlineData("toroid-50--80", 25.4)]
    [InlineData("toroid--50-80", 25.4)] [InlineData("toroid--50--80", 25.4)]
    [InlineData("toroid-0-80", 25.4)] [InlineData("toroid-50-0", 25.4)] [InlineData("toroid-50-100000", 25.4)]
    public void RaysAndPhaseMatchIndependentNativeControlsIncludingMediaAndFiniteConjugates(string id, double scale)
    {
        var fixtureName = id.StartsWith("toroid-") ? "toroidal-native-v1.json" : id.StartsWith("bire-") ? "birefringent-native-v1.json" : id.StartsWith("xy-") ? "paraxial-xy-native-v1.json" : id.StartsWith("dual-") ? "dual-zone-native-v2.json" : id == "infinite-mode0" ? "paraxial-collimated-native-v1.json" : id.Contains("mode") ? "paraxial-phase-native-v1.json"
            : id.StartsWith("grid") ? "grid-sag-native-v1.json"
            : id.StartsWith("irregular") ? "irregular-native-v1.json"
            : id.StartsWith("binary") || id.StartsWith("hologram") ? "diffractive-phase-native-v1.json" : "grating-phase-native-v1.json";
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName)));
        var data = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var rows = data.GetProperty("rows").EnumerateArray().ToArray();
        var source = ConvertUnits(data.GetProperty("source").GetString()!, scale);
        var imported = OpticalFormatCatalog.Import(source, ".zmx");
        foreach (var optic in new[] { imported, Optic.FromSnapshot(imported.ToSnapshot()),
            OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(imported, ".zmx"), ".zmx") })
        {
            Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
            Assert.True(optic.SurfaceGroup.Items[2].InteractionModel is ParaxialLensInteractionModel or RuledGratingInteractionModel or DiffractivePhaseInteractionModel or BirefringentInteractionModel
                || optic.SurfaceGroup.Items[2].Geometry is IrregularGeometry or GridSagGeometry or ToroidalGeometry);
            foreach (var row in rows.Where(r => r.GetProperty("input").GetProperty("code").GetString() == "SSAG"))
            {
                var p = row.GetProperty("input");
                Assert.InRange(Math.Abs(optic.SurfaceGroup.Items[2].Geometry.Sag(p.GetProperty("data1").GetDouble(), p.GetProperty("data2").GetDouble())
                    - row.GetProperty("value").GetDouble()), 0, 1e-10);
            }
            foreach (var group in rows.Where(r => r.GetProperty("input").GetProperty("code").GetString() != "SSAG").GroupBy(r =>
                (Wave: r.GetProperty("input").GetProperty("int2").GetInt32(), Hy: r.GetProperty("input").GetProperty("data2").GetDouble(),
                 Px: r.GetProperty("input").GetProperty("data3").GetDouble(),
                 Py: r.GetProperty("input").GetProperty("data4").GetDouble())))
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, group.Key.Hy, group.Key.Px, group.Key.Py,
                    optic.Wavelengths[group.Key.Wave - 1].Micrometers);
                using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(Enumerable.Range(1, optic.SurfaceGroup.Items.Count - 1).ToArray()));
                var first = trace.GetSurfaceSamples(1)[0]!.Value;
                var nativeFirstPath = group.Single(r => r.GetProperty("input").GetProperty("int1").GetInt32() == 1
                    && r.GetProperty("input").GetProperty("code").GetString() == "OPTH").GetProperty("value").GetDouble();
                foreach (var row in group)
                {
                    var input = row.GetProperty("input"); var index = input.GetProperty("int1").GetInt32();
                    Assert.True(trace.TryGetSample(0, index, out var sample)); Assert.False(sample.Vignetted);
                    var coordinates = optic.SurfaceGroup.Items[index].CoordinateSystem;
                    var position = coordinates.ToLocalPoint(sample.Position); var direction = coordinates.ToLocalDirection(sample.Direction);
                    var code = input.GetProperty("code").GetString()!;
                    var actual = code switch
                    {
                        "REAX" => position.X, "REAY" => position.Y, "REAZ" => position.Z,
                        "REAA" => direction.X, "REAB" => direction.Y, "REAC" => direction.Z,
                        "OPTH" => sample.PhaseInclusiveOpticalPathLength!.Value - first.PhaseInclusiveOpticalPathLength!.Value,
                        _ => throw new InvalidDataException(code)
                    };
                    var expected = row.GetProperty("value").GetDouble();
                    var isDirection = code is "REAA" or "REAB" or "REAC";
                    if (code == "OPTH") expected -= nativeFirstPath;
                    if (isDirection && optic.SurfaceGroup.Items.Take(index + 1).Count(s => s.IsReflective) % 2 == 1) expected = -expected;
                    Assert.InRange(Math.Abs(actual - expected), 0, code == "OPTH" ? 1e-7 : isDirection ? 1e-11 : 1e-9);
                }
            }
        }
    }

    [Fact]
    public void BinaryFirstOrderTraceIncludesTheNativeWavelengthDependentPower()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "diffractive-firstorder-native-v1.json")));
        foreach (var data in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var optic = OpticalFormatCatalog.Import(data.GetProperty("source").GetString()!, ".zmx");
            foreach (var row in data.GetProperty("rows").EnumerateArray())
            {
                var p = row.GetProperty("input"); var code = p.GetProperty("code").GetString();
                // EFFL's wave is int2, not int1: these captured EFFL rows explicitly requested wave 0 (primary).
                var wave = p.GetProperty("int2").GetInt32();
                var trace = optic.Paraxial.MarginalRay(optic.Wavelengths[Math.Max(0, wave - 1)].Micrometers);
                var actual = code switch { "EFFL" => optic.Paraxial.EstimateEffectiveFocalLength(), "PARY" => trace.Heights[2][0],
                    "PATY" => trace.Slopes[2][0], _ => throw new InvalidDataException(code) };
                Assert.InRange(Math.Abs(actual - row.GetProperty("value").GetDouble()), 0, 1e-10);
            }
        }
    }

    private static string ConvertUnits(string source, double scale)
    {
        if (scale == 1) return source;
        var type = ""; var finiteField = source.Contains("FTYP 1 ", StringComparison.Ordinal);
        return string.Join('\n', source.Split('\n').Select(line =>
        {
            var tokens = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return line;
            var command = tokens[0]; if (command == "TYPE") type = tokens[1];
            if (command == "UNIT") { tokens[1] = scale == 10 ? "CM" : "IN"; return string.Join(' ', tokens); }
            void Change(int i, double factor) { if (double.TryParse(tokens[i], CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                tokens[i] = (value * factor).ToString("R", CultureInfo.InvariantCulture); }
            if (command is "ENPD" or "DISZ" or "DIAM" or "MEMA") Change(1, 1 / scale);
            if (command == "CURV") Change(1, scale);
            if (command is "XFLN" or "YFLN" && finiteField) for (var i = 1; i < tokens.Length; i++) Change(i, 1 / scale);
            if (command == "PARM")
            {
                var number = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                if (type is "PARAXIAL" or "TOROIDAL" && number == 1 || type == "HOLOGRM2" && number is >= 1 and <= 6) Change(2, 1 / scale);
                if (type == "PARAX_XY" && number is 1 or 2) Change(2, scale);
                if (type == "BIRE__IN" && number == 0) Change(2, 1 / scale);
                if (type == "BINARY_2" && number is >= 1 and <= 8) Change(2, Math.Pow(scale, 2 * number - 1));
                if (type == "BINARY_3" && number is 1 or 3 or 4) Change(2, 1 / scale);
                if (type == "BINARY_3" && number >= 14 && (number - 14) % 2 == 0)
                    Change(2, Math.Pow(scale, 2 * ((number - 14) / 4 + 1) - 1));
                if (type == "IRREGULA" && number is 1 or 2 or 5 or 6 or 7) Change(2, 1 / scale);
            }
            if (command == "XDAT" && type == "BINARY_2" && tokens[1] == "2") Change(2, 1 / scale);
            if (command == "XDAT" && type == "BINARY_3")
            {
                var number = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                if (number >= 2 && (number - 2) % 2 == 0) Change(2, Math.Pow(scale, 2 * ((number - 2) / 4 + 1) - 1));
            }
            return string.Join(' ', tokens);
        }));
    }

    [Fact]
    public void IrregularSagNormalAndSurfaceEditsKeepTheSamePhysicalDefinition()
    {
        var shape = new IrregularGeometry(50, -.4, 20, .4, -.7, 2, -1.5, .03, -.05, .02, 33);
        const double h = 1e-4;
        foreach (var (x, y) in new[] { (0d, 0d), (2d, -3d), (-4d, 1d) })
        {
            var n = shape.SurfaceNormal(new(x, y, shape.Sag(x, y)));
            var dx = (shape.Sag(x + h, y) - shape.Sag(x - h, y)) / (2 * h);
            var dy = (shape.Sag(x, y + h) - shape.Sag(x, y - h)) / (2 * h);
            Assert.InRange(Math.Abs(dx + n.X / n.Z), 0, 1e-10);
            Assert.InRange(Math.Abs(dy + n.Y / n.Z), 0, 1e-10);
        }
        var optic = Optic.CreateBlank(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = shape; surface.Radius = 60; surface.SemiDiameter = 25;
        var edited = Assert.IsType<IrregularGeometry>(surface.Geometry);
        Assert.Equal(60, edited.Base.Radius); Assert.Equal(25, edited.NormalizationRadius);
        Assert.Equal(2, edited.TiltXDegrees); Assert.Equal(-1.5, edited.TiltYDegrees); Assert.Equal(.02, edited.Coma);
    }

    [Fact]
    public void NonQuadraticLensPhaseReachesFocusedWavefrontAndNormalizedTrace()
    {
        var optic = OpticalFormatCatalog.Import(Source(1), ".zmx");
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() - 100), 0, 1e-12);
        var wavefront = WavefrontEngine.GenerateChiefRay(optic, (0, 0), optic.Wavelengths[0], 4);
        Assert.NotEmpty(wavefront.Samples);
        Assert.All(wavefront.Samples.Where(s => s.Intensity > 0), s => Assert.InRange(Math.Abs(s.OpdWaves), 0, 1e-7));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(0, 0, .55, 21, "hexapolar");
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly());
        var samples = trace.GetSurfaceSamples(2).Where(s => s is { Intensity: > 0, Vignetted: false }).Select(s => s!.Value).ToArray();
        Assert.NotEmpty(samples);
        Assert.InRange(samples.Max(s => s.OpticalPathDifference) - samples.Min(s => s.OpticalPathDifference), 0, 1e-9);
        Assert.True(samples.Max(s => s.CumulativeOpticalPathLength) - samples.Min(s => s.CumulativeOpticalPathLength) > 1e-3);
    }

    [Theory] [InlineData(0)] [InlineData(3)]
    public void UnverifiedConjugateModesRemainOpaque(int mode)
    {
        var optic = OpticalFormatCatalog.Import(Source(mode).Replace("DISZ INFINITY", "DISZ 200"), ".zmx");
        Assert.IsType<OpaqueGeometryPayload>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.NotEmpty(OpticCapabilityPreflight.Inspect(optic));
    }

    [Fact]
    public void LensDirectionIsReciprocalWhenThePhysicalPropagationReverses()
    {
        var model = new ParaxialLensInteractionModel(.01, .01);
        var context = new SurfaceInteractionContext(new(0, 0, 1), 1.5, 1, 550, false, new PlaneGeometry(),
            SurfacePhaseOpticalPath: (_, _) => 0); // This invariant tests the geometric law only.
        var input = new OpticalSystemDesign.Core.Rays.RealRay(new(2, -3, 0), new(.02, .01, 1), 550).Normalize();
        var forward = model.Interact(input, context).Ray;
        var reverse = model.Interact(forward with { Direction = -forward.Direction },
            context with { RefractiveIndexBefore = 1, RefractiveIndexAfter = 1.5 }).Ray;
        Assert.InRange((reverse.Direction + input.Direction).Length, 0, 1e-14);
    }

    [Fact]
    public void ZeroPowerIsAnIdentityAndUnmappedTextFormatsCannotFlattenLensPower()
    {
        var optic = OpticalFormatCatalog.Import(Source(1).Replace("PARM 1 100", "PARM 1 0"), ".zmx");
        Assert.Equal(0, optic.Paraxial.EstimateOpticalPower());
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .3, .5, .55);
        using var trace = optic.SequentialRayTracer.Trace(source, TraceRequest.Selected([1]));
        Assert.Equal(source.Rays[0].Normalize().Direction, trace.GetSurfaceSamples(1)[0]!.Value.Direction);
        foreach (var extension in new[] { ".txt", ".seq", ".len" })
            Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(optic, extension));
    }

    private static string Source(int mode) => $"MODE SEQ\nUNIT MM\nENPD 10\nFTYP 0 0 1 1 0 0 0\nXFLN 0\nYFLN 0\nWAVM 1 .55 1\nPWAV 1\nSURF 0\nTYPE STANDARD\nCURV 0\nDISZ INFINITY\nSURF 1\nSTOP\nTYPE PARAXIAL\nCURV 0\nPARM 1 100\nPARM 2 {mode}\nDISZ 100\nDIAM 10 1 0 0 1 \"\"\nSURF 2\nTYPE STANDARD\nCURV 0";
}
