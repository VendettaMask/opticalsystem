using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

internal sealed record CoatingSavedLayout(int Version, string Tree, Dictionary<string, string> ActiveDockables, string? ActivePage);
internal readonly record struct CoatingScreenArea(double X, double Y, double Width, double Height, double Scale);
internal readonly record struct CoatingWindowRectangle(double X, double Y, double Width, double Height);

internal static class CoatingDockLayoutStore
{
    internal static string DefaultPath => Path.Combine(Path.GetDirectoryName(CoatingPlotDefaults.DefaultPath)!, "coating-workspace-layout.json");
    internal static void Save(string path, string layout)
    {
        Parse(layout);
        BoundedFile.WriteAllTextAtomic(path, layout, BoundedFile.MaximumSettingsBytes, "镀膜工作区布局");
    }
    internal static string Load(string path)
    {
        var json = BoundedFile.ReadAllText(path, BoundedFile.MaximumSettingsBytes, "镀膜工作区布局");
        Parse(json); return json;
    }
    internal static CoatingSavedLayout Parse(string json)
    {
        var saved = JsonSerializer.Deserialize<CoatingSavedLayout>(json);
        if (saved is not { Version: 1 } || string.IsNullOrWhiteSpace(saved.Tree) || saved.ActiveDockables is null
            || saved.ActiveDockables.Count > 64)
            throw new InvalidDataException("镀膜布局版本或内容无效，当前布局保持。");
        return saved;
    }
    internal static CoatingWindowRectangle ClampWindow(double x, double y, double width, double height, CoatingScreenArea[] screens)
    {
        var usable = screens.Where(s => double.IsFinite(s.X) && double.IsFinite(s.Y) && double.IsFinite(s.Width)
            && double.IsFinite(s.Height) && double.IsFinite(s.Scale) && s.Width > 0 && s.Height > 0 && s.Scale > 0).ToArray();
        if (usable.Length == 0) usable = [new(0, 0, 1440, 900, 1)];
        var screen = usable.OrderByDescending(s => double.IsFinite(x) && double.IsFinite(y)
            ? Math.Max(0, Math.Min(x + (double.IsFinite(width) ? width : 720) * s.Scale, s.X + s.Width) - Math.Max(x, s.X))
                * Math.Max(0, Math.Min(y + (double.IsFinite(height) ? height : 540) * s.Scale, s.Y + s.Height) - Math.Max(y, s.Y)) : 0).First();
        var maximumWidth = screen.Width / screen.Scale; var maximumHeight = screen.Height / screen.Scale;
        var w = Math.Clamp(double.IsFinite(width) && width > 0 ? width : 900, Math.Min(380, maximumWidth), maximumWidth);
        var h = Math.Clamp(double.IsFinite(height) && height > 0 ? height : 650, Math.Min(300, maximumHeight), maximumHeight);
        return new(Math.Clamp(double.IsFinite(x) ? x : screen.X, screen.X, screen.X + screen.Width - w * screen.Scale),
            Math.Clamp(double.IsFinite(y) ? y : screen.Y, screen.Y, screen.Y + screen.Height - h * screen.Scale), w, h);
    }
}
