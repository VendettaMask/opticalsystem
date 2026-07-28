using System.Globalization;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Phase;

namespace OptilandWorkbench.Core.Serialization;

internal static class PythonOptilandJsonConversion
{
    internal static void FitVisualSemiDiameters(Optic optic, IReadOnlyList<ParsedSurface> parsedSurfaces)
    {
        var maxima = new double[optic.SurfaceGroup.Items.Count];
        var fields = new[] { 0.0, 0.5, 1.0 };
        var pupils = new[]
        {
            (0.0, 0.0),
            (1.0, 0.0),
            (-1.0, 0.0),
            (0.0, 1.0),
            (0.0, -1.0),
            (Math.Sqrt(0.5), Math.Sqrt(0.5)),
            (-Math.Sqrt(0.5), Math.Sqrt(0.5)),
            (Math.Sqrt(0.5), -Math.Sqrt(0.5)),
            (-Math.Sqrt(0.5), -Math.Sqrt(0.5))
        };

        try
        {
            foreach (var field in fields)
            {
                foreach (var wavelength in optic.Wavelengths)
                {
                    foreach (var pupil in pupils)
                    {
                        var trace = optic.TraceGeneric(0, field, pupil.Item1, pupil.Item2, wavelength.Micrometers);
                        foreach (var sample in trace.RayHistories.Single())
                        {
                            var radius = Math.Sqrt((sample.Position.X * sample.Position.X) + (sample.Position.Y * sample.Position.Y));
                            maxima[sample.SurfaceNumber] = Math.Max(maxima[sample.SurfaceNumber], radius);
                        }
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        double visualDiameter;
        try
        {
            visualDiameter = optic.Paraxial.EstimateEntrancePupilDiameter();
        }
        catch (InvalidOperationException)
        {
            visualDiameter = optic.SurfaceGroup.ApertureRadius() * 2;
        }

        var fallback = Math.Max(0.5, visualDiameter * 0.6);
        for (var index = 0; index < optic.SurfaceGroup.Items.Count; index++)
        {
            if (parsedSurfaces[index].Aperture is null)
            {
                optic.SurfaceGroup.Items[index].SemiDiameter = maxima[index] > 0 ? maxima[index] * 1.15 : fallback;
                optic.SurfaceGroup.Items[index].PhysicalAperture = null;
            }
        }
    }

    internal static string NormalizePythonNumericTokens(string json)
    {
        var output = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;
        for (var index = 0; index < json.Length; index++)
        {
            var character = json[index];
            if (inString)
            {
                output.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                output.Append(character);
                continue;
            }

            if (MatchesToken(json, index, "-Infinity"))
            {
                output.Append("\"-Infinity\"");
                index += "-Infinity".Length - 1;
            }
            else if (MatchesToken(json, index, "Infinity"))
            {
                output.Append("\"Infinity\"");
                index += "Infinity".Length - 1;
            }
            else if (MatchesToken(json, index, "NaN"))
            {
                output.Append("\"NaN\"");
                index += "NaN".Length - 1;
            }
            else
            {
                output.Append(character);
            }
        }

        return output.ToString();
    }

    internal static bool MatchesToken(string source, int index, string token)
    {
        if (index + token.Length > source.Length
            || !source.AsSpan(index, token.Length).Equals(token, StringComparison.Ordinal))
        {
            return false;
        }

        var beforeIsIdentifier = index > 0 && (char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_');
        var end = index + token.Length;
        var afterIsIdentifier = end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == '_');
        return !beforeIsIdentifier && !afterIsIdentifier;
    }

    internal static IReadOnlyList<double> WithLeadingZero(IReadOnlyList<double> coefficients)
    {
        return new[] { 0.0 }.Concat(coefficients).ToArray();
    }

    internal static IReadOnlyList<double> ReadHighOrderAsphereCoefficients(JsonElement geometry, string geometryType)
    {
        var coefficients = ReadDoubleArray(geometry, "coefficients");
        if (coefficients.Length == 0)
        {
            return Array.Empty<double>();
        }

        if (Math.Abs(coefficients[0]) > 1e-14)
        {
            throw new NotSupportedException(
                $"Python Optiland geometry '{geometryType}' with a nonzero first asphere coefficient is not supported yet.");
        }

        return coefficients.Skip(1).ToArray();
    }

    internal static double[] ReadDoubleArray(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<double>();
        }

        return array.EnumerateArray()
            .Select(item => ReadDoubleValue(item, 0.0))
            .ToArray();
    }

    internal static double[,] ReadDoubleMatrix(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return new double[0, 0];
        }

        var parsedRows = rows.EnumerateArray()
            .Select(row => row.ValueKind == JsonValueKind.Array
                ? row.EnumerateArray().Select(item => ReadDoubleValue(item, 0)).ToArray()
                : Array.Empty<double>())
            .ToArray();
        var columns = parsedRows.Length == 0 ? 0 : parsedRows[0].Length;
        if (parsedRows.Any(row => row.Length != columns))
        {
            throw new InvalidDataException("Python Optiland phase_grid rows must have equal lengths.");
        }

        var output = new double[parsedRows.Length, columns];
        for (var row = 0; row < parsedRows.Length; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                output[row, column] = parsedRows[row][column];
            }
        }

        return output;
    }

    internal static double[][] WriteDoubleMatrix(double[,] matrix)
    {
        return Enumerable.Range(0, matrix.GetLength(0))
            .Select(row => Enumerable.Range(0, matrix.GetLength(1))
                .Select(column => matrix[row, column])
                .ToArray())
            .ToArray();
    }

    internal static IReadOnlyDictionary<(int X, int Y), double> ReadPolynomialCoefficients(JsonElement geometry)
    {
        var coefficients = new Dictionary<(int X, int Y), double>();
        if (!geometry.TryGetProperty("coefficients", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return coefficients;
        }

        var xOrder = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Array)
            {
                var yOrder = 0;
                foreach (var item in row.EnumerateArray())
                {
                    var coefficient = ReadDoubleValue(item, 0.0);
                    if (Math.Abs(coefficient) > 0)
                    {
                        coefficients[(xOrder, yOrder)] = coefficient;
                    }

                    yOrder++;
                }
            }

            xOrder++;
        }

        return coefficients;
    }

    internal static double[][] WritePolynomialCoefficients(IReadOnlyDictionary<(int X, int Y), double> coefficients)
    {
        if (coefficients.Count == 0)
        {
            return Array.Empty<double[]>();
        }

        var maxX = coefficients.Keys.Max(key => key.X);
        var maxY = coefficients.Keys.Max(key => key.Y);
        var matrix = Enumerable.Range(0, maxX + 1)
            .Select(_ => new double[maxY + 1])
            .ToArray();
        foreach (var item in coefficients)
        {
            matrix[item.Key.X][item.Key.Y] = item.Value;
        }

        return matrix;
    }

    internal static IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> ReadFringeZernikeCoefficients(JsonElement geometry)
    {
        var coefficients = ReadDoubleArray(geometry, "coefficients");
        var indices = FringeZernikeIndices(coefficients.Length);
        var result = new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>();
        for (var index = 0; index < coefficients.Length; index++)
        {
            if (Math.Abs(coefficients[index]) > 0)
            {
                result[(indices[index].RadialOrder, indices[index].AzimuthalFrequency)] = coefficients[index];
            }
        }

        return result;
    }

    internal static double[] WriteFringeZernikeCoefficients(
        IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> coefficients)
    {
        if (coefficients.Count == 0)
        {
            return Array.Empty<double>();
        }

        var numbered = coefficients
            .Select(item => new
            {
                Number = FringeZernikeNumber(item.Key.RadialOrder, item.Key.AzimuthalFrequency),
                item.Value
            })
            .ToArray();
        if (numbered.Any(item => item.Number is null))
        {
            throw new NotSupportedException("Invalid Zernike coefficient indices cannot be exported to Python Optiland JSON yet.");
        }

        var values = new double[numbered.Max(item => item.Number!.Value)];
        foreach (var item in numbered)
        {
            values[item.Number!.Value - 1] = item.Value;
        }

        return values;
    }

    internal static (int RadialOrder, int AzimuthalFrequency)[] FringeZernikeIndices(int count)
    {
        if (count == 0)
        {
            return Array.Empty<(int RadialOrder, int AzimuthalFrequency)>();
        }

        var numbersPresent = new bool[count + 1];
        numbersPresent[0] = true;
        var indices = new List<(int Number, int RadialOrder, int AzimuthalFrequency)>();
        var radialOrder = 0;
        var azimuthalFrequency = 0;
        while (numbersPresent.Any(present => !present))
        {
            var number = FringeZernikeNumber(radialOrder, azimuthalFrequency);
            if (number is not null)
            {
                indices.Add((number.Value, radialOrder, azimuthalFrequency));
                if (number.Value <= count)
                {
                    numbersPresent[number.Value] = true;
                }
            }

            if (azimuthalFrequency == radialOrder)
            {
                radialOrder++;
                azimuthalFrequency = -radialOrder;
            }
            else
            {
                azimuthalFrequency++;
            }
        }

        return indices
            .OrderBy(item => item.Number)
            .Take(count)
            .Select(item => (item.RadialOrder, item.AzimuthalFrequency))
            .ToArray();
    }

    internal static int? FringeZernikeNumber(int radialOrder, int azimuthalFrequency)
    {
        if (radialOrder < 0
            || Math.Abs(azimuthalFrequency) > radialOrder
            || ((radialOrder - azimuthalFrequency) % 2) != 0)
        {
            return null;
        }

        var absoluteM = Math.Abs(azimuthalFrequency);
        return (int)(
            Math.Pow(1 + ((radialOrder + absoluteM) / 2.0), 2)
            - (2 * absoluteM)
            + ((1 - Math.Sign(azimuthalFrequency)) / 2.0));
    }

    internal static double ReadDoubleValue(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() switch
            {
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                "NaN" => double.NaN,
                var text when double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => fallback
            };
        }

        return fallback;
    }

    internal static double GeometryRadius(IGeometry geometry)
    {
        return geometry switch
        {
            PlaneGeometry => 0,
            PlaneGratingGeometry => 0,
            StandardGratingGeometry grating => grating.Base.Radius,
            StandardGeometry standard => standard.Radius,
            EvenAsphereGeometry even => even.Base.Radius,
            OddAsphereGeometry odd => odd.Base.Radius,
            BiconicGeometry biconic => biconic.RadiusX,
            ToroidalGeometry toroidal => toroidal.SagittalRadius,
            PolynomialGeometry => double.PositiveInfinity,
            ChebyshevGeometry => double.PositiveInfinity,
            ZernikeGeometry => double.PositiveInfinity,
            _ => 0
        };
    }

    internal static double GeometryConic(IGeometry geometry)
    {
        return geometry switch
        {
            StandardGeometry standard => standard.Conic,
            StandardGratingGeometry grating => grating.Base.Conic,
            EvenAsphereGeometry even => even.Base.Conic,
            OddAsphereGeometry odd => odd.Base.Conic,
            BiconicGeometry biconic => biconic.ConicX,
            _ => 0
        };
    }

    internal static double GetDouble(JsonElement source, string propertyName, double fallback)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return ReadDoubleValue(value, fallback);
    }

    internal static string GetString(JsonElement source, string propertyName, string fallback)
    {
        return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    internal static bool GetBoolean(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    internal static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    internal static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

}
