using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraWorkspaceTests
{
    [Theory]
    [InlineData("Light","zh-CN",1440,900,18)]
    [InlineData("Dark","en-US",1260,850,13)]
    [InlineData("Light","ja-JP",760,680,18)]
    public async Task CompactWorkspaceKeepsOneStageAndAllViewsAndFileActionsReachable(string theme,string language,int width,int height,double font)
    {
        using var session=SafeHeadlessUnitTestSession.StartNew(typeof(LaboratoryPresentationTests));
        await session.Dispatch(() =>
        {
            var previous=UiLocalization.CurrentLanguage;DisplayTypography.Configure(new AppSettings{Theme=theme,FontSize=font});
            ThemeApplicationService.Apply(Avalonia.Application.Current!,theme);UiLocalization.Apply(language);
            using var app=WorkbenchApplication.Create();var window=new CameraImagingWindow(app){Width=width,Height=height};
            try
            {
                window.Show();Tick(window);var revision=app.Events.Revision;
                var header=Find<Control>(window,"LaboratoryHeader");Assert.True(header.Bounds.Height<=110);
                foreach(var button in header.GetVisualDescendants().OfType<Button>())
                {var bounds=InWindow(button,window);Assert.InRange(bounds.Right,0,window.ClientSize.Width+.5);Assert.InRange(bounds.Bottom,0,window.ClientSize.Height);}
                var stages=All(window).Where(c=>c.Name?.StartsWith("camera-stage-",StringComparison.Ordinal)==true&&c is Border).ToArray();
                Assert.Single(stages,c=>c.IsVisible);Assert.True(Find<Control>(window,"camera-stage-5").IsEffectivelyVisible);
                Capture(window,$"empty-{theme}-{language}-{width}");
                var file=Find<DropDownButton>(window,"camera-file-menu");Click(file,window);Assert.True(file.Flyout!.IsOpen);
                var save=window.FileActionsContent.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="camera-save-experiment");Assert.False(save.IsEnabled);
                Assert.Equal(font,save.FontSize);file.Flyout.Hide();Tick(window);
                var view=Find<ComboBox>(window,"camera-view-selector");var tabs=Find<TabControl>(window,"CameraResults");
                Assert.Equal(width==760,view.IsVisible);
                if(view.IsVisible)
                {
                    Click(view,window);window.KeyPress(Key.End,RawInputModifiers.None,PhysicalKey.End,null);window.KeyRelease(Key.End,RawInputModifiers.None,PhysicalKey.End,null);
                    window.KeyPress(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);window.KeyRelease(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);Tick(window);Assert.Equal(7,tabs.SelectedIndex);
                }
                for(var i=0;i<8;i++){view.SelectedIndex=i;Tick(window);Assert.Equal(i,tabs.SelectedIndex);}
                view.SelectedIndex=0;Tick(window);
                foreach(var control in new Control[]{Find<ComboBox>(window,"camera-stage-selector"),Find<ToggleButton>(window,"camera-stage-overview"),Find<ComboBox>(window,"camera-stage-zoom")})
                {var bounds=InWindow(control,window);Assert.InRange(bounds.Right,0,window.ClientSize.Width+.5);}
                var splitter=Find<GridSplitter>(window,"camera-parameter-splitter");var grid=Find<Grid>(window,"CameraWorkspace");var old=grid.ColumnDefinitions[0].ActualWidth;
                var point=InWindow(splitter,window).Center;window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseMove(point+new Vector(18,0));window.MouseUp(point+new Vector(18,0),MouseButton.Left);Tick(window);
                Assert.True(grid.ColumnDefinitions[0].ActualWidth>old+5);
                Assert.Equal(revision,app.Events.Revision);Assert.Null(window.Result);return Task.CompletedTask;
            }
            finally{window.Close();UiLocalization.Apply(previous);DisplayTypography.Configure(new AppSettings());}
        },CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
    [Theory][InlineData("Light")][InlineData("Dark")]
    public async Task ViewingStagesOverviewZoomAndABNeverChangesFrozenSensorOrImageResults(string theme)
    {
        using var session=SafeHeadlessUnitTestSession.StartNew(typeof(LaboratoryPresentationTests));
        await session.Dispatch(async()=>
        {
            DisplayTypography.Configure(new AppSettings{Theme=theme,FontSize=13});ThemeApplicationService.Apply(Avalonia.Application.Current!,theme);UiLocalization.Apply("zh-CN");
            using var app=WorkbenchApplication.Create();var window=new CameraImagingWindow(app){Width=1440,Height=900};
            try
            {
                window.Show();Tick(window);Find<ComboBox>(window,"camera-size").SelectedIndex=4;
                await window.RunAsync();Tick(window);var a=Assert.IsType<CameraImagingResult>(window.Result);var expected=await app.CameraImaging.SimulateAsync(a.Request);
                Assert.Equal(expected.RawDn,a.RawDn);Assert.Equal(expected.PhotoElectrons,a.PhotoElectrons);Assert.Equal(expected.Developed.Rgb24,a.Developed.Rgb24);
                var selector=Find<ComboBox>(window,"camera-stage-selector");var frames=new[]{a.Source,a.Optical,a.Electrons,a.Raw,a.Noiseless,a.Developed};
                for(var i=0;i<6;i++)
                {
                    selector.SelectedIndex=i;Tick(window);Assert.Same(a,window.Result);
                    var image=Find<Control>(window,"camera-stage-"+i).GetVisualDescendants().OfType<Image>().Single();AssertFrame(frames[i],Assert.IsType<WriteableBitmap>(image.Source));
                }
                selector.SelectedIndex=5;Tick(window);Capture(window,$"result-{theme}");
                Click(Find<ToggleButton>(window,"camera-stage-overview"),window);Tick(window);
                Assert.Equal(6,All(window).Count(c=>c is Border&&c.Name?.StartsWith("camera-stage-",StringComparison.Ordinal)==true&&c.IsVisible));Capture(window,$"overview-{theme}");
                Click(Find<ToggleButton>(window,"camera-stage-overview"),window);var zoom=Find<ComboBox>(window,"camera-stage-zoom");zoom.SelectedIndex=1;Tick(window);
                var actual=Find<Control>(window,"camera-stage-5").GetVisualDescendants().OfType<Image>().Single();Assert.Equal(a.Developed.Width,actual.Bounds.Width);Assert.Equal(a.Developed.Height,actual.Bounds.Height);
                zoom.SelectedIndex=0;Click(Find<Button>(window,"camera-pin"),window);Assert.Same(a,window.Comparison);
                Find<NumericUpDown>(window,"camera-exposure").Value=20;Assert.Null(window.Result);Assert.Same(a,window.Comparison);
                await window.RunAsync();Tick(window);var b=Assert.IsType<CameraImagingResult>(window.Result);Assert.Equal(a.MeanPhotoElectrons*2,b.MeanPhotoElectrons,8);
                var tabs=Find<TabControl>(window,"CameraResults");tabs.SelectedIndex=1;Tick(window);Capture(window,$"ab-{theme}");
                window.Width=760;window.Height=680;Tick(window);var views=Find<ComboBox>(window,"camera-comparison-selector");views.SelectedIndex=3;Tick(window);Capture(window,$"ab-{theme}-narrow");
                Assert.Same(a,window.Comparison);Assert.Same(b,window.Result);
                window.Width=1440;window.Height=900;tabs.SelectedIndex=0;Tick(window);
                DisplayTypography.Configure(new AppSettings{Theme=theme,FontSize=22});DisplayTypography.ApplyRecursively(window,13);Tick(window);Capture(window,$"result-{theme}-large");
                Assert.Same(a,window.Comparison);Assert.Same(b,window.Result);AssertFrame(b.Developed,Assert.IsType<WriteableBitmap>(Find<Control>(window,"camera-stage-5").GetVisualDescendants().OfType<Image>().Single().Source));
                app.Documents.NewBlank();Assert.Null(window.Result);Assert.Null(window.Comparison);
            }
            finally{window.Close();DisplayTypography.Configure(new AppSettings());}
        },CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
    private static void AssertFrame(CameraImage expected,WriteableBitmap actual)
    {
        Assert.Equal(expected.Width,actual.PixelSize.Width);Assert.Equal(expected.Height,actual.PixelSize.Height);
        using var buffer=actual.Lock();var row=new byte[expected.Width*4];
        for(var y=0;y<expected.Height;y++){Marshal.Copy(buffer.Address+y*buffer.RowBytes,row,0,row.Length);for(var x=0;x<expected.Width;x++){var i=(y*expected.Width+x)*3;Assert.Equal(expected.Rgb24[i],row[x*4+2]);Assert.Equal(expected.Rgb24[i+1],row[x*4+1]);Assert.Equal(expected.Rgb24[i+2],row[x*4]);}}
    }
    private static IEnumerable<Control> All(Window window)=>window.GetVisualDescendants().OfType<Control>().Concat(window.GetLogicalDescendants().OfType<Control>()).Distinct();
    private static T Find<T>(Window window,string name) where T:Control=>All(window).OfType<T>().Single(c=>c.Name==name);
    private static Rect InWindow(Control control,Window window)=>new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static void Click(Control control,Window window){var p=InWindow(control,window).Center;window.MouseMove(p);window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);Tick(window);}
    private static void Tick(Window window){Dispatcher.UIThread.RunJobs();window.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
    private static void Capture(Window window,string name){var root=Environment.GetEnvironmentVariable("OPTICAL_CAMERA_WORKSPACE_CAPTURE_DIR");if(string.IsNullOrWhiteSpace(root))return;Directory.CreateDirectory(root);Tick(window);using var image=window.CaptureRenderedFrame();Assert.NotNull(image);image.Save(Path.Combine(root,name+".png"),PngBitmapEncoderOptions.Default);}
}
