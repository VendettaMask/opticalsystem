using System.Runtime.InteropServices;

namespace OptilandWorkbench.App;

internal static class MacOsBranding
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static bool TryApplyApplicationIcon()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Brand",
            "AppIcon.png");
        if (!File.Exists(iconPath))
        {
            return false;
        }

        try
        {
            var application = Send(GetClass("NSApplication"), Selector("sharedApplication"));
            var imageClass = GetClass("NSImage");
            var image = Send(
                Send(imageClass, Selector("alloc")),
                Selector("initWithContentsOfFile:"),
                NativeString(iconPath));
            if (application == 0 || image == 0)
            {
                return false;
            }

            SendVoid(application, Selector("setApplicationIconImage:"), image);
            SendVoid(image, Selector("release"));
            return true;
        }
        catch (Exception) when (OperatingSystem.IsMacOS())
        {
            return false;
        }
    }

    private static nint NativeString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(
                GetClass("NSString"),
                Selector("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static nint GetClass(string name) => ObjectiveCGetClass(name);

    private static nint Selector(string name) => SelectorRegisterName(name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint ObjectiveCGetClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint SelectorRegisterName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint argument);
}
