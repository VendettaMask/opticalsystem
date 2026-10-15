Optical System Design - S.T.A.R. Labs

If you received a *-Setup.exe, run it to use the installation wizard.
Choose an installation folder and optional shortcuts. Windows Settings > Apps
provides the uninstall entry. Installation is per-user, without elevation.

If you received a portable folder instead, start OpticalSystemDesign.App.exe.
A separate .NET runtime installation is not required on the target computer.
Initial-structure and coating design are mandatory built-in modules. They run
inside the main application and are installed, upgraded, repaired and removed
together with it. There are no separate module installers or optional components.
Use the build that matches the target architecture (win-x64 or win-arm64).

When distributing the portable version, copy the ENTIRE directory, not just the EXE. DLLs, Assets, and LensLibrary
must remain beside the application. Manufacturer catalogs are kept separately
under LensLibrary/StockCatalogs and are not compressed.

Third-party font and icon notices are included under Assets/Fonts and Assets/Icons.
The default package is not digitally signed and needs no purchased certificate.
Windows may show an unknown publisher or block the package under local policy.
The companion SHA-256 verifies matching bytes, not the publisher's identity.
Do not bypass organizational security policies. A trusted signed package is an
optional build mode, and is identified by a verified Authenticode signature.

The application stores user settings and sessions outside this release folder.
Uninstalling preserves those settings and additional user-created files; it removes
files shipped by the installer. Save edited bundled examples and catalogs to a
separate user folder, since shipped files can be replaced on reinstall or removed
on uninstall. Close the application normally before updating or uninstalling.

Setup checks for running applications and never force-closes your work. New
releases are placed in versions/<release-id> and verified before activating the
new shortcuts and updating an existing file association owned by this install.
Older payloads are retained. A durable pending journal backs up the previous
state, owned shortcuts/associations, uninstall registry values and uninstaller
files before activation. After interruption, rerun Setup or launch the installed
application to replay the pending transaction. Failed validation or recovery
blocks continuation and preserves the journal. Hardware power-loss and disk
failure guarantees have not been certified. Installation logs describe failures.
Reinstalling the identical package verifies its existing files and repairs a
damaged or missing shipped payload from the package. Downgrades from the new
versioned layout are rejected. Legacy flat-layout version comparisons are not
supported. Use the installed Start Menu shortcut to launch the active release.

Unsaved projects receive a recovery copy every 30 seconds while the desktop is
running. Copies are kept in %APPDATA%\OpticalSystemDesign\Recovery, separately
from the original project. Startup offers valid copies left by a crashed session;
the Help recovery command can reopen this chooser. A recovered project is
unsaved and must be saved separately. The last edits not yet checkpointed may
be lost. Keep independent backups of important work.
