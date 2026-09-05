Optical System Design - S.T.A.R. Labs

If you received a *-Setup.exe, run it to use the installation wizard.
Choose an installation folder and optional shortcuts. Windows Settings > Apps
provides the uninstall entry. Installation is per-user, without elevation.

If you received a portable folder instead, start OptilandWorkbench.App.exe.
A separate .NET runtime installation is not required on the target computer.
Use the build that matches the target architecture (win-x64 or win-arm64).

When distributing the portable version, copy the ENTIRE directory, not just the EXE. DLLs, Assets, and LensLibrary
must remain beside the application. Manufacturer catalogs are kept separately
under LensLibrary/StockCatalogs and are not compressed.

Third-party font and icon notices are included under Assets/Fonts and Assets/Icons.
The package is not digitally signed. Do not bypass organizational security
policies if Windows or endpoint protection requests a trusted signature.

The application stores user settings and sessions outside this release folder.
Uninstalling preserves those settings and additional user-created files; it removes
files shipped by the installer. Save edited bundled examples and catalogs to a
separate user folder, since shipped files can be replaced on reinstall or removed
on uninstall. Close the application normally before updating or uninstalling.
