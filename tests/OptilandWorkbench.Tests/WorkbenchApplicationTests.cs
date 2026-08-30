using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class WorkbenchApplicationTests
{
    [Fact]
    public void StarOptExtensionHasItsOwnDocumentRoute()
    {
        Assert.True(OptilandConnector.IsStarOptProjectPath("design.STAROPT"));
        Assert.False(OptilandConnector.IsNativeJsonPath("design.staropt"));
        Assert.Equal("staropt-project", OptilandConnector.FormatNameForPath("design.staropt"));
    }

    [Fact]
    public void SemiDiameterIsAutomaticUntilExplicitlyFixed()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces()[1];

        Assert.False(surface.SemiDiameterFixed);

        application.Prescription.UpdateSurface(surface with
        {
            SemiDiameter = 123.456,
            SemiDiameterFixed = false
        });

        var automatic = application.Prescription.GetSurfaces()[1];
        Assert.False(automatic.SemiDiameterFixed);
        Assert.NotEqual(123.456, automatic.SemiDiameter, precision: 6);

        application.Prescription.UpdateSurface(automatic with
        {
            SemiDiameter = 12.345,
            SemiDiameterFixed = true
        });
        var field = application.Prescription.GetFields()[0];
        application.Prescription.UpdateField(field with { Y = field.Y + 1 });

        var fixedSurface = application.Prescription.GetSurfaces()[1];
        Assert.True(fixedSurface.SemiDiameterFixed);
        Assert.Equal(12.345, fixedSurface.SemiDiameter, precision: 12);
    }

    [Fact]
    public void PrescriptionServiceEditsCurrentGlassCatalogsWithUndoSupport()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var original = application.Prescription.GetGlassCatalogs();

        application.Prescription.UpdateGlassCatalogs(new[] { "CDGM", "SCHOTT" });

        Assert.Equal(
            new[] { "CDGM", "SCHOTT" },
            application.Prescription.GetGlassCatalogs());
        Assert.True(application.Documents.Undo());
        Assert.Equal(original, application.Prescription.GetGlassCatalogs());
    }

    [Fact]
    public void SemiDiameterFixedStateRoundTripsThroughSnapshot()
    {
        var optic = OptilandWorkbench.Core.Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].SemiDiameterFixed = true;
        optic.SurfaceGroup.Items[1].SemiDiameter = 8.75;

        var restored = OptilandWorkbench.Core.Optic.FromSnapshot(optic.ToSnapshot());

        Assert.True(restored.SurfaceGroup.Items[1].SemiDiameterFixed);
        Assert.Equal(8.75, restored.SurfaceGroup.Items[1].SemiDiameter, precision: 12);
    }

    [Fact]
    public void SnapshotPreservesAutomaticSemiDiametersWithoutCreatingPhysicalApertures()
    {
        var optic = OptilandWorkbench.Core.Optic.CreateBlank();
        optic.Fields.Add(new FieldPoint
        {
            Label = "14 deg",
            YAngleDegrees = 14,
            Weight = 1
        });
        optic.SurfaceGroup.Items[^1].SemiDiameter = 1;

        var restored = OptilandWorkbench.Core.Optic.FromSnapshot(optic.ToSnapshot());

        Assert.All(restored.SurfaceGroup.Items, surface => Assert.Null(surface.PhysicalAperture));
        var evaluations = MeritFunctionCatalog.CreateDefaultRmsSpot(restored)
            .Where(operand => operand.Field == 2 && operand.Type is "TRCX" or "TRCY")
            .Select(operand => MeritFunctionCatalog.Evaluate(restored, operand))
            .ToArray();
        Assert.NotEmpty(evaluations);
        Assert.All(evaluations, evaluation =>
        {
            Assert.True(double.IsFinite(evaluation.Value));
            Assert.True(string.IsNullOrEmpty(evaluation.Error));
        });
    }

    [Fact]
    public void WorkspaceEventsDoNotCaptureBackgroundComputationCancellation()
    {
        using var application = WorkbenchApplication.Create("cooke");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var observed = new CancellationToken(canceled: true);
        application.Events.Changed += (_, _) => observed = ComputationCancellation.Current;
        var surface = application.Prescription.GetSurfaces()[1];

        using (ComputationCancellation.Push(cancellation.Token))
        {
            application.Prescription.UpdateSurface(surface with { Label = "Cancellation boundary" });
        }

        Assert.False(observed.CanBeCanceled);
        Assert.False(observed.IsCancellationRequested);
    }

    [Fact]
    public void MeritFunctionQueryIgnoresAmbientCanceledComputationToken()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        IReadOnlyList<MeritOperandRowDto> operands;
        using (ComputationCancellation.Push(cancellation.Token))
        {
            operands = application.Optimization.GetMeritFunction();
        }

        Assert.Contains(operands, operand => operand.Type == "TRCX" && operand.Enabled);
    }

    [Fact]
    public void ImageSurfaceIgnoresThicknessEdits()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var image = application.Prescription.GetSurfaces().Last();

        application.Prescription.UpdateSurface(image with
        {
            Thickness = image.Thickness + 10,
            ThicknessVariable = true
        });

        var restored = application.Prescription.GetSurfaces().Last();
        Assert.Equal(image.Thickness, restored.Thickness, precision: 12);
        Assert.False(restored.ThicknessVariable);
    }

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
        Assert.Equal("Coordinate Pattern Search", result.Optimizer);
        Assert.Equal("coordinate-pattern-search/1", result.AlgorithmVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.StopReason));
        Assert.True(result.FunctionEvaluations > 0);
        Assert.Contains("兼容名称", result.Message, StringComparison.Ordinal);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Warnings));
    }

    [Fact]
    public void BulkOptimizationVariableCommandsMarkAndClearInternalSurfaces()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var internalSurfaceCount = application.Prescription.GetSurfaces().Count - 2;

        var radii = application.Optimization.UpdateAllSurfaceVariables(
            OptimizationVariableUpdateMode.SetAllRadii);
        var thicknesses = application.Optimization.UpdateAllSurfaceVariables(
            OptimizationVariableUpdateMode.SetAllThicknesses);
        var marked = application.Prescription.GetSurfaces()
            .Where(surface => surface.Number > 0)
            .SkipLast(1)
            .ToArray();

        Assert.Equal(internalSurfaceCount, radii.RadiusVariableCount);
        Assert.Equal(internalSurfaceCount, thicknesses.RadiusVariableCount);
        Assert.Equal(internalSurfaceCount, thicknesses.ThicknessVariableCount);
        Assert.All(marked, surface =>
        {
            Assert.True(surface.RadiusVariable);
            Assert.True(surface.ThicknessVariable);
        });

        var cleared = application.Optimization.UpdateAllSurfaceVariables(
            OptimizationVariableUpdateMode.ClearAll);

        Assert.Equal(0, cleared.RadiusVariableCount);
        Assert.Equal(0, cleared.ThicknessVariableCount);
        Assert.All(
            application.Prescription.GetSurfaces(),
            surface =>
            {
                Assert.False(surface.RadiusVariable);
                Assert.False(surface.ThicknessVariable);
            });
    }

    [Fact]
    public async Task QuickFocusUpdatesTheImageSpaceThickness()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var before = application.Prescription.GetSurfaces()[^2];

        var result = await application.Optimization.QuickFocusAsync();
        var after = application.Prescription.GetSurfaces()
            .Single(surface => surface.Number == result.SurfaceNumber);

        Assert.Equal(before.Number, result.SurfaceNumber);
        Assert.Equal(before.Thickness, result.InitialThickness, precision: 12);
        Assert.Equal(result.FinalThickness, after.Thickness, precision: 12);
        Assert.Equal(
            result.InitialThickness + result.AppliedShift,
            result.FinalThickness,
            precision: 12);
        Assert.True(double.IsFinite(result.RmsSpotRadius));
    }

    [Fact]
    public void OptimizerCatalogRejectsNamesWhoseAlgorithmsAreNotImplemented()
    {
        var unimplementedNames = new[]
        {
            "Powell",
            "COBYLA",
            "BFGS",
            "L-BFGS-B",
            "Differential Evolution",
            "Dual Annealing",
            "Basin Hopping"
        };

        foreach (var name in unimplementedNames)
        {
            var error = Assert.Throws<NotSupportedException>(() => OptimizerCatalog.Create(name));
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GeneratedRayOperandMeritOptimizesMarkedVariable()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces().First(item => item.Number == 1);
        application.Prescription.UpdateSurface(surface with { RadiusVariable = true });
        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);

        var result = await application.Optimization.OptimizeVariablesAsync(
            "Orthogonal Descent",
            maxIterations: 1);

        Assert.Single(result.Variables);
        Assert.True(double.IsFinite(result.InitialMerit));
        Assert.True(double.IsFinite(result.FinalMerit));
    }

    [Fact]
    public async Task LeastSquaresOptimizesMultipleVariablesWithIndependentOpticSnapshots()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces();
        var first = surfaces.First(item => item.Number == 1);
        var second = surfaces.First(item => item.Number == 2);
        application.Prescription.UpdateSurface(first with
        {
            Radius = first.Radius + 8,
            RadiusVariable = true
        });
        application.Prescription.UpdateSurface(second with { ThicknessVariable = true });
        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);

        var result = await application.Optimization.OptimizeVariablesAsync(
            "Least Squares",
            maxIterations: 8);

        Assert.Equal(2, result.Variables.Count);
        Assert.True(double.IsFinite(result.FinalMerit));
        Assert.True(result.FinalMerit < result.InitialMerit);
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
    public void MeritFunctionEditorPreservesNegativeConstraintWeight()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Optimization.SetMeritFunction(new[]
        {
            new MeritOperandRowDto(
                1, true, "RADI", 1, 0, 0, 0, 0, 0, 0,
                25, -10, 0, 0, "精确半径约束")
        });

        var restored = Assert.Single(application.Optimization.GetMeritFunction());

        Assert.Equal(-10, restored.Weight);
    }

    [Fact]
    public void MeritFunctionEditorPreservesReadOnlyZemaxParameterSlotsWithoutClamping()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Optimization.SetMeritFunction(new[]
        {
            new MeritOperandRowDto(
                1, true, "TTHI", 0, 0, 15, 195, -12, 6, -8,
                40, 0.02, 0, 0, "Zemax 只读记录")
        });

        var restored = Assert.Single(application.Optimization.GetMeritFunction());

        Assert.False(restored.Enabled);
        Assert.Equal(15, restored.Wavelength);
        Assert.Equal(195, restored.Hx);
        Assert.Equal(-12, restored.Hy);
        Assert.Equal(6, restored.Px);
        Assert.Equal(-8, restored.Py);
        Assert.Equal(40, restored.Target);
        Assert.Equal(0.02, restored.Weight);
    }

    [Fact]
    public void DefaultMeritFunctionCanGenerateSpotAndWavefrontOperands()
    {
        using var application = WorkbenchApplication.Create("cooke");

        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);
        var spot = application.Optimization.GetMeritFunction();
        Assert.Contains(spot, operand => operand.Type == "TRCX" && operand.Enabled);
        Assert.Contains(spot, operand => operand.Type == "TRCY" && operand.Enabled);

        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsWavefront);
        var wavefront = application.Optimization.GetMeritFunction();
        Assert.Contains(wavefront, operand => operand.Type == "OPDX" && operand.Enabled);
        Assert.Contains(wavefront, operand => operand.Type == "BLNK" && !operand.Enabled);
    }

    [Fact]
    public void DefaultWavefrontMeritFunctionUsesZemaxGaussianQuadratureRows()
    {
        using var application = WorkbenchApplication.Create("cooke");

        application.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsWavefront);
        var operands = application.Optimization.GetMeritFunction();
        var firstField = operands
            .SkipWhile(operand => operand.Type != "OPDX")
            .Take(3)
            .ToArray();

        Assert.Equal(0.3357106870197288, firstField[0].Px, precision: 12);
        Assert.Equal(0.7071067811865476, firstField[1].Px, precision: 12);
        Assert.Equal(0.9419651451198934, firstField[2].Px, precision: 12);
        Assert.All(firstField, operand => Assert.Equal(0, operand.Py, precision: 12));
        Assert.Equal(1.0 / 9.0, firstField.Sum(operand => operand.Weight), precision: 12);
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
        var sampledOperands = operands.Where(operand => operand.Type is "TRCX" or "TRCY").ToArray();
        Assert.Equal(application.Prescription.GetFields().Count * 48 * 2, sampledOperands.Length);
        Assert.Equal(3 + application.Prescription.GetFields().Count + sampledOperands.Length + 2, operands.Count);
        Assert.Equal("DMFS", operands[0].Type);
        var sampled = Assert.Single(operands.Where(operand => operand.Type == "TRCX").Take(1));
        Assert.Equal(4, sampled.PupilRings);
        Assert.Equal(8, sampled.PupilArms);
        Assert.Equal(0.2, sampled.PupilObscuration, precision: 12);
        Assert.Equal("uniform", sampled.PupilSampling);
        Assert.Contains(operands, operand => operand.Type == "EFFL");
        Assert.Contains(operands, operand => operand.Type == "FNUM");
        Assert.Equal(WorkspaceChangeCategory.Optimization, Assert.Single(events).Category);
    }

    [Fact]
    public void MeritFunctionRoundTripPreservesWizardSpecificParameters()
    {
        using var application = WorkbenchApplication.Create("cooke");
        application.Optimization.GenerateMeritFunction(new OptimizationWizardSettingsDto(
            OptimizationImageQuality.Contrast,
            OptimizationPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            StartRow: 1,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false,
            ReplaceExisting: true,
            SpatialFrequency: 42,
            IgnoreLateralColor: true));
        var generated = application.Optimization.GetMeritFunction();

        application.Optimization.SetMeritFunction(generated);
        var restored = application.Optimization.GetMeritFunction();
        var contrast = restored.First(operand => operand.Type is "MECS" or "MECT");

        Assert.Equal(42, contrast.SpatialFrequency, precision: 12);
        Assert.True(contrast.IgnoreLateralColor);
        Assert.False(contrast.PolychromaticReference);
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
        Assert.Equal("First Order", result.CanonicalAnalysisKey);
        Assert.Equal(64, result.RequestFingerprint.Length);
        Assert.Equal("Application.WorkbenchRuntime.BuildAnalysisView/v1", result.ExecutorId);
        Assert.NotEmpty(result.View.Rows);
    }

    [Fact]
    public async Task EquivalentAnalysisRequestsHaveTheSameNormalizedFingerprint()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var defaults = application.Analyses.MergeSettings("First Order", null);
        var withIgnoredInput = new Dictionary<string, string>
        {
            ["NotAParameter"] = "does not affect execution"
        };

        var implicitDefaults = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "First Order",
            withIgnoredInput));
        var explicitDefaults = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "First Order",
            defaults));

        Assert.Equal(explicitDefaults.RequestFingerprint, implicitDefaults.RequestFingerprint);
    }

    [Fact]
    public async Task AnalysisResultCarriesStablePresentationKindForLocalizedAlias()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var settings = application.Analyses.MergeSettings("波前图", null);

        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "波前图",
            settings));

        Assert.Equal(AnalysisPresentationKind.WavefrontMap, result.View.PresentationKind);
    }

    [Fact]
    public async Task SpotDiagramResultCarriesPerFieldRadiusMetricsToTheUi()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var settings = application.Analyses.MergeSettings("Spot Diagram", null);

        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "Spot Diagram",
            settings));

        Assert.Equal(application.Prescription.GetFields().Count, result.View.PlotPanes.Count);
        Assert.All(result.View.PlotPanes, pane =>
        {
            Assert.Collection(
                pane.Metrics!,
                metric => Assert.Equal("RMS 半径", metric.Label),
                metric => Assert.Equal("GEO 半径", metric.Label));
            Assert.Contains("参考", pane.Footer);
        });
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
        Assert.NotEmpty(scene.TwoDimensional.Rays);
        Assert.All(scene.TwoDimensional.Rays, ray =>
        {
            Assert.Equal(ray.Points.Count - 1, ray.Segments.Count);
            Assert.Equal(SceneRaySegmentType.Incident, ray.Segments[0].SegmentType);
            Assert.All(ray.Segments.Skip(1), segment =>
                Assert.NotEqual(SceneRaySegmentType.Unspecified, segment.SegmentType));
        });
    }

    [Fact]
    public async Task ThreeDimensionalVisualizationPreservesCurvedSurfaceMesh()
    {
        using var application = WorkbenchApplication.Create("tessar");

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);

        var threeDimensional = Assert.IsType<Scene3Dto>(scene.ThreeDimensional);
        Assert.NotEmpty(threeDimensional.LensElements);
        Assert.NotEmpty(threeDimensional.Rays);
        Assert.All(threeDimensional.Rays, ray =>
        {
            Assert.Equal(ray.Points.Count - 1, ray.Segments.Count);
            Assert.All(ray.Segments, segment =>
            {
                Assert.True(double.IsFinite(segment.Direction.X));
                Assert.True(double.IsFinite(segment.Direction.Y));
                Assert.True(double.IsFinite(segment.Direction.Z));
            });
        });
        Assert.All(
            threeDimensional.LensElements,
            element => Assert.InRange(element.RefractiveIndex, 1.0001, 2.5));
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
        Assert.Equal(
            application.Prescription.GetWavelengths().Select(wavelength => wavelength.Nanometers),
            twoDimensional.Rays
                .GroupBy(ray => ray.WavelengthIndex)
                .OrderBy(group => group.Key)
                .Select(group => group.Select(ray => ray.WavelengthNanometers).Distinct().Single()));
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

    [Fact]
    public async Task StarOptSaveAndOpenPreservesAllConfigurationsAndActiveSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"project-{Guid.NewGuid():N}.staropt");
        try
        {
            using (var source = WorkbenchApplication.Create("cooke"))
            {
                var alternate = source.MultiConfiguration.Add();
                source.MultiConfiguration.SetThickness(alternate, 2, 77);
                source.MultiConfiguration.Activate(alternate);
                await source.Documents.SaveAsync(path);
            }

            using var restored = WorkbenchApplication.Create("blank");
            await restored.Documents.OpenAsync(path);

            Assert.Equal(Path.GetFullPath(path), restored.Documents.CurrentPath);
            var configurations = restored.MultiConfiguration.GetRows();
            Assert.Equal(2, configurations.Count);
            Assert.False(configurations[0].Active);
            Assert.True(configurations[1].Active);
            Assert.Equal(
                77,
                restored.Prescription.GetSurfaces().Single(surface => surface.Number == 2).Thickness,
                precision: 12);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task StarOptProjectIsRecognizedByContentAfterRename()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"project-{Guid.NewGuid():N}.staropt");
        var renamedPath = Path.ChangeExtension(projectPath, ".bin");
        try
        {
            string expectedName;
            using (var source = WorkbenchApplication.Create("tessar"))
            {
                expectedName = source.Documents.GetSnapshot().Name;
                await source.Documents.SaveAsync(projectPath);
            }

            File.Move(projectPath, renamedPath);
            using var restored = WorkbenchApplication.Create("blank");
            await restored.Documents.OpenAsync(renamedPath);

            Assert.Equal(expectedName, restored.Documents.GetSnapshot().Name);
        }
        finally
        {
            if (File.Exists(projectPath))
            {
                File.Delete(projectPath);
            }

            if (File.Exists(renamedPath))
            {
                File.Delete(renamedPath);
            }
        }
    }
}
