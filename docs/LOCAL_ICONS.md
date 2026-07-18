# Local Icon Library

The Avalonia application embeds `lucide-static` version 1.25.0 as its local vector icon catalog. No icon font, JavaScript runtime, NuGet wrapper, or network connection is needed when the application runs.

Repository files:

- `src/OptilandWorkbench.App/Assets/Icons/lucide-icon-nodes.json` contains the complete icon-node catalog.
- `src/OptilandWorkbench.App/Assets/Icons/lucide-package.json` records the pinned upstream package metadata.
- `src/OptilandWorkbench.App/Assets/Icons/LUCIDE-LICENSE.txt` preserves the upstream ISC license and Feather attribution.
- `src/OptilandWorkbench.App/Controls/LocalIcon.cs` loads the catalog from the application assembly, then parses, caches, and renders it with native Avalonia geometry.

Use an icon-only control for familiar toolbar commands and provide a tooltip:

```csharp
var resetButton = new Button
{
    Content = new LocalIcon { IconName = "rotate-ccw", Width = 18, Height = 18 }
};
ToolTip.SetTip(resetButton, "重置视图");
```

Use `LocalIconLabel` for commands that need visible text:

```csharp
var saveButton = new Button
{
    Content = new LocalIconLabel("save", "保存")
};
```

Before adding a name, verify it with `LocalIconLibrary.Contains`. Unknown names render the local `circle-question-mark` fallback instead of leaving a blank control.

To update the catalog, download a specifically pinned `lucide-static` npm tarball, replace only the three files under `Assets/Icons`, and run the full build and test suite. Do not use a floating package version or fetch icons at runtime.
