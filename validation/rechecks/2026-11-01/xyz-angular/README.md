# 实测角表与 BTDF 链路证据

2026-11-01。**剩余三种XYZ校准仍失败，新增正式批准为0**。已验证13组有限范围的实测角表链路，不用该链路通过替代校准或全角域商业认证。[实现与准确范围](../../../../docs/XYZ_MEASURED_ANGULAR_2026-11-01.md)

- 最终入口：[汇总](final-verification-summary.json)、[执行身份](final-run-integrity.json)、[独立执行脚本](run-isolated.ps1)、[最终TRX](isolated-final-focused.trx)、[最终测试日志](test-isolated-final-focused.log)。该过滤范围预期307项，最终实际计数由TRX读取；不是全仓测试。
- [冻结运行前](before-isolated-final-run.json)/[运行后](after-isolated-final-run.json)记录专属执行副本SHA；[源与输入快照映射](source-snapshot-map.json)对应`source-snapshot/`。执行副本在`tools/diagnostics/XyzAngular20261101/bin/FrozenAngular20261101/`，不进入本目录manifest，关键程序集由两份运行快照逐个绑定。复验不得原地覆盖已封存输出。
- [最终校准](final-calibration/summary.json)：原五份数据、固定550 nm/原门槛；只有上一阶段两例仍批准。配对CSV、校准JSON和失败fit.json保留。新增三份[全精度原MAT来源与SHA](full-precision/sources.json)及[全精度诊断](final-precision/)；诊断来源占位符明确标记，禁止当作正式批准。
- [严格探索汇总](exploration-finite.json)记录未采用的候选及失败。原`exploration.json`保留部分实验数值溢出的非标准Infinity记号；严格汇总将其转为失败字符串，不隐去溢出或负值，不采用最佳候选作为产品模型。Python/SciPy仅用于独立诊断，产品仍纯C#/.NET。
- [Cornell全部八入射角独立功率审计](angular-power-audit.json)、[整表产品拒绝](third-import/import-audit.json)、[明确20°原切片导入](fourth-import/import-audit.json)。当前插值在部分切片积分>1，不能称全表通过，也不重新归一化。
- [最终实测链路](final-measured-chain/summary.json)：五材料固定10°/20°、550 nm全出射切片，另三份VIAVI法向BTDF；各目录有32箱独立角统计、工程、相机ZIP和chain.json。角统计32,768样本/组，NSC另8,192光线/组；原切片支持域外为零是明确计算约定。`FullMeasuredAngularTableRetained`限于该选定入射角。背面声明相同、供方示范波长、解析QE均有明确标记。
- [VIAVI官方原始ZIP与身份](measured-btdf/source-downloads.json)，配套PDF/ZMX用于核实法向单旋转表及显式波长声明。记录EDC50、EDS40、HiLam代表样本实测角表，不声明所有入射角、背面或光谱响应认证。[官方入口](https://www.viavisolutions.com/en-us/osp/resources/bsdf-data)
- [最终White复算](final-white/runtime6-native-power-audit.json)：复用旧原生捕获，23组、0Unsupported，最大差1.4534376569230734e−6 W。本轮没有新原生桌面追迹。
- [最终正常默认App构建](build-app-frozen-baseline.log)、[测试](build-tests-frozen.log)、[角表驱动](build-driver-frozen.log)、[White驱动](build-white-frozen.log)；App包含两个内置模块。测试/诊断在正常App之后用BuildProjectReferences=false复用构建。

第一次共享目录运行307项通过，但[默认UI输出运行中漂移](first-final-run-drift.json)、[随后其他构建覆盖](after-run-concurrent-build-changes.json)均保留；实际执行的原测试副本在运行期间未变。最终独立副本复验避免这种并发身份混淆。早期沙箱测试中断未完成、单旋转接缝/极点实现失败、透射方向测试坐标错误、探测器/像元尺寸控制配置错误及其他模块暂时编译错误日志全部保留，不计入最终通过。

[封存脚本](seal-evidence.ps1)执行[核查](verify-final.ps1)后生成evidence-manifest.json，自身不进入manifest。旧四根目录共297+533+163+219=1,212个已封存文件逐个核对；旧记录不重写。见[NOTICE](NOTICE.txt)。本目录证据用于本机验证，不将真实第三方测量加入CI，也不推定再分发权利。
