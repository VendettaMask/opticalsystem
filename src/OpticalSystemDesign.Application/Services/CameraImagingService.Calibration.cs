using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const int MaximumCalibrationBytes = 32 * 1024 * 1024;

    public async Task<CameraSensorProfile> CalibrateSensorAsync(string path, CameraSensorProfile baseline, CancellationToken cancellationToken = default)
    {
        baseline = ValidateProfile(baseline);
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumCalibrationBytes, "Camera calibration RAW frames", cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(cancellationToken);
            var payload = bytes.AsMemory();
            if (payload.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) payload = payload[3..];
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            CheckDuplicateProperties(document.RootElement);
            var capture = JsonSerializer.Deserialize<CameraCalibrationCapture>(payload.Span, ExperimentJsonOptions)
                ?? throw new InvalidDataException("标定文件为空。");
            capture = FreezeCapture(capture);
            var estimate = Estimate(capture);
            var record = new CameraCalibrationRecord(capture, Origin(path, bytes), estimate.BlackLevelDn,
                estimate.ReadNoiseElectrons, estimate.DarkElectronsPerSecond, estimate.DsnuElectrons, estimate.PrnuPercent, CameraSensorCalibration.Method);
            var sensor = baseline.Sensor with
            {
                BlackLevel = (int)Math.Round(estimate.BlackLevelDn, MidpointRounding.AwayFromZero),
                ReadNoiseElectrons = estimate.ReadNoiseElectrons, DarkElectronsPerSecond = estimate.DarkElectronsPerSecond,
                DsnuElectrons = estimate.DsnuElectrons, PrnuPercent = estimate.PrnuPercent,
                GainDnPerElectron = capture.GainDnPerElectron, BitDepth = capture.BitDepth, Pattern = capture.Pattern
            };
            cancellationToken.ThrowIfCancellationRequested();
            return ValidateProfile(new(SensorFormat, 1, capture.Name, capture.Source
                + (capture.Synthetic ? " [SYNTHETIC calibration frames]" : " [User-supplied RAW calibration; measurement provenance not independently verified]"),
                sensor, baseline.SpectralResponse, Calibration: record));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static CameraCalibrationCapture FreezeCapture(CameraCalibrationCapture capture)
    {
        if (capture.Format != CameraCalibrationCapture.FormatIdentifier || capture.SchemaVersion != 1
            || !Enum.IsDefined(capture.Pattern) || !double.IsFinite(capture.TemperatureCelsius) || capture.TemperatureCelsius is < -100 or > 150)
            throw new InvalidDataException("不支持的标定格式、版本、温度或 CFA 排列。");
        Label(capture.Name, 256, "Calibration name", false); Label(capture.Source, 2048, "Calibration source", false);
        Label(capture.Device, 256, "Sensor device", false); Label(capture.Conditions, 2048, "Capture conditions", false);
        CameraSensorPipeline.ValidateDimensions(capture.Width, capture.Height);
        if (capture.DarkFrames is null || capture.FlatFrames is null || capture.DarkFrames.Count > 128 || capture.FlatFrames.Count > 128
            || (long)(capture.DarkFrames.Count + capture.FlatFrames.Count) * capture.Width * capture.Height > CameraSensorCalibration.MaximumFrameSamples)
            throw new InvalidDataException("标定帧数组为空或超过资源限制。");
        IReadOnlyList<CameraCalibrationFrame> Freeze(IReadOnlyList<CameraCalibrationFrame> frames) => Array.AsReadOnly(frames.Select(f =>
            f is null || f.RawDn is null || f.RawDn.Count != capture.Width * capture.Height ? throw new InvalidDataException("标定 RAW 尺寸不匹配。")
            : f with { RawDn = Array.AsReadOnly(f.RawDn.ToArray()) }).ToArray());
        return capture with { DarkFrames = Freeze(capture.DarkFrames), FlatFrames = Freeze(capture.FlatFrames) };
    }

    private static SensorCalibrationEstimate Estimate(CameraCalibrationCapture capture) => CameraSensorCalibration.Estimate(
        capture.Width, capture.Height, capture.BitDepth, capture.GainDnPerElectron,
        capture.DarkFrames.Select(f => new SensorCalibrationFrame(f.ExposureMilliseconds, f.RawDn)).ToArray(),
        capture.FlatFrames.Select(f => new SensorCalibrationFrame(f.ExposureMilliseconds, f.RawDn)).ToArray());

    private static CameraCalibrationRecord FreezeCalibration(CameraCalibrationRecord record)
    {
        ValidateOrigin(record.Origin ?? throw new InvalidDataException("标定缺少原始文件指纹。"));
        var capture = FreezeCapture(record.Capture ?? throw new InvalidDataException("标定缺少原始帧。"));
        var estimate = Estimate(capture);
        if (record.Method != CameraSensorCalibration.Method || record.BlackLevelDn != estimate.BlackLevelDn
            || record.ReadNoiseElectrons != estimate.ReadNoiseElectrons || record.DarkElectronsPerSecond != estimate.DarkElectronsPerSecond
            || record.DsnuElectrons != estimate.DsnuElectrons || record.PrnuPercent != estimate.PrnuPercent)
            throw new InvalidDataException("标定摘要与原始帧复算不一致。");
        return record with { Capture = capture };
    }
}
