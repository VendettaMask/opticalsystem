using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Visualization;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LabDesktopCollection { public const string Name = "Initial structure desktop"; }

[Collection(LabDesktopCollection.Name)]
public sealed class FlatStartDesktopTests
{
    [Theory]
    [InlineData(1240)]
    [InlineData(480)]
    public async Task OrganizedSettingsKeepEveryGroupReachableWithoutChangingInputs(int width)
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(width: width, defaultInputs: true);
            window.Show(); await window.Ready;
            Assert.True(Find<Border>(window, "CandidateEmpty").IsVisible);
            Assert.True(Find<Border>(window, "PreviewEmpty").IsVisible);
            Assert.False(Find<Expander>(window, "OfficialExampleSource").IsExpanded);
            Assert.Contains("accent", Find<Button>(window, "Generate").Classes);
            Assert.DoesNotContain("accent", Find<Button>(window, "Export").Classes);
            Capture(window, $"organized-system-{width}");
            var tabs = Find<TabControl>(window, "InputTabs");
            var groups = new (int Tab, string Id, string[] Inputs)[]
            {
                (1, "Advanced", ["MinimumCenter", "MinimumAir", "MinimumBack", "MinimumEdge", "FixedBack", "FixedBackValue"]),
                (1, "MaterialSettings", ["AllowedGlasses", "InitialGlass", "Catalogs"]),
                (2, "AutoLensSettings", ["AutoLensAsphere", "AutoLensMaterials", "AutoLensCemented", "AutoLensCurriculum", "AutoLensRefinement"]),
                (2, "RefinementSettings", ["RefinementBudget"])
            };
            foreach (var group in groups)
            {
                tabs.SelectedIndex = group.Tab;
                var disclosure = Find<Expander>(window, group.Id);
                Assert.False(disclosure.IsExpanded);
                disclosure.IsExpanded = true;
                window.UpdateLayout();
                foreach (var name in group.Inputs)
                {
                    var control = Find<Control>(window, name);
                    control.BringIntoView(); window.UpdateLayout();
                    Assert.True(control.IsEffectivelyVisible, name);
                    Assert.True(control.Bounds.Width > 50, name);
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                    Assert.InRange(point.X, 0, window.Bounds.Width);
                    Assert.InRange(point.Y, 0, window.Bounds.Height);
                }
                disclosure.IsExpanded = false;
            }
            tabs.SelectedIndex = 1;
            var fixedBack = Find<CheckBox>(window, "FixedBack");
            var fixedValue = Find<NumericUpDown>(window, "FixedBackValue");
            Assert.False(fixedValue.IsEnabled);
            Assert.NotNull(ToolTip.GetTip(fixedValue));
            fixedBack.IsChecked = true;
            Assert.True(fixedValue.IsEnabled);
            fixedBack.IsChecked = false;
            Assert.False(fixedValue.IsEnabled);
            Assert.Equal(5m, Find<NumericUpDown>(window, "FocalLength").Value);
            Assert.Equal(1.8m, Find<NumericUpDown>(window, "FNumber").Value);
            Assert.Equal(.4m, Find<NumericUpDown>(window, "MinimumCenter").Value);
            Assert.Null(window.CurrentCheckpoint);
            // Expanding/collapsing the reorganized sections must not alter the specification.
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("预检查通过", Find<TextBlock>(window, "Status").Text);
            window.Close();
        });
    }

    [Fact]
    public async Task OrganizedPresentationLocalizesAndTracksThemeResources()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var app = global::Avalonia.Application.Current!;
            var originalTheme = ThemeRegistry.FromActualVariant(app.ActualThemeVariant).SettingsValue;
            var originalLanguage = UiLocalization.CurrentLanguage;
            var window = host.Window(defaultInputs: true);
            try
            {
                ThemeApplicationService.Apply(app, "Light"); UiLocalization.Apply(UiLocalization.Chinese);
                window.Show(); await window.Ready;
                var tabs = Find<TabControl>(window, "InputTabs");
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language);
                    tabs.SelectedIndex = 0; window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("开始形式搜索"), Find<Button>(window, "Generate").Content);
                    Assert.Equal(UiLocalization.Text("AutoLens · C# 自动求导"), Find<TextBlock>(window, "OptimizationMethod").Text);
                    Assert.DoesNotContain(window.GetLogicalDescendants().OfType<ComboBox>(), control => control.Name == "OptimizationMethod");
                    Assert.Equal(UiLocalization.Text("目标"), Assert.IsType<TextBlock>(((TabItem)tabs.Items[1]!).Header).Text);
                    foreach (var item in tabs.Items.Cast<TabItem>().Concat(Find<TabControl>(window, "DetailsTabs").Items.Cast<TabItem>()))
                    {
                        var label = Assert.IsType<TextBlock>(item.Header);
                        var measured = new Avalonia.Media.FormattedText(label.Text!, System.Globalization.CultureInfo.CurrentUICulture,
                            Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface(label.FontFamily, label.FontStyle, label.FontWeight),
                            label.FontSize, Avalonia.Media.Brushes.Black);
                        Assert.True(label.Bounds.Width >= measured.Width - 1,
                            $"{language}: {label.Text}; label={label.Bounds}; desired={label.DesiredSize}; tab={item.Bounds}; tab desired={item.DesiredSize}; natural={measured.Width}");
                    }
                    Capture(window, "organized-" + language);
                    tabs.SelectedIndex = 1; window.UpdateLayout();
                    Capture(window, "organized-goals-" + language);
                    tabs.SelectedIndex = 2; window.UpdateLayout();
                    Capture(window, "organized-search-" + language);
                }
                UiLocalization.Apply(UiLocalization.Chinese);
                tabs.SelectedIndex = 0;
                ThemeApplicationService.Apply(app, "Dark"); window.UpdateLayout();
                var root = Find<Grid>(window, "InitialStructureDesign");
                Assert.True(root.TryFindResource(ThemeResourceBindings.Workspace, root.ActualThemeVariant, out var brush));
                Assert.Same(brush, root.Background);
                Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, root.ActualThemeVariant);
                Capture(window, "organized-dark");
                Assert.Equal(5m, Find<NumericUpDown>(window, "FocalLength").Value);
                Assert.Equal(1.8m, Find<NumericUpDown>(window, "FNumber").Value);
                Assert.Null(window.CurrentCheckpoint);
            }
            finally
            {
                window.Close();
                ThemeApplicationService.Apply(app, originalTheme);
                UiLocalization.Apply(originalLanguage);
            }
        });
    }

    [Fact]
    public async Task OfficialExamplePresetsLoadWithoutSearchingAndClearStaleResults()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window();
            window.Show(); await window.Ready;
            Click(window, "Generate"); await window.PendingOperation;
            Assert.NotNull(window.CurrentCheckpoint);
            var selector = Find<ComboBox>(window, "OfficialExample");
            var presets = new[] { AutoLensReferenceExamples.Mobile() }.Concat(AutoLensOfficialExamples.Create()).ToArray();
            Assert.Equal(presets.Length, selector.ItemCount);
            for (var index = 0; index < presets.Length; index++)
            {
                Find<TabControl>(window, "InputTabs").SelectedIndex = 0;
                selector.SelectedIndex = index;
                Click(window, "LoadOfficialExample"); await window.PendingOperation;
                Assert.Null(window.CurrentCheckpoint);
                Assert.Empty(Find<DataGrid>(window, "Candidates").ItemsSource.Cast<CandidateRow>());
                Assert.False(Find<Button>(window, "Export").IsEnabled);
                Assert.Equal((decimal)presets[index].Specification.EffectiveFocalLengthMillimeters, Find<NumericUpDown>(window, "FocalLength").Value);
                Assert.Equal((decimal)presets[index].Specification.FNumber, Find<NumericUpDown>(window, "FNumber").Value);
                Assert.Equal((decimal)presets[index].Specification.MaximumFieldAngleDegrees, Find<NumericUpDown>(window, "HalfField").Value);
                Assert.Equal(UiLocalization.Text("AutoLens · C# 自动求导"), Find<TextBlock>(window, "OptimizationMethod").Text);
                Assert.Equal(presets[index].Specification.FlatStart!.AutoLens!.EvenAsphereOrder - 1, Find<ComboBox>(window, "AutoLensAsphere").SelectedIndex);
                Assert.Equal((decimal)presets[index].Specification.Budget.MaximumEvaluations, Find<NumericUpDown>(window, "EvaluationBudget").Value);
                Assert.Contains("尚未运行", Find<TextBlock>(window, "Status").Text);
                Click(window, "Validate"); await window.PendingOperation;
                Assert.Contains("预检查通过", Find<TextBlock>(window, "Status").Text);
            }
            Capture(window, "autolens-official-presets");
            var originalLanguage = UiLocalization.CurrentLanguage;
            try
            {
                foreach (var language in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language);
                    window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text(presets[^1].Notes), Find<TextBlock>(window, "OfficialExampleNotes").Text);
                    Assert.Equal(UiLocalization.Text("载入预设"), Find<Button>(window, "LoadOfficialExample").Content);
                    Assert.Equal(4.55m, Find<NumericUpDown>(window, "FocalLength").Value);
                    Assert.Equal(60m, Find<NumericUpDown>(window, "HalfField").Value);
                    Capture(window, "autolens-official-presets-" + language);
                }
            }
            finally { UiLocalization.Apply(originalLanguage); }
            window.Close();
        });
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(FlatStartOptimizationMethod.TrustRegion)]
    [InlineData(FlatStartOptimizationMethod.AutoLensCpuExperimentV1)]
    [InlineData(FlatStartOptimizationMethod.AutoLensManagedV1)]
    public async Task LegacyRecordsRemainViewableButCannotResumeOrRefineAndNewSearchUsesAutoLens(FlatStartOptimizationMethod method)
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var specification = method == FlatStartOptimizationMethod.AutoLensManagedV1
                ? AutoLensManagedTests.Specification(400) : FlatStartRefinementTests.Spec(400);
            specification = specification with { FlatStart = specification.FlatStart! with
            {
                OptimizationMethod = method, SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                AutoLens = specification.FlatStart.AutoLens is { } autoLens ? autoLens with { TargetPolishEvaluations = 2 } : null
            } };
            using var cancellation = new CancellationTokenSource();
            var result = await new FlatStartSearchService().RunAsync(specification, FlatStartRefinementTests.Options, cancellation.Token,
                checkpointSink: (saved, _) =>
                {
                    if (saved.Trials.Any(trial => trial.State == FamilyTrialState.Completed)) cancellation.Cancel();
                    return ValueTask.CompletedTask;
                });
            var checkpoint = result.Checkpoint;
            Assert.InRange(checkpoint.ChargedEvaluations, 1, 399);
            Assert.NotEmpty(result.Candidates);
            if (method == FlatStartOptimizationMethod.AutoLensManagedV1)
                Assert.Contains(checkpoint.Trials, trial => trial.AutoLensState?.TargetPolishEvaluationCount > 0);
            var library = new FlatStartRunLibrary(host.Directory);
            await library.SaveAsync(checkpoint);
            var sourcePath = library.PathFor(checkpoint.RunId);
            var originalBytes = await File.ReadAllBytesAsync(sourcePath);
            File.Copy(sourcePath, host.OpenPath);
            var window = host.Window();
            window.Show(); await window.Ready;
            // Both the last-run shortcut and file picker must keep historical methods read-only.
            foreach (var command in new[] { "Resume", "OpenRun" })
            {
                Click(window, command); await window.PendingOperation;
                Assert.Equal(ContentFingerprint.Compute(checkpoint), ContentFingerprint.Compute(window.CurrentCheckpoint!));
                Assert.Equal(method, window.CurrentCheckpoint!.Specification.FlatStart!.OptimizationMethod);
                Assert.False(Find<Button>(window, "Resume").IsEnabled);
                Assert.False(Find<Button>(window, "Refine").IsEnabled);
                Assert.NotNull(ToolTip.GetTip(Find<Button>(window, "Refine")));
                Assert.True(Find<Button>(window, "Export").IsEnabled);
                Assert.Contains("旧算法记录", Find<TextBlock>(window, "Status").Text);
                // Event invocation must not bypass the execution guard on disabled commands.
                foreach (var blocked in new[] { "Resume", "Refine" })
                {
                    Find<Button>(window, blocked).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.PendingOperation;
                    Assert.Equal(ContentFingerprint.Compute(checkpoint), ContentFingerprint.Compute(window.CurrentCheckpoint!));
                }
                Click(window, "Export"); await window.PendingOperation;
                var exported = await StarOptProjectStore.LoadAsync(host.ExportPath);
                var row = Assert.IsType<CandidateRow>(Find<DataGrid>(window, "Candidates").SelectedItem);
                Assert.Equal(row.Candidate.OpticFingerprint, ContentFingerprint.Compute(exported.Configurations[0].ToSnapshot()));
            }
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
            Find<TabControl>(window, "InputTabs").SelectedIndex = 2;
            Find<TextBlock>(window, "OptimizationMethod").BringIntoView(); window.UpdateLayout();
            Capture(window, "autolens-only-legacy-" + method);
            Click(window, "VisibleSpectrum");
            Find<NumericUpDown>(window, "EvaluationBudget").Value = 1;
            Find<NumericUpDown>(window, "AutoLensCurriculum").Value = 1;
            Find<NumericUpDown>(window, "AutoLensRefinement").Value = 1;
            Click(window, "Generate"); await window.PendingOperation;
            Assert.NotEqual(checkpoint.RunId, window.CurrentCheckpoint!.RunId);
            Assert.Equal(FlatStartOptimizationMethod.AutoLensManagedV1, window.CurrentCheckpoint.Specification.FlatStart!.OptimizationMethod);
            Assert.True(window.CurrentCheckpoint.Specification.FlatStart.UsePhysicalStop);
            Assert.NotNull(window.CurrentCheckpoint.Specification.FlatStart.AutoLens);
            Assert.Equal(0, window.CurrentCheckpoint.Specification.FlatStart.AutoLens.TargetPolishEvaluations);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
            window.Close();
        });
    }

    [Fact]
    public async Task ManagedAutoLensControlsRoundTripAndTopologyChangesInvalidateDisplayedResults()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window();
            window.Show(); await window.Ready;
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<ComboBox>(), control => control.Name == "OptimizationMethod");
            Assert.Equal(0, Find<ComboBox>(window, "ComputeMode").SelectedIndex);
            Find<ComboBox>(window, "AutoLensAsphere").SelectedIndex = 1;
            Find<NumericUpDown>(window, "AutoLensCurriculum").Value = 1;
            Find<NumericUpDown>(window, "AutoLensRefinement").Value = 1;
            Find<TextBox>(window, "AutoLensCemented").Text = "1";
            Find<NumericUpDown>(window, "EvaluationBudget").Value = 8;
            Find<NumericUpDown>(window, "RootCount").Value = 1;
            Click(window, "Generate"); await window.PendingOperation;
            var checkpoint = Assert.IsType<FlatStartSearchCheckpoint>(window.CurrentCheckpoint);
            Assert.Equal(FlatStartOptimizationMethod.AutoLensManagedV1, checkpoint.Specification.FlatStart!.OptimizationMethod);
            Assert.Equal(new[] { 0 }, checkpoint.Specification.FlatStart.AutoLens!.CementedGaps);
            Assert.Equal(0, checkpoint.Specification.FlatStart.AutoLens.TargetPolishEvaluations);
            Assert.Equal(OpticalSystemDesign.Core.Differentiation.DesignComputeMode.Automatic, checkpoint.Specification.FlatStart.AutoLens.ComputeMode);
            Assert.All(checkpoint.Trials.Where(trial => trial.AutoLensState is not null), trial =>
            {
                Assert.Equal(0, trial.AutoLensState!.TargetPolishEvaluationCount);
                Assert.DoesNotContain(trial.Diagnostics, diagnostic => diagnostic.Code.StartsWith("design.autolens-native-target-polish", StringComparison.Ordinal));
            });
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<NumericUpDown>(), control => control.Name == "AutoLensPolish");
            Assert.Contains("autolens-upstream-e58-v2", checkpoint.Algorithm.Name);
            File.Copy(Path.Combine(host.Directory, checkpoint.RunId + ".family.json"), host.OpenPath);
            Find<TextBox>(window, "AutoLensCemented").Text = "";
            Assert.Null(window.CurrentCheckpoint);
            Click(window, "OpenRun"); await window.PendingOperation;
            Assert.Equal(UiLocalization.Text("AutoLens · C# 自动求导"), Find<TextBlock>(window, "OptimizationMethod").Text);
            Assert.Equal(1, Find<ComboBox>(window, "AutoLensAsphere").SelectedIndex);
            Assert.Equal("1", Find<TextBox>(window, "AutoLensCemented").Text);
            Assert.Equal(0, Find<ComboBox>(window, "ComputeMode").SelectedIndex);
            Find<TabControl>(window, "InputTabs").SelectedIndex = 2;
            Find<TextBlock>(window, "OptimizationMethod").BringIntoView(); window.UpdateLayout();
            Capture(window, "autolens-managed");
            Find<ComboBox>(window, "ComputeMode").SelectedIndex = 2;
            Assert.Null(window.CurrentCheckpoint);
            Assert.False(Find<Button>(window, "Export").IsEnabled);
            window.Close();
        });
    }

    [AutoLensCudaFact]
    public async Task GpuDeviceChoiceRunsActualCudaAndRoundTripsWithoutChangingTheAlgorithm()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window();
            window.Show(); await window.Ready;
            Assert.Contains("GPU", Find<TextBlock>(window, "ComputeStatus").Text);
            Find<ComboBox>(window, "ComputeMode").SelectedIndex = 1;
            Click(window, "Generate"); await window.PendingOperation;
            var checkpoint = Assert.IsType<FlatStartSearchCheckpoint>(window.CurrentCheckpoint);
            Assert.Equal(OpticalSystemDesign.Core.Differentiation.DesignComputeMode.Gpu, checkpoint.Specification.FlatStart!.AutoLens!.ComputeMode);
            Assert.Equal(FlatStartOptimizationMethod.AutoLensManagedV1, checkpoint.Specification.FlatStart.OptimizationMethod);
            Assert.Contains(checkpoint.Trials, trial => trial.AutoLensState?.GpuRays > 0);
            File.Copy(Path.Combine(host.Directory, checkpoint.RunId + ".family.json"), host.OpenPath);
            Find<ComboBox>(window, "ComputeMode").SelectedIndex = 2;
            Assert.Null(window.CurrentCheckpoint);
            Click(window, "OpenRun"); await window.PendingOperation;
            Assert.Equal(1, Find<ComboBox>(window, "ComputeMode").SelectedIndex);
            Assert.Contains(window.CurrentCheckpoint!.Trials, trial => trial.AutoLensState?.GpuBatches > 0);
            Find<TabControl>(window, "InputTabs").SelectedIndex = 2;
            Find<ComboBox>(window, "ComputeMode").BringIntoView(); window.UpdateLayout();
            Capture(window, "autolens-gpu-device");
            window.Close();
        });
    }

    [Fact]
    public async Task NewDesktopExperimentDefaultsToPhysicalStopAndAutomaticDiametersAndPolicyChangesClearResults()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window();
            window.Show(); await window.Ready;
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CheckBox>(), control => control.Name is "AutomaticDiameters" or "PhysicalStop");
            Find<NumericUpDown>(window, "AutoLensCurriculum").Value = 1;
            Find<NumericUpDown>(window, "AutoLensRefinement").Value = 1;
            Find<NumericUpDown>(window, "EvaluationBudget").Value = 16;
            Find<NumericUpDown>(window, "RootCount").Value = 1;
            Click(window, "Generate"); await window.PendingOperation;
            var checkpoint = window.CurrentCheckpoint!;
            Assert.True(checkpoint.Specification.FlatStart!.AutomaticLensDiameters);
            Assert.True(checkpoint.Specification.FlatStart.UsePhysicalStop);
            Assert.Equal(FlatStartSamplingPolicy.UniformAreaGaussianV1, checkpoint.Specification.FlatStart.SamplingPolicy);
            var row = Assert.IsType<CandidateRow>(Find<DataGrid>(window, "Candidates").SelectedItem);
            Assert.True(row.Candidate.Optic.RayAimingEnabled);
            Assert.Contains("实体光阑：", row.Details(checkpoint));
            Assert.Contains("含瞄准与诊断", row.Details(checkpoint));
            Assert.Contains("历史计数", row.Details(checkpoint with { Algorithm = checkpoint.Algorithm with { Version = "17" } }));
            var preview = Find<CandidatePreviewControl>(window, "Preview");
            await preview.PendingLoad;
            var expected = new Layout2DBuilder(Optic.FromSnapshot(row.Candidate.Optic)).Build(options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false));
            Assert.Equal(ContentFingerprint.Compute(OpticalSystemDesign.Application.Services.WorkbenchMapper.ToScene2Dto(expected)), ContentFingerprint.Compute(preview.PrimaryScene));
            Click(window, "Export"); await window.PendingOperation;
            var exported = await StarOptProjectStore.LoadAsync(host.ExportPath);
            Assert.Equal(row.Candidate.OpticFingerprint, ContentFingerprint.Compute(exported.Configurations[0].ToSnapshot()));
            Find<CheckBox>(window, "AutoLensMaterials").IsChecked = false;
            Assert.Null(window.CurrentCheckpoint);
            Assert.Null(preview.PrimaryScene);
            Assert.False(Find<Button>(window, "Export").IsEnabled);
            Assert.True(checkpoint.Specification.FlatStart.AutomaticLensDiameters);
            window.Close();
        });
    }

    [Theory]
    [InlineData(1240)]
    [InlineData(600)]
    [InlineData(480)]
    public async Task RealButtonsGenerateRenderInspectAndExportTheSameCandidate(int width)
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(width: width);
            window.Show(); await window.Ready;
            Assert.True(Find<Border>(window, "CandidateEmpty").IsVisible);
            Assert.True(Find<Border>(window, "PreviewEmpty").IsVisible);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CheckBox>(), control => control.Name == "PhysicalStop");
            Click(window, "Generate");
            Assert.False(Find<NumericUpDown>(window, "FocalLength").IsEffectivelyEnabled);
            await window.PendingOperation;
            Assert.NotNull(window.CurrentCheckpoint);
            Assert.True(window.CurrentCheckpoint!.TracedRealRayCount > 0);
            var grid = Find<DataGrid>(window, "Candidates");
            var row = Assert.IsType<CandidateRow>(grid.SelectedItem);
            var preview = Find<CandidatePreviewControl>(window, "Preview");
            await preview.PendingLoad;
            Assert.False(Find<Border>(window, "CandidateEmpty").IsVisible);
            Assert.False(Find<Border>(window, "PreviewEmpty").IsVisible);
            Assert.Null(preview.Error);
            Assert.Equal(row.Candidate.OpticFingerprint, preview.PrimaryFingerprint);
            Assert.True(preview.TryFindResource(global::OpticalSystemDesign.App.Services.ThemeResourceBindings.PlotAxis, preview.ActualThemeVariant, out var axisBrush));
            Assert.Same(axisBrush, preview.PlotAxisBrush);
            Assert.True(Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(preview.PlotAxisBrush).Color.A > 0);
            var expected = new Layout2DBuilder(Optic.FromSnapshot(row.Candidate.Optic)).Build(options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false));
            Assert.Equal(ContentFingerprint.Compute(OpticalSystemDesign.Application.Services.WorkbenchMapper.ToScene2Dto(expected)), ContentFingerprint.Compute(preview.PrimaryScene));
            Assert.Contains(preview.PrimaryScene!.Rays, ray => ray.Segments.Count > 0);
            if (width < 820) { preview.BringIntoView(); window.UpdateLayout(); }
            Capture(window, $"generated-{width}");
            Find<TabControl>(window, "DetailsTabs").SelectedIndex = 1;
            window.UpdateLayout();
            Assert.Equal(8, Find<DataGrid>(window, "TargetTable").ItemsSource.Cast<TargetRow>().Count());
            Capture(window, $"targets-{width}");
            Find<TabControl>(window, "DetailsTabs").SelectedIndex = 3;
            window.UpdateLayout();
            Assert.Equal(row.Trial.FinalValidation!.Fields.Count, Find<DataGrid>(window, "FieldTable").ItemsSource.Cast<FieldRow>().Count());
            Click(window, "Export"); await window.PendingOperation;
            var exported = await StarOptProjectStore.LoadAsync(host.ExportPath);
            Assert.Equal(row.Candidate.OpticFingerprint, ContentFingerprint.Compute(exported.Configurations[0].ToSnapshot()));
            Assert.Contains("回读验证", Find<TextBlock>(window, "Status").Text);
            window.Close();
        });
    }

    [Fact]
    public async Task ReferenceRandomStartPlanHistoryAndInvalidationUseActualWidgets()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(budget: 32); window.Show(); await window.Ready;
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), control => control.Name is "SearchMode" or "SignFilter" or "StartingRadius" or "SearchStop" or "FreeStop");
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("独立随机起点", Find<TextBlock>(window, "SearchPlan").Text);
            Click(window, "Generate"); await window.PendingOperation;
            var checkpoint = window.CurrentCheckpoint!;
            Assert.Null(checkpoint.Options.DesignSearch!.StartingRadiusMillimeters);
            Assert.Equal(DesignSearchMode.Full, checkpoint.Options.DesignSearch.Mode);
            Assert.Null(checkpoint.RootPlan[0].BinaryStart);
            Assert.All(checkpoint.Trials, trial => Assert.Equal(1, trial.Family.StopSurfaceIndex));
            Assert.Equal(checkpoint.Trials.Count, Find<DataGrid>(window, "SearchHistory").ItemsSource.Cast<SearchHistoryRow>().Count());
            Find<TabControl>(window, "DetailsTabs").SelectedIndex = 5;
            Find<TabControl>(window, "InputTabs").SelectedIndex = 2;
            Capture(window, "form-search-plan");
            Assert.Same(checkpoint, window.CurrentCheckpoint);
            Assert.NotEmpty(Find<DataGrid>(window, "Candidates").ItemsSource.Cast<CandidateRow>());
            Find<NumericUpDown>(window, "RandomSeed").Value = 43;
            Assert.Null(window.CurrentCheckpoint);
            Assert.Null(Find<DataGrid>(window, "SearchHistory").ItemsSource);
            window.Close();
        });
    }

    [Fact]
    public async Task InputValidationAndCatalogMessagesUseActualControls()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(); window.Show(); await window.Ready;
            Find<NumericUpDown>(window, "MinimumElements").Value = 4;
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("最少片数不能大于", Find<TextBlock>(window, "Status").Text);
            Find<NumericUpDown>(window, "MinimumElements").Value = 2.5m;
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("整数", Find<TextBlock>(window, "Status").Text);
            Find<NumericUpDown>(window, "MinimumElements").Value = 3;
            var fieldInput = Find<NumericUpDown>(window, "HalfField");
            fieldInput.BringIntoView(); window.UpdateLayout();
            var editor = fieldInput.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Focus(); editor.SelectAll(); window.KeyTextInput("5");
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal(5, fieldInput.Value);
            editor.Focus(); editor.SelectAll(); window.KeyTextInput("invalid");
            var editedText = $"editor={editor.Text}; numeric={fieldInput.Text}";
            Click(window, "Validate"); await window.PendingOperation;
            Assert.False(Find<TextBlock>(window, "Status").Text!.Contains("预检查通过", StringComparison.Ordinal), $"before {editedText}; after editor={editor.Text}, numeric={fieldInput.Text}");
            editor.Focus(); editor.SelectAll(); window.KeyTextInput("5");
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var waves = Find<DataGrid>(window, "Wavelengths").ItemsSource.Cast<WavelengthRow>().ToArray();
            var primary = waves.Single(wave => wave.IsPrimary);
            primary.IsPrimary = false;
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("主波长", Find<TextBlock>(window, "Status").Text);
            primary.IsPrimary = true;
            Find<TextBox>(window, "AllowedGlasses").Text = "MISSING-P4, N-BK7";
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("预检查通过", Find<TextBlock>(window, "Status").Text);
            Assert.Contains("MISSING-P4", Find<TextBlock>(window, "Messages").Text);
            Find<ComboBox>(window, "ApertureMode").SelectedIndex = 1;
            Find<NumericUpDown>(window, "EntrancePupil").Value = 6.25m;
            Click(window, "Generate"); await window.PendingOperation;
            Assert.Equal(8, window.CurrentCheckpoint!.Specification.FNumber);
            Assert.Equal(new[] { "N-BK7" }, window.CurrentCheckpoint.UsableGlassNames);
            Assert.Contains("MISSING-P4", Find<TextBlock>(window, "Messages").Text);
            Find<TextBox>(window, "AllowedGlasses").Text = "MISSING-P4";
            Assert.Null(window.CurrentCheckpoint);
            Click(window, "Generate"); await window.PendingOperation;
            Assert.Equal(FlatStartSearchState.NoUsableGlass, window.CurrentCheckpoint!.State);
            Assert.Empty(window.CurrentCheckpoint.Trials);
            Assert.Contains("没有可用", Find<TextBlock>(window, "Status").Text);
            window.Close();
        });
    }

    [Fact]
    public async Task StopThenExportAndResumeKeepsTheOriginalBudget()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(budget: 24); window.Show(); await window.Ready;
            Click(window, "Generate");
            await Until(() => window.CurrentCheckpoint?.Trials.Any(trial => trial.State == FamilyTrialState.Completed) == true);
            Click(window, "Cancel"); await window.PendingOperation;
            var stopped = window.CurrentCheckpoint!;
            Assert.Equal(FlatStartSearchState.Cancelled, stopped.State);
            Assert.InRange(stopped.ChargedEvaluations, 1, 23);
            Click(window, "Export"); await window.PendingOperation;
            Assert.True(File.Exists(host.ExportPath));
            Click(window, "Resume"); await window.PendingOperation;
            Assert.Equal(stopped.RunId, window.CurrentCheckpoint!.RunId);
            Assert.Equal(24, window.CurrentCheckpoint.Specification.Budget.MaximumEvaluations);
            Assert.InRange(window.CurrentCheckpoint.ChargedEvaluations, stopped.ChargedEvaluations, 24);
            Assert.Equal(stopped.Trials[0].Candidate!.CandidateId, window.CurrentCheckpoint.Trials[0].Candidate!.CandidateId);
            window.Close();
        });
    }

    [Fact]
    public async Task CloseFinishesSavingAndNextWindowLoadsWithoutStartingANewRun()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(budget: 24); window.Show(); await window.Ready;
            Click(window, "Generate");
            await Until(() => window.CurrentCheckpoint?.Trials.Any(trial => trial.State == FamilyTrialState.Completed) == true);
            window.Close(); await window.PendingOperation;
            await Until(() => !window.IsVisible);
            var saved = await new FlatStartRunLibrary(host.Directory).LoadLastAsync();
            Assert.NotNull(saved);
            Assert.Equal(FlatStartSearchState.Cancelled, saved!.State);
            Assert.DoesNotContain(saved.Trials, trial => trial.State == FamilyTrialState.Reserved);
            var reopened = host.Window(); reopened.Show(); await reopened.Ready;
            Click(reopened, "Resume"); await reopened.PendingOperation;
            Assert.Equal(saved.RunId, reopened.CurrentCheckpoint!.RunId);
            Assert.Equal(saved.ChargedEvaluations, reopened.CurrentCheckpoint.ChargedEvaluations);
            Assert.NotNull(Find<DataGrid>(reopened, "Candidates").SelectedItem);
            reopened.Close();
        });
    }

    [Fact]
    public async Task SelectedRefinementStartsASeparateRunAndDoesNotRewriteSource()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var window = host.Window(); window.Show(); await window.Ready;
            Click(window, "Generate"); await window.PendingOperation;
            var source = window.CurrentCheckpoint!;
            var selected = Assert.IsType<CandidateRow>(Find<DataGrid>(window, "Candidates").SelectedItem).Candidate;
            var sourcePath = new FlatStartRunLibrary(host.Directory).PathFor(source.RunId);
            var bytes = await File.ReadAllBytesAsync(sourcePath);
            Find<NumericUpDown>(window, "RefinementBudget").Value = 16;
            Click(window, "Refine"); await window.PendingOperation;
            Assert.NotEqual(source.RunId, window.CurrentCheckpoint!.RunId);
            Assert.Equal(16, window.CurrentCheckpoint.Specification.Budget.MaximumEvaluations);
            Assert.InRange(window.CurrentCheckpoint.ChargedEvaluations, 1, 16);
            Assert.Equal(selected.CandidateId, window.CurrentCheckpoint.Origin!.Source.Candidate!.CandidateId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath));
            Assert.NotNull(Find<DataGrid>(window, "Candidates").SelectedItem);
            window.Close();
        });
    }

    [Fact]
    public async Task ComparisonsAndParameterChangesCannotPublishStaleScenes()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            var spec = FlatStartRefinementTests.Spec(500) with { Budget = FlatStartRefinementTests.Spec(500).Budget with { InitialSeedCount = 3 } };
            var result = await new FlatStartSearchService().RunAsync(spec, FlatStartRefinementTests.Options);
            Assert.True(result.Candidates.Count >= 2);
            await new FlatStartRunLibrary(host.Directory).SaveAsync(result.Checkpoint);
            var window = host.Window(); window.Show(); await window.Ready;
            Click(window, "Resume"); await window.PendingOperation;
            var grid = Find<DataGrid>(window, "Candidates");
            grid.SelectedIndex = 0; Click(window, "CompareA");
            var a = Assert.IsType<CandidateRow>(grid.SelectedItem).Candidate;
            grid.SelectedIndex = 1; Click(window, "CompareB");
            var b = Assert.IsType<CandidateRow>(grid.SelectedItem).Candidate;
            var preview = Find<CandidatePreviewControl>(window, "Preview");
            await preview.PendingLoad;
            Assert.Equal(a.OpticFingerprint, preview.PrimaryFingerprint);
            var expectedB = new Layout2DBuilder(Optic.FromSnapshot(b.Optic)).Build(options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false));
            Assert.Equal(ContentFingerprint.Compute(OpticalSystemDesign.Application.Services.WorkbenchMapper.ToScene2Dto(expectedB)), ContentFingerprint.Compute(preview.SecondaryScene));
            Capture(window, "comparison");
            Click(window, "ClearComparison");
            var pending = preview.PendingLoad;
            // A real input mutation must clear all old data immediately, even while the old scene is calculating.
            Find<NumericUpDown>(window, "FocalLength").Value = 75;
            Assert.Null(window.CurrentCheckpoint); Assert.Null(preview.PrimaryScene);
            await pending;
            Assert.Null(preview.PrimaryScene); Assert.Null(preview.SecondaryScene);
            Assert.Empty(grid.ItemsSource.Cast<CandidateRow>());
            Assert.False(Find<Button>(window, "Export").IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task DamagedRecordClearsOldResultAndReportsFailure()
    {
        using var host = new DesktopHost();
        await host.Run(async () =>
        {
            await File.WriteAllTextAsync(host.OpenPath, "{broken");
            var window = host.Window(); window.Show(); await window.Ready;
            Click(window, "Generate"); await window.PendingOperation;
            Assert.NotNull(window.CurrentCheckpoint);
            Click(window, "OpenRun"); await window.PendingOperation;
            Assert.Null(window.CurrentCheckpoint);
            Assert.Null(Find<CandidatePreviewControl>(window, "Preview").PrimaryScene);
            Assert.Contains("未完成", Find<TextBlock>(window, "Status").Text);
            Assert.False(Find<Button>(window, "Export").IsEnabled);
            window.Close();
        });
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Distinct().Single(control => control.Name == name);
    private static void Click(Window window, string name)
    {
        var control = Find<Button>(window, name);
        Assert.True(control.IsEffectivelyEnabled, name + " is disabled");
        control.BringIntoView(); window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width);
        Assert.InRange(point.Y, 0, window.Bounds.Height);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "UI operation timed out"); await Task.Delay(10); }
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("INITIAL_STRUCTURE_UI_EVIDENCE");
        if (string.IsNullOrWhiteSpace(path)) return;
        System.IO.Directory.CreateDirectory(path);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class DesktopHost : IDisposable
    {
        private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        private readonly List<MainWindow> _windows = [];
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "flat-p4-ui-" + Guid.NewGuid().ToString("N"));
        public string ExportPath => Path.Combine(Directory, "chosen.staropt");
        public string OpenPath => Path.Combine(Directory, "open.family.json");
        public DesktopHost() => System.IO.Directory.CreateDirectory(Directory);
        public MainWindow Window(int width = 1240, int budget = 16, bool defaultInputs = false)
        {
            var window = new MainWindow(new LabDesktopSettings
            {
                RunDirectory = Directory,
                InitialSpecification = defaultInputs ? null : ReferenceDesktopSpecification(budget),
                InitialOptions = FlatStartRefinementTests.Options,
                SavePicker = (_, _) => Task.FromResult<string?>(ExportPath),
                OpenPicker = _ => Task.FromResult<string?>(OpenPath)
            })
            { Width = width, Height = 850 };
            _windows.Add(window);
            return window;
        }
        private static InitialStructureSpecification ReferenceDesktopSpecification(int budget)
        {
            var source = AutoLensManagedTests.Specification(budget);
            return source with { Conjugate = ObjectConjugateMode.Finite, Wavelengths = AutoLensReferenceExamples.Mobile().Specification.Wavelengths,
                FlatStart = source.FlatStart! with { AutoLens = source.FlatStart.AutoLens! with
                { Policy = AutoLensManagedPolicy.UpstreamE58, ComputeMode = OpticalSystemDesign.Core.Differentiation.DesignComputeMode.Automatic,
                    TrainingSampling = AutoLensReferenceTests.Small().FlatStart!.AutoLens!.TrainingSampling } } };
        }
        public Task Run(Func<Task> test) => _session.Dispatch(async () =>
        {
            try { await test(); return true; }
            finally
            {
                foreach (var window in _windows)
                {
                    window.Close();
                    await window.PendingOperation;
                    await Until(() => !window.IsVisible);
                }
            }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        public void Dispose()
        {
            try { _session.Dispose(); }
            catch (NullReferenceException exception) when (exception.StackTrace?.Contains("Avalonia.Headless.HeadlessUnitTestSession.Dispose", StringComparison.Ordinal) == true) { }
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
