# Application Branding

The desktop application uses one optical mark across its executable icon, window icon, and startup image. The mark shows a biconvex lens bringing three colored rays to a common focus.

## Assets

Brand files are stored under `src/OptilandWorkbench.App/Assets/Brand`:

- `AppIcon.png`: 1024 × 1024 RGBA source used by Avalonia windows.
- `AppIcon.ico`: Windows icon containing 16, 24, 32, 48, 64, 128, and 256 pixel variants.
- `AppIcon.icns`: macOS application icon for bundle packaging.
- `Splash.png`: 1280 × 720 startup image.

All four files are copied to publish output. The project also assigns the ICO through the .NET `ApplicationIcon` property, while the PNG files are embedded as Avalonia resources for the live window and startup screen. On macOS, a platform-guarded Objective-C bridge assigns the packaged PNG to `NSApplication` before the first window appears, so `dotnet run` receives the branded Dock icon immediately instead of briefly showing Avalonia's generic development icon.

## Regeneration

All raster and platform assets are generated deterministically from one script:

```bash
python tools/generate_brand_assets.py
```

The script requires Pillow. Regenerate every format together so the executable, live window, and startup image do not drift apart. After regeneration, run the solution build and `BrandAssetTests`, then inspect both the icon at small size and the full startup image.

## Startup Lifecycle

The splash window opens before the main workbench is constructed. The main window initializes invisibly and signals readiness after restoring its workspace; the application then closes the splash, reveals the workbench, and transfers the desktop lifetime to the main window. A short minimum display interval prevents a distracting flash on fast machines.
