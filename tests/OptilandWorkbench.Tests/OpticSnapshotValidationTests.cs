using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class OpticSnapshotValidationTests
{
    [Fact]
    public void ValidatorRejectsIllegalSystemStateAndReferences()
    {
        var valid = Optic.CreateDemo().ToSnapshot();
        var cases = new (string ExpectedPath, OpticSnapshot Snapshot)[]
        {
            ("$.schemaVersion", valid with { SchemaVersion = 5 }),
            ("$.aperture.value", valid with
            {
                Aperture = valid.Aperture! with { Value = double.PositiveInfinity }
            }),
            ("$.environment.temperatureCelsius", valid with
            {
                Environment = valid.Environment! with
                {
                    TemperatureCelsius = double.NaN
                }
            }),
            ("$.fields", valid with { Fields = new List<FieldPointSnapshot>() }),
            ("$.fields[0].xAngleDegrees", valid with
            {
                Fields = ReplaceAt(
                    valid.Fields,
                    0,
                    valid.Fields[0] with { XAngleDegrees = double.NaN })
            }),
            ("$.wavelengths[0].nanometers", valid with
            {
                Wavelengths = ReplaceAt(
                    valid.Wavelengths,
                    0,
                    valid.Wavelengths[0] with { Nanometers = double.PositiveInfinity })
            }),
            ("$.wavelengths", valid with
            {
                Wavelengths = valid.Wavelengths
                    .Select(wavelength => wavelength with { IsPrimary = false })
                    .ToList()
            }),
            ("$.surfaces", valid with { Surfaces = new List<SurfaceSnapshot>() }),
            ("$.surfaces[1].number", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with { Number = 0 })
            }),
            ("$.surfaces[1].thickness", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with { Thickness = double.NegativeInfinity })
            }),
            ("$.surfaces[1].semiDiameter", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with { SemiDiameter = double.NaN })
            }),
            ("$.surfaces[1].coordinateSystem.originZ", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with
                    {
                        CoordinateSystem = valid.Surfaces[1].CoordinateSystem! with
                        {
                            OriginZ = double.PositiveInfinity
                        }
                    })
            }),
            ("$.surfaces[2].components.geometryKind", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    2,
                    valid.Surfaces[2] with
                    {
                        Components = valid.Surfaces[2].Components! with
                        {
                            GeometryKind = "plane"
                        }
                    })
            }),
            ("$.surfaces[2].radius", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    2,
                    valid.Surfaces[2] with
                    {
                        Components = valid.Surfaces[2].Components! with
                        {
                            Geometry = valid.Surfaces[2].Components!.Geometry! with
                            {
                                Numbers = new Dictionary<string, double>(
                                    valid.Surfaces[2].Components!.Geometry!.Numbers)
                                {
                                    ["radius"] = valid.Surfaces[2].Radius + 10
                                }
                            }
                        }
                    })
            }),
            ("$.surfaces[2].material", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    2,
                    valid.Surfaces[2] with
                    {
                        Material = "Air",
                        IsReflective = false,
                        Components = valid.Surfaces[2].Components! with
                        {
                            InteractionKind = "refractive",
                            Interaction = ComponentSnapshot.Empty("refractive"),
                            MaterialAfter = "N-BK7",
                            MaterialAfterComponent = new ComponentSnapshot(
                                "catalog",
                                new Dictionary<string, double>(),
                                new Dictionary<string, string> { ["name"] = "N-BK7" })
                        }
                    })
            }),
            ("$.surfaces[2].isReflective", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    2,
                    valid.Surfaces[2] with
                    {
                        IsReflective = false,
                        Components = valid.Surfaces[2].Components! with
                        {
                            InteractionKind = "reflective",
                            Interaction = ComponentSnapshot.Empty("reflective")
                        }
                    })
            }),
            ("$.radiusPickups[0].sourceSurface", valid with
            {
                RadiusPickups = new List<RadiusPickupSnapshot>
                {
                    new(10_000, 1, 1, 0)
                }
            }),
            ("$.solveSettings.desiredBackFocus", valid with
            {
                SolveSettings = new SolveSettingsSnapshot(double.NaN, true)
            }),
            ("$.meritOperands[0].surface", valid with
            {
                MeritOperands = new List<MeritOperandSnapshot>
                {
                    new(
                        Enabled: true,
                        Type: "RADI",
                        Surface: 10_000,
                        Field: 0,
                        Wavelength: 0,
                        Hx: 0,
                        Hy: 0,
                        Px: 0,
                        Py: 0,
                        Target: 0,
                        Weight: 1,
                        Comment: string.Empty)
                }
            }),
            ("$.surfaces[1].components.interactionKind", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with
                    {
                        Components = valid.Surfaces[1].Components! with
                        {
                            InteractionKind = "unknown_interaction",
                            Interaction = ComponentSnapshot.Empty("unknown_interaction")
                        }
                    })
            }),
            ("$.surfaces[1].components.coating.numbers['thickness_0']", valid with
            {
                Surfaces = ReplaceAt(
                    valid.Surfaces,
                    1,
                    valid.Surfaces[1] with
                    {
                        Components = valid.Surfaces[1].Components! with
                        {
                            CoatingKind = "thin_film_stack",
                            Coating = new ComponentSnapshot(
                                "thin_film_stack",
                                new Dictionary<string, double> { ["count"] = 100_000 },
                                new Dictionary<string, string>())
                        }
                    })
            })
        };

        foreach (var (expectedPath, snapshot) in cases)
        {
            var exception = Assert.Throws<InvalidDataException>(
                () => OpticSnapshotValidator.Validate(snapshot));
            Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MeritOperandSnapshotPreservesZemaxConstraintAndContrastSettings()
    {
        var optic = Optic.CreateDemo();
        optic.MeritFunctionOperands.Clear();
        optic.MeritFunctionOperands.Add(new MeritOperandDefinition
        {
            Type = "MECS",
            Surface = 0,
            Field = 1,
            Wavelength = 1,
            Target = 0.75,
            Weight = -0.032320912073968894,
            SpatialFrequency = 185,
            IgnoreLateralColor = true,
            PolychromaticReference = true,
            Comment = "constraint"
        });

        var snapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(snapshot);
        var restored = Optic.FromSnapshot(snapshot);
        var operand = Assert.Single(restored.MeritFunctionOperands);

        Assert.Equal(OpticSnapshotValidator.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(-0.032320912073968894, operand.Weight, precision: 15);
        Assert.Equal(185, operand.SpatialFrequency, precision: 12);
        Assert.True(operand.IgnoreLateralColor);
        Assert.True(operand.PolychromaticReference);
    }

    [Fact]
    public void LegacySchemaThreeMeritOperandUsesSchemaFourDefaults()
    {
        var valid = Optic.CreateDemo().ToSnapshot();
        var legacy = valid with
        {
            SchemaVersion = 3,
            MeritOperands = new List<MeritOperandSnapshot>
            {
                new(
                    Enabled: true,
                    Type: "MECS",
                    Surface: 0,
                    Field: 1,
                    Wavelength: 1,
                    Hx: 0,
                    Hy: 0,
                    Px: 0,
                    Py: 0,
                    Target: 0,
                    Weight: -1,
                    Comment: string.Empty)
            }
        };

        var restored = Optic.FromSnapshot(legacy);
        var operand = Assert.Single(restored.MeritFunctionOperands);

        Assert.Equal(30, operand.SpatialFrequency, precision: 12);
        Assert.False(operand.IgnoreLateralColor);
        Assert.False(operand.PolychromaticReference);
        Assert.Equal(-1, operand.Weight, precision: 12);
    }

    [Fact]
    public void ApplySnapshotDoesNotPartiallyUpdateWhenComponentConstructionFails()
    {
        var optic = Optic.CreateDemo();
        var original = optic.ToSnapshot();
        var phaseNumbers = new Dictionary<string, double>
        {
            ["xCount"] = 4,
            ["yCount"] = 4,
            ["x0"] = 0,
            ["x1"] = 0,
            ["x2"] = 2,
            ["x3"] = 3,
            ["y0"] = 0,
            ["y1"] = 1,
            ["y2"] = 2,
            ["y3"] = 3
        };
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                phaseNumbers[$"g{y}_{x}"] = x + y;
            }
        }

        var invalidGrid = new ComponentSnapshot(
            "grid",
            phaseNumbers,
            new Dictionary<string, string>());
        var phaseInteraction = new ComponentSnapshot(
            "phase",
            new Dictionary<string, double> { ["isReflective"] = 0 },
            new Dictionary<string, string>(),
            new Dictionary<string, ComponentSnapshot>
            {
                ["profile"] = invalidGrid
            });
        var changedSurface = original.Surfaces[1] with
        {
            Components = original.Surfaces[1].Components! with
            {
                InteractionKind = "phase",
                Interaction = phaseInteraction
            }
        };
        var invalid = original with
        {
            Name = "Partially updated attacker state",
            Fields = new List<FieldPointSnapshot>
            {
                new("Attacker field", 20, 30, 1)
            },
            Surfaces = ReplaceAt(original.Surfaces, 1, changedSurface)
        };

        Assert.Throws<InvalidDataException>(() => optic.ApplySnapshot(invalid));

        Assert.Equal(SerializeSnapshot(original), SerializeSnapshot(optic.ToSnapshot()));
    }

    [Fact]
    public async Task StarOptSaveRejectsIllegalInMemoryStateBeforeCreatingAFile()
    {
        var optic = Optic.CreateDemo();
        optic.SurfaceGroup.Items[1].Thickness = double.NaN;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => StarOptProjectStore.SaveAsync(
                    new StarOptProjectDocument(new[] { optic }, 0),
                    path));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task StarOptSaveRejectsTooManyConfigurationsBeforeCreatingAFile()
    {
        var optic = Optic.CreateDemo();
        var configurations = Enumerable
            .Repeat(optic, StarOptProjectStore.MaximumConfigurationCount + 1)
            .ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => StarOptProjectStore.SaveAsync(
                    new StarOptProjectDocument(configurations, 0),
                    path));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task StarOptLoadRejectsChecksumValidPayloadWithNamedNaN()
    {
        var valid = Optic.CreateDemo().ToSnapshot();
        var malicious = valid with
        {
            Wavelengths = ReplaceAt(
                valid.Wavelengths,
                0,
                valid.Wavelengths[0] with { Nanometers = double.NaN })
        };
        var bytes = CreateStarOptContainer(malicious);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => StarOptProjectStore.LoadAsync(path));
            Assert.Contains(
                "$.wavelengths[0].nanometers",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void LegacySnapshotMigrationRemovesUnsafeSentinelsAndDanglingOperands()
    {
        var valid = Optic.CreateDemo().ToSnapshot();
        var legacy = valid with
        {
            SchemaVersion = 2,
            Surfaces = ReplaceAt(
                valid.Surfaces,
                0,
                valid.Surfaces[0] with
                {
                    SemiDiameter = double.PositiveInfinity,
                    CoordinateSystem = valid.Surfaces[0].CoordinateSystem! with
                    {
                        OriginZ = double.NegativeInfinity
                    }
                }),
            MeritOperands = new List<MeritOperandSnapshot>
            {
                new(
                    Enabled: true,
                    Type: "RADI",
                    Surface: 10_000,
                    Field: 0,
                    Wavelength: 0,
                    Hx: 0,
                    Hy: 0,
                    Px: 0,
                    Py: 0,
                    Target: 0,
                    Weight: 1,
                    Comment: string.Empty)
            }
        };

        var restored = Optic.FromSnapshot(legacy);
        var upgraded = restored.ToSnapshot();

        Assert.Equal(OpticSnapshotValidator.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.Equal(0, upgraded.Surfaces[0].CoordinateSystem!.OriginZ);
        Assert.Equal(10, upgraded.Surfaces[0].SemiDiameter);
        Assert.Empty(upgraded.MeritOperands!);
    }

    private static List<T> ReplaceAt<T>(IReadOnlyList<T> source, int index, T replacement)
    {
        var copy = source.ToList();
        copy[index] = replacement;
        return copy;
    }

    private static string SerializeSnapshot(OpticSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    private static byte[] CreateStarOptContainer(OpticSnapshot snapshot)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                FormatVersion = StarOptProjectStore.ProjectFormatVersion,
                Application = "Optical System Design",
                ActiveConfigurationIndex = 0,
                Configurations = new[] { snapshot }
            },
            new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            });
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var brotli = new BrotliStream(
                       output,
                       CompressionLevel.Optimal,
                       leaveOpen: true))
            {
                brotli.Write(payload);
            }

            compressed = output.ToArray();
        }

        const int headerLength = 52;
        var bytes = new byte[headerLength + compressed.Length];
        "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(8, 2),
            StarOptProjectStore.ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressed.Length);
        SHA256.HashData(payload).CopyTo(bytes, 20);
        compressed.CopyTo(bytes, headerLength);
        return bytes;
    }
}
