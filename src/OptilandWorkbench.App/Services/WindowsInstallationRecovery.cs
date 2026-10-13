using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.App.Services;

internal sealed record InstallationRecoveryResult(bool Recovered, string? ActiveExecutable);

// Validates the entire format-2 journal and backups before restoring anything.
// Failed recovery keeps the journal; replay is deliberately idempotent.
[SupportedOSPlatform("windows")]
internal static class WindowsInstallationRecovery
{
    internal static InstallationRecoveryResult RecoverPending(string executableDirectory, string productId)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(executableDirectory));
        var root = directory.Parent?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) == true
            ? directory.Parent.Parent!.FullName : directory.FullName;
        var journal = Path.Combine(root, ".install-recovery", "pending.ini");
        if (!File.Exists(journal)) return new(false, null);
        using var gate = new Mutex(true, WindowsInstallationLifetime.MutexName(productId) + "-Setup", out var created);
        if (!created) throw new InvalidOperationException("安装任务正在运行，无法恢复中断安装。");
        try
        {
            using var files = WindowsInstallationGate.Maintenance(root);
            if (Mutex.TryOpenExisting(WindowsInstallationLifetime.MutexName(productId), out var application))
            {
                application.Dispose();
                throw new InvalidOperationException("检测到中断安装，请先保存并关闭其他程序窗口，再启动恢复。");
            }
            return RecoverAtRoot(root, productId);
        }
        finally { gate.ReleaseMutex(); }
    }

    internal static InstallationRecoveryResult RecoverAtRoot(string root, string productId,
        IReadOnlyList<string>? shortcutRoots = null)
    {
        _ = WindowsInstallationLifetime.MutexName(productId);
        root = Path.GetFullPath(root);
        var backupDirectory = Path.Combine(root, ".install-recovery");
        var journalPath = Path.Combine(backupDirectory, "pending.ini");
        CheckPath(journalPath);
        var journal = ReadIni(journalPath).ToDictionary(pair => pair.Key, pair => DecodeJournalValue(pair.Value), StringComparer.OrdinalIgnoreCase);
        string Get(string section, string name) => journal.TryGetValue(section + "/" + name, out var value)
            ? value : throw new InvalidDataException($"安装恢复日志缺少 {section}/{name}。");
        bool Flag(string section, string name) => Get(section, name) switch
        { "0" => false, "1" => true, _ => throw new InvalidDataException("安装恢复日志布尔字段无效。") };
        if (Get("Transaction", "FormatVersion") != "2" || Get("Transaction", "ProductAppId") != productId
            || !Path.GetFullPath(Get("Transaction", "Root")).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装恢复日志的产品或目录身份不匹配。");
        var phase = Get("Transaction", "Phase");
        if (phase == "Committed") { File.Delete(journalPath); return new(false, null); }
        if (phase is not ("Captured" or "Activating")) throw new InvalidDataException("未知安装恢复阶段。");
        var stateExists = Flag("Transaction", "StateExists");
        var state = Path.Combine(root, "release-state.ini");
        CheckPath(state);
        var files = new List<(string? Backup, string Target)>();
        void AddFile(bool existed, string backup, string target, string section, string hashField)
        {
            CheckPath(target);
            if (existed)
            {
                CheckPath(backup);
                using var input = BoundedApplicationFile.OpenRead(backup, 16 * 1024 * 1024, "Installation recovery backup");
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(Get(section, hashField), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装恢复备份校验失败；日志和原件已保留。");
            }
            files.Add((existed ? backup : null, target));
        }
        AddFile(stateExists, Path.Combine(backupDirectory, "previous-state.ini"), state, "Transaction", "StateSha256");
        Dictionary<string, string>? previousState = null;
        if (stateExists)
        {
            previousState = ReadIni(Path.Combine(backupDirectory, "previous-state.ini"));
            if (!previousState.TryGetValue("Release/ProductAppId", out var previousProduct) || previousProduct != productId
                || !previousState.TryGetValue("Release/Active", out var previousRelease) || previousRelease.Length == 0
                || !previousRelease.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
                || !previousState.TryGetValue("Release/Version", out var previousVersion) || !Version.TryParse(previousVersion, out _))
                throw new InvalidDataException("旧版本恢复状态无效；恢复尚未开始。");
        }
        shortcutRoots ??= [Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)];
        for (var index = 0; index < 3; index++)
        {
            var section = "Shortcut" + index;
            var path = Get(section, "Path");
            if (path.Length == 0)
            {
                if (Flag(section, "Exists")) throw new InvalidDataException("恢复快捷方式路径缺失。");
                continue;
            }
            path = Path.GetFullPath(path);
            if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || !shortcutRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => path.StartsWith(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("恢复快捷方式不属于当前用户桌面或开始菜单。");
            AddFile(Flag(section, "Exists"), Path.Combine(backupDirectory, $"shortcut-{index}.lnk"), path, section, "Sha256");
        }
        var uninstallerBase = Get("Uninstaller", "BaseName");
        if (uninstallerBase.Length > 0)
        {
            if (Path.GetFileName(uninstallerBase) != uninstallerBase || uninstallerBase.Contains("..", StringComparison.Ordinal)
                || !uninstallerBase.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("恢复卸载程序名称无效。");
            foreach (var extension in new[] { ".exe", ".dat", ".msg" })
                AddFile(Flag("Uninstaller" + extension, "Exists"), Path.Combine(backupDirectory, "uninstaller" + extension),
                    Path.Combine(root, uninstallerBase + extension), "Uninstaller" + extension, "Sha256");
        }
        var registryExists = Flag("Registry", "Exists");
        if (!int.TryParse(Get("Registry", "Count"), CultureInfo.InvariantCulture, out var count))
            throw new InvalidDataException("恢复注册信息数量无效。");
        if (count is < 0 or > 256 || !registryExists && count != 0) throw new InvalidDataException("恢复注册信息数量无效。");
        var values = new Dictionary<string, (object Data, RegistryValueKind Kind)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var section = "Registry" + index;
            var name = Get(section, "Name");
            var data = Get(section, "Data");
            if (Get(section, "Kind") == "DWord" && !uint.TryParse(data, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("恢复注册整数无效。");
            var value = Get(section, "Kind") switch
            {
                "DWord" => ((object)unchecked((int)uint.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord),
                "String" => ((object)data, RegistryValueKind.String),
                _ => throw new InvalidDataException("恢复注册数据类型无效。")
            };
            if (!values.TryAdd(name, value)) throw new InvalidDataException("重复恢复注册字段。");
        }
        var associationOwned = Flag("Association", "Owned");
        var iconExists = Flag("Association", "IconExists");
        var command = Get("Association", "Command");
        var icon = Get("Association", "Icon");
        var uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + productId + "_is1";
        using (var current = Registry.CurrentUser.OpenSubKey(uninstallKey))
        {
            if (current?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location)
                && !Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)).Equals(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("卸载注册信息已指向其他目录；恢复已停止。");
        }
        foreach (var (backup, target) in files)
        {
            if (backup is not null) RestoreFile(backup, target);
            else if (File.Exists(target)) File.Delete(target);
        }
        if (registryExists)
        {
            using var key = Registry.CurrentUser.CreateSubKey(uninstallKey);
            foreach (var name in key.GetValueNames().Where(n => !values.ContainsKey(n))) key.DeleteValue(name);
            foreach (var (name, value) in values) key.SetValue(name, value.Data, value.Kind);
            key.Flush();
        }
        else Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, throwOnMissingSubKey: false);
        if (associationOwned)
        {
            var progId = productId == WindowsInstallationLifetime.ProductId ? "STARLabs.OpticalSystemDesign.Project" : productId + ".Project";
            using var commandKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId + @"\shell\open\command");
            commandKey.SetValue("", command); commandKey.Flush();
            using var iconKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId + @"\DefaultIcon");
            if (iconExists) iconKey.SetValue("", icon); else iconKey.DeleteValue("", false);
            iconKey.Flush();
        }
        File.Delete(journalPath);
        string? active = null;
        if (stateExists)
        {
            active = Path.Combine(root, "versions", previousState!["Release/Active"], "OptilandWorkbench.App.exe");
        }
        else if (File.Exists(Path.Combine(root, "OptilandWorkbench.App.exe"))) active = Path.Combine(root, "OptilandWorkbench.App.exe");
        return new(true, active);
    }

    internal static Dictionary<string, string> ReadIni(string path)
    {
        var text = BoundedApplicationFile.ReadAllText(path, 1024 * 1024, "Installation journal");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';')) continue;
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) { section = trimmed[1..^1]; continue; }
            var separator = line.IndexOf('=');
            if (separator < 0 || !values.TryAdd(section + "/" + line[..separator].Trim(), line[(separator + 1)..]))
                throw new InvalidDataException("安装恢复日志格式无效。");
        }
        return values;
    }

    private static string DecodeJournalValue(string value)
    {
        if (!value.StartsWith("u:", StringComparison.Ordinal) || (value.Length - 2) % 4 != 0)
            throw new InvalidDataException("安装恢复日志字段编码无效。");
        var decoded = new char[(value.Length - 2) / 4];
        for (var index = 0; index < decoded.Length; index++)
        {
            if (!ushort.TryParse(value.AsSpan(2 + index * 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                throw new InvalidDataException("安装恢复日志字符编码无效。");
            decoded[index] = (char)code;
        }
        return new string(decoded);
    }

    private static void RestoreFile(string backup, string target)
    {
        var temporary = target + ".recovery." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using (var source = BoundedApplicationFile.OpenRead(backup, 16 * 1024 * 1024, "Installation recovery backup"))
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.WriteThrough))
            { source.CopyTo(destination); destination.Flush(true); }
            if (!MoveFileEx(temporary, target, 9)) throw new IOException("无法原子恢复旧安装文件。", Marshal.GetLastWin32Error());
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void CheckPath(string path)
    {
        for (var item = Path.GetFullPath(path); !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            if (Path.Exists(item) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("安装恢复路径不能包含符号链接或目录联接。");
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);
}
