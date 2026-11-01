namespace OpticalSystemDesign.Core.Propagation;

/// <summary>Beer–Lambert power transmission for a homogeneous segment in millimetres.</summary>
public static class HomogeneousPowerAttenuation
{
    public static double Evaluate(double extinctionCoefficient,double wavelengthNanometers,double distanceMillimeters)
    {
        if(!double.IsFinite(extinctionCoefficient)||extinctionCoefficient<0||!double.IsFinite(wavelengthNanometers)
            ||wavelengthNanometers<=0||!double.IsFinite(distanceMillimeters))
            throw new ArgumentOutOfRangeException(nameof(extinctionCoefficient),"Attenuation requires finite passive k, positive wavelength and finite distance.");
        var wavelengthMicrometers=wavelengthNanometers/1000.0;
        return extinctionCoefficient==0?1:Math.Exp((-4.0*Math.PI*extinctionCoefficient*Math.Abs(distanceMillimeters)*1000.0)/wavelengthMicrometers);
    }
}
