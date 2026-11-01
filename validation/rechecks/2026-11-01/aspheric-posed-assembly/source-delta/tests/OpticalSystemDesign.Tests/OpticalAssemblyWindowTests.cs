using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OpticalAssemblyWindowTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ConvertedGroupAndDetectorPropertiesRemainCompleteThroughTheOffscreenEditor(bool posed)
    {
        using var host=SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var files=new CameraSensorFileTests.TemporaryFiles();var optic=posed?PosedOpticalAssemblyTests.Prescription(true):OpticalAssemblyTests.Prescription(true);
            var source=files.Path("assembly.staropt");await StarOptProjectStore.SaveAsync(new([optic],0),source);
            using var app=WorkbenchApplication.Create();await app.Documents.OpenAsync(source);app.NonSequential.ConvertFromSequential();
            using var panel=new NonSequentialObjectEditorPanel(app.NonSequential,app.Events);
            var window=new Window{Content=panel,Width=1400,Height=950};
            try
            {
                window.Show();window.UpdateLayout();
                var grid=window.GetVisualDescendants().OfType<DataGrid>().Single();
                var face=window.GetVisualDescendants().OfType<TextBlock>().Single(c=>c.Name=="nsc-interface-2");
                Assert.Contains("Glass 0 → Glass 1",face.Text);Assert.Contains(posed?"机械半径=4":"机械半径=12",face.Text);
                if(posed)Assert.Contains("even_asphere",window.GetVisualDescendants().OfType<TextBlock>().Single(c=>c.Name=="nsc-frame-2").Text);
                var evidence=Environment.GetEnvironmentVariable(posed?"OPTICAL_POSED_ASSEMBLY_EVIDENCE_DIR":"OPTICAL_ASSEMBLY_EVIDENCE_DIR");
                if(!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame();
                    Assert.NotNull(image);image.Save(Path.Combine(evidence,posed?"posed-assembly-editor-offscreen.png":"assembly-editor-offscreen.png"),PngBitmapEncoderOptions.Default);
                }
                var apply=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="nsc-apply");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
                Assert.Equal(3,((OpticalAssemblyParameters)app.NonSequential.GetDocument().Objects[0].Parameters).Assembly.Interfaces.Count);
                var detector=app.NonSequential.GetDocument().Objects.Single(o=>o.Kind==NonSequentialObjectKind.DetectorRectangle);
                grid.SelectedItem=((IEnumerable)grid.ItemsSource!).Cast<object>().Single(r=>(Guid)r.GetType().GetProperty("Id")!.GetValue(r)! == detector.Id);
                window.UpdateLayout();apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
                Assert.NotNull(((DetectorRectangleParameters)app.NonSequential.GetDocument().Objects.Single(o=>o.Id==detector.Id).Parameters).IncidentSurface);
                return true;
            }
            finally {window.Close();}
        },CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
