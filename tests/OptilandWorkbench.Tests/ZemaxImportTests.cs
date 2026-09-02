using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxImportTests
{
    [Fact]
    public void MsL7MeritFunctionMatchesCommittedZosApiGoldenStructureAndSlots()
    {
        var sourcePath = FixturePath("zemax-ms-l7-high-na.ZMX");
        var goldenPath = FixturePath("zemax-ms-l7-merit-function.json");
        using var golden = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var root = golden.RootElement;
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Ansys Zemax OpticStudio 2026 R1 ZOS-API", root.GetProperty("source").GetString());
        Assert.Equal(expectedHash, root.GetProperty("sourceSha256").GetString());

        var optic = OpticalFormatCatalog.Import(File.ReadAllText(sourcePath), ".zmx");
        var rows = root.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(root.GetProperty("rowCount").GetInt32(), rows.Length);
        Assert.Equal(rows.Length, optic.MeritFunctionOperands.Count);
        var validatedSlots = 0;

        for (var index = 0; index < rows.Length; index++)
        {
            var expected = rows[index];
            var actual = optic.MeritFunctionOperands[index];
            Assert.Equal(index + 1, expected.GetProperty("row").GetInt32());
            Assert.Equal(expected.GetProperty("type").GetString(), actual.Type);
            if (!ZemaxOperandRegistry.TryGet(actual.Type, out var descriptor))
            {
                continue;
            }

            foreach (var parameter in descriptor.Parameters.Where(parameter => parameter.DisplayName != "Unused"))
            {
                if (parameter.Slot is "Int1" or "Int2")
                {
                    if (actual.ZemaxIntegerParameters.Length < 2)
                    {
                        continue;
                    }

                    var slotIndex = parameter.Slot == "Int1" ? 0 : 1;
                    Assert.True(
                        expected.GetProperty(parameter.Slot.ToLowerInvariant()).GetInt32()
                            == actual.ZemaxIntegerParameters[slotIndex],
                        $"Row {index + 1} {actual.Type} {parameter.Slot} mismatch.");
                    validatedSlots++;
                    continue;
                }

                if (actual.ZemaxDataParameters.Length < 4)
                {
                    continue;
                }

                var dataIndex = int.Parse(parameter.Slot.AsSpan(4), CultureInfo.InvariantCulture) - 1;
                var expectedData = expected.GetProperty(parameter.Slot.ToLowerInvariant());
                Assert.True(
                    expectedData.ValueKind != JsonValueKind.Number
                    || Math.Abs(expectedData.GetDouble() - actual.ZemaxDataParameters[dataIndex]) <= 1e-12,
                    $"Row {index + 1} {actual.Type} {parameter.Slot} mismatch.");
                validatedSlots++;
            }
        }

        Assert.True(validatedSlots >= 400, $"Expected at least 400 active slot comparisons, got {validatedSlots}.");
    }

    [Fact]
    public void MsL7ExecutableMeritRowsMatchCommittedZosApiGoldenValues()
    {
        var sourcePath = FixturePath("zemax-ms-l7-high-na.ZMX");
        using var golden = JsonDocument.Parse(File.ReadAllText(FixturePath("zemax-ms-l7-merit-function.json")));
        var expectedRows = golden.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(sourcePath), ".zmx");
        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, optic.MeritFunctionOperands);
        var validatedRows = new HashSet<int> { 9, 11, 13, 15, 19, 23, 25, 27, 28, 30 };
        var validatedValues = 0;
        var differences = new List<string>();

        for (var index = 0; index < expectedRows.Length; index++)
        {
            var operand = optic.MeritFunctionOperands[index];
            if (!validatedRows.Contains(index + 1)
                || !operand.Enabled
                || !ZemaxOperandRegistry.TryGet(operand.Type, out var descriptor)
                || descriptor.SupportLevel != ZemaxOperandSupportLevel.Executable)
            {
                continue;
            }

            var expectedValue = expectedRows[index].GetProperty("value");
            if (expectedValue.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var evaluation = evaluations[index];
            if (!string.IsNullOrEmpty(evaluation.Error))
            {
                differences.Add($"Row {index + 1} {operand.Type}: {evaluation.Error}");
                continue;
            }

            var expected = expectedValue.GetDouble();
            var tolerance = Math.Max(1e-8, Math.Abs(expected) * 5e-4);
            if (Math.Abs(expected - evaluation.Value) > tolerance)
            {
                differences.Add(
                    $"Row {index + 1} {operand.Type}: Zemax={expected:R}, Workbench={evaluation.Value:R}, tolerance={tolerance:R}.");
                continue;
            }

            validatedValues++;
        }

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        Assert.Equal(validatedRows.Count, validatedValues);
    }

    [Fact]
    public void RequiredSequentialOperandRegistryContainsExactlyTheVerified2026R1Codes()
    {
        Assert.Equal(383, ZemaxOperandRegistry.Descriptors.Count);
        Assert.Equal(
            383,
            ZemaxOperandRegistry.Descriptors
                .Select(descriptor => descriptor.Code)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            108,
            ZemaxOperandRegistry.Descriptors.Count(
                descriptor => descriptor.SupportLevel == ZemaxOperandSupportLevel.Executable));
        Assert.True(ZemaxOperandRegistry.TryGet("ABCD", out var descriptor));
        Assert.Equal(ZemaxOperandSupportLevel.CompatibilityOnly, descriptor.SupportLevel);
        var thicknessDescriptor = ZemaxOperandRegistry.Get("TTHI");
        Assert.Equal(ZemaxOperandSupportLevel.Executable, thicknessDescriptor.SupportLevel);
        Assert.Equal(new[] { "Int1", "Int2", "Data1", "Data2", "Data3", "Data4" }, thicknessDescriptor.ParameterSlots);
        Assert.True(thicknessDescriptor.UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("REAR").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("RANG").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("CTGT").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("MXEG").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("PMAG").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get("PETZ").SupportLevel);
        Assert.Equal(ZemaxOperandSupportLevel.CompatibilityOnly, ZemaxOperandRegistry.Get("DIMX").SupportLevel);
        Assert.True(ZemaxOperandRegistry.Get("MXEG").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("PMAG").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("PETZ").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("DIMX").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.False(ZemaxOperandRegistry.Get("PMAG").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field));
        Assert.False(ZemaxOperandRegistry.Get("PETZ").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field));
        Assert.True(ZemaxOperandRegistry.Get("DIMX").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Field));
        Assert.True(ZemaxOperandRegistry.Get("DIMX").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("RSCE").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Integer));
        Assert.True(ZemaxOperandRegistry.Get("RSCE").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.NormalizedField));
        Assert.False(ZemaxOperandRegistry.Get("MECS").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Surface));
        Assert.True(ZemaxOperandRegistry.Get("SINE").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.RowReference));
        Assert.True(ZemaxOperandRegistry.Get("DIVI").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.RowReference));
        Assert.True(ZemaxOperandRegistry.Get("SUMM").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.RowReference));
        Assert.True(ZemaxOperandRegistry.Get("PROD").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.RowReference));
        Assert.True(ZemaxOperandRegistry.Get("MAXX").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.RowRangeEnd));
        foreach (var code in new[]
        {
            "CTLT", "CTVA", "ETGT", "ETLT", "ETVA", "FTGT", "FTLT", "STHI",
            "MNCT", "MXCT", "MNET", "MXET", "MNCV", "MXCV", "MNSD", "MXSD",
            "XNEA", "XXEA", "XNEG", "XXEG", "XNET", "XXET", "TGTH", "TTGT", "TTLT", "TTVA",
            "WLEN", "INDX", "ENPP", "EPDI", "EXPP", "EXPD", "ISNA", "ISFN", "SFNO", "WFNO"
        })
        {
            Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get(code).SupportLevel);
        }

        Assert.True(ZemaxOperandRegistry.Get("ETGT").UsesSlotAs("Data2", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("FTGT").UsesSlotAs("Data2", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("STHI").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Numeric));
        Assert.True(ZemaxOperandRegistry.Get("STHI").UsesSlotAs("Data3", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("MNCT").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("MNET").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("XNET").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("TGTH").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("TTGT").UsesSlotAs("Data2", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("EFLX").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("WLEN").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("INDX").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Surface));
        Assert.Equal(ZemaxOperandSupportLevel.CompatibilityOnly, ZemaxOperandRegistry.Get("EFNO").SupportLevel);
        Assert.True(ZemaxOperandRegistry.Get("EFNO").UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Integer));
        Assert.True(ZemaxOperandRegistry.Get("EFNO").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field));
        Assert.True(ZemaxOperandRegistry.TryGet("CARD", out _));
        Assert.True(ZemaxOperandRegistry.TryGet("I1GT", out _));
        Assert.True(ZemaxOperandRegistry.TryGet("I6VA", out _));
        Assert.True(ZemaxOperandRegistry.TryGet("STRH", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("INGT", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("OMMI", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("UDOP", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("XDGT", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("NSDC", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("NPAF", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("RSNC", out _));
        Assert.False(ZemaxOperandRegistry.TryGet("PnGT", out _));
    }

    [Fact]
    public void GenericZemaxOperandSlotsRoundTripWithoutBecomingExecutable()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 100
            SURF 1
              DISZ 0
            ABCD 11 12 1.25 -2.5 3.75 -4.125 5 6 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var imported = Assert.Single(optic.MeritFunctionOperands);
        Assert.False(imported.Enabled);
        Assert.True(imported.CompatibilityOnly);
        Assert.Equal(new[] { 11, 12 }, imported.ZemaxIntegerParameters);
        Assert.Equal(new[] { 1.25, -2.5, 3.75, -4.125 }, imported.ZemaxDataParameters);

        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var operand = Assert.Single(restored.MeritFunctionOperands);
        Assert.True(operand.CompatibilityOnly);
        Assert.Equal(imported.ZemaxIntegerParameters, operand.ZemaxIntegerParameters);
        Assert.Equal(imported.ZemaxDataParameters, operand.ZemaxDataParameters);

        operand.Enabled = true;
        var evaluation = MeritFunctionCatalog.Evaluate(restored, operand);
        Assert.True(double.IsNaN(evaluation.Value));
        Assert.True(double.IsPositiveInfinity(evaluation.Contribution));
        Assert.Contains("not executable", evaluation.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZemaxSpecificMeritRowsArePreservedAsDisabledReadOnlyRecords()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 100
            SURF 1
              STOP
              DISZ 0
            CONF 2 0 0 0 0 0 0 0 0 0
            ABCD 1 1 0 0 0 0 1 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Collection(
            optic.MeritFunctionOperands,
            operand =>
            {
                Assert.Equal("CONF", operand.Type);
                Assert.False(operand.Enabled);
                Assert.Contains("Zemax 只读记录", operand.Comment, StringComparison.Ordinal);
            },
            operand =>
            {
                Assert.Equal("ABCD", operand.Type);
                Assert.False(operand.Enabled);
                Assert.Equal(1, operand.Surface);
                Assert.Equal(1, operand.Wavelength);
                Assert.Equal(1, operand.Target);
                Assert.Equal(1, operand.Weight);
            });
    }

    [Fact]
    public void ExecutableZemaxMeritRowsDoNotFallThroughToCompatibilityImport()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              STOP
              CURV 0
              DISZ 0
            RSCE 0 1 1 0 0 0 0 1 0 0
            TOTR 0 0 0 0 0 0 20 1 0 0
            TTHI 0 1 0 0 0 0 20 1 0 0
            REAR 0 1 0 0 0 0 0 1 0 0
            CONS 0 0 0 0 0 0 1 0 0 0
            SINE 5 0 0 0 0 0 0.8414709848078965 1 0 0
            DIVI 5 2 0 0 0 0 0.05 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(
            new[] { "RSCE", "TOTR", "TTHI", "REAR", "CONS", "SINE", "DIVI" },
            optic.MeritFunctionOperands.Select(operand => operand.Type));
        Assert.All(optic.MeritFunctionOperands, operand =>
        {
            Assert.True(operand.Enabled);
            Assert.False(operand.CompatibilityOnly);
        });

        var rms = optic.MeritFunctionOperands[0];
        Assert.Equal(1, rms.PupilRings);
        Assert.Equal(0, rms.ZemaxIntegerParameters[0]);
        Assert.Equal(0, rms.Surface);
        Assert.Equal(0, rms.Field);
        Assert.Equal(1, rms.Wavelength);
        Assert.Equal(1, rms.Hx, precision: 12);
        Assert.Equal(0, rms.Hy, precision: 12);

        var totalTrack = MeritFunctionCatalog.Evaluate(optic, optic.MeritFunctionOperands[1]);
        var rangeThickness = MeritFunctionCatalog.Evaluate(optic, optic.MeritFunctionOperands[2]);
        Assert.Equal(20, totalTrack.Value, precision: 12);
        Assert.Equal(20, rangeThickness.Value, precision: 12);
        Assert.Empty(totalTrack.Error);
        Assert.Empty(rangeThickness.Error);
    }

    [Fact]
    public void OrderedZemaxMathOperandsEvaluatePreviousRows()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 100
            SURF 1
              STOP
              DISZ 0
            CONS 0 0 0 0 0 0 1.5707963267948966 0 0 0
            SINE 1 0 0 0 0 0 1 2 0 0
            CONS 0 0 0 0 0 0 4 0 0 0
            DIVI 3 2 0 0 0 0 4 3 0 0
            SUMM 1 4 0 0 0 0 5.570796326794897 1 0 0
            PROD 3 4 0 0 0 0 16 1 0 0
            MAXX 1 6 0 0 0 0 16 1 0 0
            MINN 1 6 0 0 0 0 1 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, optic.MeritFunctionOperands);

        Assert.All(optic.MeritFunctionOperands, operand =>
        {
            Assert.True(operand.Enabled);
            Assert.False(operand.CompatibilityOnly);
        });
        Assert.Equal(Math.PI / 2, evaluations[0].Value, precision: 12);
        Assert.Equal(1, evaluations[1].Value, precision: 12);
        Assert.Equal(4, evaluations[2].Value, precision: 12);
        Assert.Equal(4, evaluations[3].Value, precision: 12);
        Assert.Equal((Math.PI / 2) + 4, evaluations[4].Value, precision: 12);
        Assert.Equal(16, evaluations[5].Value, precision: 12);
        Assert.Equal(16, evaluations[6].Value, precision: 12);
        Assert.Equal(1, evaluations[7].Value, precision: 12);
        Assert.All(evaluations, evaluation => Assert.Empty(evaluation.Error));
    }

    [Fact]
    public void ZemaxTrigonometricMathOperandsHonorDegreeFlag()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 100
            SURF 1
              STOP
              DISZ 0
            CONS 0 0 0 0 0 0 90 0 0 0
            SINE 1 1 0 0 0 0 1 1 0 0
            COSI 1 1 0 0 0 0 0 1 0 0
            ASIN 2 1 0 0 0 0 90 1 0 0
            ACOS 3 1 0 0 0 0 90 1 0 0
            ATAN 2 1 0 0 0 0 45 1 0 0
            CONS 0 0 0 0 0 0 -1 0 0 0
            LOGE 7 0 0 0 0 0 0 1 0 0
            LOGT 7 0 0 0 0 0 0 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, optic.MeritFunctionOperands);

        Assert.Equal(1, evaluations[1].Value, precision: 12);
        Assert.Equal(0, evaluations[2].Value, precision: 12);
        Assert.Equal(90, evaluations[3].Value, precision: 12);
        Assert.Equal(90, evaluations[4].Value, precision: 12);
        Assert.Equal(45, evaluations[5].Value, precision: 12);
        Assert.Equal(0, evaluations[7].Value, precision: 12);
        Assert.Equal(0, evaluations[8].Value, precision: 12);
        Assert.All(evaluations, evaluation => Assert.Empty(evaluation.Error));
    }

    [Fact]
    public void OrderedZemaxMathOperandsReportInvalidReferencesAndDomains()
    {
        var optic = new Optic("math");
        var futureReference = new[]
        {
            new MeritOperandDefinition { Type = "SINE", Surface = 2 },
            new MeritOperandDefinition { Type = "CONS", Target = 1 }
        };
        var divideByZero = new[]
        {
            new MeritOperandDefinition { Type = "CONS", Target = 2 },
            new MeritOperandDefinition { Type = "CONS", Target = 0 },
            new MeritOperandDefinition { Type = "DIVI", Surface = 1, Wavelength = 2 }
        };
        var negativeRoot = new[]
        {
            new MeritOperandDefinition { Type = "CONS", Target = -1 },
            new MeritOperandDefinition { Type = "SQRT", Surface = 1 }
        };

        var futureEvaluation = MeritFunctionCatalog.EvaluateAll(optic, futureReference);
        var divideEvaluation = MeritFunctionCatalog.EvaluateAll(optic, divideByZero);
        var rootEvaluation = MeritFunctionCatalog.EvaluateAll(optic, negativeRoot);

        Assert.Contains("前序行", futureEvaluation[0].Error, StringComparison.Ordinal);
        Assert.Contains("分母", divideEvaluation[2].Error, StringComparison.Ordinal);
        Assert.Contains("不能为负数", rootEvaluation[1].Error, StringComparison.Ordinal);
        Assert.True(double.IsPositiveInfinity(futureEvaluation[0].Contribution));
        Assert.True(double.IsPositiveInfinity(divideEvaluation[2].Contribution));
        Assert.True(double.IsPositiveInfinity(rootEvaluation[1].Contribution));
    }

    [Fact]
    public void SingleRowZemaxMathOperandRequiresOrderedEvaluation()
    {
        var optic = new Optic("math");
        var evaluation = MeritFunctionCatalog.Evaluate(
            optic,
            new MeritOperandDefinition
            {
                Type = "SINE",
                Surface = 1
            });

        Assert.Contains("有序评价函数入口", evaluation.Error, StringComparison.Ordinal);
        Assert.True(double.IsNaN(evaluation.Value));
    }

    [Fact]
    public void ZemaxCompatibilityOperandSlotsAreNotTreatedAsWavelengthReferences()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 3 1 0 0 0
            WAVM 1 0.4861327 1
            WAVM 2 0.5875618 1
            WAVM 3 0.6562725 1
            PWAV 2
            SURF 0
              DISZ 100
            SURF 1
              DISZ 0
            ABCD 1 15 0 0 0 0 0.1 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var snapshot = optic.ToSnapshot();

        OpticSnapshotValidator.Validate(snapshot);
        var restored = Optic.FromSnapshot(snapshot);
        var operand = Assert.Single(restored.MeritFunctionOperands);

        Assert.Equal("ABCD", operand.Type);
        Assert.False(operand.Enabled);
        Assert.Equal(1, operand.Surface);
        Assert.Equal(15, operand.Wavelength);
    }

    [Fact]
    public void ZemaxMeritFunctionRowsImportInOriginalOrder()
    {
        const string source = """
            MODE SEQ
            NAME "Merit import"
            ENPD 10
            FTYP 0 0 2 2 0 0 0
            XFLN 0 0
            YFLN 0 10
            FWGN 1 1
            WAVM 1 0.4861327 1
            WAVM 2 0.5875618 1
            PWAV 2
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              CURV 0
              DISZ 0
            DMFS 0 0 0 0 0 0 0 0 0 0
            BLNK 序列评价函数: RMS 波前差：质心参考高斯求积 3 环 6 臂
            BLNK 视场操作数 2.
            OPDX 0 2 0 0.7142857142857143 0.16785534350986436 0.29073398328101191 0 -0.032320912073968894 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        Assert.Collection(
            optic.MeritFunctionOperands,
            operand =>
            {
                Assert.Equal("DMFS", operand.Type);
                Assert.False(operand.Enabled);
            },
            operand =>
            {
                Assert.Equal("BLNK", operand.Type);
                Assert.Equal("序列评价函数: RMS 波前差：质心参考高斯求积 3 环 6 臂", operand.Comment);
            },
            operand =>
            {
                Assert.Equal("BLNK", operand.Type);
                Assert.Equal("视场操作数 2.", operand.Comment);
            },
            operand =>
            {
                Assert.Equal("OPDX", operand.Type);
                Assert.Equal(0, operand.Surface);
                Assert.Equal(2, operand.Wavelength);
                Assert.Equal(0, operand.Hx, precision: 12);
                Assert.Equal(0.7142857142857143, operand.Hy, precision: 12);
                Assert.Equal(0.16785534350986436, operand.Px, precision: 12);
                Assert.Equal(0.29073398328101191, operand.Py, precision: 12);
                Assert.Equal(0, operand.Target, precision: 12);
                Assert.Equal(-0.032320912073968894, operand.Weight, precision: 12);
            });
    }

    [Fact]
    public void ZemaxReferenceMeritFunctionRowsAreNotSilentlyDropped()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              CURV 0
              DISZ 0
            SINE 3 0 0 0 0 0 0 0 0 0
            TTHI 0 1 0.25 0 0 0 20 0.02 0 0
            CTGT 1 2 0 0 0 0 0.33 0.02 0 0
            PMAG 0 1 0 0 0 0 -0.018 0 0 0
            DIVI 3 2 0 0 0 0 -10 0.1 0 0
            REAR 0 1 0 1 0 0 0 0 0 0
            DIMX 0 1 0 0 0 0 2 0 0 0
            PETZ 0 1 0 0 0 0 -99.794 0 0 0
            MXEG 1 1 0 0 0 0 6 0.01 0 0
            TRAR 0 1 0 -1 0.335710687 0 0 0.0969627362 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        OpticSnapshotValidator.Validate(optic.ToSnapshot());

        Assert.Equal(
            new[] { "SINE", "TTHI", "CTGT", "PMAG", "DIVI", "REAR", "DIMX", "PETZ", "MXEG", "TRAR" },
            optic.MeritFunctionOperands.Select(operand => operand.Type));
        Assert.All(optic.MeritFunctionOperands.Where(operand => operand.Type != "DIMX"), operand =>
        {
            Assert.True(operand.Enabled);
            Assert.False(operand.CompatibilityOnly);
        });
        var distortion = optic.MeritFunctionOperands.Single(operand => operand.Type == "DIMX");
        Assert.False(distortion.Enabled);
        Assert.True(distortion.CompatibilityOnly);

        var thickness = optic.MeritFunctionOperands[1];
        Assert.True(thickness.Enabled);
        Assert.Equal(0, thickness.Surface);
        Assert.Equal(1, thickness.Wavelength);
        Assert.Equal(0.25, thickness.Hx, precision: 12);
        Assert.Equal(0, thickness.Field);
        Assert.Equal(20, thickness.Target, precision: 12);
        Assert.Equal(0.02, thickness.Weight, precision: 12);
        var thicknessEvaluation = MeritFunctionCatalog.Evaluate(optic, thickness);
        Assert.Equal(20, thicknessEvaluation.Value, precision: 12);
        Assert.Empty(thicknessEvaluation.Error);

        var radialRay = optic.MeritFunctionOperands[5];
        Assert.True(radialRay.Enabled);
        Assert.Equal("REAR", radialRay.Type);
        Assert.Equal(0, radialRay.Surface);
        Assert.Equal(1, radialRay.Wavelength);
        Assert.Equal(0, radialRay.Hx, precision: 12);
        Assert.Equal(1, radialRay.Hy, precision: 12);
        Assert.Equal(0, radialRay.Px, precision: 12);
        Assert.Equal(0, radialRay.Py, precision: 12);

        var ray = optic.MeritFunctionOperands[^1];
        Assert.True(ray.Enabled);
        Assert.Equal(0, ray.Surface);
        Assert.Equal(1, ray.Wavelength);
        Assert.Equal(0, ray.Hx, precision: 12);
        Assert.Equal(-1, ray.Hy, precision: 12);
        Assert.Equal(0.335710687, ray.Px, precision: 12);
        Assert.Equal(0, ray.Py, precision: 12);
        Assert.Equal(0.0969627362, ray.Weight, precision: 12);
    }

    [Fact]
    public void ZemaxBoundaryAndFirstOrderOperandsEvaluateWithZemaxStyleTargets()
    {
        const string source = """
            MODE SEQ
            NAME "Zemax boundary operands"
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 10
            SURF 1
              STOP
              CURV 0
              DISZ 4
              GLAS N-BK7
              DIAM 5 1 0 0 1 ""
            SURF 2
              CURV 0
              DISZ 10
              DIAM 5 1 0 0 1 ""
            SURF 3
              CURV 0
              DISZ 0
            CTGT 1 0 0 0 0 0 3 1 0 0
            CTGT 1 0 0 0 0 0 5 1 0 0
            MXEG 1 1 0 0 0 0 6 1 0 0
            MXEG 1 1 0 0 0 0 3 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        const string finiteConjugateSource = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 10
            SURF 1
              STOP
              CURV 0
              DISZ 10
            SURF 2
              CURV 0
              DISZ 0
            PMAG 0 1 0 0 0 0 -1 1 0 0
            """;
        var finiteConjugateOptic = OpticalFormatCatalog.Import(finiteConjugateSource, ".zmx");

        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, optic.MeritFunctionOperands);
        var paraxialMagnification = MeritFunctionCatalog.Evaluate(
            finiteConjugateOptic,
            finiteConjugateOptic.MeritFunctionOperands.Single());

        Assert.Equal(3, evaluations[0].Value, precision: 12);
        Assert.Equal(4, evaluations[1].Value, precision: 12);
        Assert.Equal(6, evaluations[2].Value, precision: 12);
        Assert.Equal(4, evaluations[3].Value, precision: 12);
        Assert.Equal(-1, paraxialMagnification.Value, precision: 12);
        Assert.All(evaluations, evaluation => Assert.Empty(evaluation.Error));
        Assert.Empty(paraxialMagnification.Error);
        Assert.Equal(0, evaluations[0].Contribution, precision: 12);
        Assert.Equal(1, evaluations[1].Contribution, precision: 12);
        Assert.Equal(0, evaluations[2].Contribution, precision: 12);
        Assert.Equal(1, evaluations[3].Contribution, precision: 12);
    }

    [Fact]
    public void ZemaxCommonThicknessAndLensDataOperandsEvaluateWithZemaxSlots()
    {
        const string source = """
            MODE SEQ
            NAME "Zemax common operand bundle"
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 10
            SURF 1
              STOP
              CURV 0
              DISZ 4
              GLAS N-BK7
              DIAM 5 1 0 0 1 ""
            SURF 2
              CURV 0
              DISZ 10
              DIAM 5 1 0 0 1 ""
            SURF 3
              CURV 0.25
              CONI -1
              DISZ 6
              GLAS N-BK7
              DIAM 5 1 0 0 1 ""
            SURF 4
              CURV 0
              DISZ 0
            WLEN 0 1 0 0 0 0 0.5875618 1 0 0
            INDX 1 1 0 0 0 0 1.5 1 0 0
            EFLX 1 4 0 0 0 0 0 0 0 0
            EFLY 1 4 0 0 0 0 0 0 0 0
            ENPP 0 0 0 0 0 0 0 0 0 0
            EPDI 0 0 0 0 0 0 10 1 0 0
            EXPP 0 0 0 0 0 0 0 0 0 0
            EXPD 0 0 0 0 0 0 0 0 0 0
            ISNA 0 0 0 0 0 0 0 0 0 0
            ISFN 0 0 0 0 0 0 0 0 0 0
            SFNO 0 0 0 0 0 0 0 0 0 0
            WFNO 0 0 0 0 0 0 0 0 0 0
            CTLT 1 0 0 0 0 0 3 1 0 0
            CTVA 1 0 0 0 0 0 4 1 0 0
            ETGT 1 0 0 0 0 0 3 1 0 0
            ETLT 1 0 0 0 0 0 3 1 0 0
            ETVA 1 0 0 0 0 0 4 1 0 0
            TTGT 1 0 0 0 0 0 3 1 0 0
            TTLT 1 0 0 0 0 0 3 1 0 0
            TTVA 1 0 0 0 0 0 4 1 0 0
            FTGT 2 0 0 0 0 0 9 1 0 0
            FTLT 2 0 0 0 0 0 11 1 0 0
            STHI 1 0 0 2 0 0 4 1 0 0
            CVVA 3 0 0 0 0 0 0.25 1 0 0
            MNCV 1 3 0 0 0 0 0 1 0 0
            MXCV 1 3 0 0 0 0 0.2 1 0 0
            COVA 3 0 0 0 0 0 -1 1 0 0
            MNSD 1 3 0 0 0 0 5 1 0 0
            MXSD 1 3 0 0 0 0 4 1 0 0
            MNCA 2 2 0 0 0 0 8 1 0 0
            MXCA 2 2 0 0 0 0 8 1 0 0
            MNEA 2 2 0 0 0 0 8 1 0 0
            MXEA 2 2 0 0 0 0 8 1 0 0
            MNCG 1 1 0 0 0 0 3 1 0 0
            MXCG 1 1 0 0 0 0 3 1 0 0
            MNEG 1 1 0 0 0 0 3 1 0 0
            MNCT 1 2 0 0 0 0 4 1 0 0
            MXCT 1 2 0 0 0 0 6 1 0 0
            MNET 1 1 0 0 0 0 3 1 0 0
            MXET 1 1 0 0 0 0 3 1 0 0
            XNEA 2 2 0 0 0 0 8 1 0 0
            XXEA 2 2 0 0 0 0 8 1 0 0
            XNEG 1 1 0 0 0 0 3 1 0 0
            XXEG 1 1 0 0 0 0 3 1 0 0
            XNET 1 2 0 0 0 0 3 1 0 0
            XXET 1 2 0 0 0 0 12 1 0 0
            TGTH 1 4 0 0 0 0 10 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var evaluations = MeritFunctionCatalog
            .EvaluateAll(optic, optic.MeritFunctionOperands)
            .Select((evaluation, index) => (Type: optic.MeritFunctionOperands[index].Type, Evaluation: evaluation))
            .ToDictionary(item => item.Type, item => item.Evaluation, StringComparer.Ordinal);

        var wavelength = optic.Wavelengths.Single().Nanometers;
        var expectedGlassIndex = optic.SurfaceGroup.Items.Single(surface => surface.Number == 1)
            .MaterialAfter
            .RefractiveIndex(wavelength);
        const double curvedAirEdgeThickness = 13.125;

        Assert.Equal(0.5875618, evaluations["WLEN"].Value, precision: 12);
        Assert.Equal(expectedGlassIndex, evaluations["INDX"].Value, precision: 12);
        Assert.True(double.IsFinite(evaluations["EFLX"].Value));
        Assert.Equal(evaluations["EFLX"].Value, evaluations["EFLY"].Value, precision: 12);
        Assert.Equal(10, evaluations["EPDI"].Value, precision: 12);
        Assert.True(double.IsFinite(evaluations["ENPP"].Value));
        Assert.True(double.IsFinite(evaluations["EXPP"].Value));
        Assert.True(double.IsFinite(evaluations["EXPD"].Value));
        Assert.True(evaluations["ISNA"].Value >= 0);
        Assert.Equal(evaluations["ISFN"].Value, evaluations["SFNO"].Value, precision: 12);
        Assert.Equal(evaluations["ISFN"].Value, evaluations["WFNO"].Value, precision: 12);
        Assert.Equal(4, evaluations["CTLT"].Value, precision: 12);
        Assert.Equal(4, evaluations["CTVA"].Value, precision: 12);
        Assert.Equal(3, evaluations["ETGT"].Value, precision: 12);
        Assert.Equal(4, evaluations["ETLT"].Value, precision: 12);
        Assert.Equal(4, evaluations["ETVA"].Value, precision: 12);
        Assert.Equal(3, evaluations["TTGT"].Value, precision: 12);
        Assert.Equal(4, evaluations["TTLT"].Value, precision: 12);
        Assert.Equal(4, evaluations["TTVA"].Value, precision: 12);
        Assert.Equal(9, evaluations["FTGT"].Value, precision: 12);
        Assert.Equal(curvedAirEdgeThickness, evaluations["FTLT"].Value, precision: 12);
        Assert.Equal(4, evaluations["STHI"].Value, precision: 12);
        Assert.Equal(0.25, evaluations["CVVA"].Value, precision: 12);
        Assert.Equal(0, evaluations["MNCV"].Value, precision: 12);
        Assert.Equal(0.25, evaluations["MXCV"].Value, precision: 12);
        Assert.Equal(-1, evaluations["COVA"].Value, precision: 12);
        Assert.Equal(5, evaluations["MNSD"].Value, precision: 12);
        Assert.Equal(5, evaluations["MXSD"].Value, precision: 12);
        Assert.Equal(8, evaluations["MNCA"].Value, precision: 12);
        Assert.Equal(10, evaluations["MXCA"].Value, precision: 12);
        Assert.Equal(8, evaluations["MNEA"].Value, precision: 12);
        Assert.Equal(curvedAirEdgeThickness, evaluations["MXEA"].Value, precision: 12);
        Assert.Equal(3, evaluations["MNCG"].Value, precision: 12);
        Assert.Equal(4, evaluations["MXCG"].Value, precision: 12);
        Assert.Equal(3, evaluations["MNEG"].Value, precision: 12);
        Assert.Equal(4, evaluations["MNCT"].Value, precision: 12);
        Assert.Equal(10, evaluations["MXCT"].Value, precision: 12);
        Assert.Equal(3, evaluations["MNET"].Value, precision: 12);
        Assert.Equal(4, evaluations["MXET"].Value, precision: 12);
        Assert.Equal(8, evaluations["XNEA"].Value, precision: 12);
        Assert.Equal(curvedAirEdgeThickness, evaluations["XXEA"].Value, precision: 12);
        Assert.Equal(3, evaluations["XNEG"].Value, precision: 12);
        Assert.Equal(4, evaluations["XXEG"].Value, precision: 12);
        Assert.Equal(3, evaluations["XNET"].Value, precision: 12);
        Assert.Equal(curvedAirEdgeThickness, evaluations["XXET"].Value, precision: 12);
        Assert.Equal(10, evaluations["TGTH"].Value, precision: 12);
        Assert.All(evaluations.Values, evaluation => Assert.Empty(evaluation.Error));
    }

    [Fact]
    public void ZemaxPetzvalOperandUsesAnalysisEngineWhileDimxRemainsCompatibilityOnly()
    {
        var optic = Optic.CreateCookeTriplet();
        var petzval = new MeritOperandDefinition
        {
            Type = "PETZ",
            Wavelength = 1,
            ZemaxIntegerParameters = new[] { 0, 1 },
            Target = 0,
            Weight = 0
        };
        var dimx = new MeritOperandDefinition
        {
            Type = "DIMX",
            CompatibilityOnly = true,
            Wavelength = 1,
            ZemaxIntegerParameters = new[] { 0, 1 },
            Target = 1,
            Weight = 1
        };

        var petzvalEvaluation = MeritFunctionCatalog.Evaluate(optic, petzval);
        var dimxEvaluation = MeritFunctionCatalog.Evaluate(optic, dimx);

        Assert.True(double.IsFinite(petzvalEvaluation.Value));
        Assert.Empty(petzvalEvaluation.Error);
        Assert.True(double.IsNaN(dimxEvaluation.Value));
        Assert.True(double.IsPositiveInfinity(dimxEvaluation.Contribution));
        Assert.Contains("not executable", dimxEvaluation.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MsL7MeritFunctionMatchesTrackedZemaxRowOrder()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "local-data",
            "lens-library",
            "originals",
            "user-zmx",
            "project",
            "root",
            "[MS-L7](10X大NA大视场).ZMX");
        var source = File.ReadAllText(path);
        var sourceRows = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .SkipWhile(line => !line.Trim().Equals("BLNK MICROSCOPE", StringComparison.Ordinal))
            .TakeWhile(line => !line.TrimStart().StartsWith("TOL ", StringComparison.Ordinal))
            .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(tokens => tokens.Length > 0 && tokens[0].Length == 4)
            .Select(tokens => tokens[0].ToUpperInvariant())
            .ToArray();

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(103, sourceRows.Length);
        Assert.Equal(sourceRows, optic.MeritFunctionOperands.Select(operand => operand.Type));
    }

    [Fact]
    public void ZemaxImportRejectsExcessiveMultiConfigurationCount()
    {
        var configurationCount = StarOptProjectStore.MaximumConfigurationCount + 1;
        var source = $$"""
            MODE SEQ
            ENPD 10
            MNUM {{configurationCount}}
            SURF 0
              DISZ 100
            SURF 1
              DISZ 0
            """;

        var exception = Assert.Throws<InvalidDataException>(
            () => OpticalFormatCatalog.Import(source, ".zmx"));

        Assert.Contains("MNUM", exception.Message, StringComparison.Ordinal);
        Assert.Contains(configurationCount.ToString(), exception.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> SampleLensFiles()
    {
        yield return new object[] { "achromatic-doublet.zmx", FieldDefinitionKind.Angle, 5, 2 };
        yield return new object[] { "double-gauss-50mm.zmx", FieldDefinitionKind.Angle, 11, 4 };
        yield return new object[] { "telephoto-four-element.zmx", FieldDefinitionKind.Angle, 9, 4 };
        yield return new object[] { "finite-conjugate-macro.zmx", FieldDefinitionKind.ObjectHeight, 9, 3 };
        yield return new object[] { "real-image-height-demo.zmx", FieldDefinitionKind.RealImageHeight, 5, 2 };
    }

    [Theory]
    [MemberData(nameof(SampleLensFiles))]
    public void SampleLensFilesUseCatalogGlassAndTraceEveryDefinedField(
        string fileName,
        FieldDefinitionKind expectedFieldDefinition,
        int expectedSurfaceCount,
        int expectedGlassSurfaceCount)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName));
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(expectedFieldDefinition, optic.FieldDefinition);
        Assert.Equal(expectedSurfaceCount, optic.SurfaceGroup.Items.Count);
        Assert.Equal(3, optic.Fields.Count);
        Assert.Equal(3, optic.Wavelengths.Count);
        Assert.Equal(
            expectedGlassSurfaceCount,
            optic.SurfaceGroup.Items.Count(surface => surface.MaterialAfter is CatalogGlassMaterial));

        var wavelength = optic.Wavelengths.Single(item => item.IsPrimary).Micrometers;
        foreach (var field in optic.Fields)
        {
            var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
            var history = optic.TraceGeneric(normalized.X, normalized.Y, 0, 0, wavelength)
                .RayHistories.Single();
            var final = Assert.Single(history, sample => sample.SurfaceNumber == optic.SurfaceGroup.Items[^1].Number);

            Assert.False(final.Vignetted);
            Assert.True(final.Intensity > 0);
            Assert.True(double.IsFinite(final.Position.X));
            Assert.True(double.IsFinite(final.Position.Y));
            Assert.True(double.IsFinite(final.Position.Z));

            if (expectedFieldDefinition == FieldDefinitionKind.RealImageHeight)
            {
                var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);
                Assert.Equal(field.X, local.X, precision: 8);
                Assert.Equal(field.Y, local.Y, precision: 8);
            }
        }

        var scene = new Layout2DBuilder(optic).Build3D(options: new LayoutBuildOptions(
            FieldIndex: optic.Fields.Count - 1,
            WavelengthIndex: optic.Wavelengths.ToList().FindIndex(item => item.IsPrimary),
            RayCount: 3));
        Assert.NotEmpty(scene.LensElements);
        Assert.NotEmpty(scene.Rays);
    }

    [Fact]
    public void Optiland058ZemaxFixtureImportsSystemAndPrescription()
    {
        var source = File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx"));
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal("Optiland 0.5.8 Zemax Import Reference", optic.Name);
        Assert.Equal(ApertureKind.EntrancePupilDiameter, optic.Aperture.Kind);
        Assert.Equal(12.5, optic.Aperture.Value, precision: 12);

        Assert.Equal(3, optic.Fields.Count);
        Assert.Equal((0.0, 0.0, 1.0, 0.0, 0.0), FieldValues(optic.Fields[0]));
        Assert.Equal((1.5, 7.0, 0.5, 0.1, 0.15), FieldValues(optic.Fields[1]));
        Assert.Equal((-1.5, 10.0, 0.25, 0.2, 0.25), FieldValues(optic.Fields[2]));

        Assert.Equal(3, optic.Wavelengths.Count);
        Assert.Equal(486.1327, optic.Wavelengths[0].Nanometers, precision: 10);
        Assert.Equal(587.5618, optic.Wavelengths[1].Nanometers, precision: 10);
        Assert.Equal(656.2725, optic.Wavelengths[2].Nanometers, precision: 10);
        Assert.Equal(new[] { false, true, false }, optic.Wavelengths.Select(wavelength => wavelength.IsPrimary));
        Assert.Equal(new[] { 0.5, 1.0, 0.5 }, optic.Wavelengths.Select(wavelength => wavelength.Weight));

        Assert.Equal(5, optic.SurfaceGroup.Items.Count);
        Assert.IsType<PlaneGeometry>(optic.SurfaceGroup.Items[0].Geometry);
        var standard = Assert.IsType<StandardGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(50, standard.Radius, precision: 12);
        Assert.True(optic.SurfaceGroup.Items[1].IsStop);
        Assert.Equal(6.25, optic.SurfaceGroup.Items[1].SemiDiameter, precision: 12);

        var evenAsphere = Assert.IsType<EvenAsphereGeometry>(optic.SurfaceGroup.Items[2].Geometry);
        Assert.Equal(-40, evenAsphere.Base.Radius, precision: 12);
        Assert.Equal(-1, evenAsphere.Base.Conic, precision: 12);
        Assert.Equal(1e-6, evenAsphere.Coefficients[0], precision: 15);
        Assert.Equal(-2e-8, evenAsphere.Coefficients[1], precision: 15);

        var toroidal = Assert.IsType<ToroidalGeometry>(optic.SurfaceGroup.Items[3].Geometry);
        Assert.Equal(100, toroidal.TangentialRadius, precision: 12);
        Assert.Equal(80, toroidal.SagittalRadius, precision: 12);
        Assert.Equal(100, optic.SurfaceGroup.Items[3].Radius, precision: 12);
        var importedFlint = Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[3].MaterialAfter);
        Assert.Equal("N-F2", importedFlint.Name);
        Assert.Equal("SCHOTT", importedFlint.Manufacturer);

        var positions = optic.SurfaceGroup.Items.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
        Assert.Equal(0, positions[0]);
        Assert.Equal(new[] { 0.0, 4.0, 6.0, 14.0 }, positions.Skip(1));

        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("UNIT MM", exported, StringComparison.Ordinal);
        Assert.Contains("  CURV 0.01", exported, StringComparison.Ordinal);
        Assert.Contains("  PARM 2 80", exported, StringComparison.Ordinal);
        var restoredToroidal = Assert.IsType<ToroidalGeometry>(
            OpticalFormatCatalog.Import(exported, ".zmx").SurfaceGroup.Items[3].Geometry);
        Assert.Equal(100, restoredToroidal.TangentialRadius, precision: 12);
        Assert.Equal(80, restoredToroidal.SagittalRadius, precision: 12);
    }

    [Fact]
    public void ZemaxImportPreservesUserDefinedAndPickupSemiDiameters()
    {
        const string source = """
            MODE SEQ
            ENPD 8
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              DISZ 20
              DIAM 10 0 0 0 1 ""
            SURF 1
              CURV 0.02
              DISZ 3
              GLAS N-BK7
              DIAM 5.5 1 0 0 1 ""
            SURF 2
              CURV -0.02
              DISZ 15
              DIAM 5.5 2 1 0 1 ""
            SURF 3
              DISZ 0
              DIAM 2 0 0 0 1 ""
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.False(optic.SurfaceGroup.Items[0].SemiDiameterFixed);
        Assert.True(optic.SurfaceGroup.Items[1].SemiDiameterFixed);
        Assert.True(optic.SurfaceGroup.Items[2].SemiDiameterFixed);
        Assert.False(optic.SurfaceGroup.Items[3].SemiDiameterFixed);

        AutomaticSemiDiameterSolver.Update(optic);

        Assert.Equal(5.5, optic.SurfaceGroup.Items[1].SemiDiameter, precision: 12);
        Assert.Equal(5.5, optic.SurfaceGroup.Items[2].SemiDiameter, precision: 12);
    }

    [Fact]
    public void ZemaxMultiConfigurationApmnCreatesAnnularPhysicalAperture()
    {
        const string source = """
            MODE SEQ
            ENPD 6
            MNUM 2
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              DISZ 20
            SURF 1
              STOP
              DISZ 10
              DIAM 3 1 0 0 1 ""
            SURF 2
              DISZ 0
            APMX 1 2 3
            APMN 1 2 1
            """;

        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        var configured = imported.Configurations[1];
        var stop = configured.SurfaceGroup.Items[1];

        Assert.Equal(3, stop.SemiDiameter, precision: 12);
        var aperture = Assert.IsType<AnnularAperture>(stop.PhysicalAperture);
        Assert.Equal(3, aperture.OuterRadius, precision: 12);
        Assert.Equal(1, aperture.InnerRadius, precision: 12);
        Assert.False(aperture.Contains(Vector3D.Zero));
        Assert.True(aperture.Contains(new Vector3D(1.5, 0, 0)));

        var history = configured.TraceGeneric(0, 0, 0, 0, 0.5875618).RayHistories.Single();
        var stopSample = Assert.Single(history, sample => sample.SurfaceNumber == stop.Number);
        Assert.True(stopSample.Vignetted);
        Assert.Equal(0, stopSample.Intensity, precision: 12);
    }

    [Fact]
    public void ZemaxExportRoundTripsFixedSemiDiameterState()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].SemiDiameter = 8.85;
        optic.SurfaceGroup.Items[1].SemiDiameterFixed = true;

        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");

        Assert.True(restored.SurfaceGroup.Items[1].SemiDiameterFixed);
        Assert.Equal(8.85, restored.SurfaceGroup.Items[1].SemiDiameter, precision: 12);
    }

    [Fact]
    public void ZemaxExportRoundTripsAnnularPhysicalAperture()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.SemiDiameter = 4.5;
        surface.SemiDiameterFixed = true;
        surface.PhysicalAperture = new AnnularAperture(4.5, 1.25);

        var exported = OpticalFormatCatalog.Export(optic, ".zmx");

        Assert.Contains("  DIAM 4.5 1 0 0 1 \"\"", exported, StringComparison.Ordinal);
        Assert.Contains("  APMN 1.25", exported, StringComparison.Ordinal);
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");
        var aperture = Assert.IsType<AnnularAperture>(restored.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.Equal(4.5, restored.SurfaceGroup.Items[1].SemiDiameter, precision: 12);
        Assert.Equal(4.5, aperture.OuterRadius, precision: 12);
        Assert.Equal(1.25, aperture.InnerRadius, precision: 12);
    }

    [Fact]
    public void ZemaxExportRejectsUnmappedGeometryAndAperturesInsteadOfDowngradingToStandard()
    {
        var geometryOptic = Optic.CreateCookeTriplet();
        geometryOptic.SurfaceGroup.Items[1].Geometry = new BiconicGeometry(40, 30, -0.2, -0.4);

        var geometryError = Assert.Throws<NotSupportedException>(() =>
            OpticalFormatCatalog.Export(geometryOptic, ".zmx"));
        Assert.Contains("cannot losslessly map geometry", geometryError.Message, StringComparison.Ordinal);
        Assert.Contains("biconic", geometryError.Message, StringComparison.OrdinalIgnoreCase);

        var apertureOptic = Optic.CreateCookeTriplet();
        apertureOptic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2, 1);

        var apertureError = Assert.Throws<NotSupportedException>(() =>
            OpticalFormatCatalog.Export(apertureOptic, ".zmx"));
        Assert.Contains("cannot losslessly map physical aperture", apertureError.Message, StringComparison.Ordinal);
        Assert.Contains("rectangular", apertureError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZemaxCurvedRefractingStopRemainsAnOpticalSurfaceInLayout()
    {
        const string source = """
            MODE SEQ
            ENPD 8
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              DISZ 20
            SURF 1
              CURV -0.1
              DISZ 2
              GLAS N-BK10
              DIAM 6 1 0 0 1 ""
            SURF 2
              STOP
              CURV 0.04
              DISZ 4
              GLAS N-FK56
              DIAM 6 1 0 0 1 ""
            SURF 3
              CURV -0.05
              DISZ 10
              DIAM 6 1 0 0 1 ""
            SURF 4
              DISZ 0
              DIAM 2 0 0 0 1 ""
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        var scene = new Layout2DBuilder(optic).Build(surfaceSamples: 33);

        Assert.True(stop.IsStop);
        Assert.Equal(25, Assert.IsType<StandardGeometry>(stop.Geometry).Radius, precision: 12);
        Assert.Equal("N-BK10", stop.MaterialBefore.Name);
        Assert.Equal("N-FK56", stop.MaterialAfter.Name);
        Assert.False(scene.Surfaces.Single(surface => surface.SurfaceNumber == stop.Number).IsStandaloneStop);
    }

    [Fact]
    public void ZemaxImportWithoutGcatUsesDefaultSchottGlassPriority()
    {
        const string source = """
            MODE SEQ
            ENPD 8
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              CURV 0.02
              DISZ 3
              GLAS F2
              DIAM 4
            SURF 2
              CURV -0.02
              DISZ 15
              GLAS AIR
            SURF 3
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var glass = Assert.Single(optic.SurfaceGroup.Items
            .Select(surface => surface.MaterialAfter)
            .OfType<CatalogGlassMaterial>());

        Assert.Equal("SCHOTT", glass.Manufacturer);
        Assert.Equal("F2", glass.CatalogName);
    }

    [Fact]
    public void ZemaxFixtureMatchesPython058ReferenceContract()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(
            FixturePath("optiland-0.5.8-zemax-reference.json")));
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx")),
            ".zmx");
        var root = expected.RootElement;

        Assert.Equal("0.5.8", root.GetProperty("optiland_version").GetString());
        Assert.Equal(root.GetProperty("aperture").GetProperty("value").GetDouble(), optic.Aperture.Value, precision: 12);

        var expectedFields = root.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal(expectedFields.Length, optic.Fields.Count);
        for (var index = 0; index < expectedFields.Length; index++)
        {
            Assert.Equal(expectedFields[index].GetProperty("x").GetDouble(), optic.Fields[index].X, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("y").GetDouble(), optic.Fields[index].Y, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("vx").GetDouble(), optic.Fields[index].VignetteFactorX, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("vy").GetDouble(), optic.Fields[index].VignetteFactorY, precision: 12);
        }

        var expectedWavelengths = root.GetProperty("wavelengths").EnumerateArray().ToArray();
        Assert.Equal(expectedWavelengths.Length, optic.Wavelengths.Count);
        for (var index = 0; index < expectedWavelengths.Length; index++)
        {
            Assert.Equal(
                expectedWavelengths[index].GetProperty("value_um").GetDouble() * 1000,
                optic.Wavelengths[index].Nanometers,
                precision: 10);
            Assert.Equal(
                expectedWavelengths[index].GetProperty("is_primary").GetBoolean(),
                optic.Wavelengths[index].IsPrimary);
        }

        var expectedSurfaces = root.GetProperty("surfaces").EnumerateArray().ToArray();
        Assert.Equal(expectedSurfaces.Length, optic.SurfaceGroup.Items.Count);
        for (var index = 0; index < expectedSurfaces.Length; index++)
        {
            var expectedPosition = expectedSurfaces[index]
                .GetProperty("geometry")
                .GetProperty("position")[2];
            var expectedZ = ReadPythonNumber(expectedPosition);
            if (index == 0 && double.IsNegativeInfinity(expectedZ))
            {
                expectedZ = 0;
            }

            Assert.Equal(
                expectedZ,
                optic.SurfaceGroup.Items[index].CoordinateSystem.Origin.Z,
                precision: 12);
            Assert.Equal(
                expectedSurfaces[index].GetProperty("is_stop").GetBoolean(),
                optic.SurfaceGroup.Items[index].IsStop);

            var expectedSag = expectedSurfaces[index]
                .GetProperty("geometry")
                .GetProperty("sag_sample");
            Assert.Equal(
                ReadPythonNumber(expectedSag.GetProperty("z")),
                optic.SurfaceGroup.Items[index].Geometry.Sag(
                    expectedSag.GetProperty("x").GetDouble(),
                    expectedSag.GetProperty("y").GetDouble()),
                precision: 11);
        }
    }

    [Fact]
    public void ZemaxImportSupportsFloatingStopMirrorAndCoordinateBreak()
    {
        const string source = """
            MODE SEQ
            FLOA
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.55 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 5
            SURF 1
              CURV 0.02
              DISZ 2
              GLAS CUSTOM-Z 0 0 1.7 30
              STOP
              DIAM 4
            SURF 2
              TYPE COORDBRK
              DISZ 3
              PARM 1 1
              PARM 4 90
            SURF 3
              CURV 0
              DISZ 2
              GLAS MIRROR
              DIAM 5
            SURF 4
              CURV 0
              DISZ 0
              GLAS AIR
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(4, optic.SurfaceGroup.Items.Count);
        Assert.Equal(ApertureKind.FloatByStopSize, optic.Aperture.Kind);
        Assert.Equal(4, optic.Aperture.Value, precision: 12);
        Assert.Equal(8, optic.Paraxial.EstimateEntrancePupilDiameter(), precision: 12);
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("FLOA", exported, StringComparison.Ordinal);
        Assert.Equal(
            ApertureKind.FloatByStopSize,
            OpticalFormatCatalog.Import(exported, ".zmx").Aperture.Kind);
        var mirror = optic.SurfaceGroup.Items[2];
        Assert.True(mirror.IsReflective);
        Assert.Equal("MIRROR", mirror.Material);
        Assert.Equal("CUSTOM-Z", mirror.MaterialBefore.Name);
        Assert.Equal("CUSTOM-Z", mirror.MaterialAfter.Name);
        var customGlass = Assert.IsType<AbbeMaterial>(mirror.MaterialAfter);
        Assert.Equal(1.7, customGlass.Nd, precision: 12);
        Assert.Equal(30, customGlass.Vd, precision: 12);
        Assert.Equal(4, mirror.CoordinateSystem.Origin.X, precision: 10);
        Assert.Equal(0, mirror.CoordinateSystem.Origin.Y, precision: 10);
        Assert.Equal(2, mirror.CoordinateSystem.Origin.Z, precision: 10);
        Assert.Equal(90, mirror.CoordinateSystem.RotationYDegrees, precision: 10);

        var image = optic.SurfaceGroup.Items[3];
        Assert.Equal(6, image.CoordinateSystem.Origin.X, precision: 10);
        Assert.Equal(2, image.CoordinateSystem.Origin.Z, precision: 10);
    }

    [Fact]
    public void ZemaxRealImageHeightFieldsImportAndRoundTrip()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 3 0 2 1 0 0 0
            XFLN 0 2.5
            YFLN 0 4.25
            FWGN 1 0.5
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ INFINITY
            SURF 1
              CURV 0.02
              DISZ 5
              GLAS N-BK7
              STOP
              DIAM 5
            SURF 2
              CURV -0.02
              DISZ 25
              GLAS AIR
            SURF 3
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");
        var finiteZeroDistance = OpticalFormatCatalog.Import(
            source.Replace("DISZ INFINITY", "DISZ 0", StringComparison.Ordinal),
            ".zmx");

        Assert.Equal(FieldDefinitionKind.RealImageHeight, optic.FieldDefinition);
        Assert.Equal(FieldDefinitionKind.RealImageHeight, restored.FieldDefinition);
        Assert.True(double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness));
        Assert.True(double.IsPositiveInfinity(restored.SurfaceGroup.Items[0].Thickness));
        Assert.Equal(0, optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z, precision: 12);
        Assert.Equal(30, optic.SurfaceGroup.TotalTrack, precision: 12);
        Assert.Equal(new[] { 0.0, 2.5 }, optic.Fields.Select(field => field.X));
        Assert.Equal(new[] { 0.0, 4.25 }, optic.Fields.Select(field => field.Y));
        Assert.Contains("FTYP 3", exported, StringComparison.Ordinal);
        Assert.Contains("DISZ INFINITY", exported, StringComparison.Ordinal);

        Assert.Equal(0, finiteZeroDistance.SurfaceGroup.Items[0].Thickness, precision: 12);
        Assert.False(ObjectConjugate.IsInfinite(finiteZeroDistance.SurfaceGroup.Items[0]));
        finiteZeroDistance.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        var finiteZeroDistanceRay = Assert.Single(
            finiteZeroDistance.SequentialRayTracer.RayGenerator
                .GenerateGeneric(0, 1, 0, 0, 0.5875618).Rays);
        Assert.Equal(
            FieldCoordinates.MaximumRadius(finiteZeroDistance.Fields),
            finiteZeroDistanceRay.Origin.Y,
            precision: 12);
        Assert.Equal(0, finiteZeroDistanceRay.Origin.Z, precision: 12);

        var normalized = FieldCoordinates.Normalize(optic.Fields, 2.5, 4.25);
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;
        var final = optic.TraceGeneric(normalized.X, normalized.Y, 0, 0, wavelength).RayHistories.Single()[^1];
        var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);
        Assert.Equal(2.5, local.X, precision: 8);
        Assert.Equal(4.25, local.Y, precision: 8);
    }

    [Fact]
    public void ZemaxFieldsPreserveDeclaredOrder()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 3 0 5 1 0 0 0
            XFLN 0 0 0 0 0
            YFLN 0 4.5 3.375 2.25 1.125
            FWGN 1 1 1 1 1
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ INFINITY
            SURF 1
              CURV 0
              DISZ 10
              STOP
              DIAM 5
            SURF 2
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(
            new[] { 0.0, 4.5, 3.375, 2.25, 1.125 },
            optic.Fields.Select(field => field.Y));
        Assert.Equal(
            new[] { "On axis", "Field 2", "Field 3", "Field 4", "Field 5" },
            optic.Fields.Select(field => field.Label));
    }

    [Fact]
    public void ZemaxDuplicateCoordinateFieldsPreserveDeclaredIndices()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 3 1 0 0 0
            XFLN 0 2 2
            YFLN 0 5 5
            FWGN 1 0.5 0.25
            FCOM 2 "Duplicate coordinates, first user field"
            FCOM 3 "Duplicate coordinates referenced by merit"
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ INFINITY
            SURF 1
              CURV 0
              DISZ 10
              STOP
              DIAM 5
            SURF 2
              CURV 0
              DISZ 0
            EFFL 0 1 3 0 0 0 50 0.1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(3, optic.Fields.Count);
        Assert.Equal((2.0, 5.0), (optic.Fields[1].X, optic.Fields[1].Y));
        Assert.Equal((2.0, 5.0), (optic.Fields[2].X, optic.Fields[2].Y));
        Assert.Equal(0.5, optic.Fields[1].Weight, precision: 12);
        Assert.Equal(0.25, optic.Fields[2].Weight, precision: 12);
        Assert.Equal("Duplicate coordinates, first user field", optic.Fields[1].Label);
        Assert.Equal("Duplicate coordinates referenced by merit", optic.Fields[2].Label);

        var operand = Assert.Single(optic.MeritFunctionOperands, item => item.Type == "EFFL");
        Assert.Equal(3, operand.Field);
    }

    [Fact]
    public void ZemaxMarginalRayHeightSolveFocusesParaxialMarginalRay()
    {
        const string source = """
            MODE SEQ
            FNUM 2.7 0
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 1000
            SURF 1
              CURV 0.02
              DISZ 5
              GLAS N-BK7
              STOP
              DIAM 5
            SURF 2
              CURV -0.02
              DISZ 40
              GLAS AIR
              MAZH 0 0
            SURF 3
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var wavelength = Assert.Single(optic.Wavelengths).Micrometers;
        var marginal = optic.Paraxial.MarginalRay(wavelength);

        Assert.True(double.IsFinite(optic.SurfaceGroup.Items[2].Thickness));
        Assert.NotEqual(40, optic.SurfaceGroup.Items[2].Thickness);
        Assert.Equal(0, marginal.Heights[3][0], precision: 10);
    }

    [Fact]
    public void ZemaxMirrMetadataTracesForwardAndMultiConfigurationsArePreserved()
    {
        const string source = """
            MODE SEQ
            NAME Multi configuration import
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 200
              DIAM 20
              MIRR 2 0
            SURF 1
              TYPE EVENASPH
              CURV 0.02
              DISZ 15
              GLAS N-BK7
              DIAM 8
              PARM 1 0
              STOP
              MIRR 2 0
            SURF 2
              CURV -0.02
              DISZ 30
              GLAS AIR
              DIAM 8
              MIRR 2 0
            SURF 3
              CURV 0
              DISZ 0
              DIAM 12
              MIRR 2 0
            MNUM 3 2
            THIC 0 1 100 0 0 0 1 1 1 0 0 "" 0
            THIC 0 2 150 0 0 0 1 1 1 0 0 "" 0
            THIC 0 3 200 0 0 0 1 1 1 0 0 "" 0
            THIC 1 1 5 0 0 0 1 1 1 0 0 "" 0
            THIC 1 2 10 0 0 0 1 1 1 0 0 "" 0
            THIC 1 3 15 0 0 0 1 1 1 0 0 "" 0
            PRAM 1 1 0.000001 0 1 0 1 1 1 0 0 "" 0
            PRAM 1 2 0.000002 0 1 0 1 1 1 0 0 "" 0
            PRAM 1 3 0.000003 0 1 0 1 1 1 0 0 "" 0
            """;

        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);

        Assert.Equal(3, imported.Configurations.Count);
        Assert.Equal(0, imported.ActiveConfigurationIndex);
        Assert.Same(imported.Configurations[0], imported.ActiveOptic);
        Assert.Equal(new[] { 100.0, 150.0, 200.0 }, imported.Configurations
            .Select(configuration => configuration.SurfaceGroup.Items[0].Thickness));
        Assert.Equal(new[] { 5.0, 10.0, 15.0 }, imported.Configurations
            .Select(configuration => configuration.SurfaceGroup.Items[1].Thickness));
        Assert.Equal(new[] { 0.000001, 0.000002, 0.000003 }, imported.Configurations
            .Select(configuration => Assert.IsType<EvenAsphereGeometry>(
                configuration.SurfaceGroup.Items[1].Geometry).Coefficients[0]));
        Assert.All(imported.ActiveOptic.SurfaceGroup.Items, surface => Assert.False(surface.IsReflective));

        var scene = new Layout2DBuilder(imported.ActiveOptic).Build(options: new LayoutBuildOptions(
            FirstSurface: 1,
            LastSurface: 3,
            RayCount: 3));
        Assert.NotEmpty(scene.Rays);
        Assert.All(scene.Rays, ray => Assert.True(ray.Points.Count >= 2));

        var connector = new OptilandConnector(Optic.CreateBlank());
        connector.ApplyLoadedDocument(new LoadedOpticalDocument(
            imported.ActiveOptic,
            imported.Configurations,
            imported.ActiveConfigurationIndex), "multi.zmx");
        var rows = connector.GetMultiConfigurationRows();
        Assert.Equal(3, rows.Count);
        Assert.Equal(0, Assert.Single(rows, row => row.Active).Index);
        Assert.Equal(new[] { "配置 1", "配置 2", "配置 3" }, rows.Select(row => row.Name));
    }

    [Fact]
    public async Task WorkbenchFilePathImportDetectsBomlessUtf16Zemax()
    {
        var source = File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx"));
        var path = Path.Combine(Path.GetTempPath(), $"optiland-zemax-{Guid.NewGuid():N}.zmx");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.Unicode.GetBytes(source));

            var optic = await OptilandConnector.ReadOpticAsync(path);

            Assert.Equal(5, optic.SurfaceGroup.Items.Count);
            Assert.Equal(12.5, optic.Aperture.Value, precision: 12);
            Assert.Equal(587.5618, Assert.Single(optic.Wavelengths, item => item.IsPrimary).Nanometers, precision: 10);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LensVa3StyleImportPreservesFieldsConfigurationsCurvaturesAndMeritRows()
    {
        const string source = """
            VERS 241210 1439 20120530 20120530
            MODE SEQ
            NAME
            FNUM 2.7 0
            GCAT CDGM
            FTYP 3 0 5 3 0 0 0 5
            XFLN 0 0 0 0 0
            YFLN 0 4.5 3.375 2.25 1.125
            FWGN 1 1 1 1 1
            FCOM 1 轴上视场
            FCOM 2 最大Y视场
            WAVM 1 0.42 1
            WAVM 2 0.44 1
            WAVM 3 0.46 1
            WAVM 4 0.48 1
            PWAV 2
            SURF 0
              TYPE STANDARD
              CURV 0
              DISZ 2500
            SURF 1
              TYPE STANDARD
              CURV 0.025
              DISZ 3
              GLAS H-K9L 0 0 1.5 40
              STOP
              DIAM 1000
            SURF 2
              TYPE STANDARD
              CURV 0
              DISZ 0
            EFFL 0 2 0 0 0 0 10.7 1 0 0
            DMFS 0 0 0 0 0 0 0 0 0 0
            BLNK 对比度于185 lp/MM
            MECS 0 1 1 185 0.33571068701972878 0 0 0.058177641733144006 0 0
            MECT 0 1 1 185 0.33571068701972878 0 0 0.058177641733144006 0 0
            MNUM 2 2
            CRVT 1 1 0.02 0 0 0 1 1 1 0 0 "" 0
            CRVT 1 2 0.025 0 0 0 1 1 1 0 0 "" 0
            THIC 0 1 500 0 0 0 1 1 1 0 0 "" 0
            THIC 0 2 2500 0 0 0 1 1 1 0 0 "" 0
            """;
        var zmxPath = Path.Combine(Path.GetTempPath(), $"lens-va3-{Guid.NewGuid():N}.zmx");
        var projectPath = Path.Combine(Path.GetTempPath(), $"lens-va3-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(zmxPath, Encoding.Unicode.GetBytes(source));

            var imported = await OptilandConnector.ReadDocumentAsync(zmxPath);

            Assert.Equal(2, imported.Configurations.Count);
            Assert.Equal(0, imported.ActiveConfigurationIndex);
            Assert.Equal(new[] { 500.0, 2500.0 }, imported.Configurations
                .Select(configuration => configuration.SurfaceGroup.Items[0].Thickness));
            Assert.Equal(new[] { 50.0, 40.0 }, imported.Configurations
                .Select(configuration => Assert.IsType<StandardGeometry>(
                    configuration.SurfaceGroup.Items[1].Geometry).Radius));
            Assert.Equal(5, imported.ActiveOptic.Fields.Count);
            Assert.Equal("轴上视场", imported.ActiveOptic.Fields[0].Label);
            Assert.Equal("最大Y视场", imported.ActiveOptic.Fields[1].Label);
            Assert.Equal(
                new[] { 0.0, 4.5, 3.375, 2.25, 1.125 },
                imported.ActiveOptic.Fields.Select(field => field.Y));
            Assert.Equal(3, imported.ActiveOptic.Wavelengths.Count);
            Assert.Collection(
                imported.ActiveOptic.MeritFunctionOperands,
                operand =>
                {
                    Assert.Equal("EFFL", operand.Type);
                    Assert.Equal(10.7, operand.Target, precision: 12);
                },
                operand => Assert.Equal("DMFS", operand.Type),
                operand => Assert.Equal("BLNK", operand.Type),
                operand =>
                {
                    Assert.Equal("MECS", operand.Type);
                    Assert.Equal(1, operand.Field);
                    Assert.Equal(1, operand.Wavelength);
                    Assert.Equal(185, operand.SpatialFrequency);
                    Assert.Equal(0.33571068701972878, operand.Px, precision: 15);
                    Assert.Equal(0.058177641733144006, operand.Weight, precision: 15);
                },
                operand => Assert.Equal("MECT", operand.Type));

            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(imported.Configurations, imported.ActiveConfigurationIndex),
                projectPath);
            var reopened = await OptilandConnector.ReadDocumentAsync(projectPath);

            Assert.Equal(imported.Configurations.Count, reopened.Configurations.Count);
            Assert.Equal(imported.ActiveConfigurationIndex, reopened.ActiveConfigurationIndex);
            Assert.Equal(imported.ActiveOptic.Fields.Select(field => field.Label),
                reopened.ActiveOptic.Fields.Select(field => field.Label));
            Assert.Equal(imported.ActiveOptic.MeritFunctionOperands.Count,
                reopened.ActiveOptic.MeritFunctionOperands.Count);
        }
        finally
        {
            File.Delete(zmxPath);
            File.Delete(projectPath);
        }
    }

    [Theory]
    [InlineData("EVENASPH", 0.048125)]
    [InlineData("ODDASPHE", 0.009925)]
    public void ZemaxAsphereParametersUseOptilandCoefficientOrders(string surfaceType, double expectedSag)
    {
        var source = $"""
            MODE SEQ
            ENPD 10
            SURF 0
              TYPE {surfaceType}
              CURV 0
              DISZ 0
              PARM 1 0.002
              PARM 2 -0.000003
            SURF 1
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(expectedSag, optic.SurfaceGroup.Items[0].Geometry.Sag(3, 4), precision: 12);
    }

    [Fact]
    public void ZemaxUnitScalesLengthsFieldsCurvaturesAndAsphereCoefficients()
    {
        const string source = """
            MODE SEQ
            UNIT CM
            ENPD 1
            MNUM 2
            FTYP 1 0 1 1 0 0 0
            XFLN 0.2
            YFLN 0.3
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              DISZ 10
              DIAM 1
            SURF 1
              TYPE EVENASPH
              CURV 0
              DISZ 0.4
              DIAM 0.6
              APMN 0.1
              PARM 1 0.02
            SURF 2
              DISZ 0
              DIAM 0.2
            THIC 1 2 0.8
            APMX 1 2 0.7
            APMN 1 2 0.2
            """;

        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        var active = imported.ActiveOptic;

        Assert.Equal(10, active.Aperture.Value, precision: 12);
        Assert.Equal(2, active.Fields[0].X, precision: 12);
        Assert.Equal(3, active.Fields[0].Y, precision: 12);
        Assert.Equal(-100, active.SurfaceGroup.Items[0].CoordinateSystem.Origin.Z, precision: 12);

        var surface = active.SurfaceGroup.Items[1];
        Assert.Equal(4, surface.Thickness, precision: 12);
        Assert.Equal(6, surface.SemiDiameter, precision: 12);
        var even = Assert.IsType<EvenAsphereGeometry>(surface.Geometry);
        Assert.Equal(0.002, even.Coefficients[0], precision: 15);
        Assert.Equal(0.2, even.Sag(10, 0), precision: 12);
        var aperture = Assert.IsType<AnnularAperture>(surface.PhysicalAperture);
        Assert.Equal(6, aperture.OuterRadius, precision: 12);
        Assert.Equal(1, aperture.InnerRadius, precision: 12);

        var secondConfigurationSurface = imported.Configurations[1].SurfaceGroup.Items[1];
        Assert.Equal(8, secondConfigurationSurface.Thickness, precision: 12);
        Assert.Equal(7, secondConfigurationSurface.SemiDiameter, precision: 12);
        var configuredAperture = Assert.IsType<AnnularAperture>(secondConfigurationSurface.PhysicalAperture);
        Assert.Equal(7, configuredAperture.OuterRadius, precision: 12);
        Assert.Equal(2, configuredAperture.InnerRadius, precision: 12);
    }

    [Fact]
    public void ZemaxImportPreservesUnsupportedSurfaceTypeAsOpaqueReadOnlyGeometry()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 20
            SURF 1
              TYPE BINARY_2
              CURV 0.01
              DISZ 5
              CONI -1
              PARM 1 2.5
            SURF 2
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        var opaque = Assert.IsType<OpaqueGeometryPayload>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.Equal("Zemax TYPE BINARY_2", opaque.OriginalType);
        Assert.Equal("BINARY_2", opaque.Payload.Text["zemax.type"]);
        Assert.Equal(100, opaque.Payload.Numbers["radius"], precision: 12);
        Assert.Equal(-1, opaque.Payload.Numbers["conic"], precision: 12);
        Assert.Equal(2.5, opaque.Payload.Numbers["parm1"], precision: 12);

        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var restoredOpaque = Assert.IsType<OpaqueGeometryPayload>(restored.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(opaque.OriginalType, restoredOpaque.OriginalType);
        Assert.Equal(2.5, restoredOpaque.Payload.Numbers["parm1"], precision: 12);
    }

    [Theory]
    [InlineData("MODE NSC\nENPD 10\nSURF 0\nSURF 1", "MODE SEQ")]
    public void ZemaxImportRejectsUnsupportedPhysicalContracts(string source, string expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(() => OpticalFormatCatalog.Import(source, ".zmx"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZemaxImportPreservesAfocalImageSpaceFlag()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 0 0 1 1 0 0 1
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              DISZ 100
            SURF 1
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var snapshot = optic.ToSnapshot();
        var restored = Optic.FromSnapshot(snapshot);
        var exported = OpticalFormatCatalog.Export(restored, ".zmx");

        Assert.True(optic.ImageSpaceAfocal);
        Assert.True(snapshot.ImageSpaceAfocal);
        Assert.True(restored.ImageSpaceAfocal);
        Assert.Contains("FTYP 0 0 1 1 0 0 1", exported, StringComparison.Ordinal);
        Assert.True(OpticalFormatCatalog.Import(exported, ".zmx").ImageSpaceAfocal);
    }

    [Fact]
    public void ZemaxImportPreservesSignedThicknessAndFollowingSurfaceCoordinate()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 0
            SURF 1
              DISZ -2
            SURF 2
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(-2, optic.SurfaceGroup.Items[1].Thickness, precision: 12);
        Assert.Equal(-2, optic.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z, precision: 12);
    }

    private static (double X, double Y, double Weight, double VignetteX, double VignetteY) FieldValues(
        Core.Domain.FieldPoint field) =>
        (field.X, field.Y, field.Weight, field.VignetteFactorX, field.VignetteFactorY);

    private static double ReadPythonNumber(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }

        return element.GetString() switch
        {
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var value => throw new InvalidDataException($"Unexpected Python numeric token '{value}'.")
        };
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录。");
    }
}
