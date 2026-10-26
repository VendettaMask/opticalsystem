using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

internal static class Program
{
    internal static string InstalledRoot = "";
    internal static string Mode = "";
    private static StreamWriter? _log;

    [STAThread]
    public static int Main(string[] args)
    {
        InstalledRoot = Path.GetFullPath(args[0]);
        Mode = args[1];
        _log = new StreamWriter(args[2], false) { AutoFlush = true };
        Native.SetErrorMode(3); // Suppress crash dialogs in this diagnostic process only.
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(Path.GetDirectoryName(args[2])!, "background-profile"));
        var nativeDirectory = Path.Combine(InstalledRoot, "runtimes", "win-x64", "native");
        Native.SetDllDirectory(Directory.Exists(nativeDirectory) ? nativeDirectory : InstalledRoot);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(InstalledRoot, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("unhandled", new
        {
            e.IsTerminating,
            Exception = e.ExceptionObject.ToString(),
            HResult = (e.ExceptionObject as Exception)?.HResult.ToString("X8")
        });
        Log("start", new { Mode, ProcessId = Environment.ProcessId, InstalledRoot, ClipboardSequence = Native.GetClipboardSequenceNumber(), Foreground = Native.GetForegroundWindow().ToInt64() });
        return Probe.Run();
    }

    internal static void Log(string stage, object? data = null)
    {
        lock (typeof(Program))
            _log!.WriteLine(JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Stage = stage, Data = data }));
    }
}

internal static class Probe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run()
    {
        // Load the real product application, search control and Win32 assemblies.
        // Do not call product Main, Show, Activate, Focus, Copy or any clipboard write API.
        var app = Assembly.LoadFrom(Path.Combine(Program.InstalledRoot, "OpticalSystemDesign.App.dll"));
        var build = app.GetType("OpticalSystemDesign.App.Program", true)!.GetMethod("BuildAvaloniaApp", BindingFlags.Public | BindingFlags.Static)!;
        ((AppBuilder)build.Invoke(null, null)!).SetupWithoutStarting();
        AvaloniaSynchronizationContext.InstallIfNeeded();
        var actionsType = app.GetType("OpticalSystemDesign.App.Services.ActionManager", true)!;
        var actions = Activator.CreateInstance(actionsType)!;
        var searchType = app.GetType("OpticalSystemDesign.App.Controls.CommandSearchControl", true)!;
        var search = (Control)Activator.CreateInstance(searchType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [actions], null)!;
        var box = (TextBox)searchType.GetProperty("SearchBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!;
        if (Program.Mode is "fault-global" or "unrelated-com") box = new TextBox();
        var content = Program.Mode is "fault-global" or "unrelated-com" ? (Control)box : search;
        var window = new Window { Content = content, ShowInTaskbar = false, ShowActivated = false, Width = 400, Height = 100 };
        window.ApplyTemplate();
        window.Measure(new Size(400, 100));
        window.Arrange(new Rect(0, 0, 400, 100));
        var handle = window.TryGetPlatformHandle()!.Handle;
        Program.Log("initialized", new
        {
            ProductVersion = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ProductModuleVersionId = app.ManifestModule.ModuleVersionId,
            ProductDllSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(app.Location))),
            AvaloniaVersion = typeof(TextBox).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Control = search.GetType().FullName,
            Box = box.GetType().FullName,
            Attached = ReferenceEquals(TopLevel.GetTopLevel(box), window),
            WindowVisible = Native.IsWindowVisible(handle),
            Platform = window.PlatformImpl?.GetType().FullName,
            Clipboard = window.Clipboard?.GetType().FullName,
            SynchronizationContext = SynchronizationContext.Current?.GetType().FullName
        });
        if (!ReferenceEquals(TopLevel.GetTopLevel(box), window) || Native.IsWindowVisible(handle))
            throw new InvalidOperationException("The real search TextBox must be attached to a hidden native window.");

        var warningCount = 0;
        var boundary = app.GetType("OpticalSystemDesign.App.Services.ClipboardExceptionBoundary");
        var warningEvent = boundary?.GetEvent("ReadFailed", BindingFlags.Static | BindingFlags.NonPublic);
        EventHandler warningHandler = (_, _) => warningCount++;
        warningEvent?.GetAddMethod(true)?.Invoke(null, [warningHandler]);
        IsolatedOleClipboard? injected = null;
        if (Program.Mode != "live-readonly")
        {
            // This binding affects this child process only. The global Windows
            // clipboard is never replaced, even for the controlled COM failure.
            injected = new IsolatedOleClipboard(Program.Mode);
            AvaloniaLocator.CurrentMutable.Bind<IClipboard>().ToConstant(injected);
            Program.Log("process-local-clipboard-bound", new { Clipboard = window.Clipboard?.GetType().FullName });
        }

        using var done = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            var attempts = Program.Mode == "live-readonly" ? 5 : Program.Mode == "fault-recovery" ? 2 : 1;
            for (var i = 0; i < attempts; i++)
            {
                var preserve = Program.Mode.StartsWith("fault-", StringComparison.Ordinal) || Program.Mode is "readonly" or "empty-control";
                box.Text = preserve ? "原搜索词" : "";
                box.SelectionStart = preserve ? 1 : 0;
                box.SelectionEnd = preserve ? 3 : 0;
                box.IsReadOnly = Program.Mode == "readonly";
                box.MaxLength = Program.Mode == "max-length" ? 12 : 0;
                if (Program.Mode == "fault-recovery" && i == 1) injected!.Recovered = true;
                var textBefore = box.Text;
                var selectionBefore = (box.SelectionStart, box.SelectionEnd);
                var warningsBefore = warningCount;
                var sequenceBefore = Native.GetClipboardSequenceNumber();
                Program.Log("paste-call", new { Attempt = i + 1, SequenceBefore = sequenceBefore, WindowVisible = Native.IsWindowVisible(handle) });
                // Match the application's unguarded invocation of async-void Paste.
                // There is deliberately no Dispatcher exception recovery handler.
                if (Program.Mode is "shortcut-control-v" or "fault-search-control-v" or "shortcut-shift-insert")
                {
                    var key = new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = Program.Mode == "shortcut-shift-insert" ? Key.Insert : Key.V,
                        KeyModifiers = Program.Mode == "shortcut-shift-insert" ? KeyModifiers.Shift : KeyModifiers.Control
                    };
                    box.RaiseEvent(key); // In-process routed event; no physical input or activation.
                    Program.Log("hidden-routed-shortcut", new { Key = key.Key.ToString(), key.Handled });
                }
                else box.Paste();
                Program.Log("paste-returned-to-caller", new { Attempt = i + 1 });
                await Task.Delay(1500);
                var text = box.Text;
                var expectedFailure = Program.Mode.StartsWith("fault-", StringComparison.Ordinal) && !(Program.Mode == "fault-recovery" && i == 1);
                var expectedControl = Program.Mode != "live-readonly" && !expectedFailure && Program.Mode is not "readonly" and not "empty-control";
                var expectedText = preserve ? "原" + TestOleDataObject.ControlText + "词" : TestOleDataObject.ControlText;
                if (Program.Mode == "max-length") expectedText = expectedText[..12];
                var unchanged = text == textBefore && box.SelectionStart == selectionBefore.Item1 && box.SelectionEnd == selectionBefore.Item2;
                Program.Log("paste-observed", new
                {
                    Attempt = i + 1,
                    HasText = !string.IsNullOrEmpty(text),
                    MatchesControlledText = expectedControl ? text == expectedText : (bool?)null,
                    TextAndSelectionUnchanged = unchanged,
                    Warnings = warningCount - warningsBefore,
                    ProviderReads = injected?.Reads,
                    SequenceBefore = sequenceBefore,
                    SequenceAfter = Native.GetClipboardSequenceNumber(),
                    WindowVisible = Native.IsWindowVisible(handle)
                    // Never persist the user's clipboard text or a hash of it.
                });
                if (expectedFailure && (!unchanged || warningCount == warningsBefore))
                    throw new InvalidOperationException("The failed paste must preserve the query/selection and report a warning.");
                if (expectedControl && text != expectedText)
                    throw new InvalidOperationException("Controlled Unicode paste did not match expected text.");
                if (Program.Mode is "readonly" or "empty-control" && !unchanged)
                    throw new InvalidOperationException("Empty/read-only paste changed the query.");
                if (expectedControl)
                {
                    box.Undo();
                    var undoRestored = box.Text == textBefore;
                    box.Redo();
                    var redoRestored = box.Text == expectedText;
                    Program.Log("undo-redo", new { UndoRestored = undoRestored, RedoRestored = redoRestored });
                    if (!undoRestored || !redoRestored) throw new InvalidOperationException("Native paste undo/redo changed.");
                }
            }
            Program.Log("complete", new { Attempts = attempts, WindowVisible = Native.IsWindowVisible(handle), ClipboardSequence = Native.GetClipboardSequenceNumber(), Foreground = Native.GetForegroundWindow().ToInt64() });
            window.Close();
            done.Cancel();
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        return 0;
    }
}

internal sealed class IsolatedOleClipboard(string mode) : IClipboard
{
    private readonly List<object> _keepAlive = [];
    internal int Reads { get; private set; }
    internal bool Recovered { get; set; }
    public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => Task.FromResult<IAsyncDataTransfer?>(null);
    public Task<IAsyncDataTransfer?> TryGetDataAsync()
    {
        Reads++;
        if (mode == "unrelated-com") throw new COMException("Expected unrelated test provider error", unchecked((int)0x8007000E));
        var failEnumeration = mode.StartsWith("fault-", StringComparison.Ordinal) && !Recovered
            && (mode != "fault-late" || Reads == 2);
        var nativeObject = new TestOleDataObject(failEnumeration, mode == "empty-control" ? "" : TestOleDataObject.ControlText);
        _keepAlive.Add(nativeObject);
        var ptr = Marshal.GetComInterfaceForObject(nativeObject, typeof(ComDataObject));
        var runtime = Assembly.Load("MicroCom.Runtime").GetType("MicroCom.Runtime.MicroComRuntime", true)!;
        var nativeInterface = Assembly.Load("Avalonia.Win32").GetType("Avalonia.Win32.Win32Com.IDataObject", true)!;
        var create = runtime.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "CreateProxyFor" && m.IsGenericMethodDefinition && m.GetParameters() is [var pointer, var owned] && pointer.ParameterType == typeof(IntPtr) && owned.ParameterType == typeof(bool));
        var proxy = create.MakeGenericMethod(nativeInterface).Invoke(null, [ptr, true])!;
        try
        {
            var wrapperType = Assembly.Load("Avalonia.Win32").GetType("Avalonia.Win32.OleDataObjectToDataTransferWrapper", true)!;
            var wrapper = (IDataTransfer)Activator.CreateInstance(wrapperType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [proxy], null)!;
            // The installed ClipboardImpl performs this same eager formats access.
            _ = wrapper.Formats;
            var asyncWrapperType = typeof(IDataTransfer).Assembly.GetType("Avalonia.Input.SyncToAsyncDataTransfer", true)!;
            var asyncWrapper = (IAsyncDataTransfer)Activator.CreateInstance(asyncWrapperType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [wrapper], null)!;
            return Task.FromResult<IAsyncDataTransfer?>(asyncWrapper);
        }
        finally { ((IDisposable)proxy).Dispose(); }
    }
    public Task ClearAsync() => throw new InvalidOperationException("Clipboard writes forbidden in this diagnostic.");
    public Task SetDataAsync(IAsyncDataTransfer? value) => throw new InvalidOperationException("Clipboard writes forbidden in this diagnostic.");
    public Task FlushAsync() => throw new InvalidOperationException("Clipboard writes forbidden in this diagnostic.");
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TestOleDataObject(bool failEnumeration, string text) : ComDataObject
{
    public const string ControlText = "二维视图 clipboard-native-control-20261026";
    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        Program.Log("native-com-enum-format-etc", new { ControlledFailure = failEnumeration, Direction = direction.ToString() });
        if (failEnumeration) throw new COMException("Controlled native IDataObject enumeration failure", unchecked((int)0x8007000E));
        return new TestFormatEnumerator();
    }
    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        if (QueryGetData(ref format) != 0) throw new COMException("Unsupported test format", unchecked((int)0x80040064));
        var chars = (text + '\0').ToCharArray();
        var mem = Native.GlobalAlloc(0x42, (nuint)(chars.Length * 2));
        var ptr = Native.GlobalLock(mem);
        Marshal.Copy(chars, 0, ptr, chars.Length);
        Native.GlobalUnlock(mem);
        medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = mem, pUnkForRelease = null };
    }
    public int QueryGetData(ref FORMATETC format) => format.cfFormat == 13 && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : unchecked((int)0x80040064);
    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
    public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = input; output.ptd = IntPtr.Zero; return 0x00040130; }
    public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotImplementedException();
    public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return unchecked((int)0x80040003); }
    public void DUnadvise(int connection) => throw new NotImplementedException();
    public int EnumDAdvise(out IEnumSTATDATA? advise) { advise = null; return unchecked((int)0x80040003); }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TestFormatEnumerator : IEnumFORMATETC
{
    private bool _read;
    public int Next(int count, FORMATETC[] formats, int[]? fetched)
    {
        if (!_read && count > 0)
        {
            formats[0] = new FORMATETC { cfFormat = 13, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
            _read = true;
            if (fetched is { Length: > 0 }) fetched[0] = 1;
            return count == 1 ? 0 : 1;
        }
        if (fetched is { Length: > 0 }) fetched[0] = 0;
        return 1;
    }
    public int Skip(int count) { _read = true; return count == 1 ? 0 : 1; }
    public int Reset() { _read = false; return 0; }
    public void Clone(out IEnumFORMATETC clone) => clone = new TestFormatEnumerator { _read = _read };
}

internal static class Native
{
    [DllImport("kernel32.dll")] internal static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetDllDirectory(string path);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] internal static extern IntPtr GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(IntPtr memory);
}
