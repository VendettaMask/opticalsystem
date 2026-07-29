using Avalonia.Media;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

public sealed class MeritOperandRowPaletteTests
{
    [Theory]
    [InlineData("BLNK", 255, 255, 255)]
    [InlineData("DMFS", 247, 177, 239)]
    [InlineData("TTHI", 188, 188, 248)]
    [InlineData("OPLT", 231, 237, 224)]
    [InlineData("EFFL", 190, 218, 242)]
    [InlineData("PMAG", 246, 243, 190)]
    [InlineData("CONS", 211, 231, 232)]
    [InlineData("DIVI", 255, 198, 198)]
    [InlineData("MNEA", 255, 221, 221)]
    [InlineData("MNCG", 208, 251, 211)]
    [InlineData("MNEG", 190, 218, 242)]
    [InlineData("MXEG", 188, 188, 248)]
    public void ReferenceZemaxOperandsUseExpectedRowColors(
        string type,
        byte red,
        byte green,
        byte blue)
    {
        Assert.Equal(Color.FromRgb(red, green, blue), MeritOperandRowPalette.Resolve(type));
    }

    [Fact]
    public void OperandFamiliesAndUnknownTypesHaveStableFallbackColors()
    {
        Assert.Equal(
            MeritOperandRowPalette.Resolve("RSCE"),
            MeritOperandRowPalette.Resolve("RWFE"));
        Assert.Equal(
            MeritOperandRowPalette.Resolve("TRAC"),
            MeritOperandRowPalette.Resolve("ANAY"));
        Assert.Equal(
            Color.FromRgb(236, 244, 241),
            MeritOperandRowPalette.Resolve("UNKNOWN"));
    }

    [Fact]
    public void ErrorStateOverridesOperandRowColor()
    {
        Assert.Equal(
            Color.FromRgb(255, 198, 198),
            MeritOperandRowPalette.Resolve("EFFL", hasError: true));
    }
}
