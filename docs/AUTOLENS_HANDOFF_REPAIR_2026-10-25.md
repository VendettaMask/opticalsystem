# AutoLens 阶段交接修正（2026-10-25）

延续 [e58 原版规则对齐](AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.md)，本轮只修正课程训练到目录精修之间的 JSON 交接规则，保留纯 C# 产品和既定计算边界，不比较最终优化效果。

## 查实的问题

原版 `deeplens/geolens_pkg/io.py` 的 `read_lens_json` 从 `d = 0` 开始，在创建每个面之前用累计值覆盖文件中的 `d`，随后累加舍入后的 `d_next`。此前 C# 对各个面的绝对位置分别舍入，只对像面累加间距，因此与原版读入后的处方不一致。

小型原始函数往返用例中，输入位置为 `0、0.10006、0.50012 mm`，像面为 `6.20018 mm`。原版重新读入后为 `0、0.1001、0.5002 mm`，像面为 `6.2003 mm`。独立舍入第三面会得到 `0.5001 mm`，差异会进入后续固定光线采样、梯度和 Adam 更新。

## 修改边界

- [AutoLensReferenceHandoff](../labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutoLensReferenceHandoff.cs) 在实验室内执行原版专用的舍入及交接规则：首面归零，所有光学面与像面从舍入后的面间距累计重建，保留 ROC、圆锥、口径及系数精度约定。
- 从 Core 移除 `RoundReferenceHandoff`。Core 现有 `OpticalDesignParameters.Profile` 只开放已有的只读参数描述接口，计算行为不变，返回的系数仍是副本。
- AutoLens 专用绿色参考、detach、epsilon RMS 和损失组合继续位于实验室；正式光斑分析、追迹、折射、色散和独立物理验收继续使用共享 Core。
- 不改写已有检查点的处方或 Adam 矩，不把旧记录批量转换成新的交接结果。

## 验证范围

[生成脚本](../validation/rechecks/2026-10-25/autolens-handoff/export_handoff_fixture.py) 直接调用未修改原版的 JSON 写入及读取函数，生成两个小型规则夹具：普通位置，以及首面带偏移且一面零曲率。夹具使用 float64 隔离序列化数学规则；没有运行优化、追迹或最终像质对比。原始函数、原始 JSON 和冻结夹具均有 SHA-256，原版受 Git 跟踪的源码保持未修改。

本轮只运行三个定向用例：原始 JSON 交接、迁移后的冻结 RGB 损失值与梯度、小采样完整两阶段与目录保存。Release **3/3** 通过，无失败、无跳过；默认 App Debug/Release 输出均已更新，Debug 编译零警告、零错误。详见 [本轮机器账目](validation/AUTOLENS_HANDOFF_REPAIR_2026-10-25.json) 和 [原始证据](../validation/rechecks/2026-10-25/autolens-handoff)。初次编译发现迁移后访问 Core 内部参数读取方法的可见性问题；修正为共享只读接口，保留初次失败日志。

没有扩展为全仓测试，没有运行完整默认新配置或发布安装包。Core double、训练 CUDA 随机流及另一套 CAMERA_CONFIG 拓扑的既有边界保持，不能据此宣称最终输出等效。
