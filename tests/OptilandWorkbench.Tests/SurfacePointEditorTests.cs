using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfacePointEditorTests
{
    [Theory]
    [InlineData("SSLP")]
    [InlineData("SCRV")]
    [InlineData("TSAG")]
    public async Task EighthColumnShowsOrientationAndHidesForSevenSlotRow(string code)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            app.Optimization.SetMeritFunction([
                new(1, false, code, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, "eight",
                    ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 3, ZemaxData2: 4, ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: 0, ZemaxData6: 2),
                new(2, false, "SSAG", 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, "seven",
                    ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 3, ZemaxData2: 4, ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: 0),
                new(3, false, "FNUM", 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, "friendly")
            ]);
            using var panel = new OptimizationPanel(app.Prescription, app.Optimization, app.Events);
            var window = new Window { Width = 1200, Height = 700, Content = panel };
            try
            {
                window.Show(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                var rows = grid.ItemsSource.Cast<MeritOperandEditorRow>().ToArray();
                grid.SelectedItem = rows[0];
                Assert.True(grid.Columns[9].IsVisible); Assert.Contains(code == "TSAG" ? "Tilt About Z" : "Orientation", grid.Columns[9].Header?.ToString());
                Assert.True(rows[0].IsParameterEditable(7)); Assert.Equal(2, rows[0].Parameter8);
                grid.SelectedItem = rows[1]; Assert.False(grid.Columns[9].IsVisible);
                grid.SelectedItem = rows[2]; Assert.False(grid.Columns[9].IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
