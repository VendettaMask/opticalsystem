using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Core = OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialFieldViewerWindowTests
{
    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task ActualFieldAndStokesMapsRenderWithTypedUnitsMasksAndSafeSelectionChanges(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var source = app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            var detector = app.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
            var s = app.NonSequential.GetDocument().Objects.Single(o => o.Id == source);
            app.NonSequential.UpdateObject(Update(s) with { Parameters = ((SourceRayParameters)s.Parameters) with { Physics = new(new(false, 1, 0), "laser", 30) } });
            var d = app.NonSequential.GetDocument().Objects.Single(o => o.Id == detector);
            app.NonSequential.UpdateObject(Update(d) with { Z = 1, Parameters = new DetectorRectangleParameters(4, 4, 4, 4, true, true) });
            app.NonSequential.UpdatePhysicsSettings(true, true);
            await app.NonSequentialAnalysis.TraceAsync(new(NonSequentialTraceOutputMode.RayDatabase));
            using var panel = new NonSequentialDetectorViewerPanel(app.NonSequential, app.NonSequentialAnalysis);
            var window = new Window { Content = panel, Width = 1200, Height = 820 };
            try
            {
                window.Show(); window.UpdateLayout();
                T Find<T>(string name) where T : Control => panel.GetLogicalDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
                var data = Find<ComboBox>("探测器数据类型"); var plot = Find<AnalysisPlotControl>("非序列探测器热图");
                foreach (var type in new[] { NonSequentialDetectorDataType.StokesQ, NonSequentialDetectorDataType.CoherentPower,
                    NonSequentialDetectorDataType.FieldReal, NonSequentialDetectorDataType.FieldImaginary, NonSequentialDetectorDataType.FieldPhase })
                {
                    data.SelectedItem = type; await panel.RefreshViewAsync(); window.UpdateLayout();
                    var series = Assert.Single(plot.Series); var expected = NonSequentialDetectorDisplay.ValueAxis(type, false);
                    Assert.Equal(expected.Quantity, series.ValueQuantity); Assert.Equal(expected.Unit, series.ValueUnit);
                    Assert.Contains(NonSequentialDetectorDisplay.DataLabel(type), plot.PlotOptions!.Title);
                    if (type == NonSequentialDetectorDataType.FieldPhase)
                    {
                        Assert.Equal(-180, series.ValueMinimum); Assert.Equal(180, series.ValueMaximum);
                        Assert.Equal(1, series.Points.Count(p => p.Value is { } v && double.IsFinite(v)));
                        Assert.False(Find<CheckBox>("对数显示").IsEnabled); Assert.False(Find<NumericUpDown>("平滑半径").IsEnabled);
                    }
                }
                Find<ComboBox>("探测器偏振模态").SelectedIndex = 1;
                Assert.Empty(plot.Series); // Never relabel an old field with newly selected settings.
                await panel.RefreshViewAsync(); window.UpdateLayout();
                Assert.All(Assert.Single(plot.Series).Points, p => Assert.True(double.IsNaN(p.Value!.Value)));
                using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 820), new Vector(96, 96)); bitmap.Render(window);
                data.SelectedItem = NonSequentialDetectorDataType.IncoherentIrradiance;
                Assert.Empty(plot.Series); await panel.RefreshViewAsync();
                Assert.Equal(AnalysisAxisUnit.WattsPerSquareMillimeter, Assert.Single(plot.Series).ValueUnit);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static NonSequentialObjectUpdateDto Update(NonSequentialObjectRowDto o) => new(o.Id, o.Enabled, o.Visible, o.Kind,
        o.Name, o.ReferenceObjectId, o.ContainingObjectId, o.X, o.Y, o.Z, o.TiltXDegrees, o.TiltYDegrees, o.TiltZDegrees, o.Parameters, o.SurfaceBsdf);
}
