using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PerformanceTestTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DoubleGaussBenchmarkCountsRealWorkAndLeavesTheActiveDocumentIntact()
    {
        var example = SequentialExampleCatalog.Examples.Single(item =>
            item.RelativePath == "Objectives/Double Gauss 28 degree field.zmx");
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(example.FilePath);
        var before = app.Documents.GetSnapshot();

        var result = await app.Performance.RunAsync();

        Assert.Equal(before.Name, result.LensName);
        Assert.Equal(before.Revision, app.Documents.GetSnapshot().Revision);
        Assert.Equal(before.IsDirty, app.Documents.GetSnapshot().IsDirty);
        Assert.Equal(example.FilePath, app.Documents.CurrentPath);
        Assert.Equal(3, result.TraceTrials.Count);
        Assert.Equal(3, result.UpdateTrials.Count);
        var reference = result.TraceTrials[0];
        Assert.All(result.TraceTrials, trial =>
        {
            Assert.Equal(32_768, trial.Rays);
            Assert.Equal(reference.ImageArrivals, trial.ImageArrivals);
            Assert.Equal(reference.SurfaceVisits, trial.SurfaceVisits);
            Assert.InRange(trial.ImageArrivals, 1, trial.Rays);
            Assert.InRange(trial.SurfaceVisits, (long)trial.ImageArrivals * result.SurfaceCount,
                (long)trial.Rays * result.SurfaceCount);
            Assert.True(double.IsFinite(trial.SurfaceVisitsPerSecond) && trial.SurfaceVisitsPerSecond > 0);
            output.WriteLine($"{trial.Rays:N0} rays, {trial.ImageArrivals:N0} arrivals, {trial.SurfaceVisits:N0} visits, "
                + $"{trial.Seconds:F6} s, {trial.RaysPerSecond:F0} rays/s, {trial.SurfaceVisitsPerSecond:F0} visits/s");
        });
        Assert.All(result.UpdateTrials, trial =>
        {
            Assert.InRange(trial.Updates, 1, 32);
            Assert.True(double.IsFinite(trial.UpdatesPerSecond) && trial.UpdatesPerSecond > 0);
            output.WriteLine($"{trial.Updates} updates, {trial.Seconds:F6} s, {trial.UpdatesPerSecond:F2} updates/s");
        });
    }

    [Fact]
    public async Task CancelledRunDoesNotStartBenchmarkWork()
    {
        using var app = WorkbenchApplication.Create();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Performance.RunAsync(new CancellationToken(true)));
    }

    [Fact]
    public async Task DesktopDialogRunsAndShowsMeasuredData()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var window = new PerformanceTestWindow(new SampleResult());
            try
            {
                window.Show();
                var run = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, UiLocalization.Text("开始检验")));
                run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var data = window.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(data.IsReadOnly);
                Assert.Contains("Double Gauss test result", data.Text);
                Assert.Contains(UiLocalization.Format("处理面次/秒    {0:N0}", 500.0), data.Text);
                Assert.True(run.IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class SampleResult : IPerformanceTestService
    {
        public Task<PerformanceTestResult> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new PerformanceTestResult("Double Gauss test result", 1, 5, 3, 0.5876, 8,
                [new(100, 100, 500, 1), new(100, 100, 500, 2), new(100, 100, 500, 0.5)],
                [new(3, 1), new(3, 2), new(3, 0.5)]));
    }
}
