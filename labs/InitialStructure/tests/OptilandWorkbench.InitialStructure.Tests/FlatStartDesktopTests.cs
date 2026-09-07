using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Visualization;
using OptilandWorkbench.InitialStructure.App;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LabDesktopCollection { public const string Name = "Initial structure desktop"; }

[Collection(LabDesktopCollection.Name)]
public sealed class FlatStartDesktopTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OptilandWorkbench.InitialStructure.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

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
            Click(window, "Generate");
            Assert.False(Find<NumericUpDown>(window, "FocalLength").IsEffectivelyEnabled);
            await window.PendingOperation;
            Assert.NotNull(window.CurrentCheckpoint);
            Assert.True(window.CurrentCheckpoint!.TracedRealRayCount > 0);
            var grid = Find<DataGrid>(window, "Candidates");
            var row = Assert.IsType<CandidateRow>(grid.SelectedItem);
            var preview = Find<CandidatePreviewControl>(window, "Preview");
            await preview.PendingLoad;
            Assert.Null(preview.Error);
            Assert.Equal(row.Candidate.OpticFingerprint, preview.PrimaryFingerprint);
            var expected = new Layout2DBuilder(Optic.FromSnapshot(row.Candidate.Optic)).Build(options: CandidatePreviewControl.Options);
            Assert.Equal(ContentFingerprint.Compute(expected), ContentFingerprint.Compute(preview.PrimaryScene));
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
            waves[0].IsPrimary = false;
            Click(window, "Validate"); await window.PendingOperation;
            Assert.Contains("主波长", Find<TextBlock>(window, "Status").Text);
            waves[0].IsPrimary = true;
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
            var window = host.Window(budget: 400); window.Show(); await window.Ready;
            Click(window, "Generate");
            await Until(() => window.CurrentCheckpoint?.Trials.Any(trial => trial.State == FamilyTrialState.Completed) == true);
            Click(window, "Cancel"); await window.PendingOperation;
            var stopped = window.CurrentCheckpoint!;
            Assert.Equal(FlatStartSearchState.Cancelled, stopped.State);
            Assert.InRange(stopped.ChargedEvaluations, 1, 399);
            Click(window, "Export"); await window.PendingOperation;
            Assert.True(File.Exists(host.ExportPath));
            Click(window, "Resume"); await window.PendingOperation;
            Assert.Equal(stopped.RunId, window.CurrentCheckpoint!.RunId);
            Assert.Equal(400, window.CurrentCheckpoint.ChargedEvaluations);
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
            var window = host.Window(budget: 400); window.Show(); await window.Ready;
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
            Find<NumericUpDown>(window, "RefinementBudget").Value = 80;
            Click(window, "Refine"); await window.PendingOperation;
            Assert.NotEqual(source.RunId, window.CurrentCheckpoint!.RunId);
            Assert.Equal(80, window.CurrentCheckpoint.ChargedEvaluations);
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
            var expectedB = new Layout2DBuilder(Optic.FromSnapshot(b.Optic)).Build(options: CandidatePreviewControl.Options);
            Assert.Equal(ContentFingerprint.Compute(expectedB), ContentFingerprint.Compute(preview.SecondaryScene));
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
        public MainWindow Window(int width = 1240, int budget = 120)
        {
            var window = new MainWindow(new LabDesktopSettings
            {
                RunDirectory = Directory,
                InitialSpecification = FlatStartRefinementTests.Spec(budget),
                InitialOptions = FlatStartRefinementTests.Options,
                SavePicker = (_, _) => Task.FromResult<string?>(ExportPath),
                OpenPicker = _ => Task.FromResult<string?>(OpenPath)
            })
            { Width = width, Height = 850 };
            _windows.Add(window);
            return window;
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
