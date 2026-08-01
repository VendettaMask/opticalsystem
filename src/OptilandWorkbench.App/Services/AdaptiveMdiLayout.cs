using Dock.Model.Controls;
using Dock.Model.Core;

namespace OptilandWorkbench.App.Services;

internal static class AdaptiveMdiLayout
{
    private const double PreferredDocumentAspectRatio = 16.0 / 10.0;
    private const double EmptyCellPenalty = 0.35;

    public static void TileDocuments(IDocumentDock dock)
    {
        var documents = dock.VisibleDockables?
            .OfType<IMdiDocument>()
            .ToArray()
            ?? Array.Empty<IMdiDocument>();
        if (documents.Length == 0)
        {
            return;
        }

        dock.GetVisibleBounds(out _, out _, out var availableWidth, out var availableHeight);
        if (!IsUsableLength(availableWidth) || !IsUsableLength(availableHeight))
        {
            return;
        }

        var plan = Plan(documents.Length, availableWidth, availableHeight);
        var cellWidth = availableWidth / plan.Columns;
        var cellHeight = availableHeight / plan.Rows;

        for (var index = 0; index < documents.Length; index++)
        {
            var row = index / plan.Columns;
            var column = index % plan.Columns;
            var itemsInRow = Math.Min(plan.Columns, documents.Length - (row * plan.Columns));
            var rowOffset = (plan.Columns - itemsInRow) * cellWidth / 2;
            var document = documents[index];

            document.MdiState = MdiWindowState.Normal;
            document.MdiBounds = new DockRect(
                rowOffset + (column * cellWidth),
                row * cellHeight,
                cellWidth,
                cellHeight);
            document.MdiZIndex = index;
        }
    }

    internal static GridPlan Plan(int documentCount, double availableWidth, double availableHeight)
    {
        if (documentCount <= 0)
        {
            return new GridPlan(0, 0);
        }

        if (!IsUsableLength(availableWidth) || !IsUsableLength(availableHeight))
        {
            return new GridPlan(1, documentCount);
        }

        var workspaceAspectRatio = availableWidth / availableHeight;
        var bestPlan = new GridPlan(1, documentCount);
        var bestScore = double.PositiveInfinity;

        for (var rows = 1; rows <= documentCount; rows++)
        {
            var columns = (int)Math.Ceiling(documentCount / (double)rows);
            var cellAspectRatio = workspaceAspectRatio * rows / columns;
            var aspectError = Math.Abs(Math.Log(cellAspectRatio / PreferredDocumentAspectRatio));
            var emptyCells = (rows * columns) - documentCount;
            var emptyRatio = emptyCells / (double)(rows * columns);
            var score = aspectError + (emptyRatio * EmptyCellPenalty);

            if (score < bestScore)
            {
                bestScore = score;
                bestPlan = new GridPlan(rows, columns);
            }
        }

        return bestPlan;
    }

    private static bool IsUsableLength(double value) =>
        double.IsFinite(value) && value > 0;

    internal readonly record struct GridPlan(int Rows, int Columns);
}
