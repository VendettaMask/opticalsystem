using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

public sealed class WorkbenchApplicationTests
{
    [Fact]
    public void MaterialCatalogExposesGlassEditorDetails()
    {
        using var application = WorkbenchApplication.Create();

        var cdgm = Assert.Single(
            application.Materials.GetCatalogs(),
            catalog => catalog.Manufacturer == "CDGM");
        var baf7 = Assert.Single(
            application.Materials.GetGlasses(),
            glass => glass.Manufacturer == "CDGM" && glass.Name == "BAF7");

        Assert.Equal(275, cdgm.GlassCount);
        Assert.Equal("zemax formula 1", baf7.Formula);
        Assert.Equal(10, baf7.DispersionCoefficients.Count);
        Assert.Equal(2.5436416, baf7.DispersionCoefficients[0], precision: 10);
        Assert.Equal(0.365, baf7.MinimumWavelengthMicrometers, precision: 10);
        Assert.Equal(0.7065, baf7.MaximumWavelengthMicrometers, precision: 10);
        Assert.True(baf7.ExtinctionSampleCount > 0);
    }

    [Fact]
    public async Task ZemaxCatalogImportPersistsOwnFormatAndExposesGlass()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"optiland-glass-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(directory, "CODEXAPP.AGF");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(sourcePath, """
            CC Application import test
            NM H-ZLAF96 1 900000 1.900000 30.000000 0 0 0
            GC Imported from Zemax
            ED 8.3 0.2 3.56 0.0001 0
            CD 3.61 -0.02 0.03 0.001 -0.0001 0.00001
            TD 1e-6 2e-8 3e-10 4e-7 5e-9 0.2 20
            LD 0.365 2.5
            """);

        try
        {
            using var application = WorkbenchApplication.Create(userCatalogDirectory: directory);
            var result = await application.Materials.ImportZemaxCatalogAsync(sourcePath);
            var glass = Assert.Single(
                application.Materials.GetGlasses(),
                item => item.Manufacturer == "CODEXAPP" && item.Name == "H-ZLAF96");

            Assert.Equal("CODEXAPP", result.CatalogName);
            Assert.Equal(1, result.GlassCount);
            Assert.Equal(".ogcat", Path.GetExtension(result.SavedPath));
            Assert.True(File.Exists(result.SavedPath));
            Assert.Equal(1, glass.ZemaxFormulaNumber);
            Assert.Equal("Imported from Zemax", glass.Comment);
            Assert.Equal(3.56, glass.Density);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SurfaceEditPublishesOneRevisionAndSupportsUndoRedo()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var events = new List<WorkspaceChangedEventArgs>();
        application.Events.Changed += (_, args) => events.Add(args);
        var original = application.Prescription.GetSurfaces()
            .First(surface => surface.Number > 0 && double.IsFinite(surface.Radius));
        var initialRevision = application.Events.Revision;

        application.Prescription.UpdateSurface(original with { Radius = original.Radius + 2.5 });

        Assert.Equal(initialRevision + 1, application.Events.Revision);
        var changed = Assert.Single(events);
        Assert.Equal(WorkspaceChangeCategory.Surface, changed.Category);
        Assert.Equal(
            original.Radius + 2.5,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);

        events.Clear();
        Assert.True(application.Documents.Undo());
        Assert.Equal(
            original.Radius,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);
        Assert.Single(events);

        events.Clear();
        Assert.True(application.Documents.Redo());
        Assert.Equal(
            original.Radius + 2.5,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);
        Assert.Single(events);
    }

    [Fact]
    public void EnvironmentSettingsUseDefaultsAndPublishSystemChange()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var events = new List<WorkspaceChangedEventArgs>();
        application.Events.Changed += (_, args) => events.Add(args);

        var defaults = application.Prescription.GetEnvironmentSettings();
        Assert.True(defaults.MatchRefractiveIndexData);
        Assert.Equal(20.0, defaults.TemperatureCelsius, precision: 12);
        Assert.Equal(1.0, defaults.PressureAtmospheres, precision: 12);

        application.Prescription.UpdateEnvironmentSettings(new EnvironmentSettingsDto(
            false,
            24.5,
            0.85));

        var changed = application.Prescription.GetEnvironmentSettings();
        Assert.False(changed.MatchRefractiveIndexData);
        Assert.Equal(24.5, changed.TemperatureCelsius, precision: 12);
        Assert.Equal(0.85, changed.PressureAtmospheres, precision: 12);
        Assert.Equal(WorkspaceChangeCategory.SystemSettings, Assert.Single(events).Category);
    }

    [Fact]
    public async Task MarkedLensVariablesAreOptimizedTogether()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces();
        var radiusSurface = surfaces.First(surface => surface.Number == 1);
        var thicknessSurface = surfaces.First(surface => surface.Number == 2);

        application.Prescription.UpdateSurface(radiusSurface with { RadiusVariable = true });
        application.Prescription.UpdateSurface(thicknessSurface with { ThicknessVariable = true });

        var result = await application.Optimization.OptimizeVariablesAsync(
            "Orthogonal Descent",
            maxIterations: 4);

        Assert.Equal(2, result.Variables.Count);
        Assert.Contains(result.Variables, variable =>
            variable.SurfaceNumber == 1 && variable.Kind == OptimizationVariableKind.Radius);
        Assert.Contains(result.Variables, variable =>
            variable.SurfaceNumber == 2 && variable.Kind == OptimizationVariableKind.Thickness);
        Assert.True(double.IsFinite(result.InitialMerit));
        Assert.True(double.IsFinite(result.FinalMerit));
        Assert.True(result.FinalMerit <= result.InitialMerit + 1e-12);
        Assert.All(result.Variables, variable => Assert.True(double.IsFinite(variable.FinalValue)));
    }

    [Fact]
    public async Task VariableOptimizationRequiresAtLeastOneMarkedValue()
    {
        using var application = WorkbenchApplication.Create("cooke");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.Optimization.OptimizeVariablesAsync("Orthogonal Descent", 1));

        Assert.Contains("优化变量", exception.Message);
    }

    [Fact]
    public async Task CustomMeritOperandDrivesMarkedVariableOptimization()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces().First(item => item.Number == 1);
        application.Prescription.UpdateSurface(surface with { RadiusVariable = true });
        var target = surface.Radius + Math.Max(5, Math.Abs(surface.Radius) * 0.2);
        application.Optimization.SetMeritFunction(new[]
        {
            new MeritOperandRowDto(
                1,
                true,
                "RADI",
                surface.Number,
                0,
                0,
                0,
                0,
                0,
                0,
                target,
                1,
                0,
                0,
                "半径目标")
        });

        var result = await application.Optimization.OptimizeVariablesAsync(
            "Orthogonal Descent",
            maxIterations: 12);
        var optimized = application.Prescription.GetSurfaces().Single(item => item.Number == 1);

        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.True(Math.Abs(optimized.Radius - target) < Math.Abs(surface.Radius - target));
        var evaluated = Assert.Single(application.Optimization.GetMeritFunction());
        Assert.Equal("RADI", evaluated.Type);
        Assert.True(evaluated.Contribution < result.InitialMerit);
    }

    [Fact]
    public void DefaultMeritFunctionCanGenerateSpotAndWavefrontOperands()
    {
        using var application = WorkbenchApplication.Create("cooke");

        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);
        var spot = application.Optimization.GetMeritFunction();
        Assert.Contains(spot, operand => operand.Type == "RSCE" && operand.Enabled);

        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsWavefront);
        var wavefront = application.Optimization.GetMeritFunction();
        Assert.Contains(wavefront, operand => operand.Type == "OPDX" && operand.Enabled);
        Assert.Contains(wavefront, operand => operand.Type == "BLNK" && !operand.Enabled);
    }

    [Fact]
    public void OptimizationWizardGeneratesSampledMeritFunction()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var events = new List<WorkspaceChangedEventArgs>();
        application.Events.Changed += (_, args) => events.Add(args);

        application.Optimization.GenerateMeritFunction(new OptimizationWizardSettingsDto(
            OptimizationImageQuality.RmsSpot,
            OptimizationPupilSampling.RectangularArray,
            PupilRings: 4,
            PupilArms: 8,
            PupilObscuration: 0.2,
            StartRow: 1,
            WeightScale: 2,
            UseAllWavelengths: false,
            IncludeCommonOperands: true,
            ReplaceExisting: true));

        var operands = application.Optimization.GetMeritFunction();
        Assert.Equal(1 + application.Prescription.GetFields().Count + 2, operands.Count);
        Assert.Equal("DMFS", operands[0].Type);
        var sampled = Assert.Single(operands.Where(operand => operand.Type == "RSCE").Take(1));
        Assert.Equal(4, sampled.PupilRings);
        Assert.Equal(8, sampled.PupilArms);
        Assert.Equal(0.2, sampled.PupilObscuration, precision: 12);
        Assert.Equal("uniform", sampled.PupilSampling);
        Assert.Contains(operands, operand => operand.Type == "EFFL");
        Assert.Contains(operands, operand => operand.Type == "FNUM");
        Assert.Equal(WorkspaceChangeCategory.Optimization, Assert.Single(events).Category);
    }

    [Fact]
    public async Task AnalysisResultCarriesRequestIdentityAndSourceRevision()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var instanceId = Guid.NewGuid();
        var sourceRevision = application.Events.Revision;
        var settings = application.Analyses.MergeSettings("First Order", null);

        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            instanceId,
            7,
            "First Order",
            settings));

        Assert.Equal(instanceId, result.InstanceId);
        Assert.Equal(7, result.Generation);
        Assert.Equal(sourceRevision, result.SourceRevision);
        Assert.NotEmpty(result.View.Rows);
    }

    [Fact]
    public async Task FileSwitchCancelsRunningHeavyAnalysis()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var settings = application.Analyses.MergeSettings("Encircled Energy", null);
        settings["NumRays"] = "200000";
        settings["NumPoints"] = "2048";
        var running = application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "Encircled Energy",
            settings));

        application.Documents.NewBlank();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    [Fact]
    public async Task FileSwitchCancelsRunningTolerancing()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces().First(item => item.Number > 1);
        var running = application.Tolerancing.RunAsync(new TolerancingRequestDto(
            surface.Number,
            0.1,
            0.05,
            10_000,
            1234,
            100));

        application.Documents.NewBlank();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    [Fact]
    public async Task VisualizationUsesImmutableRevisionSnapshot()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var expectedRevision = application.Events.Revision;

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional);

        Assert.Equal(expectedRevision, scene.SourceRevision);
        Assert.Equal(SceneDimension.TwoDimensional, scene.Dimension);
        Assert.NotNull(scene.TwoDimensional);
        Assert.Null(scene.ThreeDimensional);
        Assert.NotEmpty(scene.TwoDimensional!.Surfaces);
    }

    [Fact]
    public async Task ThreeDimensionalVisualizationPreservesCurvedSurfaceMesh()
    {
        using var application = WorkbenchApplication.Create("tessar");

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);

        var threeDimensional = Assert.IsType<Scene3Dto>(scene.ThreeDimensional);
        var curvedSurface = threeDimensional.Surfaces.Single(surface => surface.SurfaceNumber == 1);
        var points = curvedSurface.Faces.SelectMany(face => face.Points).ToArray();
        Assert.NotEmpty(points);
        Assert.True(points.Max(point => point.Z) - points.Min(point => point.Z) > 0.01);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.True(double.IsFinite(point.Z));
        });
    }

    [Fact]
    public async Task VisualizationRequestIncludesAllSelectedWavelengths()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var options = application.Visualization.GetVisualizationOptions();

        var scene = await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(
            SceneDimension.TwoDimensional,
            FirstSurface: 1,
            LastSurface: options.SurfaceNumbers.Max(),
            FieldIndex: 0,
            IncludeAllWavelengths: true,
            RayCount: 3,
            LowerPupil: -1,
            UpperPupil: 1));

        var twoDimensional = Assert.IsType<Scene2Dto>(scene.TwoDimensional);
        Assert.Equal(
            options.Wavelengths.Select(wavelength => wavelength.Index),
            twoDimensional.Rays.Select(ray => ray.WavelengthIndex).Distinct().Order());
        Assert.Equal(options.Wavelengths.Count * 3, twoDimensional.Rays.Count);
    }

    [Fact]
    public async Task FailedOpenKeepsCurrentDocumentAndPath()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var original = application.Documents.GetSnapshot();
        var path = Path.Combine(Path.GetTempPath(), $"invalid-optic-{Guid.NewGuid():N}.optiland.json");
        try
        {
            await File.WriteAllTextAsync(path, "not-json");

            await Assert.ThrowsAnyAsync<Exception>(() => application.Documents.OpenAsync(path));

            var current = application.Documents.GetSnapshot();
            Assert.Null(application.Documents.CurrentPath);
            Assert.Equal(original.Name, current.Name);
            Assert.Equal(original.SurfaceCount, current.SurfaceCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
