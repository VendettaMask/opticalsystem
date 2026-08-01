using System.Collections.Specialized;
using System.ComponentModel;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Raytrace;

internal sealed class RayTraceCacheBinding : IDisposable
{
    private readonly Optic _optic;
    private readonly Action _invalidate;
    private readonly OpticalSurface[] _surfaces;
    private readonly FieldPoint[] _fields;
    private readonly Wavelength[] _wavelengths;
    private bool _disposed;

    public RayTraceCacheBinding(Optic optic, Action invalidate)
    {
        _optic = optic;
        _invalidate = invalidate;
        _surfaces = optic.SurfaceGroup.Items.ToArray();
        _fields = optic.Fields.ToArray();
        _wavelengths = optic.Wavelengths.ToArray();

        optic.SurfaceGroup.Items.CollectionChanged += OnCollectionChanged;
        optic.Fields.CollectionChanged += OnCollectionChanged;
        optic.Wavelengths.CollectionChanged += OnCollectionChanged;
        optic.Aperture.PropertyChanged += OnPropertyChanged;
        optic.Environment.PropertyChanged += OnPropertyChanged;
        Subscribe(_surfaces);
        Subscribe(_fields);
        Subscribe(_wavelengths);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _optic.SurfaceGroup.Items.CollectionChanged -= OnCollectionChanged;
        _optic.Fields.CollectionChanged -= OnCollectionChanged;
        _optic.Wavelengths.CollectionChanged -= OnCollectionChanged;
        _optic.Aperture.PropertyChanged -= OnPropertyChanged;
        _optic.Environment.PropertyChanged -= OnPropertyChanged;
        Unsubscribe(_surfaces);
        Unsubscribe(_fields);
        Unsubscribe(_wavelengths);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        _invalidate();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        _invalidate();

    private void Subscribe(IEnumerable<INotifyPropertyChanged> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged += OnPropertyChanged;
        }
    }

    private void Unsubscribe(IEnumerable<INotifyPropertyChanged> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= OnPropertyChanged;
        }
    }
}
