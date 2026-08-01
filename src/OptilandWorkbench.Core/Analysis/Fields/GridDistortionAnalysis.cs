using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class GridDistortionAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly int _referenceFieldNumber;
    private readonly string _displayMode;
    private readonly double _scale;
    private readonly double _heightWidthAspect;
    private readonly bool _symmetricMagnification;
    private readonly double _fieldWidth;

    public GridDistortionAnalysis(
        Optic optic,
        int numPoints = 12,
        int wavelengthNumber = 1,
        int referenceFieldNumber = 1,
        string displayMode = "cross",
        double scale = 1,
        double heightWidthAspect = 1,
        bool symmetricMagnification = false,
        double fieldWidth = 0) : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _wavelengthNumber = Math.Max(1, wavelengthNumber);
        _referenceFieldNumber = Math.Max(1, referenceFieldNumber);
        _displayMode = AnalysisTrace.NormalizeGridDisplayMode(displayMode);
        _scale = Math.Max(0, scale);
        _heightWidthAspect = Math.Max(1e-6, heightWidthAspect);
        _symmetricMagnification = symmetricMagnification;
        _fieldWidth = Math.Max(0, fieldWidth);
    }

    public override string Name => "Grid Distortion";

    public override AnalysisData GenerateData()
    {
        if (Optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
        {
            var converted = RealImageFieldConversion.ForDistortion(Optic);
            return new GridDistortionAnalysis(
                converted,
                _numPoints,
                _wavelengthNumber,
                _referenceFieldNumber,
                _displayMode,
                _scale,
                _heightWidthAspect,
                _symmetricMagnification,
                _fieldWidth).GenerateData();
        }

        var wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No wavelengths"
            });
        }

        const string coordinateModel = "f-tan";
        var wavelength = wavelengths[0];
        var mapping = AnalysisTrace.BuildDistortionReferenceMapping(
            Optic,
            wavelength.Micrometers,
            _referenceFieldNumber,
            coordinateModel,
            _symmetricMagnification);
        var (halfWidth, halfHeight) = GridHalfExtents();
        var horizontal = Enumerable.Range(0, _numPoints)
            .Select(index => -halfWidth + ((2 * halfWidth) * index / (_numPoints - 1.0)))
            .ToArray();
        var vertical = Enumerable.Range(0, _numPoints)
            .Select(index => -halfHeight + ((2 * halfHeight) * index / (_numPoints - 1.0)))
            .ToArray();
        var idealX = new double[_numPoints, _numPoints];
        var idealY = new double[_numPoints, _numPoints];
        var actualX = new double[_numPoints, _numPoints];
        var actualY = new double[_numPoints, _numPoints];
        var maximumDistortion = 0.0;

        for (var row = 0; row < _numPoints; row++)
        {
            for (var column = 0; column < _numPoints; column++)
            {
                var linearX = horizontal[column];
                var linearY = vertical[row];
                idealX[row, column] = linearX;
                idealY[row, column] = linearY;
                var sample = AnalysisTrace.TraceChiefAtLinearField(
                    Optic,
                    linearX,
                    linearY,
                    wavelength.Micrometers,
                    coordinateModel);
                var imageX = sample.X - mapping.ReferenceImageX;
                var imageY = sample.Y - mapping.ReferenceImageY;
                var mappedObject = mapping.MapImageToObject(imageX, imageY);
                actualX[row, column] = linearX + (_scale * (mappedObject.X - linearX));
                actualY[row, column] = linearY + (_scale * (mappedObject.Y - linearY));

                var predicted = mapping.MapFromReference(linearX, linearY);
                var predictedRadius = Math.Sqrt((predicted.X * predicted.X) + (predicted.Y * predicted.Y));
                if (predictedRadius > 1e-30)
                {
                    var actualRadius = Math.Sqrt((imageX * imageX) + (imageY * imageY));
                    var distortion = 100 * (actualRadius - predictedRadius) / predictedRadius;
                    if (Math.Abs(distortion) > Math.Abs(maximumDistortion))
                    {
                        maximumDistortion = distortion;
                    }
                }
            }
        }

        var series = new List<AnalysisSeries>((_numPoints * 2) + 1);
        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(idealX, idealY, index, false, "理想网格", 10, AnalysisLineStyle.Solid, 1.2));
        }

        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(idealX, idealY, index, true, "", 10, AnalysisLineStyle.Solid, 1.2));
        }

        series.Add(new AnalysisSeries(
            "Object field X",
            "Object field Y",
            _displayMode == "vector"
                ? GridVectors(idealX, idealY, actualX, actualY)
                : GridPoints(actualX, actualY),
            _displayMode == "vector" ? AnalysisSeriesKind.Line : AnalysisSeriesKind.Scatter,
            _displayMode == "vector" ? "畸变向量" : "实际像点",
            ColorIndex: 0,
            MarkerStyle: AnalysisMarkerStyle.Cross,
            MarkerSize: 2.8,
            LineWidth: 1,
            XQuantity: AnalysisAxisQuantity.ObjectHeight,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.ObjectHeight,
            YUnit: AnalysisAxisUnit.Millimeter));

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaximumDistortionPercent"] = maximumDistortion,
            ["GridSize"] = _numPoints,
            ["DisplayMode"] = _displayMode,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["ReferenceFieldNumber"] = _referenceFieldNumber,
            ["Scale"] = _scale,
            ["HeightWidthAspect"] = _heightWidthAspect,
            ["SymmetricMagnification"] = _symmetricMagnification,
            ["FieldWidth"] = _fieldWidth,
            ["MappingA"] = mapping.M00,
            ["MappingB"] = mapping.M01,
            ["MappingC"] = mapping.M10,
            ["MappingD"] = mapping.M11
        }, series[0], series, new AnalysisPlotOptions(
            EqualAspect: true,
            ShowLegend: false,
            HideAxes: true));
    }

    private IReadOnlyList<AnalysisPoint> GridPoints(double[,] x, double[,] y)
    {
        var points = new AnalysisPoint[_numPoints * _numPoints];
        var outputIndex = 0;
        for (var row = 0; row < _numPoints; row++)
        {
            for (var column = 0; column < _numPoints; column++)
            {
                points[outputIndex++] = new AnalysisPoint(x[row, column], y[row, column]);
            }
        }

        return points;
    }

    private IReadOnlyList<AnalysisPoint> GridVectors(
        double[,] idealX,
        double[,] idealY,
        double[,] actualX,
        double[,] actualY)
    {
        var points = new AnalysisPoint[_numPoints * _numPoints * 3];
        var outputIndex = 0;
        for (var row = 0; row < _numPoints; row++)
        {
            for (var column = 0; column < _numPoints; column++)
            {
                points[outputIndex++] = new AnalysisPoint(idealX[row, column], idealY[row, column]);
                points[outputIndex++] = new AnalysisPoint(actualX[row, column], actualY[row, column]);
                points[outputIndex++] = new AnalysisPoint(double.NaN, double.NaN);
            }
        }

        return points;
    }

    private (double HalfWidth, double HalfHeight) GridHalfExtents()
    {
        if (_fieldWidth > 0)
        {
            var halfWidth = Optic.FieldDefinition == FieldDefinitionKind.Angle
                ? Math.Tan(0.5 * _fieldWidth * Math.PI / 180.0)
                : 0.5 * _fieldWidth;
            return (halfWidth, halfWidth * _heightWidthAspect);
        }

        var maximumRadius = Optic.Fields
            .Select(field => AnalysisTrace.ToDistortionLinearField(Optic, field.X, field.Y, "f-tan"))
            .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();
        if (maximumRadius <= 1e-12)
        {
            maximumRadius = 1;
        }

        var halfWidthAuto = maximumRadius / Math.Sqrt(1 + (_heightWidthAspect * _heightWidthAspect));
        return (halfWidthAuto, halfWidthAuto * _heightWidthAspect);
    }

    private AnalysisSeries GridLine(
        double[,] x,
        double[,] y,
        int fixedIndex,
        bool row,
        string name,
        int colorIndex,
        AnalysisLineStyle lineStyle,
        double lineWidth)
    {
        var points = new AnalysisPoint[_numPoints];
        for (var index = 0; index < _numPoints; index++)
        {
            var r = row ? fixedIndex : index;
            var c = row ? index : fixedIndex;
            points[index] = new AnalysisPoint(x[r, c], y[r, c]);
        }

        return new AnalysisSeries(
            "Object field X",
            "Object field Y",
            points,
            Name: name,
            LineStyle: lineStyle,
            ColorIndex: colorIndex,
            LineWidth: lineWidth,
            XQuantity: AnalysisAxisQuantity.ObjectHeight,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.ObjectHeight,
            YUnit: AnalysisAxisUnit.Millimeter);
    }
}

internal readonly record struct DistortionReferenceMapping(
    double ReferenceLinearX,
    double ReferenceLinearY,
    double ReferenceImageX,
    double ReferenceImageY,
    double M00,
    double M01,
    double M10,
    double M11)
{
    public (double X, double Y) MapFromReference(double linearX, double linearY)
    {
        var x = linearX - ReferenceLinearX;
        var y = linearY - ReferenceLinearY;
        return ((M00 * x) + (M01 * y), (M10 * x) + (M11 * y));
    }

    public (double X, double Y) MapImageToObject(double imageX, double imageY)
    {
        var determinant = (M00 * M11) - (M01 * M10);
        if (Math.Abs(determinant) <= 1e-20)
        {
            throw new InvalidOperationException("The distortion reference mapping is singular.");
        }

        var x = ((M11 * imageX) - (M01 * imageY)) / determinant;
        var y = ((M00 * imageY) - (M10 * imageX)) / determinant;
        return (ReferenceLinearX + x, ReferenceLinearY + y);
    }
}
