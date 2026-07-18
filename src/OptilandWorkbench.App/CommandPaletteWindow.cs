using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed class CommandPaletteWindow : Window
{
    private readonly ActionManager _actions;
    private readonly TextBox _search = new()
    {
        PlaceholderText = "搜索命令、面板或动作",
        MinWidth = 420
    };
    private readonly ListBox _list = new()
    {
        MinHeight = 260
    };

    public CommandPaletteWindow(ActionManager actions)
    {
        _actions = actions;

        Title = "命令面板";
        Width = 560;
        Height = 420;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();

        _search.TextChanged += (_, _) => Refresh();
        _search.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
            }
            else if (args.Key == Key.Down && _list.ItemCount > 0)
            {
                args.Handled = true;
                _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
            }
            else if (args.Key == Key.Up && _list.ItemCount > 0)
            {
                args.Handled = true;
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
            }
            else if (args.Key == Key.Enter)
            {
                args.Handled = true;
                await RunSelectedAsync();
            }
        };
        _list.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
            }
            else if (args.Key == Key.Enter)
            {
                args.Handled = true;
                await RunSelectedAsync();
            }
        };
        _list.DoubleTapped += async (_, _) => await RunSelectedAsync();

        Refresh();
        Opened += (_, _) => _search.Focus();
    }

    private Control BuildContent()
    {
        var root = new DockPanel
        {
            Margin = new Avalonia.Thickness(12)
        };

        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_search);
        root.Children.Add(_list);
        return root;
    }

    private void Refresh()
    {
        _list.ItemsSource = _actions.Search(_search.Text ?? string.Empty);
        if (_list.SelectedItem is null && _list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private async Task RunSelectedAsync()
    {
        if (_list.SelectedItem is not AppAction action)
        {
            return;
        }

        if (await _actions.ExecuteAsync(action))
        {
            Close();
        }
    }
}
