# 有颜色适用域的 XYZ 辐射度校准验证

2026-11-01。实现及限制见[专项报告](../../../../docs/XYZ_GAMUT_CALIBRATION_2026-11-01.md)。协议7/STAROPT v15只用于显式有域约束的校准；旧Grid/1–6不升级。本目录保留初步诊断、失败和最终复算，各类通过分别判定。

## 最终结论

| 范围 | 结果与限制 |
| --- | --- |
| 定向产品回归 | **280通过、0失败、0跳过**，1分23秒；原264项加新16项，不是全仓测试 |
| 默认构建 | App含两内置模块、测试驱动、XYZ/White诊断均0警告/0错误 |
| 运行身份 | 14个二进制、118个相关源文件前后无漂移；App/测试/两诊断Core副本一致 |
| 新NSC→RAW/ISP | 反射/透射各8,192次抽样；光子换算最大差均2.84217e−14 e⁻；冻结RAW/ISP逐值一致；为合成解析控制 |
| 旧White功率 | 23/23捕获复算通过、0不支持；最大场景功率差1.4534376569 µW，未重新启动商业软件 |
| 旧冻结证据 | 原297及533文件逐个SHA核对，0变化 |
| 发布Krylon配对数据 | 550 nm原留出策略NRMSE0.0083800331、最大差0.0027323996 sr⁻¹，拟合通过；观察者未核实，实际拒绝导出 |
| 实测正式校准 | 仍未完成；已知条件的派生XYZ失败，原门槛未变 |

未执行全仓、原生GUI、安装或发布验收，无前台窗口/输入操作。没有用内部回归或局部拟合通过替代真实材料商业认证。

## 机器判定与身份

- [最终TRX](gamut-final-focused.trx)、[日志](test-gamut-final-focused.log)、[可复查运行脚本](run-focused.ps1)。过滤器覆盖新XYZ域约束、Speos v11、二进制/Zemax、实测/Lambertian、NSC相机/杂散光和文档边界。
- [运行前](before-gamut-final-run.json)、[运行后](after-gamut-final-run.json)、[无漂移核对](final-run-integrity.json)、[副本一致性](pre-final-copy-identity.json)、[最终汇总](final-verification-summary.json)。汇总保留真实材料/商业认证未通过标记。
- [默认App最终构建](build-gamut-app-final.log)、[测试最终构建](build-gamut-tests-final.log)、[XYZ诊断构建](build-xyz-diagnostic-final.log)、[White诊断构建](build-power-diagnostic-final.log)。测试/诊断在默认App完成后使用BuildProjectReferences=false，未把旧Core驱动结果作为最终结果。
- [旧证据核对](prior-evidence-integrity.json)与[White23组复算](white-power-recheck/runtime6-native-power-audit.json)。功率驱动新增可选独立输出目录，仅输出新根，原捕获与旧结果均未覆盖。
- 新链路：[反射](gamut-final-xyz-chain/gamut-chain-False.json)、[透射](gamut-final-xyz-chain/gamut-chain-True.json)及同目录相机ZIP。数据库/工程v15、禁用对象降级拒绝、越域导入原子性、旧Grid/6 ZIP重放均有事实断言。

`gamut-final-chain/`、`gamut-final-results/`保存原集合本次执行产生的能量/采样、White、XYZ原模型和相机机器结果；不覆盖旧阶段结果。

测试结束后，共享`Core/bin`构建副本发生变化；[后续输出检查](post-run-shared-output-check.json)记录其不同摘要，未将该副本冒充已测结果。已测默认App、测试和两个诊断目录内Core仍逐字节一致，SHA-256为`f63b6841ac0783aecc1a3bc067f87a1dc44728ccd5fd6bffd3034aa91ec26084`；118个相关源文件也未改变。14个二进制“无漂移”特指完整测试运行的前后区间，不能理解为共享目录此后不再构建。

## 实测与数学限制

[四材料最终对照](final-measured-comparison.json)及`final-measured/<material>/inspection.json`保留原始非负拟合、共享Core有域约束拟合、独立约束解、输入/执行SHA和实际导出拒绝。每份1,439组四角完整配对，固定550 nm、959训练/480留出，原策略NRMSE≤0.01、最大绝对差≤0.01 sr⁻¹、底1e−6保持。域在拟合前仅由未标注XYZ特征建立，包含验证输入颜色但不使用其辐射度标签。

| 材料 | 原模型NRMSE | 新模型NRMSE | 新最大绝对差 sr⁻¹ | 拟合 |
| --- | ---: | ---: | ---: | --- |
| Cayman | 0.32920326 | 0.32920326 | 0.01107594 | 失败 |
| Garnet Red | 0.08034165 | 0.05682363 | 0.09950984 | 失败 |
| Krylon Blue | 0.13196594 | 0.00838003 | 0.00273240 | 通过原拟合策略 |
| Blue Acrylic | 0.61200403 | 0.61200403 | 0.00216511 | 失败 |

四份发布数据的条件标记均为false，`ApprovedExportRejected=true`；Krylon即使拟合通过也没有正式导出。其最大相对误差仍约1054.97；原策略不以该项判定，完整值保留。该通过案例参与了模型开发，不能称新盲测或跨材料认证。

[已知条件的独立派生结果](final-derived-krylon/derived-gamut-fit.json)：官方CIE1931 2°/D65，400–700 nm、范围外零、线性光谱插值、1 nm梯形及白Y归一化；新模型留出NRMSE0.03380156、最大差0.04817358 sr⁻¹，失败，`ApprovedExported=false`。推导XYZ不是独立XYZ仪器测量。未选择较粗积分方法代替该失败。

前向1 nm/10 nm、等能/D65及端点约定比较仅为诊断；没有把逐通道拟合尺度、猜测观察者或有符号无约束负预测用于产品。非负三通道“验证集自身最小残差”只是可达性诊断下界，不导出系数、不进入正式留出拟合。Cayman训练单高亮目标平方份额99.89298%说明训练总NRMSE为何容易掩盖普通方向误差。

`measured/download.json`记录Cornell官方目录链接所指向的归档原件和字节SHA；[NOTICE](NOTICE.txt)保留CIE署名/CC BY-SA 4.0和Cornell数据范围。原件只作本地验证，未加入产品材质库或CI夹具。

## 复核命令与中间记录

产品运行无需Speos、原始实测数据或外部进程。普通CI使用自制解析颜色/角分布以及旧实验夹具；不把本地下载的实测材料设为测试依赖。

```powershell
# Read old captures, write a NEW destination.
dotnet tools/diagnostics/CommercialBsdf20261031/bin/Release/net10.0/CommercialBsdf20261031.dll --runtime-power-audit validation/rechecks/2026-10-31/white-specular-runtime <new-output>
# Arguments: new-output spectral.astm XYZ.astm CMF.csv D65.csv
dotnet tools/diagnostics/XyzRadiometry20261101/bin/Release/net10.0/XyzRadiometry20261101.dll <new-output> <spectral> <XYZ> <CMF> <D65>
```

`gamut-first.trx`为新旧校准子集20/20；`gamut-second.trx`为最后新增16/16，不代替最终280项。`build-gamut-tests-first.log`保留初次测试驱动API/命名空间编译失败，随后修正并在最终构建通过；未隐藏中间失败。早期`*-inspection`、`*-feasibility`、`*-gamut`及有符号探索目录保留原诊断身份，最终采用`final-measured`和`final-derived-krylon`。

封存清单为`evidence-manifest.json`（排除自身）。后续复跑使用新输出目录，不覆盖本目录或上阶段冻结证据。
