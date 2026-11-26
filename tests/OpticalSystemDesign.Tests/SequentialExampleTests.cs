using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SequentialExampleTests
{
    [Fact]
    public void CatalogContainsEveryCategoryAndByteExactPackagedSource()
    {
        var expected = new (string Name, int Count)[]
        {
            ("Afocal", 3), ("Birefringent prisms", 4), ("Diffractive components", 6),
            ("GRINs", 2), ("Image Simulation", 6), ("Interconnects", 5), ("Manufacturing", 1),
            ("Miscellaneous", 19), ("Objectives", 19), ("Optical testing", 5), ("Polarization", 2),
            ("Telescopes", 11), ("Tilted systems & prisms", 9), ("Zoom systems", 3)
        };
        Assert.Equal(expected, SequentialExampleCatalog.Groups.Select(group => (group.Name, group.Examples.Count)));
        Assert.Equal(95, SequentialExampleCatalog.Examples.Count);
        Assert.Equal(95, SequentialExampleCatalog.Examples.Select(example => example.ActionId).Distinct().Count());
        Assert.All(SequentialExampleCatalog.Examples, example =>
        {
            Assert.True(File.Exists(example.FilePath), example.FilePath);
            Assert.Equal(example.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(example.FilePath))));
        });
        var packaged = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential"),
            "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(95, packaged.Count());
    }

    [Fact]
    public async Task CategoryMenuOpensTheSelectedLensAndUsesTheExistingDropDownStyle()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var selected = new List<SequentialExample>();
            var button = MainWindow.CreateSequentialExamplesButton(example =>
            {
                selected.Add(example);
                return Task.CompletedTask;
            });
            Assert.Contains("ribbon-dropdown", button.Classes);
            Assert.Equal("ribbon-menu-examples", AutomationProperties.GetAutomationId(button));
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.Equal(14, flyout.Items.Count);
            Assert.Equal(new[]
            {
                "无焦系统", "双折射棱镜", "衍射元件", "梯度折射率系统", "图像模拟", "光学互连", "制造",
                "其他系统", "物镜", "光学检测", "偏振", "望远镜", "倾斜系统与棱镜", "变焦与扫描"
            }, flyout.Items.Cast<MenuItem>().Select(category => Assert.IsType<string>(category.Header)));
            foreach (var (category, group) in flyout.Items.Cast<MenuItem>().Zip(SequentialExampleCatalog.Groups))
            {
                Assert.Equal(group.DisplayName, category.Header);
                Assert.Equal(group.DisplayName, AutomationProperties.GetName(category));
                Assert.Equal(group.Name, UiLocalization.Text(group.DisplayName, UiLocalization.English));
                Assert.Equal(group.Examples.Count, category.Items.Count);
                foreach (var (item, example) in category.Items.Cast<MenuItem>().Zip(group.Examples))
                {
                    Assert.Equal(example.Name, AutomationProperties.GetName(item));
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Same(example, selected[^1]);
                }
            }
            Assert.Equal(95, selected.Count);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Objectives/Double Gauss 28 degree field.zmx", 1)]
    [InlineData("Zoom systems/Zoom lens.zmx", 3)]
    public async Task PackagedExampleUsesNormalDocumentImportAndPreservesConfigurations(string relativePath, int configurations)
    {
        var example = SequentialExampleCatalog.Examples.Single(item => item.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
        using var application = WorkbenchApplication.Create();
        await application.Documents.OpenAsync(example.FilePath);
        Assert.Equal(example.FilePath, application.Documents.CurrentPath);
        Assert.True(application.Documents.GetSnapshot().SurfaceCount > 2);
        Assert.Equal(configurations, application.MultiConfiguration.GetRows().Count);
        Assert.False(application.Documents.GetSnapshot().IsDirty);
    }

    [Fact]
    public async Task TiltedImagePlaneOpensWithSavedConfigurationForCurrentRmsAnalysis()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential", "Image Simulation", "Example 6, tilted image plane.ZMX");
        var runtime = new WorkbenchRuntime(Optic.CreateBlank());
        await runtime.LoadAsync(path);
        Assert.Equal(1, Assert.Single(runtime.GetMultiConfigurationRows(), row => row.Active).Index);
        var surfaces = runtime.CurrentOptic.SurfaceGroup.Items;
        var glass = Assert.IsType<CatalogGlassMaterial>(surfaces[1].MaterialAfter);
        var pickup = Assert.IsType<CatalogGlassMaterial>(surfaces[6].MaterialAfter);
        Assert.Equal("N-LAK34", glass.CatalogName);
        Assert.Equal("SF5", Assert.IsType<CatalogGlassMaterial>(surfaces[3].MaterialAfter).CatalogName);
        foreach (var wavelength in runtime.CurrentOptic.Wavelengths)
            Assert.Equal(glass.RefractiveIndex(wavelength.Nanometers), pickup.RefractiveIndex(wavelength.Nanometers));

        var settings = runtime.MergeAnalysisSettings("RMS vs Field", null);
        var tilted = runtime.BuildAnalysisView("RMS vs Field", settings).SeriesList;
        Assert.Equal(4, tilted.Count);
        // Native 2026 R1 wavefront maps 0704/0734 give 0.0477 waves on axis at 550 nm
        // with grid sampling. The interactive RMS scan uses Gaussian quadrature.
        Assert.InRange(Math.Abs(tilted[2].Points[0].Y - 0.0477), 0, 0.001);
        Assert.All(tilted, line =>
        {
            Assert.Equal(16, line.Points.Count);
            Assert.Equal(10, line.Points[^1].X);
            Assert.True(line.Points.Zip(line.Points.Skip(1)).All(pair => pair.Second.Y > pair.First.Y));
        });
        Assert.True(tilted[1].Points[^1].Y > tilted[0].Points[^1].Y);
        Assert.True(tilted[0].Points[^1].Y > tilted[2].Points[^1].Y);
        Assert.True(tilted[2].Points[^1].Y > tilted[3].Points[^1].Y);
        // Native 10 mm meridional OPD fans (0750, 0754, 0758) independently
        // constrain the off-axis wavefront at both ends of the pupil for each wavelength.
        foreach (var (wave, negativePupil, positivePupil) in new[] {
            (1, -13.76355, -14.84096), (2, -11.78018, -12.54179), (3, -10.52083, -11.08686) })
        {
            var edgeFan = Assert.Single(new OpticalPathDifferenceAnalysis(runtime.CurrentOptic,
                wavelengthNumber: wave, fieldNumber: 3).GenerateData().PlotSeries);
            Assert.Equal(negativePupil, edgeFan.Points[0].Y, 3);
            Assert.Equal(positivePupil, edgeFan.Points[^1].Y, 3);
        }
        runtime.ActivateMultiConfiguration(0);
        var untilted = runtime.BuildAnalysisView("RMS vs Field", settings).SeriesList;
        Assert.InRange(Math.Abs(untilted[2].Points[0].Y - 0.0477), 0, 0.001);
        Assert.All(untilted, line => Assert.InRange(line.Points[^1].Y, 0, 0.2));
    }

    [Fact]
    public async Task PrismExampleLoadsTheNowSupportedCoordinateBreaks()
    {
        var example = SequentialExampleCatalog.Examples.Single(item => item.RelativePath == "Miscellaneous/Apochromat_prism.zmx");
        using var application = WorkbenchApplication.Create();
        var before = application.Documents.GetSnapshot();
        await application.Documents.OpenAsync(example.FilePath);
        var after = application.Documents.GetSnapshot();
        Assert.True(after.Revision > before.Revision);
        Assert.Equal(example.FilePath, application.Documents.CurrentPath);
        Assert.True(after.SurfaceCount > 2);
    }
}
