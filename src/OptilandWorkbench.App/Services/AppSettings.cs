using System.Text.Json;

namespace OptilandWorkbench.App.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public double LeftPaneWidth { get; set; } = 520;

    public int LeftTabIndex { get; set; }

    public int RightTabIndex { get; set; }

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
        File.WriteAllText(SettingsPath, json);
    }

    private static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var root = string.IsNullOrWhiteSpace(appData)
                ? AppContext.BaseDirectory
                : appData;
            return Path.Combine(root, "OptilandWorkbench", "settings.json");
        }
    }
}
