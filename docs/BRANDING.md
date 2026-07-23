# Application Branding

The desktop application icon uses a cinematic black-hole mark with a warm gravitationally lensed accretion disk and the product name `Optical System Design`. The startup view extends the same visual identity into a widescreen composition, with the black hole on the left and crisp runtime-rendered product information and loading progress on the right.

## Assets

Brand files are stored under `src/OptilandWorkbench.App/Assets/Brand`:

- `AppIconArtwork.png`: 1024 × 1024 RGBA master black-hole artwork.
- `AppIcon.png`: 1024 × 1024 RGBA runtime icon with transparent rounded corners and platform-safe visual padding.
- `AppIcon.ico`: Windows icon containing 16, 24, 32, 48, 64, 128, and 256 pixel variants.
- `AppIcon.icns`: macOS application icon for bundle packaging.
- `Splash.png`: 1280 × 720 text-free black-hole startup background; Avalonia overlays the product identity, version, loading phase, percentage, and progress bar.

All four files are copied to publish output. The project also assigns the ICO through the .NET `ApplicationIcon` property, while the PNG files are embedded as Avalonia resources for the live window and startup screen. On macOS, a platform-guarded Objective-C bridge assigns the packaged PNG to `NSApplication` before the first window appears, so `dotnet run` receives the branded Dock icon immediately instead of briefly showing Avalonia's generic development icon.

## Regeneration

The approved black-hole source can be given a true transparent rounded silhouette and repackaged for Windows and macOS with:

```bash
python tools/round_brand_icon.py src/OptilandWorkbench.App/Assets/Brand
```

The legacy deterministic generator remains available for the original lens-based branding only:

```bash
python tools/generate_brand_assets.py
```

The script requires Pillow and still contains the original lens-mark artwork; do not use it to overwrite the approved black-hole desktop icon or splash background. Platform icon derivatives must be regenerated from `AppIcon.png`: the ICO contains 16, 24, 32, 48, 64, 128, and 256 pixel PNG frames, while the ICNS contains the standard macOS 16 through 1024 pixel iconset. After changing an asset, run the solution build and `BrandAssetTests`, then inspect both the icon at small size and the full startup view.

## Startup Lifecycle

The splash window opens before the main workbench is constructed. The main window initializes invisibly and signals readiness after restoring its workspace; the application then closes the splash, reveals the workbench, and transfers the desktop lifetime to the main window. A short minimum display interval prevents a distracting flash on fast machines.
