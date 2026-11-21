using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Contracts;

internal static class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [STAThread] static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(output, ".settings"));
        AppBuilder.Configure<OpticalSystemDesign.App.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseHarfBuzz().SetupWithoutStarting();
        typeof(OpticalSystemDesign.App.App).GetMethod("ApplyTheme",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Avalonia.Application.Current,new object[] { "Light" });
        using var cancellation = new CancellationTokenSource();
        int exit = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Capture(args[0], output, args.Length > 2 ? args[2] : null); }
            catch(Exception e) { File.WriteAllText(Path.Combine(output,"error.txt"), e.ToString()); exit=1; }
            finally { cancellation.Cancel(); }
        });
        try { Dispatcher.UIThread.MainLoop(cancellation.Token); } catch(OperationCanceledException) { }
        return exit;
    }
    static async Task Capture(string source, string output, string? selected)
    {
        using var app=WorkbenchApplication.Create(); await app.Documents.OpenAsync(Path.GetFullPath(source));
        var keys=app.Analyses.AnalysisNames.Select(n=>new { Name=n, Key=app.Analyses.CanonicalKey(n) }).ToArray();
        var graphicsOnly=Environment.GetEnvironmentVariable("OPTICAL_VISUAL_CAPTURE_GRAPHICS_ONLY")=="1";
        var reports=new HashSet<string> { "Single Ray Trace","First Order","Cardinal Points Data","Seidel Coefficients","Zernike Fringe","Zernike Standard","Zernike Annular","Prescription Report","System Data Report","Classified Data Report" };
        if(graphicsOnly) keys=keys.Where(x=>!reports.Contains(x.Key)).ToArray();
        File.WriteAllText(Path.Combine(output,"catalog.json"), JsonSerializer.Serialize(keys, Json));
        var manifestPath=Path.Combine(output,"manifest.json");
        var results = File.Exists(manifestPath)?JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement.GetProperty("Results").EnumerateArray().Select(x=>(object)x.Clone()).ToList():new List<object>();
        var settings=new AppSettings { Theme="Light", WindowWidth=1600,WindowHeight=1000 };
        var host=new Border { Background=Avalonia.Media.Brushes.White }; var window=new Window { Width=1600,Height=1000,Content=host }; window.Show();
        foreach(var (key,dimension,mode) in new[] { ("2D Layout",SceneDimension.TwoDimensional,ViewerPresentationMode.OpticalLayout),("3D Layout",SceneDimension.ThreeDimensional,ViewerPresentationMode.OpticalLayout),("Solid Model",SceneDimension.ThreeDimensional,ViewerPresentationMode.SolidModel) })
        {
            if(selected!=null && selected!=key) continue;
            var slug=key.ToLowerInvariant().Replace(' ','-'); if(File.Exists(Path.Combine(output,slug+".png"))) continue;
            using var viewer=new ViewerPanel(app.Visualization,app.Events,new SurfaceSelectionService(),dimension,mode);
            host.Child=viewer; await Task.Delay(100);
            var generation=(int)typeof(ViewerPanel).GetField("_refreshGeneration",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(viewer)!;
            await (Task)typeof(ViewerPanel).GetMethod("RefreshAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(viewer,new object[] { TimeSpan.Zero,CancellationToken.None,generation })!;
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap=new RenderTargetBitmap(new PixelSize(1600,1000),new Vector(96,96)); bitmap.Render(host);bitmap.Save(Path.Combine(output,slug+".png"),PngBitmapEncoderOptions.Default);
            results.Add(new {Key=key,Name=key,Status="captured",Image=slug+".png"});host.Child=null;
        }
        foreach(var item in keys)
        {
            if(selected!=null && item.Key!=selected) continue;
            var slug=string.Concat(item.Key.Select(c=>char.IsAsciiLetterOrDigit(c)?char.ToLowerInvariant(c):'-')).Trim('-');
            if(File.Exists(Path.Combine(output,slug+".png"))) continue;
            var values=app.Analyses.MergeSettings(item.Name,null);
            File.WriteAllText(Path.Combine(output,slug+"-settings.json"),JsonSerializer.Serialize(values,Json));
            using var panel=new AnalysisPanel(app.Analyses,app.Visualization,app.Documents,app.Events,settings,item.Name,initialSettings:values);
            string status="captured"; string? error=null; string? outcome=null; string? reason=null; var started=DateTime.UtcNow;
            try
            {
                var method=typeof(AnalysisPanel).GetMethod("RunForGuiCaptureAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var run=(Task<bool>)method.Invoke(panel,null)!;
                var done=await Task.WhenAny(run,Task.Delay(TimeSpan.FromSeconds(75)));
                if(done!=run)
                {
                    (typeof(AnalysisPanel).GetField("_runCancellation",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel) as CancellationTokenSource)?.Cancel();
                    await Task.WhenAny(run,Task.Delay(3000));
                    status="timeout";
                }
                else if(!await run) status="analysis-error";
                if(typeof(AnalysisPanel).GetField("_view",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel) is AnalysisViewDto actualView)
                {
                    outcome=actualView.Outcome.ToString();reason=actualView.OutcomeReason;
                    if(status=="captured" && actualView.Outcome!=AnalysisOutcome.Success)status=actualView.Outcome==AnalysisOutcome.NotApplicable?"not-applicable":"unavailable";
                }
                host.Child=panel;
                await Task.Delay(60); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var image=new RenderTargetBitmap(new PixelSize(1600,1000),new Vector(96,96));
                image.Render(host); image.Save(Path.Combine(output,slug+".png"),PngBitmapEncoderOptions.Default);
            }
            catch(Exception e) { status="failed"; error=e.ToString(); }
            finally { host.Child=null; }
            results.Add(new { item.Key, item.Name, Status=status, Outcome=outcome, Reason=reason, Error=error, Image=File.Exists(Path.Combine(output,slug+".png"))?slug+".png":null, Seconds=(DateTime.UtcNow-started).TotalSeconds });
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new { Source=Path.GetFullPath(source),Kind="actual-current-Avalonia-AnalysisPanel-headless",Results=results },Json));
            Console.WriteLine(item.Key+": "+status);
            if(status=="timeout") break;
        }
        window.Close();
    }
}
