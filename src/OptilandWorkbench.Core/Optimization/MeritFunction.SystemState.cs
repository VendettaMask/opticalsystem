using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    public static bool IsSystemStateOperand(string type) => CanonicalType(type) is "CONF" or "PRIM" or "SVIG" or "CVIG" or "IMSF" or "FDMO" or "FDRE";

    private sealed class MeritSystemState(MeritConfigurationContext configurations)
    {
        private Optic source = configurations.ActiveOptic;
        private bool _unverifiedSwitchState;
        private Optic? _full;
        private Optic? _current;
        private int _imageSurface;
        private bool _refocus;
        private Dictionary<int, FieldPoint> _originalFields = new();
        public Optic Current => _current ?? source;

        public void Apply(MeritOperandDefinition definition)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (CanonicalType(definition.Type) == "CONF")
            {
                var number = ZemaxIntegerParameter(definition, 0, 1);
                if (number < 1 || number > configurations.Configurations.Count)
                    throw new ArgumentOutOfRangeException(nameof(definition), "CONF 必须选择有效的正配置编号（从 1 开始）。");
                if (_unverifiedSwitchState)
                    throw new NotSupportedException("PRIM/SVIG/CVIG/IMSF 后再执行 CONF 的状态组合尚未核实，不能切换配置。");
                // Selecting even the same configuration discards all temporary FDMO changes.
                source = configurations.Configurations[number - 1];
                _full = null; _current = null; _originalFields.Clear();
                ActiveEvaluationBatch.Value = new EvaluationBatch();
                return;
            }
            var candidate = (_full ?? source).CreateMeritEvaluationCopy();
            var image = _imageSurface; var refocus = _refocus;
            var originals = new Dictionary<int, FieldPoint>(_originalFields);
            switch (CanonicalType(definition.Type))
            {
                case "PRIM":
                    var wave = ZemaxIntegerParameter(definition, 0, definition.Wavelength);
                    if (wave < 1 || wave > candidate.Wavelengths.Count)
                        throw new ArgumentOutOfRangeException(nameof(definition), "PRIM 必须选择有效的正波长编号。");
                    for (var i = 0; i < candidate.Wavelengths.Count; i++) candidate.Wavelengths[i].IsPrimary = i == wave - 1;
                    break;
                case "SVIG":
                    var precision = ZemaxIntegerParameter(definition, 0, 0);
                    var vignetteSystem = image == 0 ? candidate : IntermediateImageSystem.Create(candidate, image, refocus);
                    VignettingSolver.Apply(candidate, VignettingSolver.Calculate(vignetteSystem, precision));
                    break;
                case "CVIG":
                    foreach (var item in candidate.Fields) PupilVignetting.Clear(item);
                    break;
                case "FDMO":
                case "FDRE":
                    var fieldIndex = ZemaxIntegerParameter(definition, 0, definition.Field) - 1;
                    if (fieldIndex < 0 || fieldIndex >= candidate.Fields.Count)
                        throw new ArgumentOutOfRangeException(nameof(definition), "FDMO/FDRE 必须选择有效的正视场编号。");
                    if (CanonicalType(definition.Type) == "FDRE")
                    {
                        if (originals.Remove(fieldIndex, out var original)) candidate.Fields[fieldIndex] = original.Clone();
                        break;
                    }
                    var hx = ZemaxDataParameter(definition, 0, definition.Hx);
                    var hy = ZemaxDataParameter(definition, 1, definition.Hy);
                    if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 1 || Math.Abs(hy) > 1)
                        throw new ArgumentOutOfRangeException(nameof(definition), "FDMO Hx/Hy 必须为 [-1,1] 内的有限归一化视场坐标。");
                    var scale = FieldCoordinates.MaximumRadius(source.Fields);
                    if (!double.IsFinite(scale)) throw new InvalidOperationException("FDMO 的初始视场归一化半径不是有限数值。");
                    var field = candidate.Fields[fieldIndex];
                    originals.TryAdd(fieldIndex, field.Clone());
                    field.X = hx * scale; field.Y = hy * scale;
                    field.VignetteDecenterX = ZemaxDataParameter(definition, 2, 0);
                    field.VignetteDecenterY = ZemaxDataParameter(definition, 3, 0);
                    field.VignetteFactorX = ZemaxDataParameter(definition, 4, 0);
                    field.VignetteFactorY = ZemaxDataParameter(definition, 5, 0);
                    break;
                case "IMSF":
                    image = ZemaxIntegerParameter(definition, 0, definition.Surface);
                    var flag = ZemaxIntegerParameter(definition, 1, 0);
                    if (flag is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(definition), "IMSF Refocus 必须为 0 或 1。");
                    if (image < 0) throw new ArgumentOutOfRangeException(nameof(definition), "IMSF Surface 不能为负；0 恢复原像面。");
                    refocus = flag == 1;
                    break;
            }
            Publish(candidate, image, refocus, originals);
            if (CanonicalType(definition.Type) is "PRIM" or "SVIG" or "CVIG" or "IMSF") _unverifiedSwitchState = true;
        }

        private void Publish(Optic candidate, int image, bool refocus, Dictionary<int, FieldPoint> originals)
        {
            // Complete validation and image construction before publishing any change to this batch.
            var current = image == 0 ? candidate : IntermediateImageSystem.Create(candidate, image, refocus);
            _full = candidate; _current = current; _imageSurface = image; _refocus = refocus;
            _originalFields = originals;
            ActiveEvaluationBatch.Value = new EvaluationBatch();
        }
    }
}
