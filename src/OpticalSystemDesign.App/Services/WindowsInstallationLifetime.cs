using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpticalSystemDesign.App.Services;

// The mutex is a presence marker, not a single-instance restriction. Setup must
// never terminate a running application or discard its unsaved document.
internal sealed class WindowsInstallationLifetime : IDisposable
{
    internal const string ProductId = "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A";
    private readonly Mutex? _presence;
    private readonly WindowsInstallationGate? _files;

    private WindowsInstallationLifetime(Mutex? presence, WindowsInstallationGate? files = null)
    { _presence = presence; _files = files; }

    internal static string MutexName(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || productId.Length > 128
            || productId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidDataException("Invalid installation product identity.");
        return @"Local\" + productId + "-Application";
    }

    internal static string ReadProductId(string directory)
    {
        var path = Path.Combine(directory, "release.identity.json");
        if (!File.Exists(path)) return ProductId;
        if (new FileInfo(path).Length > 16 * 1024)
            throw new InvalidDataException("Installation identity is too large.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var identity = json.RootElement.GetProperty("ProductAppId").GetString()
            ?? throw new InvalidDataException("Installation identity is missing.");
        _ = MutexName(identity);
        return identity;
    }

    internal static WindowsInstallationLifetime Enter(string productId, string? executableDirectory = null)
    {
        if (!OperatingSystem.IsWindows()) return new WindowsInstallationLifetime(null);
        var name = MutexName(productId);
        var presence = new Mutex(false, name);
        WindowsInstallationGate? files = null;
        try
        {
            // Create the application marker first, then check the installer
            // marker. Setup also rechecks before copying: either side fails closed.
            if (Mutex.TryOpenExisting(name + "-Setup", out var setup))
            {
                setup.Dispose();
                throw new InvalidOperationException("安装或升级正在进行，请完成安装后再启动程序。未保存的现有窗口不会被关闭。");
            }
            if (executableDirectory is not null && WindowsInstallationGate.InstallationRoot(executableDirectory) is { } root)
                files = WindowsInstallationGate.Application(root);
            return new WindowsInstallationLifetime(presence, files);
        }
        catch
        {
            presence.Dispose();
            files?.Dispose();
            throw;
        }
    }

    public void Dispose() { if (OperatingSystem.IsWindows()) _files?.Dispose(); _presence?.Dispose(); }

    internal static void ShowBlockedStartup(string message)
    {
        if (OperatingSystem.IsWindows())
            _ = MessageBox(0, message, "Optical System Design", 0x30);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint flags);
}
