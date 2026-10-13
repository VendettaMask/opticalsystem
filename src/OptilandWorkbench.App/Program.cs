using Avalonia;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var productId = WindowsInstallationLifetime.ProductId;
        WindowsInstallationLifetime installationLifetime;
        try
        {
            ProductModules.Verify();
            productId = WindowsInstallationLifetime.ReadProductId(AppContext.BaseDirectory);
            if (OperatingSystem.IsWindows())
            {
                var recovered = WindowsInstallationRecovery.RecoverPending(AppContext.BaseDirectory, productId);
                if (recovered.Recovered && !args.Contains("--verify-installation", StringComparer.Ordinal)
                    && (recovered.ActiveExecutable is null || !Path.GetDirectoryName(recovered.ActiveExecutable)!
                        .Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("上次中断安装已恢复。请从恢复后的快捷方式启动旧版本，或重新运行安装包完成升级。");
            }
            if (OperatingSystem.IsWindows())
                WindowsInstallationCleanup.TryRun(AppContext.BaseDirectory, productId);
            installationLifetime = WindowsInstallationLifetime.Enter(productId, AppContext.BaseDirectory);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or System.Security.SecurityException or KeyNotFoundException
            or TypeLoadException or System.Reflection.ReflectionTypeLoadException or BadImageFormatException)
        {
            if (args.Contains("--verify-installation", StringComparer.Ordinal))
                Console.Error.WriteLine(exception.Message);
            else WindowsInstallationLifetime.ShowBlockedStartup(exception.Message);
            Environment.ExitCode = 75;
            return;
        }
        using var lifetime = installationLifetime;
        if (args.Length > 0 && args[0] is "--batch-coating-examples" or "--batch-coating-daily-examples")
        {
            RunCoatingExamples(
                args.Length > 1 ? args[1] : "coating-examples",
                args[0] == "--batch-coating-daily-examples");
            return;
        }
        if (args.Contains("--verify-installation", StringComparer.Ordinal))
        {
            InstallationVerification.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (productId == WindowsInstallationLifetime.ProductId)
            WindowsStarOptFileAssociation.TryRegister();
        if (args.Contains("--register-file-associations", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Keep module resolution inside the guarded startup call, not Main's JIT compilation.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunCoatingExamples(string directory, bool daily) =>
        Modules.CoatingExampleBatch.RunExamplesAsync(directory, daily).GetAwaiter().GetResult();

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
