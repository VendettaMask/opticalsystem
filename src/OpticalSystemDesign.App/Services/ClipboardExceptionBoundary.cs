using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OpticalSystemDesign.App.Services;

/// <summary>Aborts a failed clipboard read without changing native TextBox paste semantics.</summary>
internal static class ClipboardExceptionBoundary
{
    internal const string FailureMessage = "剪贴板暂时不可用，未粘贴。请重试或手动输入。";
    private static readonly ConditionalWeakTable<Dispatcher, object> InstalledDispatchers = new();
    internal static event EventHandler? ReadFailed;

    internal static void Install()
    {
        InstalledDispatchers.GetValue(Dispatcher.UIThread, dispatcher =>
        {
            dispatcher.UnhandledException += OnUnhandledException;
            return new object();
        });
    }

    internal static async Task<(bool Succeeded, string? Text)> TryReadTextAsync(Func<Task<string?>> read)
    {
        try { return (true, await read()); }
        catch (Exception error) when (error is COMException or TimeoutException or UnauthorizedAccessException)
        {
            Report(error);
            return (false, null);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        // Last resort for native paste reads, including a clipboard change between
        // the search control's preflight and Avalonia's own asynchronous read.
        // Do not turn unrelated UI faults or other COM errors into silent success.
        if (!args.Handled && IsNativePasteFailure(args.Exception))
        {
            args.Handled = true;
            Report(args.Exception);
        }
    }

    internal static bool IsNativePasteFailure(Exception exception)
    {
        if (exception is not COMException) return false;
        var frames = new StackTrace(exception).GetFrames();
        var nativeClipboard = false;
        var textBoxPaste = false;
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (type is null) continue;
            if (type.Assembly.GetName().Name == "Avalonia.Win32"
                && (type.FullName is "Avalonia.Win32.OleDataObjectToDataTransferWrapper"
                    or "Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy"
                    || type.FullName?.StartsWith("Avalonia.Win32.ClipboardImpl", StringComparison.Ordinal) == true))
                nativeClipboard = true;
            if (type.Assembly == typeof(TextBox).Assembly
                && ((type == typeof(TextBox) && method!.Name == nameof(TextBox.Paste))
                    || (type.DeclaringType == typeof(TextBox) && type.Name.StartsWith("<Paste>", StringComparison.Ordinal))))
                textBoxPaste = true;
        }
        return nativeClipboard && textBoxPaste;
    }

    internal static void Report(Exception exception)
    {
        // Never log clipboard contents or a provider-controlled exception message.
        var diagnostic = $"{DateTimeOffset.UtcNow:O} Clipboard read cancelled; {exception.GetType().FullName}; HRESULT=0x{exception.HResult:X8}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}";
        Trace.TraceWarning($"Clipboard read cancelled; HRESULT=0x{exception.HResult:X8}");
        try
        {
            var settings = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var directory = string.IsNullOrWhiteSpace(settings)
                ? Path.GetDirectoryName(App.StartupDiagnosticsLogPath())!
                : Path.Combine(settings, "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "clipboard.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, diagnostic);
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            Trace.TraceWarning("Clipboard diagnostics could not be written.");
        }
        ReadFailed?.Invoke(null, EventArgs.Empty);
    }
}
