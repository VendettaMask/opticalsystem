using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Tests;
using Nsc = OpticalSystemDesign.Core.NonSequential;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
await session.Dispatch(async () =>
{
    var watch = Stopwatch.StartNew();
    var objects = new List<Nsc.NonSequentialObjectDefinition>();
    Guid Add(Nsc.NonSequentialObjectKind kind, string name, double x, double y, double z, Nsc.NonSequentialObjectParameters parameters, Guid? reference = null)
    {
        var id = Guid.NewGuid(); objects.Add(new(id, name, kind, true, true, new CoordinateSystem(new(x,y,z), 0,0,0), reference, null, parameters)); return id;
    }
    for (var i = 1; i <= 32; i++)
    {
        var frame = Add(Nsc.NonSequentialObjectKind.Null, $"CS{i:00} 通道坐标", ((i-1)%8-3.5)*38, ((i-1)/8-1.5)*38, 0, new Nsc.NullObjectParameters());
        Add(Nsc.NonSequentialObjectKind.SourceEllipse, $"S{i:00} 椭圆面光源", 0,0,-30, new Nsc.SourceEllipseParameters(3,3,9,1,1,9,200), frame);
        Add(Nsc.NonSequentialObjectKind.StandardLens, $"L1-{i:00} 准直透镜",0,0,0,new Nsc.StandardLensParameters(40,-40,0,0,4,12.5,"N-BK7"),frame);
        Add(Nsc.NonSequentialObjectKind.StandardLens, $"L2-{i:00} 中继透镜",0,0,24,new Nsc.StandardLensParameters(55,-55,0,0,3,10,"N-BK7"),frame);
        Add(Nsc.NonSequentialObjectKind.DetectorRectangle, $"D{i:00} 矩形探测器",0,0,70,new Nsc.DetectorRectangleParameters(28,28,32,32,true,true),frame);
        foreach (var (x,y,w,h) in new[] {(-16d,0d,2d,32d),(16d,0d,2d,32d),(0d,-16d,30d,2d),(0d,16d,30d,2d)})
            Add(Nsc.NonSequentialObjectKind.Box,$"M{i:00}-{objects.Count%13} 遮光支撑",x,y,20,new Nsc.BoxParameters(w,h,100,"Air",Nsc.NonSequentialSurfaceBehavior.Absorbing),frame);
        foreach (var (x,y) in new[] {(-13d,-13d),(-13d,13d),(13d,-13d),(13d,13d)})
            Add(Nsc.NonSequentialObjectKind.Box,$"B{i:00}-{objects.Count%13} 挡光块",x,y,35,new Nsc.BoxParameters(4,4,3,"Air",Nsc.NonSequentialSurfaceBehavior.Absorbing),frame);
    }
    for (var i = 0; i < 16; i++) Add(Nsc.NonSequentialObjectKind.Box,$"P{i+1:00} 平台支撑",(i%8-3.5)*38,i<8?-82:82,20,new Nsc.BoxParameters(30,4,100,"Air",Nsc.NonSequentialSurfaceBehavior.Absorbing));
    var doc = new Nsc.NonSequentialDocument("32 通道照明检测平台",[new("550 nm",550,1,true)],objects);
    var project = Path.Combine(output,"complex-demo.staropt");
    await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()],0,NonSequentialDocument:doc),project);
    using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(project); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
    var physicalIdentity = app.NonSequential.GetDesignSceneIdentity();
    var layout = await app.NonSequentialAnalysis.PrepareLayoutSessionAsync();
    var scene = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
    var sceneBuildMs = watch.ElapsedMilliseconds;
    var window = new MainWindow(new AppSettings {Theme="Light",Language="zh-CN",WorkbenchMode="NonSequential",WindowWidth=1840,WindowHeight=1120}, app, new WorkspaceSessionStore(Path.Combine(output,"session")));
    bool ready=false; window.StartupCompleted += (_,_)=>ready=true;
    try
    {
        window.Show(); for(var n=0;n<250&&!ready;n++){await Task.Delay(10);Dispatcher.UIThread.RunJobs();}
        if(!ready)throw new InvalidOperationException("Shell startup failed.");
        var manager=typeof(MainWindow).GetField("_panels",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var old=(Control)manager.GetType().GetProperty("WorkspaceGrid")!.GetValue(manager)!;
        var host=(Grid)old.Parent!; host.Children.Remove(old);
        var ui=new ComplexWorkspace(app.NonSequential.GetDocument(),scene.ThreeDimensional!,layout);
        host.Children.Insert(0,ui.Root);
        var shell=(DockPanel)host.Parent!; var bar=Ui.Surface(Ui.Text("非序列模式   |   32 通道   |   432 个场景对象   |   长度 mm   |   波长 550 nm",12));bar.Height=27;bar.Padding=new Thickness(12,0);DockPanel.SetDock(bar,Avalonia.Controls.Dock.Bottom);shell.Children.RemoveAt(1);shell.Children.Insert(1,bar);
        var tabs=window.GetVisualDescendants().OfType<TabControl>().First(t=>t.Classes.Contains("ribbon-tabs")); tabs.SelectedIndex=1;((TabItem)tabs.Items[1]!).Content=ui.Commands;
        window.Title="32 通道照明检测平台 · 复杂场景工作区预览 — Optical System Design";
        UiLocalization.Attach(window);DisplayTypography.Apply(window);
        async Task Settle(){for(var i=0;i<4;i++){Dispatcher.UIThread.RunJobs();window.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick();await Task.Delay(30);}}
        void Capture(string name){using var frame=window.CaptureRenderedFrame()??throw new InvalidOperationException("No pixels.");frame.Save(Path.Combine(output,name),PngBitmapEncoderOptions.Default);}
        ui.ShowOverview();await Settle();Capture("v3-overview.png");
        ui.IsolateChannel(4); ui.SelectNumbers([43]);await Settle();Capture("v3-workspace.png");
        ui.Search.Text="N-BK7";await Settle();if(ui.MatchedObjectCount!=64)throw new InvalidOperationException("Material search failed.");
        ui.Search.Text="L2-04";await Settle();
        if(ui.MatchedObjectCount!=1||!ui.TreeHasChannelPath(4))throw new InvalidOperationException("Ancestor-preserving search failed.");
        Capture("v3-search.png"); ui.Search.Text="";
        ui.SelectNumbers([42,43]);await Settle();
        if(ui.SelectedNumbers.Count!=2||!ui.MixedGeometryShown)throw new InvalidOperationException("Common/mixed property summary failed.");
        Capture("v3-batch.png");
        ui.SaveSelection("通道04 / 两片透镜");if(ui.SavedSelections.Count!=1)throw new InvalidOperationException("Selection set failed.");
        ui.SetNavigation("光学");await Settle();Capture("v3-optical.png");
        if(ui.NavigationRootKey!="optical")throw new InvalidOperationException("Optical navigation failed.");
        ui.SetNavigation("仿真");await Settle();Capture("v3-simulation.png");
        if(ui.NavigationRootKey!="simulation")throw new InvalidOperationException("Simulation navigation failed.");
        ui.SetNavigation("结构");ui.SelectNumbers([43]);await Settle();
        if(ui.NavigationRootKey!="project")throw new InvalidOperationException("Structure navigation failed.");
        ui.ShowObjectTable(true);await Settle();Capture("v3-table.png");ui.ShowObjectTable(false);await Settle();
        var point=ui.View.TranslatePoint(ui.View.PickPoint(42),window)!.Value;
        window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);await Settle();
        if(!ui.SelectedNumbers.SetEquals([42]))throw new InvalidOperationException("Model selection failed.");
        ui.MoveToPresentationFolder(42,"光学检查集合");
        if(physicalIdentity!=app.NonSequential.GetDesignSceneIdentity())throw new InvalidOperationException("Organization changed physical identity.");
        var checks=new {schema=1,date="2026-11-23",scope="Complex workspace candidate, not production integration",objects=objects.Count,channels=32,sources=32,lenses=64,detectors=32,referenceFrames=32,layoutBranches=layout.BranchCount,layoutSegments=layout.SegmentCount,sceneBuildMilliseconds=sceneBuildMs,materializedTreeItems=ui.MaterializedTreeItems,checks=new {threeNavigationViews=true,searchKeepsAncestorPath=true,selectionSynchronizes=true,commonAndMixedProperties=true,localSelectionSets=true,readOnlyObjectTable=true,isolationKeepsPhysicalScene=true,organizationalFolderKeepsPhysicalIdentity=true},productionWorkspaceChanged=false,batchApplyImplemented=false};
        File.WriteAllText(Path.Combine(output,"v3-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
    }
    finally{window.Close();}
},CancellationToken.None);
Console.WriteLine(output);

static class Ui
{
    public static TextBlock Text(string value,double size=14,bool bold=false)=>new(){Text=value,FontSize=size,FontWeight=bold?FontWeight.SemiBold:FontWeight.Normal,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    public static Border Surface(Control content,Thickness? border=null){var result=new Border{Child=content,BorderThickness=border??new Thickness(0)};result.BindThemeResource(Border.BackgroundProperty,ThemeResourceBindings.Surface);result.BindThemeResource(Border.BorderBrushProperty,ThemeResourceBindings.Border);return result;}
    public static LocalIcon Icon(string name,double size=18)=>new(){IconName=name,Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center};
    public static Button Command(string icon,string label)=>new(){Content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,Children={Icon(icon),Text(label)}},Padding=new Thickness(10,6),MinHeight=34};
}
