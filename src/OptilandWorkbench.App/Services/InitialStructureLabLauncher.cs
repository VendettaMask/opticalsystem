using System.Diagnostics;

namespace OptilandWorkbench.App.Services;

// Process boundary only: the formal application never loads laboratory assemblies.
internal static class InitialStructureLabLauncher
{
    internal const string ApplicationName = "OptilandWorkbench.InitialStructure.App";
    internal const string PathEnvironmentVariable = "OPTILAND_INITIAL_STRUCTURE_LAB_PATH";
#if DEBUG
    internal const string BuildConfiguration = "Debug";
#else
    internal const string BuildConfiguration = "Release";
#endif

    internal static void Start()
    {
        var startInfo = ResolveStartInfo(AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(PathEnvironmentVariable));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("未能启动初始结构实验室。");
    }

    internal static ProcessStartInfo ResolveStartInfo(string applicationDirectory, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath) || !File.Exists(configuredPath))
            {
                throw new FileNotFoundException("配置的初始结构实验室程序不存在，请检查独立程序路径。", configuredPath);
            }
            return CreateStartInfo(configuredPath);
        }

        var deployed = FindApplication(Path.Combine(applicationDirectory, "labs", "InitialStructure"));
        if (deployed is not null) return CreateStartInfo(deployed);

        // Development builds locate the separate lab output, without building it
        // or adding it to the formal solution, installer, or dependency graph.
        for (var directory = new DirectoryInfo(applicationDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) continue;
            var built = FindApplication(Path.Combine(directory.FullName, "labs", "InitialStructure",
                "src", ApplicationName, "bin", BuildConfiguration, "net10.0"));
            if (built is not null) return CreateStartInfo(built);
            break;
        }

        throw new FileNotFoundException("未找到独立的初始结构实验室程序。请先单独构建或安装实验室，再使用此入口。");
    }

    private static string? FindApplication(string directory)
    {
        var executable = Path.Combine(directory, ApplicationName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        if (File.Exists(executable)) return executable;
        var assembly = Path.Combine(directory, ApplicationName + ".dll");
        return File.Exists(assembly) ? assembly : null;
    }

    private static ProcessStartInfo CreateStartInfo(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        var isAssembly = Path.GetExtension(absolutePath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isAssembly ? "dotnet" : absolutePath,
            WorkingDirectory = Path.GetDirectoryName(absolutePath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (isAssembly) startInfo.ArgumentList.Add(absolutePath);
        return startInfo;
    }
}
