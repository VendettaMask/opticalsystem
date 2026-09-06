namespace OptilandWorkbench.ZemaxComparison.Zemax;

public static class ZemaxScreenshotExecutor
{
    // Port of the existing native ZPL screenshot workflow. The explicit CFG argument prevents a second,
    // implicit-default analysis from being substituted for the numeric capture. All orchestration is C#.
    private const string Macro = """
input_file$ = $GETARG("Lens")
settings_file$ = $GETARG("Settings")
output_file$ = $GETARG("Image")
analysis_code$ = $GETARG("Code")
LOADLENS input_file$
UPDATE ALL
OPENANALYSISWINDOW analysis_code$, settings_file$
PAUSE THREADS
window_number = WINL()
EXPORTJPG window_number, output_file$
CLOSEWINDOW window_number
""";
    public static async Task<string> Capture(string apiPath, string directory, string? code, int timeout, CancellationToken ct)
    {
        if (code is null) return "Unsupported: no verified native ZPL window code";
        var macro = Path.Combine(directory, "capture-native-window.zpl");
        File.WriteAllText(macro, Macro);
        var image = Path.Combine(directory, "screenshot.jpg");
        var result = await ProcessIsolation.Run(Path.Combine(apiPath, "OpticStudio.exe"),
            ["-zpl=" + macro, "-vLens=" + Path.Combine(directory, "screenshot-input.ZMX"),
            "-vSettings=" + Path.Combine(directory, "settings.CFG"), "-vImage=" + image, "-vCode=" + code], directory, timeout, ct);
        File.WriteAllText(Path.Combine(directory, "screenshot-process.log"), result.StandardOutput + result.StandardError);
        if (result.Cancelled) return "Cancelled";
        if (result.TimedOut) return "TimedOut (numeric capture retained)";
        if (!File.Exists(image)) return "Unavailable: native window produced no image; see screenshot-process.log";
        var bytes = File.ReadAllBytes(image);
        if (bytes.Length < 3 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff) return "Failed: invalid JPEG export";
        var actualName = Directory.EnumerateFiles(directory).Single(p => string.Equals(Path.GetFileName(p), "screenshot.jpg", StringComparison.OrdinalIgnoreCase));
        if (Path.GetFileName(actualName) != "screenshot.jpg")
        {
            File.Move(actualName, image + ".rename");
            File.Move(image + ".rename", image);
        }
        JsonFiles.Write(Path.Combine(directory, "screenshot-provenance.json"), new
        {
            Kind = "NativeOpticStudioWindow",
            Code = code,
            LensSha256 = JsonFiles.Hash(File.ReadAllBytes(Path.Combine(directory, "screenshot-input.ZMX"))),
            CapturedSettingsSha256 = JsonFiles.Hash(File.ReadAllBytes(Path.Combine(directory, "settings.CFG"))),
            ImageSha256 = JsonFiles.Hash(bytes),
            NumericPass = false
        });
        return "CapturedNativeJpegWithExplicitCfg";
    }
}
