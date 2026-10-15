using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal sealed record InstallationCleanupResult(int Removed, IReadOnlyList<string> Retained);

// Receipts are written only by a successfully committed installer. They live
// outside the payload, so an interrupted deletion can resume with the same proof.
// This is ownership/integrity checking, not publisher authentication.
[SupportedOSPlatform("windows")]
internal static class WindowsInstallationCleanup
{
    internal static void TryRun(string executableDirectory, string productId)
    {
        try
        {
            if (WindowsInstallationGate.InstallationRoot(executableDirectory) is not { } root
                || !Directory.Exists(Path.Combine(root, ".installed-releases"))) return;
            var result = Run(root, executableDirectory, productId);
            System.Diagnostics.Trace.WriteLine($"Installation cleanup: {result.Removed} removed; " + string.Join("; ", result.Retained));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or System.Security.SecurityException)
        { System.Diagnostics.Trace.WriteLine("Installation cleanup deferred: " + exception.Message); }
    }

    internal static InstallationCleanupResult Run(string root, string executableDirectory, string productId,
        Action<string>? afterDelete = null)
    {
        root = Path.GetFullPath(root);
        var mutexName = WindowsInstallationLifetime.MutexName(productId);
        // Older installers only know the session-local setup marker. Keep it
        // alive throughout maintenance, in addition to the cross-session gate.
        using var compatibilityGate = new Mutex(false, mutexName + "-Setup", out var created);
        if (!created) throw new IOException("安装任务仍在运行，稍后清理旧载荷。");
        using var gate = WindowsInstallationGate.Maintenance(root);
        if (Mutex.TryOpenExisting(WindowsInstallationLifetime.MutexName(productId), out var application))
        { application.Dispose(); throw new IOException("仍有程序窗口运行，稍后清理旧载荷。"); }
        if (File.Exists(Path.Combine(root, ".install-recovery", "pending.ini")))
            throw new IOException("安装尚待恢复，暂不清理旧载荷。");
        var statePath = Path.Combine(root, "release-state.ini");
        WindowsInstallationRecovery.CheckPath(statePath);
        var state = WindowsInstallationRecovery.ReadIni(statePath);
        if (state.GetValueOrDefault("Release/ProductAppId") != productId
            || !ValidId(state.GetValueOrDefault("Release/Active"))
            || !state.TryGetValue("Release/Previous", out var previous)
            || previous.Length > 0 && !ValidId(previous))
            throw new InvalidDataException("无法确认安装归属或恢复版本，旧载荷已保留。");
        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { state["Release/Active"], previous };
        var executing = new DirectoryInfo(Path.GetFullPath(executableDirectory));
        protectedIds.Add(executing.Name);
        var receipts = Path.Combine(root, ".installed-releases");
        WindowsInstallationRecovery.CheckPath(receipts);
        var removed = 0;
        var retained = new List<string>();
        // Bound work and directory enumeration. Further old releases can be
        // collected on a later startup; missing receipts never authorize deletion.
        foreach (var receipt in Directory.EnumerateFiles(receipts, "*.ini").Take(1000))
        {
            var id = Path.GetFileNameWithoutExtension(receipt);
            if (!ValidId(id) || protectedIds.Contains(id)) continue;
            try
            {
                RemoveRelease(root, productId, id, receipt, afterDelete);
                if (++removed == 4) break;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { retained.Add(id + ": " + exception.Message); }
        }
        return new(removed, retained);
    }

    private static bool ValidId(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 160
        && !value.EndsWith('.') && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    private static void RemoveRelease(string root, string productId, string id, string receipt, Action<string>? afterDelete)
    {
        WindowsInstallationRecovery.CheckPath(receipt);
        var ownership = WindowsInstallationRecovery.ReadIni(receipt);
        if (ownership.GetValueOrDefault("Release/ProductAppId") != productId
            || ownership.GetValueOrDefault("Release/Id") != id || ownership.GetValueOrDefault("Release/Format") != "1")
            throw new InvalidDataException("安装归属记录无效。");
        var manifestCopy = Path.ChangeExtension(receipt, ".sha256");
        WindowsInstallationRecovery.CheckPath(manifestCopy);
        var manifest = BoundedApplicationFile.ReadAllText(manifestCopy, 4 * 1024 * 1024, "Installed release manifest");
        if (!Hash(manifestCopy).Equals(ownership.GetValueOrDefault("Release/ManifestSha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装归属清单校验失败。");
        var payload = Path.Combine(root, "versions", id);
        WindowsInstallationRecovery.CheckPath(payload);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var row = line.TrimEnd('\r');
            if (files.Count >= 10000 || row.Length < 67 || row.Substring(64, 2) != "  "
                || !row[..64].All(char.IsAsciiHexDigit)) throw new InvalidDataException("安装清单格式无效。");
            var relative = row[66..].Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Split('\\', '/').Any(part => part.Length == 0
                || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException("安装清单路径无效。");
            if (!files.TryAdd(Path.Combine(payload, relative), row[..64]))
                throw new InvalidDataException("安装清单存在重复路径。");
        }
        if (files.Count == 0 || !files.ContainsKey(Path.Combine(payload, "OpticalSystemDesign.App.exe")))
            throw new InvalidDataException("安装清单缺少程序。");
        files.Add(Path.Combine(payload, "release.files.sha256"), ownership["Release/ManifestSha256"]);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { payload };
        foreach (var file in files.Keys)
            for (var directory = Path.GetDirectoryName(file); directory is not null
                && directory.StartsWith(payload + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                directory = Path.GetDirectoryName(directory)) directories.Add(directory);
        if (Directory.Exists(payload))
        {
            var pending = new Stack<(string Path, int Depth)>(); pending.Push((payload, 0));
            var count = 0;
            while (pending.TryPop(out var item))
            {
                if (item.Depth > 64) throw new InvalidDataException("目录层级过深。");
                WindowsInstallationRecovery.CheckPath(item.Path);
                foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path))
                {
                    if (++count > 20000) throw new InvalidDataException("目录条目过多。");
                    WindowsInstallationRecovery.CheckPath(entry);
                    if (Directory.Exists(entry))
                    {
                        if (!directories.Contains(entry)) throw new InvalidDataException("保留含自建目录的旧载荷。");
                        pending.Push((entry, item.Depth + 1));
                    }
                    else if (!files.TryGetValue(entry, out var expected) || !Hash(entry).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("保留含新增或修改文件的旧载荷。");
                }
            }
        }
        // Older binaries may not participate in the directory lock protocol.
        // Refuse any deletion if their executable is mapped/running or locked.
        var executable = Path.Combine(payload, "OpticalSystemDesign.App.exe");
        if (File.Exists(executable))
        {
            using var idle = File.Open(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        // Missing files are allowed: a previous cleanup may have been interrupted.
        // Recheck bytes through an exclusive handle immediately before deletion.
        foreach (var (path, expected) in files)
        {
            WindowsInstallationRecovery.CheckPath(path);
            if (!File.Exists(path)) continue;
            DeleteVerified(path, expected);
            afterDelete?.Invoke(path);
        }
        foreach (var directory in directories.OrderByDescending(p => p.Length))
        {
            WindowsInstallationRecovery.CheckPath(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
        }
        // Keep the receipt until all payload removals succeed. No recursive delete.
        File.Delete(receipt);
        File.Delete(manifestCopy);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void DeleteVerified(string path, string expected)
    {
        using var handle = CreateFile(path, 0x80010000, 0, 0, 3, 0, 0); // READ | DELETE, exclusive
        if (handle.IsInvalid) throw new IOException("旧载荷正在使用或无法访问。", new Win32Exception(Marshal.GetLastWin32Error()));
        using var input = new FileStream(handle, FileAccess.Read);
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("清理前文件已变化，原件保留。");
        var delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, sizeof(int)))
            throw new IOException("无法清理旧载荷。", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref int information, int size);
}
