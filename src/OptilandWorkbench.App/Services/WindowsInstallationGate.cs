namespace OptilandWorkbench.App.Services;

// File sharing is enforced by Windows across processes and login sessions.
// Keep the session-local mutex as a compatibility guard for older applications.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsInstallationGate : IDisposable
{
    private readonly FileStream _stream;
    private WindowsInstallationGate(FileStream stream) => _stream = stream;

    internal static string? InstallationRoot(string executableDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(executableDirectory));
        if (directory.Parent?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) == true)
            return directory.Parent.Parent!.FullName;
        return File.Exists(Path.Combine(directory.FullName, "release-state.ini"))
            || File.Exists(Path.Combine(directory.FullName, ".install-recovery", "pending.ini"))
            ? directory.FullName : null;
    }

    private static FileStream Open(string root, string name, FileShare share)
    {
        var path = Path.Combine(root, name);
        WindowsInstallationRecovery.CheckPath(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, share);
    }

    internal static WindowsInstallationGate Application(string root)
    {
        var application = Open(root, ".application.lock", FileShare.ReadWrite);
        try
        {
            using var setup = Open(root, ".setup.lock", FileShare.None);
            return new(application);
        }
        catch { application.Dispose(); throw; }
    }

    internal static WindowsInstallationGate Maintenance(string root)
    {
        var setup = Open(root, ".setup.lock", FileShare.None);
        try
        {
            using var application = Open(root, ".application.lock", FileShare.None);
            return new(setup);
        }
        catch { setup.Dispose(); throw; }
    }

    public void Dispose() => _stream.Dispose();
}
