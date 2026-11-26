using System.Text.RegularExpressions;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class SequentialImportFidelityTests
{
    [Fact]
    public async Task PackagedSequentialExamplesOpenThroughTheDocumentService()
    {
        var failures = new List<string>();
        foreach (var example in SequentialExampleCatalog.Examples)
        {
            using var app = WorkbenchApplication.Create("blank");
            var error = await Record.ExceptionAsync(() => app.Documents.OpenAsync(example.FilePath));
            if (error is not null)
                failures.Add($"{example.RelativePath}: {error.Message}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task MenuPrescriptionsRetainDeclaredTypesAndConfigurationText()
    {
        var types = new Dictionary<string, string>
        {
            ["STANDARD"] = "标准面", ["COORDBRK"] = "坐标间断", ["TILTSURF"] = "倾斜面", ["PARAXIAL"] = "近轴面",
            ["PARAX_XY"] = "近轴 XY 面", ["BIRE__IN"] = "双折射输入", ["BIRE_OUT"] = "双折射输出",
            ["GRINSUR1"] = "梯度折射率面 1", ["GRINSUR9"] = "梯度折射率面 9", ["GRINSU11"] = "网格梯度折射率面",
            ["EVENASPH"] = "偶次非球面", ["TOROIDAL"] = "环形面", ["GRID_SAG"] = "网格矢高面", ["DGRATING"] = "衍射光栅",
            ["BINARY_2"] = "二元面 2", ["BINARY_3"] = "二元面 3", ["HOLOGRM2"] = "全息面 2", ["IRREGULA"] = "不规则面",
            ["FZERNSAG"] = "Zernike Fringe 矢高面", ["CHEBYSHV"] = "Chebyshev 矢高面", ["USERSURF"] = "微镜阵列",
            ["NONSEQCO"] = "非顺序多边形组"
        };
        var differences = new List<string>();
        foreach (var example in SequentialExampleCatalog.Examples)
        {
            var text = await File.ReadAllTextAsync(example.FilePath);
            var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(example.FilePath);
            var selected = Regex.Match(text, @"(?m)^MNUM\s+(\d+)\s+(\d+)");
            if (selected.Success)
            {
                Assert.Equal(int.Parse(selected.Groups[1].Value), imported.Configurations.Count);
                Assert.Equal(int.Parse(selected.Groups[2].Value) - 1, imported.ActiveConfigurationIndex);
            }
            foreach (Match cell in Regex.Matches(text, "(?m)^(MCOM|LTTL) +([0-9]+) +([0-9]+) +\"([^\"]*)\""))
            {
                var optic = imported.Configurations[int.Parse(cell.Groups[3].Value) - 1];
                var actual = cell.Groups[1].Value == "MCOM" ? optic.SurfaceGroup.Items[int.Parse(cell.Groups[2].Value)].Label : optic.Name;
                Assert.Equal(cell.Groups[4].Value, actual);
            }
            foreach (Match block in Regex.Matches(text, @"(?ms)^SURF (\d+)\s*\r?\n(?:(?!^SURF ).)*?^\s*TYPE\s+(\w+)"))
            {
                var number = int.Parse(block.Groups[1].Value);
                var type = block.Groups[2].Value;
                for (var i = 0; i < imported.Configurations.Count; i++)
                {
                    var surface = imported.Configurations[i].SurfaceGroup.Items[number];
                    var ignored = Regex.IsMatch(text, $@"(?m)^IGNR\s+{number}\s+{i + 1}\s+1(?:\.0*(?:E\+00)?)?\s");
                    var expected = ignored ? "忽略面" : types[type];
                    var actual = new SurfaceEditorRow(WorkbenchMapper.ToSurfaceDto(surface)).SurfaceType;
                    if (actual != expected) differences.Add($"{example.RelativePath}, configuration {i + 1}, surface {number}: {type} => {actual}, expected {expected}");
                }
            }
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Theory]
    [InlineData("Grating.zmx", 3)]
    [InlineData("Reflection hologram.zmx", 1)]
    [InlineData("Transmission hologram.zmx", 1)]
    [InlineData("Dual Zone CD-DVD lens.ZMX", 2)]
    [InlineData("Rochon prism.zmx", 2)]
    [InlineData("Rochon prism.zmx", 3)]
    public async Task EditingOtherPropertiesPreservesSpecialSurfaceAndTracedRay(string filename, int number)
    {
        var source = SequentialExampleCatalog.Examples.Single(e => Path.GetFileName(e.FilePath) == filename).FilePath;
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source);
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(source);
        var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[number]);
        app.Prescription.UpdateSurfaceComponents(number, new(row.SurfaceType, row.ApertureKind,
            row.GratingOrder, row.GratingPeriodMicrometers, row.GrooveOrientationAngleDegrees, row.ThinLensFocalLength,
            SemiDiameterFixed: true, SemiDiameter: row.SemiDiameter));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            var optic = (await StarOptProjectStore.LoadAsync(path)).Configurations[imported.ActiveConfigurationIndex];
            var saved = optic.SurfaceGroup.Items[number];
            Assert.True(saved.SemiDiameterFixed);
            Assert.Equal(row.SurfaceType, new SurfaceEditorRow(WorkbenchMapper.ToSurfaceDto(saved)).SurfaceType);
            var original = imported.ActiveOptic.SurfaceGroup.Items[number];
            Assert.Equal(original.InteractionModel.GetType(), saved.InteractionModel.GetType());
            if (original.InteractionModel is RuledGratingInteractionModel grating)
            {
                var restored = Assert.IsType<RuledGratingInteractionModel>(saved.InteractionModel);
                Assert.Equal(grating.Order, restored.Order);
                Assert.Equal(grating.LinesPerMicrometer, restored.LinesPerMicrometer);
                Assert.Equal(grating.IsReflective, restored.IsReflective);
            }
            Assert.Equal(Trace(imported.ActiveOptic), Trace(optic));
        }
        finally { File.Delete(path); }

        static object Trace(Optic optic)
        {
            var wavelength = optic.Wavelengths.Single(w => w.IsPrimary);
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .1, wavelength.Micrometers);
            var image = optic.SurfaceGroup.Items.Count - 1;
            using var trace = optic.SequentialRayTracer.Trace(ray, TraceRequest.Selected([image]));
            Assert.True(trace.TryGetSample(0, image, out var sample));
            return (sample.Position, sample.Direction, sample.PhaseInclusiveOpticalPathLength);
        }
    }

    [Fact]
    public async Task MaterialEditPropagatesAcrossCoordinateBreaksToTheNextInterface()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zmx");
        var saved = Path.ChangeExtension(source, ".staropt");
        try
        {
            await File.WriteAllTextAsync(source, """
                MODE SEQ
                ENPD 2
                SURF 0
                DISZ INFINITY
                SURF 1
                STOP
                GLAS BK7 0 0
                DISZ 2
                SURF 2
                TYPE COORDBRK
                SURF 3
                TYPE COORDBRK
                DISZ 1
                SURF 4
                TYPE STANDARD
                DISZ 2
                SURF 5
                TYPE STANDARD
                """);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(source);
            app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Material = "SF5" });
            await app.Documents.SaveAsync(saved);
            var surfaces = (await StarOptProjectStore.LoadAsync(saved)).Configurations[0].SurfaceGroup.Items;
            var expected = surfaces[1].MaterialAfter;
            Assert.Equal("SF5", Assert.IsType<CatalogGlassMaterial>(expected).CatalogName);
            foreach (var number in new[] { 2, 3, 4 })
            {
                Assert.Equal(expected.RefractiveIndex(480), surfaces[number].MaterialBefore.RefractiveIndex(480));
                if (number == 4) break;
                Assert.IsType<CoordinateReferenceInteractionModel>(surfaces[number].InteractionModel);
                Assert.Equal(expected.RefractiveIndex(650), surfaces[number].MaterialAfter.RefractiveIndex(650));
            }
            Assert.Equal("Air", surfaces[4].MaterialAfter.Name);
        }
        finally { File.Delete(source); File.Delete(saved); }
    }

    [Fact]
    public void ConfiguredGlassKeepsItsOwnCellWhenBothStructuresInitiallyUseTheSameGlass()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet("""
            MODE SEQ
            ENPD 2
            SURF 0
            DISZ INFINITY
            SURF 1
            STOP
            GLAS BK7 0 0
            DISZ 2
            SURF 2
            MNUM 2 1
            GLSS 1 1 "BK7" 0
            GLSS 1 2 "BK7" 0
            """);
        var multi = new MultiConfiguration(imported.Configurations, imported.BrokenLinks);
        var first = multi.Configurations[0].SurfaceGroup.Items[1];
        var second = multi.Configurations[1].SurfaceGroup.Items[1];
        var original = second.MaterialAfter.RefractiveIndex(550);
        first.MaterialAfter = multi.Configurations[0].Materials.Resolve("SF5");
        multi.PropagateBaseProperty(1, "material");
        Assert.NotEqual(original, first.MaterialAfter.RefractiveIndex(550));
        Assert.Equal(original, second.MaterialAfter.RefractiveIndex(550));
    }

    [Fact]
    public async Task DifferentCrystalAxesWithTheSameGlassNameRemainIndependent()
    {
        var path = SequentialExampleCatalog.Examples.Single(e => e.RelativePath.EndsWith("/Rochon prism.zmx")).FilePath;
        var first = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path)).ActiveOptic;
        var second = first.Clone();
        var entry = second.SurfaceGroup.Items[2];
        var crystal = Assert.IsType<UniaxialMaterial>(entry.MaterialAfter);
        entry.MaterialAfter = new UniaxialMaterial(crystal.Ordinary, crystal.Extraordinary, new(1, 0, 0),
            crystal.TraceExtraordinary, crystal.ParaxialIgnore);
        second.SurfaceGroup.SynchronizeFollowingMedia(2);
        var multi = new MultiConfiguration([first, second]);
        multi.PropagateBaseLinks();
        foreach (var medium in new[] { multi.Configurations[1].SurfaceGroup.Items[2].MaterialAfter,
            multi.Configurations[1].SurfaceGroup.Items[3].MaterialAfter, multi.Configurations[1].SurfaceGroup.Items[4].MaterialBefore })
            Assert.Equal(1, Assert.IsType<UniaxialMaterial>(medium).Axis.X);
        Assert.Equal(0, Assert.IsType<UniaxialMaterial>(multi.Configurations[0].SurfaceGroup.Items[2].MaterialAfter).Axis.X);
    }
}
