using System.Text.Json;

namespace OptilandWorkbench.App.Services;

public sealed class AppSettings
{
    public const string DefaultTheme = "Light";
    public const int DefaultDecimalPlaces = 6;
    public const int DefaultUpperScientificExponent = 6;
    public const int DefaultLowerScientificExponent = -4;
    public const double DefaultFontSize = 13;

    public string Theme { get; set; } = DefaultTheme;

    public int DecimalPlaces { get; set; } = DefaultDecimalPlaces;

    public int UpperScientificExponent { get; set; } = DefaultUpperScientificExponent;

    public int LowerScientificExponent { get; set; } = DefaultLowerScientificExponent;

    public string FontFamily { get; set; } = string.Empty;

    public string FontShape { get; set; } = "Regular";

    public double FontSize { get; set; } = DefaultFontSize;

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public double LeftPaneWidth { get; set; } = 286;

    public int LeftTabIndex { get; set; }

    public int RightTabIndex { get; set; }

    public Dictionary<int, WorkspaceLayoutState> LayoutSlots { get; set; } = new();

    public Dictionary<string, Dictionary<string, string>> AnalysisSettings { get; set; } = new();

    public WorkspaceLayoutState CurrentLayout => new(LeftPaneWidth, LeftTabIndex, RightTabIndex);

    public void NormalizeDisplaySettings()
    {
        Theme = Theme is "Dark" or "Isekai" or "System" ? Theme : DefaultTheme;
        DecimalPlaces = Math.Clamp(DecimalPlaces, 0, 15);
        UpperScientificExponent = Math.Clamp(UpperScientificExponent, 1, 15);
        LowerScientificExponent = Math.Clamp(LowerScientificExponent, -15, -1);
        if (LowerScientificExponent >= UpperScientificExponent)
        {
            LowerScientificExponent = Math.Min(-1, UpperScientificExponent - 1);
        }

        FontFamily = FontFamily?.Trim() ?? string.Empty;
        FontShape = FontShape is "Bold" or "Italic" or "BoldItalic"
            ? FontShape
            : "Regular";
        FontSize = Math.Clamp(double.IsFinite(FontSize) ? FontSize : DefaultFontSize, 9, 32);
    }

    public void ResetDisplaySettings()
    {
        Theme = DefaultTheme;
        DecimalPlaces = DefaultDecimalPlaces;
        UpperScientificExponent = DefaultUpperScientificExponent;
        LowerScientificExponent = DefaultLowerScientificExponent;
        FontFamily = string.Empty;
        FontShape = "Regular";
        FontSize = DefaultFontSize;
    }

    public void ApplyLayout(WorkspaceLayoutState layout)
    {
        LeftPaneWidth = layout.LeftPaneWidth;
        LeftTabIndex = layout.LeftTabIndex;
        RightTabIndex = layout.RightTabIndex;
    }

    public void SaveLayoutSlot(int slot, WorkspaceLayoutState layout)
    {
        LayoutSlots[slot] = layout;
        Save();
    }

    public WorkspaceLayoutState? LoadLayoutSlot(int slot)
    {
        return LayoutSlots.TryGetValue(slot, out var layout) ? layout : null;
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.NormalizeDisplaySettings();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = $"{SettingsPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string SettingsPath
    {
        get
        {
            var settingsDirectory = Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                return Path.Combine(settingsDirectory, "settings.json");
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var root = string.IsNullOrWhiteSpace(appData)
                ? AppContext.BaseDirectory
                : appData;
            return Path.Combine(root, "OptilandWorkbench", "settings.json");
        }
    }
}

public sealed record WorkspaceLayoutState(
    double LeftPaneWidth = 286,
    int LeftTabIndex = 0,
    int RightTabIndex = 0);
