using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

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
            foreach (var (category, group) in flyout.Items.Cast<MenuItem>().Zip(SequentialExampleCatalog.Groups))
            {
                Assert.Equal(group.Name, category.Header);
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
