using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OpeningEditingAndSavingKeepsTheSelectedDesignProtocol(int? version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-protocol-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var input = Examples.Create(version is 2 or 3 or 4 ? DesignKind.NarrowBand : DesignKind.Antireflection);
            input = input with
            {
                Settings = input.Settings with { DesignProtocolVersion = version },
                Target = input.Target with { Structure = version == 2 ? new(2) { PrototypeFamilyVersion = 2 } : null }
            };
            var source = Path.Combine(directory, "source.coating.json"); var saved = Path.Combine(directory, "saved.coating.json");
            await ExperimentStore.SaveAsync(input, source);
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(saved),
                    () => Task.FromResult<string?>(source), () => Task.FromResult<string?>(null)))
                { ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window);
                    Click(window, "打开实验"); await window.PendingOperation;
                    Assert.Equal(version, window.CurrentExperiment.Settings.DesignProtocolVersion);
                    Assert.Equal(version == 2 ? 2 : (int?)null, window.CurrentExperiment.Target.Structure?.PrototypeFamilyVersion);
                    Find<TextBox>(window, "实验名称").Text = "协议编辑检查";
                    Click(window, "计算 / 复验"); await window.PendingOperation;
                    Assert.Equal(version, window.CurrentExperiment.Settings.DesignProtocolVersion);
                    Assert.Equal(version switch { 2 => DesignService.TargetBalancedProtocol, 3 => DesignService.NarrowBandCompetitionProtocol, 4 => DesignService.GeneralCoatingProtocol, _ => null }, window.CurrentExperiment.Result!.Run.DesignProtocol);
                    Click(window, "保存实验"); await window.PendingOperation;
                    Assert.Equal(version, (await ExperimentStore.OpenAsync(source)).Settings.DesignProtocolVersion);
                    Assert.Equal(version == 2 ? 2 : (int?)null, (await ExperimentStore.OpenAsync(source)).Target.Structure?.PrototypeFamilyVersion);
                }
                finally { window.Close(); }
            });
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
