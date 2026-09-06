using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OptilandWorkbench.ZemaxComparison.Zemax;

public sealed class ZemaxExecutor(string apiPath, string hostExe)
{
    public static string Locate(string? supplied)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live ZOS-API capture requires Windows");
        var candidates = new List<string?> { supplied, Environment.GetEnvironmentVariable("ZOS_API_PATH"), Environment.GetEnvironmentVariable("ZEMAX_ROOT") };
        candidates.AddRange(RegistryPaths());
        foreach (var root in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => Path.Combine(d.RootDirectory.FullName, "Program Files"))
            .Prepend(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ansys = Path.Combine(root, "ANSYS Inc");
            if (Directory.Exists(ansys)) candidates.AddRange(Directory.EnumerateDirectories(ansys, "v*").OrderDescending().Select(v => Path.Combine(v, "Zemax OpticStudio")));
            candidates.Add(Path.Combine(root, "Zemax OpticStudio"));
        }
        foreach (var candidate in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var path = Path.GetFullPath(candidate!);
            if (Path.GetFileName(path).Equals("ZOS-API", StringComparison.OrdinalIgnoreCase)) path = Path.GetDirectoryName(path)!;
            if (new[] { "ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll" }.All(n => File.Exists(Path.Combine(path, n)))) return path;
            if (supplied == candidate) throw new DirectoryNotFoundException($"ZOS-API assemblies not found at --zos-api-path: {path}");
        }
        throw new DirectoryNotFoundException("ZOS-API not found. Specify --zos-api-path or ZOS_API_PATH.");
    }
    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryPaths()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Zemax");
        foreach (var key in new[] { "ZemaxRoot", "InstallDir", "ZemaxData" }) if (user?.GetValue(key) is string s) yield return s;
    }
    public static async Task<ZemaxExecutor> Build(string apiPath, string output, CancellationToken ct)
    {
        var directory = Path.Combine(output, "host"); Directory.CreateDirectory(directory);
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        if (!File.Exists(compiler)) throw new FileNotFoundException(".NET Framework 4.x C# compiler not found", compiler);
        var exe = Path.Combine(directory, "ZemaxHost.exe");
        var args = new List<string> { "/nologo", "/target:exe", "/platform:x64", "/out:" + exe, "/r:System.Web.Extensions.dll" };
        args.AddRange(new[] { "ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll" }.Select(n => "/r:" + Path.Combine(apiPath, n)));
        args.AddRange(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "ZemaxHost"), "*.cs").Order(StringComparer.Ordinal));
        var result = await ProcessIsolation.Run(compiler, args, directory, 60, ct);
        File.WriteAllText(Path.Combine(directory, "build.log"), result.StandardOutput + result.StandardError);
        if (result.ExitCode != 0) throw new InvalidOperationException("ZOS host compilation failed: " + result.StandardOutput + result.StandardError);
        // Local runtime binding only. Proprietary assemblies are never copied or committed.
        File.WriteAllText(exe + ".config", "<configuration><startup useLegacyV2RuntimeActivationPolicy=\"true\"><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>");
        return new(apiPath, exe);
    }
    public async Task<ProcessResult> Capture(string input, string output, string version, string adapter,
        string? analysisType, CanonicalAnalysisRequest? request, int configuration, int seconds, bool screenshots, CancellationToken ct)
    {
        Directory.CreateDirectory(output);
        if (request?.SourceImagePath is { } source)
        {
            var bytes = File.ReadAllBytes(source);
            if (JsonFiles.Hash(bytes) != request.SourceImageSha256) throw new InvalidDataException("Source image changed after canonical request creation");
            File.WriteAllBytes(Path.Combine(output, "source-image" + Path.GetExtension(source)), bytes);
        }
        var payload = request is null ? new Dictionary<string, object?>() : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
            System.Text.Json.JsonSerializer.Serialize(request, JsonFiles.Options), JsonFiles.Options)!;
        payload["zosApiPath"] = apiPath; payload["input"] = input; payload["zemaxVersion"] = version;
        payload["adapter"] = adapter; payload["analysisType"] = analysisType; payload["configuration"] = configuration;
        payload["captureScreenshots"] = screenshots;
        var path = Path.Combine(output, "request.json"); JsonFiles.Write(path, payload);
        var result = await ProcessIsolation.Run(hostExe, [path, output], Path.GetDirectoryName(hostExe)!, seconds, ct,
            new Dictionary<string, string> { ["PATH"] = apiPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
        File.WriteAllText(Path.Combine(output, "process.log"), result.StandardOutput + result.StandardError);
        return result;
    }
}
