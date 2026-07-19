using System.Text.Json;

namespace OptilandWorkbench.App.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public double LeftPaneWidth { get; set; } = 286;

    public int LeftTabIndex { get; set; }

    public int RightTabIndex { get; set; }

    public Dictionary<int, WorkspaceLayoutState> LayoutSlots { get; set; } = new();

    public Dictionary<string, Dictionary<string, string>> AnalysisSettings { get; set; } = new();

    public WorkspaceLayoutState CurrentLayout => new(LeftPaneWidth, LeftTabIndex, RightTabIndex);

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
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
