using System.Globalization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.App.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class ReportZernikeFormulaTests
{
    [Fact]
    public void PdfHighPowersUsePositionedDigitsSupportedByTheEmbeddedFont()
    {
        using var source = typeof(DesignReportPresentation).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Fonts.NotoSansCJKsc-Regular.otf")!;
        using var typeface = SKTypeface.FromStream(source); using var font = new SKFont(typeface, 9);
        var runs = DesignReportPresentation.MathRuns("(10ρ^5 − 12ρ^3 + 3ρ) · cos(θ) + ρ^20").ToArray();
        Assert.Equal(new[] { "5", "3", "20" }, runs.Where(run => run.Superscript).Select(run => run.Text));
        Assert.All(runs, run => Assert.All(font.GetGlyphs(run.Text), glyph => Assert.NotEqual((ushort)0, glyph)));
        Assert.DoesNotContain(runs, run => run.Text.Contains('^'));
    }

    [Theory]
    [InlineData("Zernike Fringe")]
    [InlineData("Zernike Standard")]
    [InlineData("Zernike Annular")]
    public async Task EveryFittedTermPublishesItsOrdersFormulaAndUnroundedCoefficient(string key)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>
        { ["ZernikeTerms"] = "37", ["ObscurationRatio"] = "0.65" }));
        var table = Assert.IsType<OpticalSystemDesign.Application.Contracts.AnalysisTableDto>(result.View.Table);
        Assert.Equal(new[] { "序号", "n", "m", "公式", "系数 / 波" }, table.Columns);
        Assert.Equal(37, table.Rows.Count);
        Assert.Equal("Z37", table.Rows[^1][0]);
        foreach (var row in table.Rows)
        {
            Assert.NotEmpty(row[3]);
            Assert.True(double.TryParse(row[4], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        }
        Assert.Equal("1", table.Rows[0][3]);
        if (key == "Zernike Fringe") Assert.Equal("(2ρ^2 − 1)", table.Rows[3][3]);
        if (key == "Zernike Standard") Assert.Equal("√3 · (2ρ^2 − 1)", table.Rows[3][3]);
        if (key == "Zernike Annular") Assert.Contains("u", table.Rows[3][3]);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, .5)]
    [InlineData(false, true, .9)]
    public void PublishedPolynomialMatchesTheActualBasisIncludingAnnularNormalization(bool standard, bool annular, double epsilon)
    {
        for (var n = 0; n <= 8; n++)
        for (var m = -n; m <= n; m += 2)
        {
            var expression = ZernikeFitEngine.PolynomialExpression(n, m, standard, annular, epsilon);
            var c = new[] { new ZernikeCoefficient(1, n, m, 1) };
            foreach (var fraction in new[] { .15, .45, .85 })
            {
                var radius = epsilon + (1 - epsilon) * fraction; const double theta = .43;
                var expected = annular ? ZernikeFitEngine.EvaluateAnnular(c, radius * Math.Cos(theta), radius * Math.Sin(theta), epsilon)
                    : standard ? ZernikeFitEngine.EvaluateStandard(c, radius * Math.Cos(theta), radius * Math.Sin(theta))
                    : ZernikeFitEngine.Evaluate(c, radius * Math.Cos(theta), radius * Math.Sin(theta));
                var actual = EvaluateExpression(expression, radius, theta, epsilon);
                Assert.True(Math.Abs(actual - expected) < 2e-8, $"{expression}: {actual:G16} != {expected:G16}");
            }
        }
    }

    // Independent parsing checks that the published expression, rather than an internal descriptor, is numerically usable.
    private static double EvaluateExpression(string expression, double radius, double theta, double epsilon)
    {
        expression = Regex.Replace(expression, @"√(\d+)", match => Math.Sqrt(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToString("G17", CultureInfo.InvariantCulture));
        expression = Regex.Replace(expression, @"(cos|sin)\((\d*)θ\)", match =>
        {
            var order = match.Groups[2].Length == 0 ? 1 : int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return (match.Groups[1].Value == "cos" ? Math.Cos(order * theta) : Math.Sin(order * theta)).ToString("G17", CultureInfo.InvariantCulture);
        });
        var u = (2 * radius * radius - 1 - epsilon * epsilon) / (1 - epsilon * epsilon);
        double Atom(string value)
        {
            var sum = 0d;
            foreach (Match term in Regex.Matches(value.Replace(" ", "", StringComparison.Ordinal), @"(?<sign>[+−-]?)(?<coefficient>\d+(?:\.\d+)?(?:E[+-]?\d+)?)?(?<variable>[ρu])?(?:\^(?<power>\d+))?"))
            {
                if (term.Length == 0) continue;
                var factor = term.Groups["coefficient"].Success ? double.Parse(term.Groups["coefficient"].Value, CultureInfo.InvariantCulture) : 1;
                if (term.Groups["sign"].Value is "−" or "-") factor = -factor;
                if (term.Groups["variable"].Success) factor *= Math.Pow(term.Groups["variable"].Value == "ρ" ? radius : u,
                    term.Groups["power"].Success ? int.Parse(term.Groups["power"].Value, CultureInfo.InvariantCulture) : 1);
                sum += factor;
            }
            return sum;
        }
        return expression.Split('·').Select(part => Atom(part.Trim(' ', '(', ')'))).Aggregate(1d, (left, right) => left * right);
    }
}
