using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OpticalDrawingStandardSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingStandardKeepsElementAndAllEditedDrawingParameters(bool cemented)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("tessar");
            using var panel = new OpticalDrawingPanel(application.Prescription, application.Materials,
                application.Events, application.Visualization);
            var elements = Field<ComboBox>(panel, "_elementPicker");
            elements.SelectedItem = elements.Items.Cast<object>().First(choice =>
                choice.GetType().GetProperty("Element")!.GetValue(choice) is OpticalDrawingElementDefinition element
                && element.IsCemented == cemented);
            var elementChoice = elements.SelectedItem;
            Field<ComboBox>(panel, "_pageSize").SelectedIndex = 1;
            Field<TextBox>(panel, "_drawingNumber").Text = "SHARED-123";
            Field<TextBox>(panel, "_partName").Text = "共用参数";
            Field<TextBox>(panel, "_coating").Text = "共享膜层要求";
            Field<TextBox>(panel, "_laserDamageThreshold").Text = "按合同 LDT-123";
            Field<NumericUpDown>(panel, "_diameterUpperDeviation").Value = 0.07m;
            Field<NumericUpDown>(panel, "_thicknessLowerDeviation").Value = -0.09m;
            Field<NumericUpDown>(panel, "_frontRadiusTolerance").Value = 0.25m;
            Field<NumericUpDown>(panel, "_frontForm").Value = 250;
            Field<NumericUpDown>(panel, "_centering").Value = 2.5m;
            Field<NumericUpDown>(panel, "_refractiveIndexTolerance").Value = 0.0012m;
            Field<NumericUpDown>(panel, "_abbeNumberTolerance").Value = 1.25m;
            Field<NumericUpDown>(panel, "_texture").Value = 3.5m;
            Field<NumericUpDown>(panel, "_opticalRa").Value = 0.01m;
            Field<NumericUpDown>(panel, "_otherRa").Value = 1.6m;
            Field<NumericUpDown>(panel, "_opticalRaSample").Value = 0.08m;
            Field<NumericUpDown>(panel, "_otherRaSample").Value = 0.8m;
            Field<NumericUpDown>(panel, "_fringeWavelength").Value = 546.07m;
            var legacyEditors = Field<Dictionary<string, TextBox>>(panel, "_legacyGbEditors");
            legacyEditors[nameof(LegacyGb1991Requirements.PowerFringes)].Text = "3";
            legacyEditors[nameof(LegacyGb1991Requirements.TestPlateGrade)].Text = "A";
            legacyEditors[nameof(LegacyGb1991Requirements.AbsorptionGrade)].Text = "2";
            typeof(OpticalDrawingPanel).GetMethod("UpdatePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
            var iso = Sheet(panel);
            var isoPreview = Preview(panel);
            var standards = Field<ComboBox>(panel, "_standardPicker");

            standards.SelectedIndex = 1;
            var legacy = Sheet(panel);
            Assert.Equal(OpticalDrawingStandard.GbT13323_1991, legacy.Standard);
            Assert.Equal("3", legacy.LegacyGb1991!.PowerFringes);
            Assert.Equal("2", legacy.LegacyGb1991.AbsorptionGrade);
            Assert.Equal(0.01, legacy.LegacyGb1991.OpticalSurfaceRaMicrometers);
            Assert.Equal(1.6, legacy.LegacyGb1991.OtherSurfaceRaMicrometers);
            Assert.Equal(0.08, legacy.LegacyGb1991.OpticalRaSamplingLengthMillimeters);
            Assert.Equal(0.8, legacy.LegacyGb1991.OtherRaSamplingLengthMillimeters);
            Assert.Equal(546.07, legacy.LegacyGb1991.FringeReferenceWavelengthNanometers);
            Assert.Same(elementChoice, elements.SelectedItem);
            Assert.Equal(JsonSerializer.Serialize(iso), JsonSerializer.Serialize(legacy with { Standard = iso.Standard }));
            Assert.True(Field<NumericUpDown>(panel, "_texture").IsVisible);
            Assert.False(Field<TextBox>(panel, "_laserDamageThreshold").IsVisible);
            Assert.True(Field<NumericUpDown>(panel, "_opticalRa").IsVisible);
            Assert.False(Field<NumericUpDown>(panel, "_frontRadiusTolerance").IsVisible);
            Assert.False(isoPreview.SequenceEqual(Preview(panel)));
            Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(legacy));

            standards.SelectedIndex = 0;
            Assert.Equal(JsonSerializer.Serialize(iso), JsonSerializer.Serialize(Sheet(panel)));
            Assert.True(isoPreview.SequenceEqual(Preview(panel)));
            Assert.False(Field<NumericUpDown>(panel, "_texture").IsVisible);
            Assert.True(Field<TextBox>(panel, "_laserDamageThreshold").IsVisible);
            Assert.False(Field<NumericUpDown>(panel, "_opticalRa").IsVisible);
            Assert.True(Field<NumericUpDown>(panel, "_frontRadiusTolerance").IsVisible);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("hidden")]
    [InlineData("floating")]
    public async Task SavedSeparateDrawingPagesRestoreAsOneEditorWithSelectedStandard(string location)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "drawing-unified-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var application = WorkbenchApplication.Create("cooke");
                var store = new WorkspaceSessionStore(directory);
                var iso = new WorkspaceDocumentDescriptor("document:optical-drawing-iso", WorkspaceDocumentTypes.OpticalDrawing,
                    "光学制图 · ISO 10110", Settings: new() { ["standard"] = nameof(OpticalDrawingStandard.Iso10110) });
                var legacy = new WorkspaceDocumentDescriptor("document:optical-drawing-gb-1991", WorkspaceDocumentTypes.OpticalDrawing,
                    "光学制图 · GB/T 13323—1991", Settings: new() { ["standard"] = nameof(OpticalDrawingStandard.GbT13323_1991) });
                using (var previous = new PanelManager(application, new AppSettings(), store))
                {
                    previous.Factory.AddDockable(previous.Factory.PrimaryDocumentDock!, new Document { Id = iso.Id, Title = iso.Title });
                    var oldPage = new Document { Id = legacy.Id, Title = legacy.Title };
                    if (location == "hidden")
                    {
                        previous.Layout.HiddenDockables = previous.Factory.CreateList<IDockable>(oldPage);
                        oldPage.Owner = previous.Layout;
                    }
                    else if (location == "floating")
                    {
                        var dock = new DocumentDock { Id = "drawing:float-dock", VisibleDockables = previous.Factory.CreateList<IDockable>(oldPage), ActiveDockable = oldPage };
                        oldPage.Owner = dock;
                        var root = new RootDock { Id = "drawing:float-root", VisibleDockables = previous.Factory.CreateList<IDockable>(dock), ActiveDockable = dock };
                        var window = new DockWindow { X = 100, Y = 100, Width = 900, Height = 700, Layout = root };
                        root.Window = window;
                        previous.Layout.Windows = previous.Factory.CreateList<IDockWindow>(window);
                    }
                    else previous.Factory.AddDockable(previous.Factory.PrimaryDocumentDock!, oldPage);
                    await store.SaveSlotAsync(1, new WorkspaceSession(WorkspaceSessionStore.CurrentVersion,
                        new WorkspaceDockLayoutSerializer().Serialize(previous.Layout),
                        previous.Factory.SnapshotDescriptors().Concat(new[] { iso, legacy }).ToArray(), legacy.Id));
                }

                using var manager = new PanelManager(application, new AppSettings(), store);
                await manager.LoadLayoutSlotAsync(1);
                var document = Assert.Single(manager.Factory.OpenDocuments(), item => item.Context is OpticalDrawingPanel);
                var panel = Assert.IsType<OpticalDrawingPanel>(document.Context);
                Assert.Equal("光学制图", document.Title);
                Assert.Equal(OpticalDrawingStandard.GbT13323_1991, panel.DrawingStandard);
                Assert.Single(manager.Factory.Descriptors, item => item.TypeId == WorkspaceDocumentTypes.OpticalDrawing);
                manager.ShowOpticalDrawing();
                Assert.Same(document, Assert.Single(manager.Factory.OpenDocuments(), item => item.Context is OpticalDrawingPanel));
                Assert.Contains(document, Assert.IsAssignableFrom<IDock>(document.Owner).VisibleDockables!);

                Field<ComboBox>(panel, "_standardPicker").SelectedIndex = 0;
                await manager.SaveLayoutSlotAsync(2);
                var saved = (await store.LoadSlotAsync(2))!;
                Assert.Equal(nameof(OpticalDrawingStandard.Iso10110),
                    Assert.Single(saved.Documents, item => item.TypeId == WorkspaceDocumentTypes.OpticalDrawing).Settings!["standard"]);
                await manager.LoadLayoutSlotAsync(2);
                Assert.Equal(OpticalDrawingStandard.Iso10110,
                    Assert.IsType<OpticalDrawingPanel>(Assert.Single(manager.Factory.OpenDocuments(), item => item.Context is OpticalDrawingPanel).Context).DrawingStandard);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }, CancellationToken.None);
    }

    private static T Field<T>(OpticalDrawingPanel panel, string name) =>
        (T)typeof(OpticalDrawingPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;

    private static OpticalDrawingSheet Sheet(OpticalDrawingPanel panel) =>
        Assert.IsType<OpticalDrawingSheet>(typeof(OpticalDrawingPanel)
            .GetMethod("CreateSheet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null));

    private static byte[] Preview(OpticalDrawingPanel panel)
    {
        using var output = new MemoryStream();
        Field<Bitmap>(panel, "_previewBitmap").Save(output, new PngBitmapEncoderOptions());
        return output.ToArray();
    }
}
