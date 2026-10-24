using System.Security.Cryptography;

namespace OpticalSystemDesign.App.Services.Updates;

// The updater runs the product's existing code from a private runtime copy.
// Never load the installation's EXE/DLLs while Setup probes or maintains them.
internal static class OtaHelperRuntime
{
    internal const string ExecutableName = "OpticalSystemDesign.App.exe";
    private const long MaximumBytes = 1024L * 1024 * 1024;

    internal static string Stage(string source, string operationDirectory)
    {
        source = Path.GetFullPath(source);
        var destination = Path.Combine(Path.GetFullPath(operationDirectory), "helper");
        CheckPath(source); CheckPath(destination);
        if (Directory.Exists(destination)) throw new IOException("Update helper runtime already exists.");
        Directory.CreateDirectory(destination);
        long total = 0; var count = 0;
        void Copy(string file)
        {
            CheckPath(file);
            if (++count > 2048) throw new InvalidDataException("Too many update runtime files.");
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            CheckPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            total = checked(total + input.Length);
            if (total > MaximumBytes) throw new InvalidDataException("Update runtime exceeds its copy budget.");
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            input.CopyTo(output); output.Flush(true);
            input.Position = output.Position = 0;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), SHA256.HashData(output)))
                throw new IOException("Update runtime copy verification failed.");
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                name is ExecutableName or "OpticalSystemDesign.App.deps.json" or
                    "OpticalSystemDesign.App.runtimeconfig.json" or "release.identity.json") Copy(file);
        }
        // Framework-dependent builds can retain native libraries under runtimes;
        // self-contained packages flatten them. Include satellite DLLs as well.
        void CopyLibraries(string folder, int depth)
        {
            CheckPath(folder);
            if (depth > 8 || ++count > 2048) throw new InvalidDataException("Invalid update runtime tree.");
            foreach (var file in Directory.EnumerateFiles(folder, "*.dll")) Copy(file);
            foreach (var child in Directory.EnumerateDirectories(folder)) CopyLibraries(child, depth + 1);
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            CheckPath(folder);
            if (Path.GetFileName(folder) == "runtimes" || Directory.EnumerateFiles(folder, "*.resources.dll").Any())
                CopyLibraries(folder, 0);
        }
        foreach (var name in new[] { ExecutableName, "OpticalSystemDesign.App.dll",
                     "OpticalSystemDesign.App.deps.json", "OpticalSystemDesign.App.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(destination, name))) throw new InvalidDataException("Incomplete update helper runtime: " + name);
        return Path.Combine(destination, ExecutableName);
    }

    internal static void ValidateLocation(string operationDirectory, string processPath)
    {
        var expected = Path.Combine(Path.GetFullPath(operationDirectory), "helper", ExecutableName);
        if (!Path.GetFullPath(processPath).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update helper must run outside the installation.");
        CheckPath(expected);
    }

    private static void CheckPath(string path)
    {
        if (OperatingSystem.IsWindows()) WindowsInstallationRecovery.CheckPath(path);
    }
}
