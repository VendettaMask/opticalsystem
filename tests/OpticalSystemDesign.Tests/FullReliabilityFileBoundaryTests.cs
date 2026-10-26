using System.Diagnostics;
using System.Reflection;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;

namespace OpticalSystemDesign.Tests;

public sealed class FullReliabilityFileBoundaryTests
{
    [Fact]
    public void LibraryChildDirectoryLinkCannotEscapeItsTrustedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lens-link-boundary-" + Guid.NewGuid().ToString("N"));
        var library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var link = Path.Combine(library, "projects");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                process.WaitForExit(); Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(link, target);
            File.WriteAllText(Path.Combine(target, "outside.staropt"), "preserve");
            var method = typeof(LensLibraryService).GetMethod("SafeChildPath", BindingFlags.Static | BindingFlags.NonPublic)!;
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [library, "projects/outside.staropt"]));
            Assert.IsType<InvalidDataException>(error.InnerException);
            Assert.Equal("preserve", File.ReadAllText(Path.Combine(target, "outside.staropt")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConfigurationStatusUsesTheSameOneBasedNumbersAsTheInterface()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateBlank());
        Assert.Equal(1, runtime.AddMultiConfiguration());
        Assert.Contains("配置 2", runtime.Status);
        runtime.ActivateMultiConfiguration(1);
        Assert.Contains("配置 2", runtime.Status);
        runtime.ActivateMultiConfiguration(0);
        Assert.Contains("配置 1", runtime.Status);
    }
}
