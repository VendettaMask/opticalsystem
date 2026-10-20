using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LocalizedCommandSearchTests
{
    [Theory]
    [InlineData("zh-CN", "快速聚焦", "优化")]
    [InlineData("en-US", "Quick Focus", "Optimize")]
    [InlineData("ja-JP", "クイックフォーカス", "最適化")]
    public void SearchFindsTranslatedNamesAndCategoriesWithoutMutatingActionsOrLanguage(
        string language, string name, string category)
    {
        var actions = new ActionManager();
        var action = actions.Register("quick-focus", "快速聚焦", "优化", () => { });
        var before = UiLocalization.CurrentLanguage;
        Assert.Same(action, Assert.Single(actions.Search(name)));
        Assert.Same(action, Assert.Single(actions.Search("  " + category + "  ")));
        Assert.Same(action, Assert.Single(actions.Search("QUICK-FOCUS")));
        Assert.Equal(name, UiLocalization.Text(action.Text, language));
        Assert.Equal(before, UiLocalization.CurrentLanguage);
        Assert.Equal("快速聚焦", action.Text);
        Assert.Equal("优化", action.Category);
        Assert.Equal("quick-focus", action.Id);
        Assert.Empty(actions.Search("definitely-not-a-command"));
    }
}
