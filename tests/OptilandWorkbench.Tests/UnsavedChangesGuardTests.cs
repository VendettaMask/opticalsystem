using OptilandWorkbench.App;

namespace OptilandWorkbench.Tests;

public sealed class UnsavedChangesGuardTests
{
    [Fact]
    public async Task CleanDocumentContinuesWithoutShowingPromptOrSaving()
    {
        var prompted = false;
        var saved = false;

        var result = await UnsavedChangesGuard.CanContinueAsync(
            false,
            () =>
            {
                prompted = true;
                return Task.FromResult(UnsavedChangesChoice.Cancel);
            },
            () =>
            {
                saved = true;
                return Task.FromResult(true);
            });

        Assert.True(result);
        Assert.False(prompted);
        Assert.False(saved);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public async Task DirtyDocumentHonorsNonSaveChoice(
        int choiceValue,
        bool expected)
    {
        var saved = false;
        var choice = (UnsavedChangesChoice)choiceValue;

        var result = await UnsavedChangesGuard.CanContinueAsync(
            true,
            () => Task.FromResult(choice),
            () =>
            {
                saved = true;
                return Task.FromResult(true);
            });

        Assert.Equal(expected, result);
        Assert.False(saved);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SaveChoiceContinuesOnlyWhenSaveCompletes(bool saveResult, bool expected)
    {
        var result = await UnsavedChangesGuard.CanContinueAsync(
            true,
            () => Task.FromResult(UnsavedChangesChoice.Save),
            () => Task.FromResult(saveResult));

        Assert.Equal(expected, result);
    }
}
