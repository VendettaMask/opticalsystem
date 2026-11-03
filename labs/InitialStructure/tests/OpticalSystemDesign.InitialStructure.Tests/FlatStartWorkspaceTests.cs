using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class FlatStartDesktopTests
{
    [Theory]
    [InlineData("Light","zh-CN",1240,13)]
    [InlineData("Dark","en-US",1000,18)]
    [InlineData("Light","ja-JP",480,18)]
    [InlineData("Light","zh-CN",1440,22)]
    public async Task CompactToolbarAndNativePaneSplittersPreserveInputsWithoutStartingSearch(string theme,string language,int width,double font)
    {
        using var host=new DesktopHost();
        await host.Run(async()=>
        {
            var previousLanguage=UiLocalization.CurrentLanguage;
            var previousTheme=ThemeRegistry.FromActualVariant(Avalonia.Application.Current!.ActualThemeVariant).SettingsValue;
            DisplayTypography.Configure(new AppSettings{Theme=theme,FontSize=font});ThemeApplicationService.Apply(Avalonia.Application.Current!,theme);UiLocalization.Apply(language);
            var window=host.Window(width:width,defaultInputs:true);
            try
            {
                window.Show();await window.Ready;TickChrome(window);
                using var traces=SequentialTraceMeasurement.Begin();
                var commands=Find<WrapPanel>(window,"InitialStructureCommands");
                Assert.Equal(5,commands.Children.OfType<Button>().Count());
                Assert.InRange(commands.Bounds.Height,30,115);
                foreach(var command in commands.Children.OfType<Button>())
                {
                    var bounds=new Rect(command.Bounds.Size).TransformToAABB(command.TransformToVisual(window)!.Value);
                    Assert.InRange(bounds.Right,0,window.ClientSize.Width+.5);Assert.InRange(bounds.Bottom,0,window.ClientSize.Height);
                }
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),label=>label.Text==UiLocalization.Text("自动初始结构"));
                var focal=Find<NumericUpDown>(window,"FocalLength").Value;var fNumber=Find<NumericUpDown>(window,"FNumber").Value;
                Click(window,"RunRecords");TickChrome(window);var records=Find<DropDownButton>(window,"RunRecords");Assert.True(records.Flyout!.IsOpen);
                Assert.True(Find<Button>(window,"OpenRun").IsEffectivelyVisible);Assert.False(Find<Button>(window,"Resume").IsEnabled);
                records.Flyout.Hide();Click(window,"DesignTools");TickChrome(window);var tools=Find<DropDownButton>(window,"DesignTools");Assert.True(tools.Flyout!.IsOpen);
                Assert.True(Find<Button>(window,"OpenAutonomousTopology").IsEffectivelyVisible);tools.Flyout.Hide();TickChrome(window);
                Capture(window,$"workspace-{theme}-{language}-{width}-{font}");
                if(width>=820)
                {
                    var panes=Find<Grid>(window,"InitialStructurePanes");var before=panes.ColumnDefinitions[0].ActualWidth;
                    DragChrome(Find<GridSplitter>(window,"ParameterSplitter"),new Vector(24,0),window);Assert.True(panes.ColumnDefinitions[0].ActualWidth>before+10);
                    var workspace=Find<Grid>(window,"CandidateWorkspace");var height=workspace.RowDefinitions[0].ActualHeight;
                    DragChrome(Find<GridSplitter>(window,"CandidateDetailsSplitter"),new Vector(0,24),window);Assert.True(workspace.RowDefinitions[0].ActualHeight>height+10);
                }
                Assert.Equal(focal,Find<NumericUpDown>(window,"FocalLength").Value);Assert.Equal(fNumber,Find<NumericUpDown>(window,"FNumber").Value);
                Assert.Null(window.CurrentCheckpoint);Assert.Equal(0,traces.RayCount);
                Click(window,"Validate");await window.PendingOperation;TickChrome(window);
                Assert.Equal(UiLocalization.Text("预检查通过；搜索计划与过程页可查看形式覆盖范围。"),Find<TextBlock>(window,"Status").Text);
                Assert.Null(window.CurrentCheckpoint);Assert.Equal(0,traces.RayCount);
            }
            finally{window.Close();DisplayTypography.Configure(new AppSettings());ThemeApplicationService.Apply(Avalonia.Application.Current!,previousTheme);UiLocalization.Apply(previousLanguage);}
        });
    }
    private static void TickChrome(Window window){Dispatcher.UIThread.RunJobs();window.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
    private static void DragChrome(Control control,Vector movement,Window window)
    {
        control.BringIntoView();TickChrome(window);
        var point=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)!.Value;
        window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseMove(point+movement);window.MouseUp(point+movement,MouseButton.Left);TickChrome(window);
    }
}