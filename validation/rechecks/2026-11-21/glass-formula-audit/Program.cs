using System.Text.Json;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Materials;

var registry = new MaterialRegistry();
var material = (CatalogGlassMaterial)registry.Resolve("APEL:APL5014CL");
var data = material.ZemaxData ?? throw new Exception("Missing source data.");
if (data.DispersionFormulaNumber != 1) throw new Exception("Expected Schott.");
var c = material.Coefficients;
var wavelengths = new[] { 486.1327, 500, 587.5618, 656.2725, 1000 };
var samples = wavelengths.Select(wavelength =>
{
    var lambda = wavelength / 1000;
    var powers = new[] { 0, 2, -2, -4, -6, -8 };
    var reference = Math.Sqrt(powers.Select((power, i) => c[i] * Math.Pow(lambda, power)).Sum());
    var actual = material.RefractiveIndex(wavelength);
    if (Math.Abs(actual - reference) > 5e-15) throw new Exception("Schott expression mismatch.");
    return new { wavelengthNanometers = wavelength, index = actual, independentExpression = reference };
}).ToArray();
var changed = c.ToArray();
changed[0] += 0.01;
var altered = new CatalogGlassMaterial("perturbation", "audit", material.Formula,
    material.MinimumWavelengthNanometers, material.MaximumWavelengthNanometers, changed);
if (altered.RefractiveIndex(587.5618) == material.RefractiveIndex(587.5618))
    throw new Exception("Coefficient perturbation did not affect the index.");
var nd = material.RefractiveIndex(587.5618);
var nf = material.RefractiveIndex(486.1327);
var nc = material.RefractiveIndex(656.2725);
using var application = WorkbenchApplication.Create("cooke");
var display = application.Materials.FindGlass("APEL:APL5014CL") ?? throw new Exception("Missing application material.");
var result = new
{
    scope = "Current shared C# Core against the published Schott equation; no new native optical-software capture",
    material = "APEL:APL5014CL", material.Formula, coefficients = c.Take(6),
    sourceReferenceNd = data.ReferenceIndexD, sourceReferenceVd = data.ReferenceAbbeNumber,
    applicationDisplayNd = display.RefractiveIndexD, applicationDisplayVd = display.AbbeNumber,
    calculatedNd = nd, calculatedVd = (nd - 1) / (nf - nc), samples,
    coefficientPerturbationChangesResult = true,
    formulaDefinitionsInspected = Enumerable.Range(1, 13),
    sources = new[] {
        "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Schott_Formula.html",
        "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Description_of_Catalog_Data.html" }
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "audit.json"), json);
Console.WriteLine(json);
