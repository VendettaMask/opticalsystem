using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

internal static class AnalysisTrace
{
    public static IReadOnlyList<AnalysisFieldSample> DefinedFieldSamples(Optic optic)
    {
        var maximumField = FieldCoordinates.MaximumRadius(optic.Fields);
        return optic.Fields.Select((field, index) => new AnalysisFieldSample(
            index,
            field.Label,
            field.X,
            field.Y,
            maximumField <= 1e-12 ? 0 : field.X / maximumField,
            maximumField <= 1e-12 ? 0 : field.Y / maximumField,
            DisplayFieldCoordinate(field.X, field.Y))).ToArray();
    }

    public static double DisplayFieldCoordinate(double x, double y)
    {
        if (Math.Abs(x) <= 1e-12)
        {
            return y;
        }

        if (Math.Abs(y) <= 1e-12)
        {
            return x;
        }

        return Math.Sqrt((x * x) + (y * y));
    }

    public static string FormatFieldTitle(double x, double y, FieldDefinitionKind definition)
    {
        var label = definition is FieldDefinitionKind.ParaxialImageHeight or FieldDefinitionKind.RealImageHeight
            ? "像面"
            : "物面";
        var unit = definition == FieldDefinitionKind.Angle ? "度" : "mm";
        if (Math.Abs(x) <= 1e-12)
        {
            return $"{label}: {y:0.00} ({unit})";
        }

        if (Math.Abs(y) <= 1e-12)
        {
            return $"{label}: {x:0.00} ({unit})";
        }

        return $"{label}: X {x:0.00}, Y {y:0.00} ({unit})";
    }

    public static double MaxFieldValue(Optic optic)
    {
        return FieldCoordinates.MaximumRadius(optic.Fields);
    }

    public static string FieldAxisLabel(Optic optic)
    {
        return optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => "Object Height (mm)",
            FieldDefinitionKind.ParaxialImageHeight => "Paraxial Image Height (mm)",
            FieldDefinitionKind.RealImageHeight => "Real Image Height (mm)",
            _ => "Field Angle (deg)"
        };
    }

    public static string MaximumFieldValueKey(Optic optic)
    {
        return optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => "MaxObjectHeightMillimeters",
            FieldDefinitionKind.ParaxialImageHeight => "MaxParaxialImageHeightMillimeters",
            FieldDefinitionKind.RealImageHeight => "MaxRealImageHeightMillimeters",
            _ => "MaxFieldDegrees"
        };
    }

    public static Wavelength[] SelectWavelengths(Optic optic, int wavelengthNumber)
    {
        var wavelengths = optic.Wavelengths.ToArray();
        if (wavelengthNumber <= 0 || wavelengths.Length == 0)
        {
            return wavelengths;
        }

        return new[] { wavelengths[Math.Clamp(wavelengthNumber - 1, 0, wavelengths.Length - 1)] };
    }

    public static (double X, double Y) ToDistortionLinearField(
        Optic optic,
        double fieldX,
        double fieldY,
        string distortionType)
    {
        if (optic.FieldDefinition != FieldDefinitionKind.Angle)
        {
            return (fieldX, fieldY);
        }

        var xRadians = fieldX * Math.PI / 180.0;
        var yRadians = fieldY * Math.PI / 180.0;
        return distortionType == "f-theta"
            ? (xRadians, yRadians)
            : (Math.Tan(xRadians), Math.Tan(yRadians));
    }

    public static (double X, double Y) TraceChiefAtLinearField(
        Optic optic,
        double linearX,
        double linearY,
        double wavelengthMicrometers,
        string distortionType)
    {
        var physical = FromDistortionLinearField(optic, linearX, linearY, distortionType);
        var normalized = FieldCoordinates.Normalize(optic.Fields, physical.X, physical.Y);
        var sample = FinalSample(
            optic,
            normalized.X,
            normalized.Y,
            0,
            0,
            wavelengthMicrometers);
        return (sample.Position.X, sample.Position.Y);
    }

    public static DistortionReferenceMapping BuildDistortionReferenceMapping(
        Optic optic,
        double wavelengthMicrometers,
        int referenceFieldNumber,
        string distortionType,
        bool symmetricMagnification = false)
    {
        var fields = optic.Fields.ToArray();
        var referenceField = fields.Length == 0
            ? new FieldPoint()
            : fields[Math.Clamp(referenceFieldNumber - 1, 0, fields.Length - 1)];
        var referenceLinear = ToDistortionLinearField(
            optic,
            referenceField.X,
            referenceField.Y,
            distortionType);
        var referenceImage = TraceChiefAtLinearField(
            optic,
            referenceLinear.X,
            referenceLinear.Y,
            wavelengthMicrometers,
            distortionType);
        var maximumLinearRadius = fields
            .Select(field => ToDistortionLinearField(optic, field.X, field.Y, distortionType))
            .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();
        var delta = Math.Max(1e-8, Math.Max(1, maximumLinearRadius) * 1e-6);
        var xColumn = DistortionDerivative(
            optic,
            referenceLinear,
            referenceImage,
            wavelengthMicrometers,
            distortionType,
            delta,
            xAxis: true);
        var yColumn = DistortionDerivative(
            optic,
            referenceLinear,
            referenceImage,
            wavelengthMicrometers,
            distortionType,
            delta,
            xAxis: false);
        var m00 = xColumn.X;
        var m01 = yColumn.X;
        var m10 = xColumn.Y;
        var m11 = yColumn.Y;
        if (symmetricMagnification)
        {
            var real = 0.5 * (m00 + m11);
            var imaginary = 0.5 * (m10 - m01);
            m00 = real;
            m01 = -imaginary;
            m10 = imaginary;
            m11 = real;
        }

        var determinant = (m00 * m11) - (m01 * m10);
        if (Math.Abs(determinant) <= 1e-20)
        {
            throw new InvalidOperationException("Unable to establish a non-singular distortion reference mapping.");
        }

        return new DistortionReferenceMapping(
            referenceLinear.X,
            referenceLinear.Y,
            referenceImage.X,
            referenceImage.Y,
            m00,
            m01,
            m10,
            m11);
    }

    public static (double X, double Y) ScanField(string scanDirection, double magnitude)
    {
        return scanDirection switch
        {
            "+x" => (magnitude, 0),
            "-x" => (-magnitude, 0),
            "-y" => (0, -magnitude),
            _ => (0, magnitude)
        };
    }

    public static double ScanFieldValue(string scanDirection, double magnitude)
    {
        return scanDirection[0] == '-' ? -magnitude : magnitude;
    }

    public static string NormalizeScanDirection(string scanDirection)
    {
        var normalized = scanDirection.Trim().ToLowerInvariant();
        return normalized is "+x" or "-x" or "+y" or "-y"
            ? normalized
            : throw new ArgumentException("Scan direction must be +x, -x, +y, or -y.", nameof(scanDirection));
    }

    public static string NormalizeDistortionDisplayMode(string displayMode)
    {
        if (string.Equals(displayMode, "percent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayMode, "百分比", StringComparison.Ordinal))
        {
            return "percent";
        }

        if (string.Equals(displayMode, "absolute", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayMode, "绝对值", StringComparison.Ordinal))
        {
            return "absolute";
        }

        throw new ArgumentException("Distortion display mode must be 'percent' or 'absolute'.", nameof(displayMode));
    }

    public static string NormalizeGridDisplayMode(string displayMode)
    {
        if (string.Equals(displayMode, "cross", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayMode, "截面", StringComparison.Ordinal))
        {
            return "cross";
        }

        if (string.Equals(displayMode, "vector", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayMode, "向量", StringComparison.Ordinal))
        {
            return "vector";
        }

        throw new ArgumentException("Grid display mode must be 'cross' or 'vector'.", nameof(displayMode));
    }

    private static (double X, double Y) FromDistortionLinearField(
        Optic optic,
        double linearX,
        double linearY,
        string distortionType)
    {
        if (optic.FieldDefinition != FieldDefinitionKind.Angle)
        {
            return (linearX, linearY);
        }

        var xRadians = distortionType == "f-theta" ? linearX : Math.Atan(linearX);
        var yRadians = distortionType == "f-theta" ? linearY : Math.Atan(linearY);
        return (xRadians * 180.0 / Math.PI, yRadians * 180.0 / Math.PI);
    }

    private static (double X, double Y) DistortionDerivative(
        Optic optic,
        (double X, double Y) referenceLinear,
        (double X, double Y) referenceImage,
        double wavelengthMicrometers,
        string distortionType,
        double delta,
        bool xAxis)
    {
        var plus = xAxis
            ? (referenceLinear.X + delta, referenceLinear.Y)
            : (referenceLinear.X, referenceLinear.Y + delta);
        var minus = xAxis
            ? (referenceLinear.X - delta, referenceLinear.Y)
            : (referenceLinear.X, referenceLinear.Y - delta);
        var canTracePlus = CanTraceLinearField(optic, plus, distortionType);
        var canTraceMinus = CanTraceLinearField(optic, minus, distortionType);
        if (canTracePlus && canTraceMinus)
        {
            var plusImage = TraceChiefAtLinearField(
                optic, plus.Item1, plus.Item2, wavelengthMicrometers, distortionType);
            var minusImage = TraceChiefAtLinearField(
                optic, minus.Item1, minus.Item2, wavelengthMicrometers, distortionType);
            return ((plusImage.X - minusImage.X) / (2 * delta), (plusImage.Y - minusImage.Y) / (2 * delta));
        }

        if (canTracePlus)
        {
            var plusImage = TraceChiefAtLinearField(
                optic, plus.Item1, plus.Item2, wavelengthMicrometers, distortionType);
            return ((plusImage.X - referenceImage.X) / delta, (plusImage.Y - referenceImage.Y) / delta);
        }

        if (canTraceMinus)
        {
            var minusImage = TraceChiefAtLinearField(
                optic, minus.Item1, minus.Item2, wavelengthMicrometers, distortionType);
            return ((referenceImage.X - minusImage.X) / delta, (referenceImage.Y - minusImage.Y) / delta);
        }

        throw new InvalidOperationException("The selected reference field cannot be perturbed for distortion calibration.");
    }

    private static bool CanTraceLinearField(
        Optic optic,
        (double X, double Y) linearField,
        string distortionType)
    {
        var physical = FromDistortionLinearField(optic, linearField.X, linearField.Y, distortionType);
        var normalized = FieldCoordinates.Normalize(optic.Fields, physical.X, physical.Y);
        return Math.Abs(normalized.X) <= 1 + 1e-10 && Math.Abs(normalized.Y) <= 1 + 1e-10;
    }

    public static string NormalizeDistortionType(string distortionType)
    {
        if (string.Equals(distortionType, "f-tan", StringComparison.OrdinalIgnoreCase)
            || string.Equals(distortionType, "F-Tan(Theta)", StringComparison.OrdinalIgnoreCase))
        {
            return "f-tan";
        }

        if (string.Equals(distortionType, "f-theta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(distortionType, "F-Theta", StringComparison.OrdinalIgnoreCase))
        {
            return "f-theta";
        }

        throw new ArgumentException("Distortion type must be 'f-tan' or 'f-theta'.", nameof(distortionType));
    }

    public static Rays.RayTraceSample FinalSample(
        Optic optic,
        double hx,
        double hy,
        double px,
        double py,
        double wavelengthMicrometers)
    {
        var history = optic.TraceGeneric(hx, hy, px, py, wavelengthMicrometers).RayHistories.Single();
        if (history.Count == 0)
        {
            throw new InvalidOperationException("Ray tracing did not produce an image-plane sample.");
        }

        var sample = history[^1];
        var imageSurface = optic.SurfaceGroup.Items.LastOrDefault();
        return imageSurface is null
            ? sample
            : sample with
            {
                Position = imageSurface.CoordinateSystem.ToLocalPoint(sample.Position),
                Direction = imageSurface.CoordinateSystem.ToLocalDirection(sample.Direction)
            };
    }
}
