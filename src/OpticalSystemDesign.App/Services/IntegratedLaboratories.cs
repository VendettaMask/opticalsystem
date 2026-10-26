using Avalonia.Controls;
using CoatingDesignWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;
using InitialStructureWindow = OpticalSystemDesign.App.Laboratories.InitialStructure.MainWindow;

namespace OpticalSystemDesign.App.Services;

// Both workspaces are owned windows in the one product process and assembly.
internal sealed class IntegratedLaboratories(Window owner)
{
    private InitialStructureWindow? _initialStructure;
    private CoatingDesignWindow? _coatingDesign;
    private bool _closing;

    internal InitialStructureWindow OpenInitialStructure()
    {
        if (_closing) throw new InvalidOperationException("程序正在退出，请等待完成。");
        if (_initialStructure is null)
        {
            _initialStructure = new InitialStructureWindow();
            _initialStructure.Closed += (_, _) => _initialStructure = null;
            _initialStructure.Show(owner);
        }
        _initialStructure.Activate();
        return _initialStructure;
    }

    internal CoatingDesignWindow OpenCoatingDesign()
    {
        if (_closing) throw new InvalidOperationException("程序正在退出，请等待完成。");
        if (_coatingDesign is null)
        {
            _coatingDesign = new CoatingDesignWindow();
            _coatingDesign.Closed += (_, _) => _coatingDesign = null;
            _coatingDesign.Show(owner);
        }
        _coatingDesign.Activate();
        return _coatingDesign;
    }

    internal async Task<bool> TryCloseAsync()
    {
        if (_closing) return false;
        _closing = true;
        try
        {
            if (_initialStructure is { } initial && !await initial.PrepareForHostCloseAsync()) return false;
            if (_coatingDesign is { } coating && !await coating.PrepareForHostCloseAsync()) return false;
            _coatingDesign?.CloseAfterHostApproval();
            _initialStructure?.Close();
            return true;
        }
        finally { _closing = false; }
    }

    internal void ApplyDisplaySettings(double previousFontSize)
    {
        foreach (var window in owner.OwnedWindows)
            DisplayTypography.ApplyRecursively(window, previousFontSize);
    }
}
