using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    private static (Complex[] Tangential, Complex[] Sagittal) ComputePsfOtfAxes(PsfResult psf)
    {
        var size = psf.GridSize;
        AnalysisResourceLimits.ValidateMtfTransformWork(size);
        var center = size / 2;
        var count = size - center;
        // The two axial OTF sections are the 1D transforms of the orthogonal
        // PSF projections. Integrate every original pixel, retain its physical
        // origin and complex phase, and avoid constructing two unused N² OTFs.
        var tangential = new Complex[size];
        var sagittal = new Complex[size];
        for (var row = 0; row < size; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var shiftedRow = (row - center + size) % size;
            var rowPower = 0d;
            for (var column = 0; column < size; column++)
            {
                var power = psf.Values[row, column];
                rowPower += power;
                var shiftedColumn = (column - center + size) % size;
                sagittal[shiftedColumn] += power;
            }
            tangential[shiftedRow] = rowPower;
        }
        Fft(tangential);
        Fft(sagittal);
        var dc = tangential[0].Magnitude <= 1e-30 ? Complex.One : tangential[0];
        var tangentialOtf = new Complex[count];
        var sagittalOtf = new Complex[count];
        for (var index = 0; index < count; index++)
        {
            tangentialOtf[index] = tangential[index] / dc;
            sagittalOtf[index] = sagittal[index] / dc;
        }
        return (tangentialOtf, sagittalOtf);
    }
}
