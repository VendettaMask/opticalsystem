using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class InitialStructureLabLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-launcher-" + Guid.NewGuid().ToString("N"));
    private static string ExecutableName => InitialStructureLabLauncher.ApplicationName
        + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

    [Fact]
    public void ExplicitAssemblyPathIsOneArgumentWithoutShellParsing()
    {
        var assembly = FileAt("separate lab & data", InitialStructureLabLauncher.ApplicationName + ".dll");

        var start = InitialStructureLabLauncher.ResolveStartInfo(_root, assembly);

        Assert.Equal("dotnet", start.FileName);
        Assert.Equal(new[] { assembly }, start.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(assembly), start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void OptionalInstalledLabStartsAsASeparateExecutable()
    {
        var executable = FileAt("labs", "InitialStructure", ExecutableName);

        var start = InitialStructureLabLauncher.ResolveStartInfo(_root);

        Assert.Equal(executable, start.FileName);
        Assert.Empty(start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void BadExplicitPathDoesNotSilentlyLaunchAnotherInstalledLab()
    {
        FileAt("labs", "InitialStructure", ExecutableName);

        Assert.Throws<FileNotFoundException>(() => InitialStructureLabLauncher.ResolveStartInfo(
            _root, Path.Combine(_root, "missing", ExecutableName)));
        Assert.Throws<FileNotFoundException>(() => InitialStructureLabLauncher.ResolveStartInfo(_root, ExecutableName));
    }

    [Fact]
    public void DevelopmentEntryUsesTheMatchingSeparateBuild()
    {
        FileAt("OptilandWorkbench.slnx");
        var host = Path.Combine(_root, "src", "OptilandWorkbench.App", "bin",
            InitialStructureLabLauncher.BuildConfiguration, "net10.0");
        Directory.CreateDirectory(host);
        var executable = FileAt("labs", "InitialStructure", "src", InitialStructureLabLauncher.ApplicationName,
            "bin", InitialStructureLabLauncher.BuildConfiguration, "net10.0", ExecutableName);

        var start = InitialStructureLabLauncher.ResolveStartInfo(host);

        Assert.Equal(executable, start.FileName);
        Assert.Empty(start.ArgumentList);
    }

    [Fact]
    public void MissingMatchingBuildDoesNotFallBackToStaleOtherConfiguration()
    {
        FileAt("OptilandWorkbench.slnx");
        var otherConfiguration = InitialStructureLabLauncher.BuildConfiguration == "Debug" ? "Release" : "Debug";
        FileAt("labs", "InitialStructure", "src", InitialStructureLabLauncher.ApplicationName,
            "bin", otherConfiguration, "net10.0", ExecutableName);

        Assert.Throws<FileNotFoundException>(() => InitialStructureLabLauncher.ResolveStartInfo(_root));
    }

    [Fact]
    public void MissingLabReportsTheSeparateBuildRequirement()
    {
        Directory.CreateDirectory(_root);

        var error = Assert.Throws<FileNotFoundException>(() => InitialStructureLabLauncher.ResolveStartInfo(_root));

        Assert.Contains("单独构建或安装实验室", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void EntryDoesNotAddLaboratoryAssemblyDependenciesToTheProduct()
    {
        foreach (var assembly in new[] { typeof(MainWindow).Assembly, typeof(IWorkbenchApplication).Assembly, typeof(Optic).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("OptilandWorkbench.InitialStructure.", StringComparison.Ordinal));
        }
    }

    private string FileAt(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
