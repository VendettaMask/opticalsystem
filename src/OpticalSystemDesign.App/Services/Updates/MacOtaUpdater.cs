using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace OpticalSystemDesign.App.Services.Updates;

internal enum MacOtaState { Idle, Checking, Available, Downloading, Extracting, Ready, Installing, UpToDate, Cancelled, Error }
internal sealed record MacOtaStatus(MacOtaState State, string Version = "", double Progress = 0);

internal interface IMacOtaUpdater
{
    MacOtaStatus Status { get; }
    event Action? Changed;
    void Check(bool includeTests);
    void Download();
    bool Install();
    void Cancel();
}

// Sparkle owns authenticated downloads, bundle replacement and relaunch. This
// adapter owns no installer code and never changes Gatekeeper or quarantine.
internal sealed class MacOtaUpdater : IMacOtaUpdater
{
    private const string Library = "OpticalSystemDesign.Updater";
    private static MacOtaUpdater? _current;
    internal static MacOtaUpdater Current => _current ??= new();
    internal static MacOtaUpdater? Existing => _current;
    private readonly NativeEvent _callback;
    private bool _initialized;
    public MacOtaStatus Status { get; private set; } = new(MacOtaState.Idle);
    public event Action? Changed;

    private MacOtaUpdater() => _callback = OnNativeEvent;

    public void Check(bool includeTests)
    {
        Dispatcher.UIThread.VerifyAccess();
        try
        {
            if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
                throw new PlatformNotSupportedException();
            if (!_initialized)
            {
                if (Initialize(_callback) != 1) throw new InvalidOperationException("Mac updater initialization failed.");
                _initialized = true;
            }
            CheckNative(includeTests ? 1 : 0);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or InvalidOperationException or NotSupportedException)
        { Set(new(MacOtaState.Error)); }
    }

    public void Download() { Dispatcher.UIThread.VerifyAccess(); if (_initialized) DownloadNative(); }
    public bool Install() { Dispatcher.UIThread.VerifyAccess(); return _initialized && Status.State == MacOtaState.Ready && InstallNative() == 1; }
    public void Cancel() { Dispatcher.UIThread.VerifyAccess(); if (_initialized) CancelNative(); }
    internal bool CancelForClose() { Dispatcher.UIThread.VerifyAccess(); return !_initialized || CancelNative() == 1; }

    private void OnNativeEvent(int state, nint value, double progress)
    {
        // Copy borrowed UTF-8 before returning to native code; never let managed
        // exceptions cross the C ABI. UI notifications run on Avalonia's loop.
        if (!Enum.IsDefined(typeof(MacOtaState), state)) return;
        var text = Marshal.PtrToStringUTF8(value) ?? "";
        Dispatcher.UIThread.Post(() => Set(new((MacOtaState)state, text,
            double.IsFinite(progress) ? Math.Clamp(progress, 0, 100) : 0)));
    }

    private void Set(MacOtaStatus status) { Status = status; Changed?.Invoke(); }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeEvent(int state, nint value, double progress);
    [DllImport(Library, EntryPoint = "osd_updater_initialize", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Initialize(NativeEvent callback);
    [DllImport(Library, EntryPoint = "osd_updater_check", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CheckNative(int includeTests);
    [DllImport(Library, EntryPoint = "osd_updater_download", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DownloadNative();
    [DllImport(Library, EntryPoint = "osd_updater_install", CallingConvention = CallingConvention.Cdecl)]
    private static extern int InstallNative();
    [DllImport(Library, EntryPoint = "osd_updater_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CancelNative();
}
