namespace OptilandWorkbench.App.Services;

public sealed class ActionManager
{
    private readonly List<AppAction> _actions = new();

    public IReadOnlyList<AppAction> Actions => _actions;

    public AppAction Register(string id, string text, string category, Func<Task> executeAsync)
    {
        var action = new AppAction(id, text, category, executeAsync);
        _actions.Add(action);
        return action;
    }

    public AppAction Register(string id, string text, string category, Action execute)
    {
        return Register(id, text, category, () =>
        {
            execute();
            return Task.CompletedTask;
        });
    }

    public AppAction Find(string id)
    {
        return _actions.First(action => action.Id == id);
    }

    public IReadOnlyList<AppAction> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _actions;
        }

        return _actions
            .Where(action =>
                action.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || action.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || action.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

public sealed record AppAction(string Id, string Text, string Category, Func<Task> ExecuteAsync)
{
    public override string ToString()
    {
        return $"{Text}    {Category}";
    }
}
