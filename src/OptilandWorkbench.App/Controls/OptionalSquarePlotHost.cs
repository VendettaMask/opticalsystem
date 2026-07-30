using Avalonia;
using Avalonia.Controls;

namespace OptilandWorkbench.App.Controls;

internal sealed class OptionalSquarePlotHost : Decorator
{
    private bool _isSquare;

    public bool IsSquare
    {
        get => _isSquare;
        set
        {
            if (_isSquare == value)
            {
                return;
            }

            _isSquare = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            return default;
        }

        if (!IsSquare)
        {
            Child.Measure(availableSize);
            return Child.DesiredSize;
        }

        var squareSize = SquareSize(availableSize);
        Child.Measure(squareSize);
        return Child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null)
        {
            return finalSize;
        }

        Child.Arrange(SquareBounds(finalSize, IsSquare));
        return finalSize;
    }

    internal static Rect SquareBounds(Size finalSize, bool isSquare)
    {
        if (!isSquare)
        {
            return new Rect(finalSize);
        }

        var side = Math.Min(finalSize.Width, finalSize.Height);
        return new Rect(
            (finalSize.Width - side) / 2,
            (finalSize.Height - side) / 2,
            side,
            side);
    }

    private static Size SquareSize(Size availableSize)
    {
        var finiteWidth = double.IsFinite(availableSize.Width);
        var finiteHeight = double.IsFinite(availableSize.Height);
        if (!finiteWidth && !finiteHeight)
        {
            return availableSize;
        }

        var side = finiteWidth && finiteHeight
            ? Math.Min(availableSize.Width, availableSize.Height)
            : finiteWidth
                ? availableSize.Width
                : availableSize.Height;
        return new Size(side, side);
    }
}
