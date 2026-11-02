using System.Diagnostics;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingRecoveryProcessTests
{
    [Fact]
    public async Task FreshProcessRecoversDraftAfterOnlyItsOwnedWriterIsKilled()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "tools/diagnostics/CoatingRecovery20261102/CoatingRecovery20261102.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var helper = Path.Combine(root.FullName, "tools/diagnostics/CoatingRecovery20261102/bin/Release/net10.0/CoatingRecovery20261102.dll");
        Assert.True(File.Exists(helper), helper);
        var dir = Path.Combine(Path.GetTempPath(), "coating-crash-process-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var original = Path.Combine(dir, "original.coating.json"); await File.WriteAllTextAsync(original, "original file untouched");
        Process Start(string mode)
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(helper); info.ArgumentList.Add(mode); info.ArgumentList.Add(dir);
            return Process.Start(info) ?? throw new IOException("Unable to start owned recovery probe.");
        }
        try
        {
            using (var writer = Start("write"))
            {
                try { Assert.Equal("READY", await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30))); }
                finally { if (!writer.HasExited) writer.Kill(entireProcessTree: true); await writer.WaitForExitAsync(); }
            }
            Assert.Single(Directory.GetFiles(dir, "*.draft.json"));
            using var reader = Start("read");
            string output;
            try { output = await reader.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30)); await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { if (!reader.HasExited) { reader.Kill(entireProcessTree: true); await reader.WaitForExitAsync(); } }
            Assert.Equal(0, reader.ExitCode); Assert.Contains("unfinished.", output); Assert.Contains("625", output); Assert.Contains("73.123456789", output);
            Assert.Empty(Directory.GetFiles(dir, "*.draft.json")); Assert.Equal("original file untouched", await File.ReadAllTextAsync(original));
        }
        finally { Directory.Delete(dir, true); }
    }
}
