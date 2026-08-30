using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class FormatFuzzTests
{
    private const int CasesPerSeed = 32;

    [Theory]
    [InlineData(17)]
    [InlineData(20260829)]
    public void BinaryReadersHandleBoundedRandomInputsWithControlledFailures(int seed)
    {
        var random = new Random(seed);
        for (var index = 0; index < CasesPerSeed; index++)
        {
            var data = CreateRandomBytes(random, index);

            AssertControlledFailure(() => NonSequentialMeshCodec.Decode(data));
            AssertControlledFailure(() =>
            {
                using var stream = new MemoryStream(data, writable: false);
                using var reader = new NonSequentialRayDatabaseReader(stream);
            });
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(20260830)]
    public async Task StarOptReaderHandlesBoundedRandomInputsWithControlledFailures(int seed)
    {
        var random = new Random(seed);
        var path = Path.Combine(Path.GetTempPath(), $"staropt-fuzz-{Guid.NewGuid():N}.staropt");
        try
        {
            for (var index = 0; index < CasesPerSeed; index++)
            {
                await File.WriteAllBytesAsync(path, CreateRandomBytes(random, index));
                await AssertControlledFailureAsync(() => StarOptProjectStore.LoadAsync(path));
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(20260831)]
    public void ZemaxReaderHandlesBoundedRandomTextWithControlledFailures(int seed)
    {
        var random = new Random(seed);
        for (var index = 0; index < CasesPerSeed; index++)
        {
            var bytes = CreateRandomBytes(random, index);
            var text = Encoding.UTF8.GetString(bytes);
            AssertControlledFailure(() => ZemaxZmxReader.ImportConfigurationSet(text));
        }
    }

    private static byte[] CreateRandomBytes(Random random, int caseIndex)
    {
        var boundaryLengths = new[]
        {
            0, 1, 2, 3, 4, 7, 8, 15, 16, 31, 32, 47, 48, 51, 52, 63,
            64, 127, 128, 255, 256, 511, 512, 1023, 2048, 4096
        };
        var length = caseIndex < boundaryLengths.Length
            ? boundaryLengths[caseIndex]
            : random.Next(0, 4097);
        var data = new byte[length];
        random.NextBytes(data);

        var knownHeaders = new[]
        {
            "STAROPT\x1a"u8.ToArray(),
            "STARMESH"u8.ToArray(),
            "STARRDB\x1a"u8.ToArray()
        };
        if (data.Length >= 8 && (caseIndex & 1) == 0)
        {
            knownHeaders[caseIndex % knownHeaders.Length].CopyTo(data, 0);
        }

        return data;
    }

    private static void AssertControlledFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            AssertControlled(exception);
        }
    }

    private static async Task AssertControlledFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            AssertControlled(exception);
        }
    }

    private static void AssertControlled(Exception exception)
    {
        Assert.True(
            exception is InvalidDataException
                or ArgumentException
                or ArithmeticException
                or EndOfStreamException
                or IOException
                or JsonException
                or NotSupportedException,
            $"Unexpected parser exception {exception.GetType().FullName}: {exception.Message}");
    }
}
