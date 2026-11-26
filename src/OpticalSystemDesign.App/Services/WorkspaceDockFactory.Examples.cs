using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Orientation = Dock.Model.Core.Orientation;

namespace OpticalSystemDesign.App.Services;

public sealed partial class WorkspaceDockFactory
{
    private IDock CreateExampleDocuments(ExampleWorkspacePane layout)
    {
        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        var paneCount = 0;
        var groupCount = 0;
        return Build(layout).Dock;

        (IDock Dock, bool Focused) Build(ExampleWorkspacePane node)
        {
            if (node.Windows is { Length: > 0 } windows)
            {
                var pane = (DocumentDock)CreateDocumentDock();
                var hasLens = windows.Any(w => w.Kind == WorkspaceDocumentTypes.LensEditor);
                pane.Id = hasLens ? DocumentDockId : $"workspace:sample-pane:{++paneCount}";
                pane.Title = "文档";
                pane.IsCollapsable = !hasLens;
                pane.VisibleDockables = CreateList<IDockable>();
                var focused = false;
                foreach (var window in windows)
                {
                    var descriptor = window.Descriptor(documentIds);
                    _descriptors[descriptor.Id] = descriptor;
                    var document = CreateDocument(descriptor);
                    pane.VisibleDockables.Add(document);
                    if (window.Focused)
                    {
                        pane.FocusedDockable = document;
                        focused = true;
                    }
                }
                pane.ActiveDockable = pane.VisibleDockables[node.Selected];
                pane.DefaultDockable = pane.ActiveDockable;
                if (hasLens) PrimaryDocumentDock = pane;
                return (pane, focused);
            }

            var group = new ProportionalDock
            {
                Id = $"workspace:sample-group:{++groupCount}",
                Orientation = node.Direction == "Vertical" ? Orientation.Vertical : Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>()
            };
            var children = node.Children!;
            var total = children.Sum(child => child.Weight);
            var groupFocused = false;
            foreach (var child in children)
            {
                var (dock, focused) = Build(child);
                if (group.VisibleDockables.Count > 0) group.VisibleDockables.Add(new ProportionalDockSplitter());
                dock.Proportion = child.Weight / total;
                group.VisibleDockables.Add(dock);
                if (focused || group.ActiveDockable is null) group.ActiveDockable = dock;
                groupFocused |= focused;
            }
            group.DefaultDockable = group.ActiveDockable;
            return (group, groupFocused);
        }
    }
}
