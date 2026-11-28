using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpticalSystemDesign.App.Services;

internal sealed class RuntimeFileLog : TraceListener
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Mutex _writeLock;
    private readonly object _gate = new();
    private bool _closed;
    private static readonly Encoding Encoding = new UTF8Encoding(false);

    private RuntimeFileLog(string directory, long maxBytes)
    {
        _path = Path.GetFullPath(Path.Combine(directory, "runtime.log"));
        _maxBytes = maxBytes;
        var key = OperatingSystem.IsWindows() ? _path.ToUpperInvariant() : _path;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.GetBytes(key)));
        _writeLock = new Mutex(false, "OpticalSystemDesign.RuntimeLog." + hash);
    }

    internal static RuntimeFileLog? Start(string? directory = null, long maxBytes = 5 * 1024 * 1024)
    {
        try
        {
            if (directory is null)
            {
                var profile = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
                directory = string.IsNullOrWhiteSpace(profile) ? Path.GetDirectoryName(App.StartupDiagnosticsLogPath())! : Path.Combine(profile, "Logs");
            }
            Directory.CreateDirectory(directory);
            var log = new RuntimeFileLog(directory, maxBytes);
            Trace.Listeners.Add(log);
            AppDomain.CurrentDomain.UnhandledException += log.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += log.OnUnobservedTaskException;
            log.Append("INFO", $"Session started. Version={ProductVersionInfo.Current} OS={Environment.OSVersion} .NET={Environment.Version}");
            return log;
        }
        catch (Exception error) when (IsLogAccessError(error)) { return null; }
    }

    public override void Write(string? message) => WriteLine(message);
    public override void WriteLine(string? message) => Append("TRACE", message);
    public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType type, int id, string? message) =>
        Append(type.ToString(), message);
    public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType type, int id, string? format, params object?[]? args) =>
        Append(type.ToString(), args is null ? format : string.Format(CultureInfo.InvariantCulture, format ?? "", args));

    private void Append(string level, string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        lock (_gate)
        {
            if (!_closed) WriteRecord(level, message);
        }
    }

    private void WriteRecord(string level, string message)
    {
        if (message.Length > 16_384) message = message[..16_384] + "…";
        var line = $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] [{level}] {message.ReplaceLineEndings(Environment.NewLine + "    ")}{Environment.NewLine}";
        var acquired = false;
        try
        {
            // Independent desktop instances share both the append and rotation operation.
            try { acquired = _writeLock.WaitOne(TimeSpan.FromMilliseconds(250)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            var file = new FileInfo(_path);
            if (file.Exists && file.Length > 0 && file.Length + Encoding.GetByteCount(line) > _maxBytes)
                File.Move(_path, _path + ".previous", overwrite: true);
            File.AppendAllText(_path, line, Encoding);
        }
        catch (Exception error) when (IsLogAccessError(error)) { }
        finally { if (acquired) _writeLock.ReleaseMutex(); }
    }

    private static bool IsLogAccessError(Exception error) => error is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or NotSupportedException or ArgumentException or WaitHandleCannotBeOpenedException;

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        Append("ERROR", $"Unhandled exception. Terminating={args.IsTerminating} {args.ExceptionObject}");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
        Append("ERROR", $"Unobserved task exception. {args.Exception}");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Trace.Listeners.Remove(this);
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            lock (_gate)
            {
                if (!_closed)
                {
                    WriteRecord("INFO", $"Session ended. ExitCode={Environment.ExitCode}");
                    _closed = true;
                    _writeLock.Dispose();
                }
            }
        }
        base.Dispose(disposing);
    }
}
