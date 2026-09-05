using System.Diagnostics;
using System.Xml.Linq;

namespace OptilandWorkbench.Tests;

public sealed class WindowsPackagingTests
{
    [Fact]
    public void PackagingUsesPortableUntrimmedOutputWithoutOverwritingTrackedLocks()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "publish-windows.ps1"));
        Assert.Contains("--self-contained true", script);
        Assert.Contains("-p:PublishSingleFile=false", script);
        Assert.Contains("-p:PublishTrimmed=false", script);
        Assert.Contains("NuGetLockFilePath=obj/packages.$Runtime.lock.json", script);
        Assert.Contains("[System.IO.Directory]::Move($stagingDirectory, $packageDirectory)", script);
        Assert.DoesNotContain("Remove-Item", script);
        Assert.Contains("LensLibrary\\StockCatalogs", script);
        var project = XDocument.Load(Path.Combine(root, "src", "OptilandWorkbench.App", "OptilandWorkbench.App.csproj"));
        Assert.Contains(project.Descendants("OutputType"), property =>
            property.Value == "WinExe" && (string?)property.Attribute("Condition") == "'$(WindowsPackage)' == 'true'");
        Assert.Contains("%BUILD_EXIT_CODE%", File.ReadAllText(Path.Combine(root, "Build-Exe.cmd")));
    }

    [Fact]
    public void WindowsPackagingDryRunAcceptsSpacesAndDoesNotCreateOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var destination = Path.Combine(Path.GetTempPath(), $"optical package dry run {Guid.NewGuid():N}");
        var result = RunScript("-WhatIf", "-OutputRoot", destination);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(destination, result.Output);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void WindowsPackagingRejectsUnsupportedRuntimeBeforePublishing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var result = RunScript("-Runtime", "linux-x64");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("linux-x64", result.Output);
    }

    [Fact]
    public void InstallerUsesChineseWizardAndLoggedUninstallWithoutDeletingUserData()
    {
        var root = RepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(root, "packaging", "windows", "OpticalSystemDesign.iss"));
        Assert.Contains("WizardStyle=modern", installer);
        Assert.Contains("DisableWelcomePage=no", installer);
        Assert.Contains("DisableDirPage=no", installer);
        Assert.Contains("ChineseSimplified.isl", installer);
        Assert.Contains("PrivilegesRequired=lowest", installer);
        Assert.Contains("CloseApplications=no", installer);
        Assert.Contains("Compression=none", installer);
        Assert.Contains("Uninstallable=yes", installer);
        Assert.Contains("recursesubdirs createallsubdirs", installer);
        Assert.Contains("postinstall skipifsilent unchecked", installer);
        Assert.DoesNotContain("[UninstallDelete]", installer);
        Assert.DoesNotContain("[InstallDelete]", installer);
        Assert.DoesNotContain("filesandordirs", installer);
        Assert.Contains("build-installer.ps1", File.ReadAllText(Path.Combine(root, "Build-Exe.cmd")));
        Assert.True(File.Exists(Path.Combine(root, "Build-Installer.cmd")));
        var builder = File.ReadAllText(Path.Combine(root, "scripts", "build-installer.ps1"));
        Assert.Contains("-PassThru -CompactName", builder);
        Assert.Contains("Get-FileHash -LiteralPath $setupPath -Algorithm SHA256", builder);
        Assert.Contains("[IO.Directory]::Move($stagingDirectory, $buildDirectory)", builder);
    }

    [Fact]
    public void CompilerBootstrapChecksPinnedHashAndUsesProjectLocalPortableMode()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "get-inno-setup.ps1"));
        Assert.Contains("https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/", script);
        Assert.Contains("9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732", script);
        Assert.Contains("Get-FileHash -LiteralPath $download -Algorithm SHA256", script);
        Assert.Contains("/PORTABLE=1", script);
        Assert.Contains("/CURRENTUSER", script);
        Assert.Contains("-WindowStyle Hidden", script);
        Assert.DoesNotContain("Remove-Item", script);
        Assert.True(script.IndexOf("Get-FileHash", StringComparison.Ordinal)
            < script.IndexOf("Start-Process", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallerDryRunDoesNotDownloadToolsOrPublishAndSupportsSpaces()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var destination = Path.Combine(Path.GetTempPath(), $"optical installer dry run {Guid.NewGuid():N}");
        var result = RunScriptFile("build-installer.ps1", "-WhatIf", "-OutputRoot", destination,
            "-InnoSetupCompiler", "deliberately missing compiler.exe");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(destination, result.Output);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void MissingExplicitInstallerCompilerFailsBeforeCreatingRelease()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var destination = Path.Combine(Path.GetTempPath(), $"optical installer missing compiler {Guid.NewGuid():N}");
        var result = RunScriptFile("build-installer.ps1", "-OutputRoot", destination,
            "-InnoSetupCompiler", "deliberately missing compiler.exe");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Inno Setup compiler not found", result.Output);
        Assert.False(Directory.Exists(destination));
    }

    private static (int ExitCode, string Output) RunScript(params string[] arguments) =>
        RunScriptFile("publish-windows.ps1", arguments);

    private static (int ExitCode, string Output) RunScriptFile(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                     Path.Combine(RepositoryRoot(), "scripts", fileName) }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Packaging script validation did not finish in 30 seconds.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
