using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal static class InstallationVerification
{
    // Explicit, non-GUI verification. Never register associations, save the
    // opened project, load the real user's settings or persist a workspace.
    internal static async Task RunAsync(string[] args)
    {
        string? Value(string key)
        {
            var index = Array.IndexOf(args, key);
            if (index < 0) return null;
            if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {key}.");
            return args[index + 1];
        }

        var reportPath = Value("--verify-installation")
            ?? throw new ArgumentException("An explicit verification report path is required.");
        var projectPath = Value("--project");
        var settingsPath = Value("--settings");
        var hold = Value("--hold-milliseconds") is { } holdText ? int.Parse(holdText) : 0;
        if (hold is < 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(args));
        using var application = WorkbenchApplication.Create(
            userCatalogDirectory: Path.Combine(AppContext.BaseDirectory, ".verification-no-user-catalog"));
        if (projectPath is not null) await application.Documents.OpenAsync(projectPath);
        var document = application.Documents.GetSnapshot();
        var settings = settingsPath is null ? null : AppSettings.Load(settingsPath);
        if (settings?.LoadWarning is not null) throw new InvalidDataException(settings.LoadWarning);
        var modules = ProductModules.Verify();
        var report = new
        {
            Success = true,
            Version = typeof(InstallationVerification).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Framework = Environment.Version.ToString(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProductAppId = WindowsInstallationLifetime.ReadProductId(AppContext.BaseDirectory),
            document.Revision,
            document.SurfaceCount,
            ProjectOpened = projectPath is not null,
            SettingsLoaded = settings is not null,
            SettingsTheme = settings?.Theme,
            modules.Modules,
            ModuleLibraries = modules.Libraries,
            ModulesInProcess = modules.InProcess,
            UserDataWritten = false,
            GuiVerified = false
        };
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        if (hold > 0) await Task.Delay(hold);
    }
}
