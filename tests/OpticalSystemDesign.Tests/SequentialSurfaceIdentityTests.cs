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
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SequentialSurfaceIdentityTests
{
    public static Avalonia.AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    private static string AchromaticSinglet => SequentialExampleCatalog.Examples.Single(e =>
        e.RelativePath == "Diffractive components/Achromatic singlet.zmx").FilePath;

    [Fact]
    public async Task AchromaticSingletShowsBinary2InGridAndProperties()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SequentialSurfaceIdentityTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(AchromaticSinglet);
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1450, Height = 850, Content = panel };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "LensSurfaceGrid");
                grid.SelectedIndex = 2;
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.DataContext is SurfaceEditorRow { Number: 2 } && t.Text == UiLocalization.Text("二元面 2"));
                var toggle = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SurfacePropertiesToggle");
                toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var picker = panel.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SurfaceGeometry");
                Assert.Equal("二元面 2", picker.SelectedItem);
                Assert.False(picker.IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Binary2RetainsPhaseAfterPropertyEditsAndNativeReopen()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(AchromaticSinglet);
        var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[2]);
        Assert.Equal("偶次非球面", row.GeometryKind);
        Assert.Equal("衍射", row.InteractionKind);
        Assert.Equal("二元面 2", row.SurfaceType);
        app.Prescription.UpdateSurfaceComponents(2, new(row.SurfaceType, "圆形", 1, 1, 0, 50));
        app.Prescription.UpdateSurface(row.ToDto() with { Label = "renamed", Material = "N-BK7" });
        Assert.True(app.Documents.Undo());
        Assert.True(app.Documents.Redo());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            await app.Documents.OpenAsync(path);
            Assert.Equal("二元面 2", new SurfaceEditorRow(app.Prescription.GetSurfaces()[2]).SurfaceType);
            var saved = (await StarOptProjectStore.LoadAsync(path)).Configurations[0].SurfaceGroup.Items[2];
            var phase = Assert.IsType<DiffractivePhaseInteractionModel>(saved.InteractionModel);
            var radial = Assert.IsType<NormalizedRadialPhaseProfile>(phase.Profile);
            Assert.Equal(1, radial.Order);
            Assert.Equal(30, radial.NormalizationRadius);
            Assert.Equal(new[] { -2993.832387049, 1135.544608547, -59.321054543 }, radial.Coefficients);
        }
        finally { File.Delete(path); }
    }

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
