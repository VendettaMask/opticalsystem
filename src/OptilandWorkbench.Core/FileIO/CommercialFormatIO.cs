namespace OptilandWorkbench.Core.FileIO;

public interface IOpticalFormatImporter
{
    string FormatName { get; }

    string[] Extensions { get; }

    Optic Import(string text);
}

public interface IOpticalFormatExporter
{
    string FormatName { get; }

    string Export(Optic optic);
}

public sealed class SequentialLensTextImporter : IOpticalFormatImporter
{
    public string FormatName => "common-sequential-lens";

    public string[] Extensions { get; } = { ".zmx", ".seq", ".len" };

    public Optic Import(string text)
    {
        var optic = new Optic("Imported sequential lens");
        foreach (var line in text.Split('\n'))
        {
            var columns = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 3 && double.TryParse(columns[0], out var radius) && double.TryParse(columns[1], out var thickness))
            {
                optic.SurfaceGroup.Items.Add(new Domain.OpticalSurface
                {
                    Radius = radius,
                    Thickness = thickness,
                    Material = columns[2]
                });
            }
        }

        return optic;
    }
}

public sealed class SequentialLensTextExporter : IOpticalFormatExporter
{
    public string FormatName => "common-sequential-lens";

    public string Export(Optic optic)
    {
        return string.Join(Environment.NewLine, optic.SurfaceGroup.Items.Select(surface =>
            $"{surface.Radius:0.########} {surface.Thickness:0.########} {surface.Material}"));
    }
}
