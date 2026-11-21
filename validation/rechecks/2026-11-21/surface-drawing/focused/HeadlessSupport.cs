using Avalonia;
using Avalonia.Headless;
namespace OpticalSystemDesign.Tests;
public static class DiagramViewportFitTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessAvaloniaCollection { public const string Name = "Headless Avalonia"; }
