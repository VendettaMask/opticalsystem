using Avalonia;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Controls;

public sealed class OpticSceneObjectPickedEventArgs(int number, bool toggle) : EventArgs
{
    public int Number { get; } = number;
    public bool Toggle { get; } = toggle;
}

public sealed partial class OpticSceneControl
{
    private readonly List<(int Number, Point[] Points)> _objectPickFaces = [];
    private Point _objectPress;
    private bool _objectDrag;
    private Scene3Dto? _objectFitScene;
    private Size _objectFitSize;
    private double _objectFitStretch;
    private double _objectFitScale;
    /// <summary>Object-workspace fitting; ordinary optical drawing geometry is unchanged.</summary>
    public bool FitObjectBounds { get; set; }
    public IReadOnlySet<int> HighlightedObjectNumbers { get; set; } = new HashSet<int>();
    public event EventHandler<OpticSceneObjectPickedEventArgs>? ObjectPicked;
    public void ClearObjectPicking() => _objectPickFaces.Clear();
    internal Rect? ObjectPickingBounds
    {
        get
        {
            var points = _objectPickFaces.SelectMany(f => f.Points).ToArray();
            return points.Length == 0 ? null : new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)),
                new Point(points.Max(p => p.X), points.Max(p => p.Y)));
        }
    }

    public bool FocusObjects(IReadOnlySet<int> numbers)
    {
        var points = _objectPickFaces.Where(f => numbers.Contains(f.Number)).SelectMany(f => f.Points).ToArray();
        if (points.Length == 0) return false;
        var center = new Point((points.Min(p => p.X) + points.Max(p => p.X)) / 2, (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2);
        _viewport.PanBy(new Vector(Bounds.Width / 2 - center.X, Bounds.Height / 2 - center.Y));
        InvalidateVisual(); return true;
    }
    internal Point? ObjectPickPoint(int number) => _objectPickFaces.LastOrDefault(f => f.Number == number).Points is { Length: > 0 } points
        ? new Point(points.Average(p => p.X), points.Average(p => p.Y)) : null;

    internal int? PickObjectAt(Point point, bool toggle = false)
    {
        foreach (var face in _objectPickFaces.AsEnumerable().Reverse())
        {
            var inside = false; var points = face.Points;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                if ((points[i].Y > point.Y) != (points[j].Y > point.Y)
                    && point.X < (points[j].X - points[i].X) * (point.Y - points[i].Y) / (points[j].Y - points[i].Y) + points[i].X)
                    inside = !inside;
            if (!inside) continue;
            ObjectPicked?.Invoke(this, new(face.Number, toggle)); return face.Number;
        }
        return null;
    }
}
