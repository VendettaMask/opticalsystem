using System.Text;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ToleranceScriptImportTests
{
    internal const string Script = """
        ! Portable two-step alignment
        CLEARCOMP
        LOADMERIT "C:\native\Gap Setup.MF"
        COMP 2 0
        OPTIMIZE 4
        CLEARCOMP
        LOADMERIT Focus.ZMX
        COMP 6 0
        OPTIMIZE 4
        CLEARCOMP
        LOADMERIT Acceptance.ZMX
        GETMERIT
        """;

    [Fact]
    public async Task ScriptLoadsSelectedZmxTablesAndRunsTheMappedStages()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var optic = Optic.CreateCookeTriplet();
        var count = optic.SurfaceGroup.Items.Count;
        var first = GlobalCoordinateMetrics.Vertex(optic, optic.SurfaceGroup.Items[3]).Z;
        var image = GlobalCoordinateMetrics.Vertex(optic, optic.SurfaceGroup.Items[^1]).Z;
        var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TSC_CAPTURE_DIR");
        var directory = capture ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        try
        {
            var tables = new Dictionary<string, ToleranceMeritTableDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, surface, target) in new[] { ("Gap Setup", 3, first + .05),
                         ("Focus", count - 1, image + .1), ("Acceptance", count - 1, image + .125) })
            {
                var path = Path.Combine(directory, name + ".zmx");
                paths.Add(path);
                await File.WriteAllTextAsync(path, MeritZmx(count, surface, target), Encoding.Unicode);
                tables.Add(name, new(name, await new ZemaxZmxImporter().ReadMeritTableAsync(path, count)));
            }
            var source = Path.Combine(directory, "two-stage.tsc");
            paths.Add(source);
            await File.WriteAllTextAsync(source, Script);
            var result = ToleranceScriptImporter.Preview(await File.ReadAllTextAsync(source), tables, count, [], null, .2);
            Assert.True(result.CanImport, string.Join(Environment.NewLine, result.Lines.Where(row => row.BlocksImport)));
            Assert.Equal(4, result.Iterations);
            Assert.Equal(new[] { "Gap Setup", "Focus" }, result.Stages.Select(stage => stage.MeritTable!.Name));
            Assert.Equal(new[] { 2, 6 }, result.Compensators.Select(row => row.SurfaceNumber));
            var options = TolerancingPanel.ApplyScriptOptions(TolerancingRunWindow.DefaultOptions() with
            {
                Criterion = ToleranceCriterion.Mtf, YieldLimit = .4, Mode = ToleranceAnalysisMode.InverseLimit
            }, result);
            Assert.Equal(0, options.YieldLimit);
            Assert.Equal(ToleranceAnalysisMode.Sensitivity, options.Mode);
            var revision = app.Events.Revision;
            var request = new TolerancingRequestDto(2, 0, 0, 1, 51, options.CompensationIterations,
                [new(1, true, ToleranceOperandKind.Thickness, 2, .04, .04)],
                Criterion: options.Criterion, YieldLimit: .01, MaxDegreeOfParallelism: 1,
                Mode: options.Mode, CompensationAlgorithm: options.CompensationAlgorithm,
                AdditionalCompensators: options.AdditionalCompensators, StageObjectives: options.StageObjectives, MeritTable: options.MeritTable);
            var validation = app.Tolerancing.ValidateStudy(request);
            Assert.True(validation.IsValid, string.Join("; ", validation.Messages));
            var run = await app.Tolerancing.RunAsync(request);
            var trial = Assert.Single(run.TrialRows);
            Assert.All(trial.Stages!, stage => Assert.InRange(stage.Criterion, 0, 1e-6));
            Assert.Equal(new[] { "Gap Setup", "Focus" }, trial.Stages!.Select(stage => stage.MeritTableName));
            Assert.InRange(Math.Abs(trial.CriterionValue!.Value - .025), 0, 1e-6);
            Assert.False(trial.Passed);
            Assert.Equal(revision, app.Events.Revision);
            var archive = Path.Combine(directory, "imported-script.startolsample.json");
            paths.Add(archive);
            await app.Tolerancing.SaveSampleAsync(run.WorstSample!, archive);
            using var other = WorkbenchApplication.Create("tessar");
            Assert.True((await other.Tolerancing.ReplaySampleAsync(archive)).MatchesRecorded);
        }
        finally
        {
            if (capture is null)
            {
                foreach (var path in paths) File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public void UnsafeOrUnmappedLinesNeverProduceAnApplicablePreview()
    {
        var tables = NamedTables();
        var preview = ToleranceScriptImporter.Preview(Script + "\nSAVE 2\nOPENFILE \"test.dat\" TXT", tables, 8, [], null, .2);
        Assert.False(preview.CanImport);
        Assert.Equal(new[] { 13, 14 }, preview.Lines.Where(row => row.BlocksImport).Select(row => row.Number));
        Assert.Throws<ArgumentException>(() => TolerancingPanel.ApplyScriptOptions(TolerancingRunWindow.DefaultOptions(), preview));
        Assert.False(ToleranceScriptImporter.Preview(Script.Replace("COMP 6 0", "CPAR 6 1"), tables, 8, [], null, .2).CanImport);
        Assert.False(ToleranceScriptImporter.Preview(Script.Replace("OPTIMIZE 4", "OPTIMIZE 0"), tables, 8, [], null, .2).CanImport);
        Assert.False(ToleranceScriptImporter.Preview(Script.Replace("COMP 6 0", "COMP 6 0\nOPTIMIZE 5"), tables, 8, [], null, .2).CanImport);
        Assert.False(ToleranceScriptImporter.Preview(Script.Replace("COMP 6 0", "COMP 2 0"), tables, 8, [], null, .2).CanImport);
        Assert.False(ToleranceScriptImporter.Preview(Script.Replace("Focus.ZMX", "\"D:\\other\\Gap Setup.MF\""), tables, 8, [], null, .2).CanImport);
        var missing = ToleranceScriptImporter.Preview(Script, new Dictionary<string, ToleranceMeritTableDto>(), 8, [], null, 0);
        Assert.Equal(3, missing.Lines.Count(row => row.Status == "缺少评价表"));
        Assert.Contains(missing.Lines, row => row.Status == "缺少行程");
    }

    [Fact]
    public void ConfigurationCompensatorsUseTheSelectedThicknessRow()
    {
        var tables = NamedTables();
        var script = Script.Replace("COMP 6 0", "CMCO 1 2");
        var rows = new[] { new MultiConfigurationOperandRowDto(1, MultiConfigurationParameterKind.Thickness, 6, [42, 42.5], [false, false], [null, null]) };
        var selected = new ToleranceConfigurationSettingsDto(ToleranceConfigurationScope.Selected, [1, 2]);
        var preview = ToleranceScriptImporter.Preview(script, tables, 8, rows, selected, .12);
        Assert.True(preview.CanImport);
        var mapped = Assert.Single(preview.ConfigurationCompensators);
        Assert.Equal(new ToleranceMceAdjustmentDto(2, 1, 6, -.12, .12, true, 2), mapped);
        Assert.False(ToleranceScriptImporter.Preview(script, tables, 8, rows, selected with { Configurations = [1] }, .12).CanImport);
        var options = TolerancingPanel.ApplyScriptOptions(TolerancingRunWindow.DefaultOptions() with
        {
            ConfigurationSettings = selected with { MceRows = [new(1, 1, 6, -.03, .03)] }
        }, preview);
        Assert.Equal(new[] { false, true }, options.ConfigurationSettings!.MceRows!.Select(row => row.Compensator));
    }

    private static string MeritZmx(int count, int surface, double target) =>
        "MODE SEQ\nUNIT MM\n" + string.Concat(Enumerable.Range(0, count).Select(index => $"SURF {index}\n  TYPE STANDARD\n"))
        + FormattableString.Invariant($"GLCZ {surface} 0 0 0 0 0 {target:R} 1 0 0\n");

    internal static Dictionary<string, ToleranceMeritTableDto> NamedTables() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gap Setup"] = ExampleTable("Gap Setup", 3),
            ["Focus"] = ExampleTable("Focus", 7),
            ["Acceptance"] = ExampleTable("Acceptance", 7)
        };

    private static ToleranceMeritTableDto ExampleTable(string name, int surface) => new(name,
        [OpticalSystemDesign.Core.Serialization.MeritOperandSnapshot.FromDefinition(
            new OpticalSystemDesign.Core.Optimization.MeritOperandDefinition { Type = "GLCZ", Surface = surface })]);
}
