using System.Text;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxLibraryImporter;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxVariableImportTests
{
    [Theory]
    [InlineData("MM", 1)]
    [InlineData("CM", 10)]
    [InlineData("IN", 25.4)]
    public void SurfaceVariablesReachEditorAndOptimizationWithoutChangingValues(string unit, double scale)
    {
        var optic = new ZemaxZmxImporter().Import(Source.Replace("UNIT MM", $"UNIT {unit}"));
        var runtime = new WorkbenchRuntime(optic);
        Assert.Equal(RadiusSolveKind.Variable, runtime.GetRadiusSolve(1).Kind);
        Assert.Equal(ThicknessSolveKind.Variable, runtime.GetThicknessSolve(1).Kind);
        Assert.Equal(RadiusSolveKind.Fixed, runtime.GetRadiusSolve(2).Kind);
        Assert.Equal(ThicknessSolveKind.Fixed, runtime.GetThicknessSolve(2).Kind);
        Assert.Equal(50 * scale, runtime.Surfaces[1].Radius, 10);
        Assert.Equal(5 * scale, runtime.Surfaces[1].Thickness, 10);
        Assert.Equal(new[] { OptimizationVariableKind.Radius, OptimizationVariableKind.Thickness },
            runtime.GetMarkedOptimizationVariables().Select(variable => variable.Kind));
        var restored = new WorkbenchRuntime(Optic.FromSnapshot(optic.ToSnapshot()));
        Assert.Equal(runtime.GetMarkedOptimizationVariables(), restored.GetMarkedOptimizationVariables());
    }

    [Fact]
    public void PlaneCurvatureCanBeVariableAndMissingSolveCodeRemainsFixed()
    {
        var runtime = new WorkbenchRuntime(new ZemaxZmxImporter().Import(Source
            .Replace("CURV 0.02 1", "CURV 0 1").Replace("CURV -0.02 0", "CURV -0.02")));
        Assert.Equal(0, SurfaceCurvatureParameter.Read(runtime.Surfaces[1]));
        Assert.Equal(RadiusSolveKind.Variable, runtime.GetRadiusSolve(1).Kind);
        Assert.Equal(RadiusSolveKind.Fixed, runtime.GetRadiusSolve(2).Kind);
    }

    [Fact]
    public void PickupAndParaxialSolveCodesAreNotIndependentVariables()
    {
        var optic = new ZemaxZmxImporter().Import(Source
            .Replace("CURV 0.02 1", "CURV 0.02 2 0")
            .Replace("CURV -0.02 0", "CURV -0.02 4 1 -1 0")
            .Replace("DISZ 20", "DISZ 20\n  PZUP 1 2 1"));
        var runtime = new WorkbenchRuntime(optic);
        Assert.Equal(RadiusSolveKind.Fixed, runtime.GetRadiusSolve(1).Kind);
        Assert.Equal(RadiusSolveKind.Pickup, runtime.GetRadiusSolve(2).Kind);
        Assert.Equal(ThicknessSolveKind.Pickup, runtime.GetThicknessSolve(2).Kind);
        Assert.Equal(OptimizationVariableKind.Thickness, Assert.Single(runtime.GetMarkedOptimizationVariables()).Kind);
    }

    [Fact]
    public void VariableIdentityFollowsPhysicalSurfaceAfterCoordinateBreakFolding()
    {
        var source = Source.Replace("SURF 3", "SURF 4").Replace("SURF 2", "SURF 3")
            .Replace("SURF 1", "SURF 1\n  TYPE COORDBRK\n  DISZ 0\nSURF 2")
            .Replace("UNIT MM", "UNIT MM\nGLRS 2");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Equal(new[] { 0, 2, 3, 4 }, imported.SourceSurfaceNumbers[0]);
        var runtime = new WorkbenchRuntime(imported.ActiveOptic);
        Assert.All(runtime.GetMarkedOptimizationVariables(), variable => Assert.Equal(1, variable.SurfaceNumber));
    }

    [Fact]
    public void ZeroThicknessVariableKeepsCoordinateBreakAsEditableReferencePlane()
    {
        var source = Source.Replace("SURF 3", "SURF 4").Replace("SURF 2", "SURF 3")
            .Replace("SURF 1", "SURF 1\n  TYPE COORDBRK\n  DISZ 0\n  VDSZ 0 0\nSURF 2");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, imported.SourceSurfaceNumbers[0]);
        var runtime = new WorkbenchRuntime(imported.ActiveOptic);
        Assert.Equal(ThicknessSolveKind.Variable, runtime.GetThicknessSolve(1).Kind);
        Assert.Equal(3, runtime.GetMarkedOptimizationVariables().Count);
    }

    [Fact]
    public async Task LensLibraryConversionRetainsConfigurationVariableMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zmx-vars-library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "variables.zmx");
            await File.WriteAllTextAsync(path, Source + "\nMNUM 2 1\nTHIC 1 1 5 1\nTHIC 1 2 5 1");
            var result = await new ZemaxLibraryInstaller().InstallAsync(new(path,
                Path.Combine(root, "examples"), Path.Combine(root, "library"), License: "test"));
            var project = await StarOptProjectStore.LoadAsync(result.LibraryProjectPath);
            Assert.Single(project.OperandRows!);
            Assert.Equal(2, project.OperandVariables!.Count);
            Assert.All(project.Configurations, optic => Assert.True(optic.SurfaceGroup.Items[1].RadiusVariable));
            Assert.Equal(await File.ReadAllBytesAsync(result.ExampleProjectPath),
                await File.ReadAllBytesAsync(result.LibraryProjectPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileOpenSaveReopenClearAndUndoRetainImportedVariables(bool unicode)
    {
        var zmxPath = Path.Combine(Path.GetTempPath(), $"zmx-vars-{Guid.NewGuid():N}.zmx");
        var projectPath = Path.ChangeExtension(zmxPath, ".staropt");
        try
        {
            await File.WriteAllTextAsync(zmxPath, Source, unicode ? Encoding.Unicode : new UTF8Encoding(false));
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(zmxPath);
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            await app.Documents.SaveAsync(projectPath);
            await app.Documents.OpenAsync(projectPath);
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables());
            Assert.True(app.Documents.Undo());
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
        }
        finally
        {
            File.Delete(zmxPath);
            File.Delete(projectPath);
        }
    }

    [Fact]
    public async Task ConfigurationCellVariablesKeepTheirConfigurationAndOptimizeTogether()
    {
        var zmxPath = Path.Combine(Path.GetTempPath(), $"zmx-mce-vars-{Guid.NewGuid():N}.zmx");
        var projectPath = Path.ChangeExtension(zmxPath, ".staropt");
        try
        {
            var source = Source + """

                MNUM 2 1
                THIC 1 1 5 1
                THIC 1 2 5 1
                CRVT 1 1 0.02 0
                CRVT 1 2 0.02 1
                """;
            await File.WriteAllTextAsync(zmxPath, source);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(zmxPath);
            Assert.Equal(new[] { true, true }, app.MultiConfiguration.GetOperandRows()[0].Variables);
            Assert.Equal(new[] { false, true }, app.MultiConfiguration.GetOperandRows()[1].Variables);
            Assert.Equal(3, app.Optimization.GetMarkedVariables().Count);
            await app.Documents.SaveAsync(projectPath);
            await app.Documents.OpenAsync(projectPath);
            Assert.Equal(3, app.Optimization.GetMarkedVariables().Count);
            // Keep the imported thickness cells; remove the curvature cell for this scalar objective.
            app.MultiConfiguration.SetOperandVariable(2, 1, false, app.Events.Revision);
            var document = await WorkbenchRuntime.ReadDocumentAsync(projectPath);
            var runtime = new WorkbenchRuntime(document.ActiveOptic);
            runtime.ApplyLoadedDocument(document, projectPath);
            runtime.SetMultiConfigurationOperandVariable(2, 1, false);
            runtime.ReplaceMeritFunction([
                new() { Type = "MCOV", ZemaxIntegerParameters = [1, 1], Target = 6, Weight = 1 },
                new() { Type = "MCOV", ZemaxIntegerParameters = [1, 2], Target = 7, Weight = 1 }
            ]);
            Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 40).FinalMerit < 1e-8);
            var context = runtime.CreateMeritConfigurationContext();
            Assert.Equal(6, context.Configurations[0].SurfaceGroup.Items[1].Thickness, 4);
            Assert.Equal(7, context.Configurations[1].SurfaceGroup.Items[1].Thickness, 4);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables());
            Assert.True(app.Documents.Undo());
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
        }
        finally
        {
            File.Delete(zmxPath);
            File.Delete(projectPath);
        }
    }

    private const string Source = """
        MODE SEQ
        UNIT MM
        ENPD 2
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          TYPE STANDARD
          CURV 0.02 1 0 0 0
          DISZ 5
          VDSZ 0 0
          DIAM 2 1
        SURF 2
          TYPE STANDARD
          CURV -0.02 0 0 0 0
          DISZ 20
          DIAM 2 1
        SURF 3
          DISZ 0
          DIAM 2 1
        """;
}
