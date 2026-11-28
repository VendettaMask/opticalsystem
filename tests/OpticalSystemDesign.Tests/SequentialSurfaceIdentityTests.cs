using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SequentialSurfaceIdentityTests
{
    public static Avalonia.AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Fact]
    public async Task EveryMenuSourceRetainsAllCoordinateRowsInEveryConfiguration()
    {
        foreach (var example in SequentialExampleCatalog.Examples)
        {
            var source = await File.ReadAllTextAsync(example.FilePath);
            var sourceRows = Regex.Matches(source, @"(?m)^SURF (\d+)\s*$")
                .Select(m => int.Parse(m.Groups[1].Value)).ToArray();
            var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(example.FilePath);
            foreach (var (optic, index) in imported.Configurations.Select((optic, index) => (optic, index)))
            {
                Assert.Equal(sourceRows, imported.SourceSurfaceNumbers[index]);
                Assert.Equal(sourceRows, optic.SurfaceGroup.Items.Select(s => s.Number));
                Assert.Equal(sourceRows.Length, optic.SurfaceGroup.Items.Count);
                foreach (var block in Regex.Split(source, @"(?m)^SURF ").Skip(1))
                    if (Regex.IsMatch(block, @"(?m)^\s*TYPE COORDBRK(?:\s|$)"))
                        Assert.IsType<CoordinateReferenceInteractionModel>(optic.SurfaceGroup.Items[int.Parse(block.Split((char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries)[0])].InteractionModel);
            }
        }
    }

    [Fact]
    public async Task RochonShowsCrystalAndCoordinateIdentityThroughEditsUndoAndNativeReopen()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(Rochon.FilePath);
        AssertRochon(app);
        var row = app.Prescription.GetSurfaces()[2];
        app.Prescription.UpdateSurfaceDrawingSettings(2, row.Drawing ?? new(), "#123456");
        AssertRochon(app);
        Assert.Throws<NotSupportedException>(() => app.Prescription.UpdateSurfaceComponents(3,
            new("偶次非球面", "无", 1, 1, 0, 50)));
        AssertRochon(app);
        Assert.True(app.Documents.Undo());
        AssertRochon(app);
        var path = Path.Combine(Path.GetTempPath(), "rochon-identity-" + Guid.NewGuid().ToString("N") + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            await app.Documents.OpenAsync(path);
            AssertRochon(app);
            Assert.Equal(0, row.Birefringent!.AxisX);
            Assert.Equal(0, row.Birefringent.AxisY);
            Assert.Equal(1, row.Birefringent.AxisZ);
            Assert.False(row.Birefringent.TraceExtraordinary);
            Assert.True(app.Prescription.GetSurfaces()[5].Birefringent!.TraceExtraordinary);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Isekai")]
    public async Task ActualEditorShowsDistinctTypesAndCrystalParameters(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SequentialSurfaceIdentityTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Rochon.FilePath);
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1450, Height = 850, Content = panel };
            try
            {
                window.Show();
                Render();
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "LensSurfaceGrid");
                Assert.Equal(10, grid.ItemsSource!.Cast<SurfaceEditorRow>().Count());
                grid.SelectedIndex = 2;
                var toggle = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SurfacePropertiesToggle");
                toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Render();
                var picker = panel.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SurfaceGeometry");
                Assert.False(picker.IsEnabled);
                Assert.Equal("双折射输入", picker.SelectedItem);
                var axis = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SurfaceCrystalAxis");
                Assert.Equal("0, 0, 1", axis.Text);
                axis.BringIntoView();
                Render();
                var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_IMPORT_IDENTITY_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    using var bitmap = new RenderTargetBitmap(new(1450, 850));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(capture, "rochon-" + theme + ".png"), PngBitmapEncoderOptions.Default);
                }
                grid.SelectedIndex = 3;
                Render();
                Assert.Equal("坐标间断", picker.SelectedItem);
                Assert.False(picker.IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        static void Render() { Dispatcher.UIThread.RunJobs(); }
    }

    private static SequentialExample Rochon => SequentialExampleCatalog.Examples.Single(e => e.RelativePath.EndsWith("/Rochon prism.zmx"));
    private static void AssertRochon(WorkbenchApplication app)
    {
        var rows = app.Prescription.GetSurfaces().Select(s => new SurfaceEditorRow(s, s.Number == 9)).ToArray();
        Assert.Equal(new[] { "标准面", "标准面", "双折射输入", "坐标间断", "双折射输出", "双折射输入", "坐标间断", "双折射输出", "近轴面", "标准面" }, rows.Select(r => r.SurfaceType));
        Assert.Equal(rows[2].Birefringent, rows[2].ToDto().Birefringent);
        Assert.True(rows[3].IsCoordinateReference);
    }
}
