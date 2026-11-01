# White Specular 实际追迹功率修复验证

2026-10-31。实现说明见[专项报告](../../../../docs/WHITE_SPECULAR_RUNTIME_2026-10-31.md)。新运行时功率协议为 MeasuredBsdfGrid/6，工程负载v14，旧1–5载荷保持原结果。本目录同时保留排查过程与失败，最终判定只取下列明确列出的结果。

## 最终结论

| 范围 | 结果 |
| --- | --- |
| 原400 nm White功率缺口 | 旧全半球0.251318301489 W → 新0.269720018444 W；Speos场景逃逸0.269720 W |
| 原有限64×64窗口 | 0.269664549098 W；与旧模型差0.018346247609 W，窗口与全半球分别记账 |
| 原生实际追迹控制 | 23/23功率通过，0不支持；累计24,117,672条实际发射光线 |
| 最终最大场景功率误差 | 1.4534376569 µW，已独立扣除黑色发射器背板遮挡 |
| 方向统计 | 20组正常入射的方向余弦矩通过原六西格玛；3组倾斜留出只验证功率 |
| 产品定向回归 | 264通过，0失败，0跳过，2分14秒 |
| 运行身份 | 12个二进制、101个相关源文件前后无变化；App/测试/诊断共享Core字节一致 |
| 原阶段证据 | 原清单297文件逐一核对，0变化 |
| 实测XYZ | Cayman 550 nm留出失败，未导出；真实材料正式校准仍未完成 |

最终默认Release App构建成功、包含两内置模块，0错误、1个InitialStructure.Persistence文件复制占用重试警告（MSB3026）；未关闭用户进程。测试和诊断驱动构建均0警告/0错误。未运行全仓测试、原生GUI、安装或发布验收。

## 判定文件

- [最终23组原生功率/方向矩审计](runtime6-native-power-audit.json)、[执行日志](runtime6-final2-native-audit.log)。全半球减独立背板遮挡，与HTML场景逃逸比较；容差为0.5 µW显示舍入加按实际发射数计算的六西格玛加权MC方差。有限窗口通量未缩放到全半球。
- [原生功率账本](native-power-ledger.json)逐项列出输入SHA、实际光线数、场景R/A/逃逸及HTML/XMP摘要；最大账本闭合差5.55112e−17 W。`Update-Ledger.ps1` 从各独立case的HTML与trace元数据生成。
- [最终TRX](runtime6-final2-focused.trx)、[日志](test-runtime6-final2-focused.log)、[运行脚本](run-runtime6-final2-focused.ps1)。包括White、Speos v11/XYZ、二进制/Zemax BSDF、Lambertian/Measured BSDF、NSC相机/杂散光、文档边界和旧链路回放，不是全仓基线。
- [运行前](before-runtime6-final2-run.json)、[运行后](after-runtime6-final2-run.json)、[无漂移核对](runtime6-final2-run-integrity.json)、[App/测试副本身份](pre-runtime6-final2-test-copy-identity.json)。最终Core SHA-256：`4bd425b9eb1d32709d941abf83c27398eb87e986a33b54b9586339d0ff0a66ea`；数值诊断：`f17200b6c96bb8908285801b34fa99a7efa6b5d5f5eef09af5453077814a5b81`。
- [默认App构建](build-runtime6-final2-app.log)、[测试构建](build-runtime6-final2-tests.log)、[诊断构建](build-runtime6-final2-diagnostic.log)。测试构建在默认App完成后使用BuildProjectReferences=false，未把旧Core的诊断结果作为最终结果。
- [旧297份证据完整性](final-prior-evidence-integrity.json)。原目录`commercial-bsdf-calibration`及其失败/判定未改写。

`runtime6-final2-chain/` 与 `runtime6-final2-results/` 保存本次链路、电子/RAW/ISP、保存重开及独立采样检查的机器结果。旧Grid/5 ZIP逐值电子/RAW/ISP重放为自动化事实断言；其余Grid/4等旧冻结夹具继续参加回归。

[White链路](runtime6-final2-chain/chain-speos-white.json)执行16,384次抽样，工程v14，光子→电子独立换算最大差9.09495e−13 e⁻，冻结RAW/ISP逐值一致；[联合R/T统计](runtime6-final2-chain/joint-white-400.json)保持六西格玛二项统计门槛。[最终汇总](final-verification-summary.json)仅汇集明确判定，不把XYZ失败或未执行GUI范围计入通过。

## 捕获范围与独立性

20组正常入射控制覆盖头标志0/1、锥高/角度、400 nm White基准、三个波长、1°/0.5°/0.25°表及高斯峰。各case包含自己的追迹参数、HTML和XMP。3组`heldout-input-6`、`heldout-input-19`、`heldout-input-45`在20项产品源/二进制冻结后捕获，[前](runtime6-before-heldout.json)/[后](runtime6-after-heldout.json)摘要相同。最终只增加角点/零参考守卫及预计算改进后重新执行全部23组审计；最终264回归另外冻结101个相关源文件。

原生引擎为2026.1.0.37094，EXE SHA-256 `277c76b6e609a8f97378f6647c1b8123943494fbfc81aa2a4e5f9e986ffa019d`。后台隔离RPC仅监听本机回环，无前台窗口或输入。运行负载位于上一阶段忽略目录，不随产品/CI分发；产品保持纯C#/.NET共享Core。`NativePowerStateProbe`仅只读本任务创建的版本固定RPC数值缓冲，不改写进程；其记录用于分辨锥边界/积分，不进入产品运行依赖。

最终模型保持double。源锥余弦不往返acos/cos，边界归背景；反射与等介质透射使用直接cos入射值。排查中的float方向假设没有采用。固定轴运行分支已接入统一接口，但本批无完整固定轴/BTDF/非等介质原生方向认证。倾斜case的传感器轴仍为全局+Z，因此未用其全局图矩宣称倾斜方向通过。

## XYZ实测诊断

[Cayman原件/来源说明](xyz-measured/NOTICE.txt)、[非负拟合结果](xyz-measured/paired-green-fit.json)、[最终执行身份](xyz-measured/final2-execution-identity.json)、[有符号独立诊断](xyz-measured/signed-linear-diagnostic.json)。完整四角方向匹配1,439组，固定550 nm，959训练、480验证；原门槛NRMSE≤0.01、最大绝对差≤0.01 sr⁻¹、底1e−6保持。非负模型训练NRMSE0.000336701，留出0.329203261、最大差0.011075942 sr⁻¹，失败；有符号诊断留出0.0826270且有负预测，也失败，未加入产品。

Cornell官方说明发布XYZ由实测光谱在D65下计算；不是独立XYZ仪器测量，观察者角度仍未明确。原件为本地验证数据，未推断再分发许可、未添加为产品材质或CI夹具。该诊断报告`Exported=false`，本次未调用CreateCalibration导出；不能将此写成针对Cayman的实测导出拒绝调用证据。旧API拒绝失败/条件未知的规则由已有自动化检查覆盖。合成校准的完整链路通过不代替真实材质校准。

## 保留的中间结果

- `runtime-final-focused.trx`：修复协议6之前的250/250基线，不是最终产品结果。
- `runtime6-final-focused.trx`：中间263项中262通过，1个电子断言的十进制舍入跨界（差约9.1e−13 e⁻）。最终改用更严格的绝对误差≤1e−11 e⁻，未放宽商业精度门槛。
- `runtime6-before-scene-occlusion-audit.json`：早期固定2 µW试算，尚未独立扣除背板遮挡，不是最终判定。`runtime6-pre-final2-native-power-audit.json`保留最后守卫修改前的23/23结果。
- 旧Viewer15系数与White关闭603点结果属于旧协议5/原控制范围。旧White开启方向失败与这次场景功率修复分开保留，不改写为二维逐值认证。
- `recovered-surface-bsdf.cs`、`recovered-bsdf-axis.cs`为本次编辑事故后从自身已验证Core二进制恢复行为的本地记录，非商业实现来源。

最终清单`evidence-manifest.json`封存本目录结果（排除清单自身）。后续复跑请输出到新目录，避免覆盖当前记录。无需在普通产品运行/CI中启动Speos；23组数值复算读取这里已捕获的原始结果，264项测试使用自制受控夹具。
