using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class CoatingDesignLabLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-launcher-" + Guid.NewGuid().ToString("N"));
    private static string ExecutableName => CoatingDesignLabLauncher.ApplicationName
        + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

    [Fact]
    public void ExplicitAssemblyPathIsOneArgumentWithoutShellParsing()
    {
        var assembly = FileAt("separate lab & data", CoatingDesignLabLauncher.ApplicationName + ".dll");

        var start = CoatingDesignLabLauncher.ResolveStartInfo(_root, assembly);

        Assert.Equal("dotnet", start.FileName);
        Assert.Equal(new[] { assembly }, start.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(assembly), start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void OptionalInstalledLabStartsAsASeparateExecutable()
    {
        var executable = FileAt("labs", "CoatingDesign", ExecutableName);

        var start = CoatingDesignLabLauncher.ResolveStartInfo(_root);

        Assert.Equal(executable, start.FileName);
        Assert.Empty(start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void BadExplicitPathDoesNotSilentlyLaunchAnotherInstalledLab()
    {
        FileAt("labs", "CoatingDesign", ExecutableName);

        Assert.Throws<FileNotFoundException>(() => CoatingDesignLabLauncher.ResolveStartInfo(
            _root, Path.Combine(_root, "missing", ExecutableName)));
        Assert.Throws<FileNotFoundException>(() => CoatingDesignLabLauncher.ResolveStartInfo(_root, ExecutableName));
    }

    [Fact]
    public void DevelopmentEntryUsesTheMatchingSeparateBuild()
    {
        FileAt("OptilandWorkbench.slnx");
        var host = Path.Combine(_root, "src", "OptilandWorkbench.App", "bin",
            CoatingDesignLabLauncher.BuildConfiguration, "net10.0");
        Directory.CreateDirectory(host);
        var executable = FileAt("labs", "CoatingDesign", "src", CoatingDesignLabLauncher.ApplicationName,
            "bin", CoatingDesignLabLauncher.BuildConfiguration, "net10.0", ExecutableName);

        var start = CoatingDesignLabLauncher.ResolveStartInfo(host);

        Assert.Equal(executable, start.FileName);
        Assert.Empty(start.ArgumentList);
    }

    [Fact]
    public void MissingMatchingBuildDoesNotFallBackToStaleOtherConfiguration()
    {
        FileAt("OptilandWorkbench.slnx");
        var otherConfiguration = CoatingDesignLabLauncher.BuildConfiguration == "Debug" ? "Release" : "Debug";
        FileAt("labs", "CoatingDesign", "src", CoatingDesignLabLauncher.ApplicationName,
            "bin", otherConfiguration, "net10.0", ExecutableName);

        Assert.Throws<FileNotFoundException>(() => CoatingDesignLabLauncher.ResolveStartInfo(_root));
    }

    [Fact]
    public void MissingLabReportsTheSeparateBuildRequirement()
    {
        Directory.CreateDirectory(_root);

        var error = Assert.Throws<FileNotFoundException>(() => CoatingDesignLabLauncher.ResolveStartInfo(_root));

        Assert.Contains("单独构建或安装实验室", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void EntryDoesNotAddLaboratoryAssemblyDependenciesToTheProduct()
    {
        foreach (var assembly in new[] { typeof(MainWindow).Assembly, typeof(IWorkbenchApplication).Assembly, typeof(Optic).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("OptilandWorkbench.CoatingDesign.", StringComparison.Ordinal));
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
