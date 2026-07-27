using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

public sealed class LensEditorLayoutTests
{
    [Fact]
    public void NumericEditorAlignsTextToTheRight()
    {
        var factory = typeof(LensEditorPanel).GetMethod(
            "CreateNumericEditor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(factory);
        var editor = Assert.IsType<TextBox>(factory.Invoke(
            null,
            new object[] { "12.345", (Action<string>)(_ => { }) }));
        Assert.Equal(TextAlignment.Right, editor.TextAlignment);
        Assert.Equal(HorizontalAlignment.Right, editor.HorizontalContentAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, editor.HorizontalAlignment);
    }

    [Fact]
    public void NumericDataGridColumnAlignsHeaderAndCellsToTheRight()
    {
        var factory = typeof(LensEditorPanel).GetMethod(
            "NumericColumn",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(factory);
        var column = Assert.IsType<DataGridTextColumn>(factory.Invoke(
            null,
            new object[] { "圆锥系数", "Conic", 100d, false }));
        Assert.Equal("numeric", column.Tag);

        var header = Assert.IsType<TextBlock>(column.Header);
        Assert.Equal(TextAlignment.Right, header.TextAlignment);
        Assert.Equal(HorizontalAlignment.Right, header.HorizontalAlignment);

        Assert.NotNull(column.CellTheme);
        var alignment = Assert.IsType<Setter>(Assert.Single(column.CellTheme.Setters));
        Assert.Equal(DataGridCell.HorizontalContentAlignmentProperty, alignment.Property);
        Assert.Equal(HorizontalAlignment.Right, alignment.Value);
    }
}
