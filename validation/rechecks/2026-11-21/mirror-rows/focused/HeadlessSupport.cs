using Avalonia;
using Avalonia.Headless;

namespace OpticalSystemDesign.Tests;

// The same bootstrap used by DiagramViewportFitTests; exclude unrelated analysis tests.
public static class DiagramViewportFitTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessAvaloniaCollection
{
    public const string Name = "Headless Avalonia";
}
