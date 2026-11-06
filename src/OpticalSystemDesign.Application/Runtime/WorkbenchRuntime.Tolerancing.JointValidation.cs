using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    private sealed partial class JointToleranceStudy
    {
        private static int[] SelectConfigurations(JointToleranceSnapshot snapshot, ToleranceConfigurationSettingsDto settings)
        {
            var selected = settings.Scope switch
            {
                ToleranceConfigurationScope.All => Enumerable.Range(1, snapshot.Configurations.Length).ToArray(),
                ToleranceConfigurationScope.Selected => settings.Configurations?.Order().ToArray() ?? [],
                _ => throw new ArgumentException("多配置研究须选择指定配置或全部配置。")
            };
            if (selected.Length == 0 || selected.Length > 32 || selected.Distinct().Count() != selected.Length
                || selected.Any(number => number < 1 || number > snapshot.Configurations.Length))
                throw new ArgumentException("所选配置为空、重复、超过 32 个或不存在。");
            return selected;
        }

        private void Validate(Optic[] nominal)
        {
            if (!Enum.IsDefined(_request.Mode) || _request.CompensationIterations is < 0 or > MonteCarlo.MaximumCompensationIterations)
                throw new ArgumentException("分析模式或补偿循环数无效。");
            var settings = _settings.Limits ?? [];
            if (settings.Any(item => item is null || !_selected.Contains(item.Configuration))
                || settings.Select(item => item.Configuration).Distinct().Count() != settings.Count)
                throw new ArgumentException("配置验收限值引用重复或未选中的配置。");
            var definitions = _request.AdditionalCompensators ?? [];
            if (definitions.Any(item => item is null || item.Configuration != 0 && !_selected.Contains(item.Configuration)))
                throw new ArgumentException("补偿器引用未选中的配置。");
            if (_operands.All(item => item.Kind == ToleranceOperandKind.Compensator)
                && !_mce.Any(item => !item.Row.Compensator))
                throw new ArgumentException("多配置公差至少需要一个共享公差或 TMCO。");
            if (_request.Mode == ToleranceAnalysisMode.SkipSensitivity && _request.Trials == 0)
                throw new ArgumentException("跳过灵敏度时至少需要一次 Monte Carlo 试验。");
            if ((_request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement)
                && !(_request.InverseValue > 0))
                throw new ArgumentException("联合反求的限值或增量必须为正数。");
            if (_mtf && _request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement
                && _request.InverseValue > 1)
                throw new ArgumentException("MTF 反求限值或增量不得超过 1。");

            var phases = definitions.Select(item => item.Stage)
                .Concat(_mce.Where(item => item.Row.Compensator).Select(item => item.Row.Stage))
                .Concat(_operands.Any(row => row.Kind == ToleranceOperandKind.Compensator) ? [_focusStage] : [])
                .Distinct().ToArray();
            TolerancingService.ValidateStageObjectives(_request, phases);
            if (phases.Length > 0 && _request.CompensationIterations > MonteCarlo.MaximumCompensationIterations / phases.Length)
                throw new ArgumentException("补偿阶段数乘以每阶段循环数不得超过 500。");

            var reference = nominal[_selected[0] - 1].SurfaceGroup.Items;
            foreach (var number in _selected)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var optic = nominal[number - 1];
                var surfaces = optic.SurfaceGroup.Items;
                if (surfaces.Count != reference.Count)
                    throw new ArgumentException($"配置 {number} 的表面数量不同，无法对应共享公差。");
                try
                {
                    using var detached = new WorkspaceCoordinator(new OpticContext(optic));
                    new TolerancingService(detached).ValidateRequest(_request with
                    {
                        SurfaceNumber = _operands.FirstOrDefault()?.SurfaceNumber ?? Math.Min(1, surfaces.Count - 1),
                        Operands = _operands,
                        AdditionalCompensators = CompensationDefinitions(number).Select(row => row with { Configuration = 0 }).ToArray(),
                        ConfigurationSettings = null,
                        YieldLimit = ConfigurationLimit(number), MtfSettings = ConfigurationMtf(number),
                        Mode = ToleranceAnalysisMode.Sensitivity, CompensationIterations = 0
                    }, surface => SurfaceFringePerturbation.ApertureRadius(reference[surface]), joint: true);
                    OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Tolerancing);

                    foreach (var row in _operands)
                    {
                        var grouped = row.Kind is ToleranceOperandKind.ElementDecenterX or ToleranceOperandKind.ElementDecenterY
                            or ToleranceOperandKind.ElementTiltX or ToleranceOperandKind.ElementTiltY;
                        var pose = grouped || row.Kind is ToleranceOperandKind.DecenterX or ToleranceOperandKind.DecenterY
                            or ToleranceOperandKind.TiltX or ToleranceOperandKind.TiltY;
                        if (row.Kind != ToleranceOperandKind.Compensator)
                        {
                            try
                            {
                                VerifyEntity(reference, surfaces, row.SurfaceNumber, grouped ? row.EndSurfaceNumber : row.SurfaceNumber, pose);
                            }
                            catch (ArgumentException error)
                            {
                                throw new ArgumentException($"公差第 {row.Index} 行：{error.Message}", error);
                            }
                        }
                        var property = row.Kind switch
                        {
                            ToleranceOperandKind.Radius or ToleranceOperandKind.FringePower => "radius",
                            ToleranceOperandKind.Thickness or ToleranceOperandKind.Compensator => "thickness",
                            _ => ""
                        };
                        if (property.Length > 0)
                            VerifyIndependentWrite(optic, number, row.SurfaceNumber, property, row.Kind == ToleranceOperandKind.Thickness);
                    }

                    foreach (var definition in CompensationDefinitions(number))
                    {
                        if (definition.Configuration == 0)
                            VerifyEntity(reference, surfaces, definition.SurfaceNumber,
                                definition.Kind.IsElement() ? definition.EndSurfaceNumber : definition.SurfaceNumber,
                                definition.Kind != ToleranceCompensatorKind.Thickness);
                        if (definition.Kind == ToleranceCompensatorKind.Thickness)
                            VerifyIndependentWrite(optic, number, definition.SurfaceNumber, "thickness", false);
                    }

                    foreach (var focus in _operands.Where(row => row.Kind == ToleranceOperandKind.Compensator))
                        if (focus.SurfaceNumber < 1 || focus.Minimum >= focus.Maximum
                            || surfaces[focus.SurfaceNumber].MaterialAfter.RefractiveIndex(587.6) > 1.0001)
                            throw new ArgumentException($"COMP 第 {focus.Index} 行的空气间隔或补偿范围无效。");
                }
                catch (Exception error) when (IsEvaluationError(error))
                {
                    throw new ArgumentException($"配置 {number}：{error.Message}", error);
                }
            }
        }

        private static void VerifyEntity(IList<OpticalSurface> reference, IList<OpticalSurface> surfaces,
            int first, int last, bool requireAlignedAxes)
        {
            if (first < 0 || last < first || last >= surfaces.Count)
                throw new ArgumentException($"面 {first}–{last} 的实体范围不存在。");
            for (var number = first; number <= last; number++)
            {
                var a = reference[number];
                var b = surfaces[number];
                var x = a.CoordinateSystem;
                var y = b.CoordinateSystem;
                if (a.Number != b.Number || a.Label != b.Label || a.Geometry.Kind != b.Geometry.Kind
                    || a.InteractionModel.Kind != b.InteractionModel.Kind || a.Material != b.Material)
                    throw new ArgumentException($"面 {number} 的实体标识或几何类型与基准配置不一致。");
                if (requireAlignedAxes && (Math.Abs(x.RotationXDegrees - y.RotationXDegrees) > 1e-9
                    || Math.Abs(x.RotationYDegrees - y.RotationYDegrees) > 1e-9
                    || Math.Abs(x.RotationZDegrees - y.RotationZDegrees) > 1e-9))
                    throw new ArgumentException($"面 {number} 的装配坐标轴与基准配置不一致。");
            }
        }

        private void VerifyIndependentWrite(Optic optic, int configuration, int surface, string property, bool manufacturing)
        {
            var conflict = property == "radius"
                ? optic.Pickups.RadiusPickups.Any(pickup => pickup.SourceSurface == surface || pickup.TargetSurface == surface)
                : optic.Pickups.ThicknessPickups.Any(pickup => pickup.SourceSurface == surface || pickup.TargetSurface == surface)
                    || (optic.Solves.KeepImageAtBackFocus && surface == optic.SurfaceGroup.Items[^1].Number);
            conflict |= _snapshot.Pickups.Any(pickup =>
                pickup.ConfigurationIndex + 1 == configuration && pickup.Operand.SurfaceNumber == surface && pickup.Operand.Property == property
                || pickup.SourceConfigurationIndex + 1 == configuration && pickup.SourceOperand.SurfaceNumber == surface && pickup.SourceOperand.Property == property);
            conflict |= property == "thickness" && _mce.Any(binding => binding.Row.Compensator != manufacturing
                && binding.Effects.Any(effect => effect.Configuration == configuration && effect.Surface == surface));
            if (conflict) throw new ArgumentException($"面 {surface} 的 {property} 写入与拾取、求解或配置操作数冲突。");
        }
    }
}
