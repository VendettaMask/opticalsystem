using System.Collections.ObjectModel;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.App.Connectors;

public sealed class OptilandConnector
{
    private readonly UndoRedoManager _undoRedo = new();

    public OptilandConnector(Optic optic)
    {
        CurrentOptic = optic;
        Status = "Ready";
    }

    public event EventHandler? OpticLoaded;

    public event EventHandler? OpticChanged;

    public event EventHandler? SurfaceDataChanged;

    public Optic CurrentOptic { get; private set; }

    public ObservableCollection<OpticalSurface> Surfaces => CurrentOptic.SurfaceGroup.Items;

    public ObservableCollection<FieldPoint> Fields => CurrentOptic.Fields;

    public ObservableCollection<Wavelength> Wavelengths => CurrentOptic.Wavelengths;

    public string Status { get; private set; }

    public bool CanUndo => _undoRedo.CanUndo;

    public bool CanRedo => _undoRedo.CanRedo;

    public IReadOnlyList<string> AnalysisNames => CurrentOptic.Analyses.Names;

    public string BuildAnalysisReport()
    {
        return BuildAnalysisReport("Prescription Report");
    }

    public string BuildAnalysisReport(string analysisName)
    {
        var analysis = CurrentOptic.Analyses.Create(analysisName);
        var data = analysis.GenerateData();
        return data.ExportText();
    }

    public void NewDemo()
    {
        CurrentOptic = Optic.CreateDemo();
        _undoRedo.Clear();
        SetStatus("Created demo optic.");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void CaptureCurrentState()
    {
        _undoRedo.Capture(CurrentOptic);
    }

    public void CommitSurfaceEdit()
    {
        CurrentOptic.Pickups.ApplyAll();
        CurrentOptic.Solves.ApplyAll();
        SetStatus("Surface data updated.");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitSystemEdit()
    {
        SetPrimaryWavelengthGuard();
        SetStatus("System properties updated.");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddSurface()
    {
        CaptureCurrentState();
        CurrentOptic.SurfaceGroup.AddDefaultSurface();
        SetStatus("Surface added.");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSurface(OpticalSurface? surface)
    {
        if (surface is null)
        {
            return;
        }

        CaptureCurrentState();
        CurrentOptic.SurfaceGroup.Remove(surface);
        SetStatus("Surface removed.");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddField()
    {
        CaptureCurrentState();
        Fields.Add(new FieldPoint
        {
            Label = $"Field {Fields.Count}",
            YAngleDegrees = Fields.Count * 4,
            Weight = 1
        });
        SetStatus("Field added.");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddWavelength()
    {
        CaptureCurrentState();
        Wavelengths.Add(new Wavelength
        {
            Label = $"W{Wavelengths.Count + 1}",
            Nanometers = 550,
            Weight = 1,
            IsPrimary = Wavelengths.Count == 0
        });
        SetPrimaryWavelengthGuard();
        SetStatus("Wavelength added.");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public OptimizationResult OptimizeRadius(OpticalSurface surface)
    {
        CaptureCurrentState();
        var result = new SimpleOptimizer(CurrentOptic).OptimizeRadius(surface);
        SetStatus(result.Message);
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public bool Undo()
    {
        var changed = _undoRedo.TryUndo(CurrentOptic);
        if (changed)
        {
            SetStatus("Undo complete.");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public bool Redo()
    {
        var changed = _undoRedo.TryRedo(CurrentOptic);
        if (changed)
        {
            SetStatus("Redo complete.");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public async Task SaveAsync(string path)
    {
        await OpticJsonStore.SaveAsync(CurrentOptic, path);
        SetStatus($"Saved {Path.GetFileName(path)}.");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadAsync(string path)
    {
        CurrentOptic = await OpticJsonStore.LoadAsync(path);
        _undoRedo.Clear();
        SetStatus($"Loaded {Path.GetFileName(path)}.");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string status)
    {
        Status = status;
    }

    private void SetPrimaryWavelengthGuard()
    {
        if (Wavelengths.Count == 0)
        {
            return;
        }

        if (!Wavelengths.Any(item => item.IsPrimary))
        {
            Wavelengths[0].IsPrimary = true;
        }
    }
}
