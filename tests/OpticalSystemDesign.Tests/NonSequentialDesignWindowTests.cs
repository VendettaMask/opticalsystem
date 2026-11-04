using Avalonia.Controls;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialDesignWindowTests
{
    [Fact]
    public async Task ArchivedExperimentReplaysInAnEmptyWindowWithoutPublishingTheArchivedScene()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("replay.zip");
            using (var producer = WorkbenchApplication.Create())
            {
                var source = producer.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
                var detector = producer.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
                var d = producer.NonSequential.GetDocument().Objects.Single(o => o.Id == detector);
                producer.NonSequential.UpdateObject(new(d.Id, true, true, d.Kind, d.Name, null, null, 0, 0, 1, 0, 0, 0,
                    new DetectorRectangleParameters(4, 4, 2, 2, true, true)));
                var v = new OpticalSystemDesign.Core.NonSequential.NonSequentialDesignVariable(source,
                    OpticalSystemDesign.Core.NonSequential.NonSequentialVariableDomain.Position, "X", -1, 1, .02);
                var t = new OpticalSystemDesign.Core.NonSequential.NonSequentialDesignTarget(detector,
                    OpticalSystemDesign.Core.NonSequential.NonSequentialMetric.TotalPower, 1, .01);
                var plan = new OpticalSystemDesign.Core.NonSequential.NonSequentialDesignExperiment(1, producer.NonSequential.GetDesignSceneIdentity(), [v], [t], new());
                var result = await producer.NonSequential.OptimizeAsync(plan.Variables, plan.Targets, plan.Settings);
                await producer.NonSequential.SaveDesignExperimentAsync(path, plan, result);
            }
            using var consumer = WorkbenchApplication.Create(); var before = consumer.NonSequential.GetDesignSceneIdentity();
            var window = new NonSequentialDesignWindow(consumer.NonSequential);
            try
            {
                window.Show(); window.UpdateLayout(); await window.ReplayExperimentFromPathAsync(path); window.UpdateLayout();
                Assert.Equal(before, consumer.NonSequential.GetDesignSceneIdentity()); Assert.Empty(consumer.NonSequential.GetDocument().Objects);
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "应用通过验证的候选").IsEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }
    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task DesignImportAndPhysicsControlsAreReachableOffscreen(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            app.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
            var windows = new Window[] { new NonSequentialDesignWindow(app.NonSequential), new NonSequentialZemaxImportWindow(app.NonSequential),
                new NonSequentialTraceControlWindow(app.NonSequential, app.NonSequentialAnalysis) };
            foreach (var window in windows)
            {
                try
                {
                    window.Show(); window.UpdateLayout(); Assert.True(window.Bounds.Width > 0);
                    Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsVisible && b.Bounds.Width > 0);
                    if (window is NonSequentialTraceControlWindow)
                    {
                        var check = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString() == "偏振追迹（Jones / Stokes）");
                        Assert.True(check.IsEffectivelyVisible); Assert.True(check.IsEnabled);
                    }
                }
                finally { window.Close(); }
            }
        }, CancellationToken.None);
    }
}
