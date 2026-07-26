using Avalonia;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WindowsStarOptFileAssociation.TryRegister();
        if (args.Contains("--register-file-associations", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
