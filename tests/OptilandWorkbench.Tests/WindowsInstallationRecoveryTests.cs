using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class WindowsInstallationRecoveryTests
{
    [Theory]
    [InlineData("Captured")]
    [InlineData("Activating")]
    public void InterruptedActivationRestoresExactFilesAndWholeUninstallValues(string phase)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installation-recovery-" + Guid.NewGuid().ToString("N"));
        var id = "OpticalSystemDesign-UpgradeValidation-" + Guid.NewGuid().ToString("N");
        var keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id + "_is1";
        Directory.CreateDirectory(root);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
            { key.SetValue("InstallLocation", root); key.SetValue("DisplayVersion", "new"); key.SetValue("NewField", "new"); }
            var journal = CreateJournal(root, id, phase);
            var result = WindowsInstallationRecovery.RecoverAtRoot(root, id, [root]);
            Assert.True(result.Recovered);
            Assert.Equal(OldState(id), File.ReadAllText(Path.Combine(root, "release-state.ini")));
            Assert.Equal("old-shortcut", File.ReadAllText(Path.Combine(root, "shortcut.lnk")));
            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal("1.0.0", restored!.GetValue("DisplayVersion"));
            Assert.Equal(123, restored.GetValue("EstimatedSize"));
            Assert.Null(restored.GetValue("NewField"));
            Assert.False(File.Exists(journal));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("identity")]
    [InlineData("shortcut")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void InvalidJournalFailsBeforeAnyRestorationAndKeepsEvidence(string failure)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "installation-recovery-" + Guid.NewGuid().ToString("N"));
        var id = "OpticalSystemDesign-UpgradeValidation-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            var journal = CreateJournal(root, id, "Activating");
            if (failure == "backup") File.AppendAllText(Path.Combine(root, ".install-recovery", "previous-state.ini"), "corrupt");
            if (failure == "identity") File.WriteAllText(journal, File.ReadAllText(journal).Replace(Encode(id), Encode("other-product")), Encoding.Unicode);
            if (failure == "shortcut") File.WriteAllText(journal, File.ReadAllText(journal).Replace(Encode(Path.Combine(root, "shortcut.lnk")),
                Encode(Path.Combine(Path.GetTempPath(), "foreign.lnk"))), Encoding.Unicode);
            Assert.Throws<InvalidDataException>(() => WindowsInstallationRecovery.RecoverAtRoot(root, id, [root]));
            Assert.Equal("new-state", File.ReadAllText(Path.Combine(root, "release-state.ini")));
            Assert.Equal("new-shortcut", File.ReadAllText(Path.Combine(root, "shortcut.lnk")));
            Assert.True(File.Exists(journal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateJournal(string root, string id, string phase)
    {
        var directory = Path.Combine(root, ".install-recovery"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "previous-state.ini"), OldState(id));
        File.WriteAllText(Path.Combine(directory, "shortcut-0.lnk"), "old-shortcut");
        File.WriteAllText(Path.Combine(root, "release-state.ini"), "new-state");
        File.WriteAllText(Path.Combine(root, "shortcut.lnk"), "new-shortcut");
        string Hash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name))));
        var text = $"[Transaction]\nFormatVersion=2\nProductAppId={id}\nRoot={root}\nPhase={phase}\nStateExists=1\nStateSha256={Hash("previous-state.ini")}\n"
            + $"[Shortcut0]\nPath={Path.Combine(root, "shortcut.lnk")}\nExists=1\nSha256={Hash("shortcut-0.lnk")}\n"
            + "[Shortcut1]\nPath=\nExists=0\n[Shortcut2]\nPath=\nExists=0\n[Uninstaller]\nBaseName=\n"
            + "[Registry]\nExists=1\nCount=2\n[Registry0]\nName=DisplayVersion\nKind=String\nData=1.0.0\n"
            + "[Registry1]\nName=EstimatedSize\nKind=DWord\nData=123\n[Association]\nOwned=0\nCommand=\nIconExists=0\nIcon=\n";
        text = string.Join('\n', text.Split('\n').Select(line => line.Contains('=')
            ? line[..(line.IndexOf('=') + 1)] + Encode(line[(line.IndexOf('=') + 1)..]) : line));
        var journal = Path.Combine(directory, "pending.ini"); File.WriteAllText(journal, text, Encoding.Unicode);
        return journal;
    }

    private static string OldState(string id) => $"[Release]\nProductAppId={id}\nActive=old-release\nVersion=1.0.0\n";
    private static string Encode(string value) => "u:" + string.Concat(value.Select(c => ((int)c).ToString("X4")));
}
