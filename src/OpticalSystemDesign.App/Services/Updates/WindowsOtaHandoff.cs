using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpticalSystemDesign.App.Services.Updates;

internal sealed record OtaInstallation(string Root, string Version, string Active, string Executable);
internal sealed record OtaHandoffTicket(string Envelope, string Nonce, int ParentId, long ParentStartUtcTicks, DateTime CreatedUtc);

// Runs in the same product executable, but outside the GUI/application installation gate.
// The helper is started ONLY from a successfully closed main window, never on save cancellation.
internal static class WindowsOtaHandoff
{
    internal static string? UnavailableReason()
    {
        if (!OperatingSystem.IsWindows() || ProductReleaseSource.CurrentRuntime is null)
            return "在线更新仅支持已安装的 Windows 版本。";
        try { _ = ReadCurrentInstallation(); return null; }
        catch (Exception exception) when (IsExpectedFailure(exception))
        { return "开发版、便携版或安装身份不完整，请使用完整安装包。"; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static OtaInstallation ReadCurrentInstallation()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("Missing executable identity.");
        var directory = Path.GetDirectoryName(executable)!;
        if (WindowsInstallationLifetime.ReadProductId(directory) != WindowsInstallationLifetime.ProductId)
            throw new InvalidDataException("Not the production installation.");
        var root = WindowsInstallationGate.InstallationRoot(directory) ?? throw new InvalidDataException("Not installed.");
        var installed = ReadInstallation(root);
        if (!SamePath(executable, installed.Executable) || installed.Version != ProductVersionInfo.Current)
            throw new InvalidDataException("The running executable is not the active installed version.");
        return installed;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static OtaInstallation ReadInstallation(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        WindowsInstallationRecovery.CheckPath(root);
        using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + WindowsInstallationLifetime.ProductId + "_is1");
        if (uninstall?.GetValue("InstallLocation") is not string location || !SamePath(root, location))
            throw new InvalidDataException("Installed directory is not registered to this user.");
        var statePath = Path.Combine(root, "release-state.ini");
        WindowsInstallationRecovery.CheckPath(statePath);
        var state = WindowsInstallationRecovery.ReadIni(statePath);
        if (state.GetValueOrDefault("Release/ProductAppId") != WindowsInstallationLifetime.ProductId ||
            state.GetValueOrDefault("Release/Version") is not { } version || !ReleaseVersion.TryParse(version, out _) ||
            uninstall.GetValue("DisplayVersion") is not string registered || registered != version ||
            state.GetValueOrDefault("Release/Active") is not { } active ||
            !Regex.IsMatch(active, "^" + Regex.Escape(version) + @"-win-(?:x64|arm64)-[0-9a-f]{12}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Inconsistent installation identity.");
        var executable = Path.Combine(root, "versions", active, "OpticalSystemDesign.App.exe");
        WindowsInstallationRecovery.CheckPath(executable);
        if (!File.Exists(executable)) throw new InvalidDataException("Missing installed executable.");
        return new(root, version, active, executable);
    }

    internal static void StartAfterClose(PreparedOtaUpdate prepared)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _ = ReadCurrentInstallation();
        var verified = OtaManifest.Verify(prepared.Update.Envelope);
        if (verified is null || verified.Payload.Runtime != ProductReleaseSource.CurrentRuntime ||
            !OtaManifest.IsNewer(verified.Payload, ProductVersionInfo.Current)) throw new InvalidDataException("Untrusted update.");
        ValidateCacheDirectory(prepared.Directory);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var parent = Process.GetCurrentProcess();
        var ticket = new OtaHandoffTicket(verified.Envelope, nonce, parent.Id, parent.StartTime.ToUniversalTime().Ticks, DateTime.UtcNow);
        var ticketPath = Path.Combine(prepared.Directory, "handoff.json");
        using (var stream = new FileStream(ticketPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, ticket); stream.Flush(true); }
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(ticketPath); start.ArgumentList.Add(nonce);
        using var helper = Process.Start(start) ?? throw new IOException("Cannot start the update helper.");
        prepared.HandedOff = true;
    }

    internal static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 75;
        try { UiLocalization.Apply(AppSettings.Load().Language); }
        catch (Exception exception) when (IsExpectedFailure(exception)) { }
        try { RunAsync(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            // This process has no GUI loop. Use an ordinary Windows error, never kill a process.
            var logPath = TryWriteFailureLog(args, exception);
            WindowsInstallationLifetime.ShowBlockedStartup(UiLocalization.Text("在线更新未完成，请重新打开软件或手动安装；工程和设置不会被删除。") +
                (logPath is null ? "" : "\n" + UiLocalization.Text("诊断日志：") + logPath));
            return 75;
        }
    }

    private static string? TryWriteFailureLog(string[] args, Exception exception)
    {
        try
        {
            if (args.Length != 3) return null;
            var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            ValidateCacheDirectory(directory);
            var path = Path.Combine(directory, "handoff-error.log");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(exception.ToString());
            return path;
        }
        catch (Exception failure) when (IsExpectedFailure(failure)) { return null; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task RunAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != "--apply-update" || !Regex.IsMatch(args[2], "^[0-9A-F]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid update handoff arguments.");
        var ticketPath = Path.GetFullPath(args[1]);
        var directory = Path.GetDirectoryName(ticketPath)!;
        ValidateCacheDirectory(directory);
        if (Path.GetFileName(ticketPath) != "handoff.json" || new FileInfo(ticketPath).Length > 32768)
            throw new InvalidDataException("Invalid update ticket.");
        WindowsInstallationRecovery.CheckPath(ticketPath);
        OtaHandoffTicket ticket;
        using (var stream = File.OpenRead(ticketPath))
            ticket = JsonSerializer.Deserialize<OtaHandoffTicket>(stream) ?? throw new InvalidDataException("Missing update ticket.");
        var update = ValidateTicket(ticket, args[2], DateTime.UtcNow, ProductReleaseSource.CurrentRuntime, ProductVersionInfo.Current);
        var installed = ReadCurrentInstallation();
        // Verify a still-existing parent belongs to this exact executable, not a recycled PID.
        Process? parent = null;
        try { parent = Process.GetProcessById(ticket.ParentId); }
        catch (ArgumentException) { } // Parent can finish before the helper gets scheduled.
        if (parent is not null)
        {
            using (parent)
            {
                if (parent.StartTime.ToUniversalTime().Ticks != ticket.ParentStartUtcTicks ||
                    !SamePath(parent.MainModule?.FileName ?? "", Environment.ProcessPath!))
                    throw new InvalidDataException("Update parent identity mismatch.");
                using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await parent.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
        }
        // Recheck installation/version after waiting. Another maintenance operation may have won.
        var current = ReadCurrentInstallation();
        if (current != installed || !OtaManifest.IsNewer(update.Payload, current.Version))
            throw new InvalidDataException("Installation changed during update handoff.");
        using (WindowsInstallationGate.Maintenance(installed.Root)) { }
        if (Mutex.TryOpenExisting(WindowsInstallationLifetime.MutexName(WindowsInstallationLifetime.ProductId), out var other))
        { other.Dispose(); throw new IOException("Please save and close other application windows first."); }
        using var prepared = new PreparedOtaUpdate(directory, update);
        var installerPath = prepared.InstallerPath;
        WindowsInstallationRecovery.CheckPath(installerPath);
        // Deny writes/deletes from the final hash check until Setup exits.
        using var installerLock = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (installerLock.Length != update.Payload.Size || installerLock.ReadByte() != 'M' || installerLock.ReadByte() != 'Z')
            throw new InvalidDataException("Installer identity rejected.");
        installerLock.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(installerLock).ConfigureAwait(false)).ToLowerInvariant() != update.Payload.Sha256)
            throw new InvalidDataException("Installer hash rejected.");
        var setup = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        foreach (var argument in SetupArguments(directory)) setup.ArgumentList.Add(argument);
        using var process = Process.Start(setup) ?? throw new IOException("Cannot start the verified installer.");
        await process.WaitForExitAsync().ConfigureAwait(false); // No forced timeout/kill during installation.
        if (process.ExitCode != 0)
        {
            // Only the existing validated journal recovery may restore an interrupted transaction.
            _ = WindowsInstallationRecovery.RecoverPending(AppContext.BaseDirectory, WindowsInstallationLifetime.ProductId);
            throw new IOException("Setup did not complete (exit " + process.ExitCode + "). Log: " + Path.Combine(directory, "setup.log"));
        }
        var activated = ReadInstallation(installed.Root);
        if (activated.Version != update.Payload.Version || activated.Active != update.Payload.ReleaseId)
            throw new InvalidDataException("Setup did not activate the signed release identity.");
        using var restarted = Process.Start(new ProcessStartInfo(activated.Executable) { UseShellExecute = true })
            ?? throw new IOException("Installed successfully; please start the application from its shortcut.");
        // Dispose installer lock before deleting our downloaded copy; keep setup.log for diagnosis.
        installerLock.Dispose();
    }

    internal static string[] SetupArguments(string directory) =>
        ["/SILENT", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/LOG=" + Path.Combine(directory, "setup.log")];

    internal static SignedOtaUpdate ValidateTicket(OtaHandoffTicket ticket, string nonce, DateTime now,
        string? runtime, string installedVersion, string? publicKey = null)
    {
        var update = OtaManifest.Verify(ticket.Envelope ?? "", publicKey) ?? throw new InvalidDataException("Update signature rejected.");
        if (!Regex.IsMatch(nonce, "^[0-9A-F]{64}$", RegexOptions.CultureInvariant) || ticket.Nonce != nonce ||
            ticket.ParentId <= 0 || ticket.ParentStartUtcTicks <= 0 || ticket.CreatedUtc.Kind != DateTimeKind.Utc ||
            ticket.CreatedUtc > now || now - ticket.CreatedUtc > TimeSpan.FromMinutes(10) ||
            update.Payload.Runtime != runtime || !OtaManifest.IsNewer(update.Payload, installedVersion))
            throw new InvalidDataException("Expired or incompatible update ticket.");
        return update;
    }

    internal static void ValidateCacheDirectory(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Guid.TryParseExact(Path.GetFileName(full), "N", out _) || !SamePath(Path.GetDirectoryName(full)!, OtaDownloader.CacheRoot))
            throw new InvalidDataException("Update files are outside the owned cache directory.");
        if (OperatingSystem.IsWindows()) WindowsInstallationRecovery.CheckPath(full);
    }

    private static bool SamePath(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException or CryptographicException or JsonException or OperationCanceledException;
}
