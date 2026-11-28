# Non-sequential scene-tree previews

These are historical UI proposals rendered on 2026-11-23. Their interaction direction was integrated into the desktop workspace on 2026-11-24 using the existing production scene renderer, document services and transactional undo. See the [current workspace guide](../../../../NONSEQUENTIAL_WORKSPACE.md) for the delivered interactions. This folder retains the standalone design prototype and its original scope.

The latest proposal uses a native 32-channel STAROPT scene containing 432 objects. Structure, optical properties and simulation navigation share object identities; the prototype demonstrates ancestor-preserving search, channel isolation, selection synchronization, shared/mixed property display, a read-only object table and local selection sets. Its batch controls remain illustrative; the integrated product supplies real transactional edits, undo and persistent organization independently of this harness.

Run the standalone C# offscreen harness from the repository root:

```powershell
dotnet run --project docs/assets/ui-proposals/2026-11-23/non-sequential-scene-tree/prototype/Prototype.csproj -c Release -- .tmp/scene-tree-preview-output
```

It creates a native scene, prepares small-sample layout rays through the existing Core, checks the demonstrated interactions and writes seven screenshots with a scope receipt. It does not launch a foreground desktop window. The harness uses the existing test friend assembly identity in its own output directory and is not referenced by the product solution or installer.

The candidate viewport draws projected convex lens and box silhouettes from Core scene coordinates. It is a presentation experiment, not a general CAD renderer or numerical acceptance result. Previous first/second-revision images are retained as design history; the harness reproduces the latest layout and interaction checks rather than their exact historical pixels.

[Overview](v3-overview.png) · [Isolated channel](v3-workspace.png) · [Search](v3-search.png) · [Shared/mixed properties](v3-batch.png) · [Optical navigation](v3-optical.png) · [Simulation navigation](v3-simulation.png) · [Object table](v3-table.png)
