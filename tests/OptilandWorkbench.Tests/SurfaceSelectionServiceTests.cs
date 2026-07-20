using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceSelectionServiceTests
{
    [Fact]
    public void SelectPublishesOnlyWhenSurfaceChanges()
    {
        var selection = new SurfaceSelectionService();
        var observed = new List<int?>();
        selection.Changed += (_, args) => observed.Add(args.SurfaceNumber);

        selection.Select(3);
        selection.Select(3);
        selection.Select(7);
        selection.Select(null);

        Assert.Null(selection.SelectedSurfaceNumber);
        Assert.Equal(new int?[] { 3, 7, null }, observed);
    }
}
