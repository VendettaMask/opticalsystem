using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

/// <summary>The fixed S.T.A.R.Labs wordmark, drawn from the shared SVG outlines.</summary>
public sealed class CompanyLogoControl : Image
{
    public CompanyLogoControl() : this(null) { }

    public CompanyLogoControl(Color? color)
    {
        Stretch = Stretch.Uniform;
        Source = BrandAssets.LoadCompanyLogoImage(color);
        AutomationProperties.SetName(this, "S.T.A.R.Labs");
        if (color is null) this.BindThemeResource(SourceProperty, ThemeAssetBindings.CompanyLogo);
    }
}
