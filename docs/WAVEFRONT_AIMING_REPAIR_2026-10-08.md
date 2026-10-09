# 波前图系统瞄准修复 2026年10月8日

本页保留波前图实现轮的历史范围。后续 FFT、零离焦、Zernike、参考球等路径见[计算路径修复与结果对比](CALCULATION_PATH_REPAIR_2026-10-08.md)；当前实现和验收见[FFT 采样与参考点修复及当前验收](FFT_SAMPLING_REFERENCE_REPAIR_2026-10-09.md)。下面的旧测试计数及当时未完成项不冒充当前状态。

实现阶段原测试输出已按原始字节归档到 `artifacts/validation/wavefront-aiming-repair-20261008/formal` 和 `tool`，文件名不变；包含修复前观察、修复后专项和相关两配置记录，不删除原输出，也不把修复前失败计入最新结果。远端保存边界见[本轮同步记录](PROJECT_SYNC_2026-10-08.md)。

波前图现在遵循镜头的系统光线瞄准设置，不再由“使用出瞳形状”显示选项决定光线发射。均匀网格和六角环采样均已修复。Cooke、Double Gauss 的实际波前图边场共节点误差由数百个波长降至约 2e-5、4e-5 waves；原本开启瞄准的 Relay 保持不变。原生参考、镜头快照、容差和光程公式均未改动。

后续同日[验收报告](WAVEFRONT_ACCEPTANCE_2026-10-08.md)已完整运行正式与比较工具 Release：分别为 **4430 通过 / 4 失败 / 共 4434 项**、**178 通过 / 2 失败 / 共 180 项**，零跳过。新增 25 项及此处 743 项正式相关用例在全量中全部通过，但正式全量新增观察到两个界面会话关闭失败身份，整体发布验收不通过。下方保留实现轮的验证范围与历史计数。

## 实现范围

`WavefrontAnalysis.GenerateData` 的两条采样分支均显式传递 `Optic.RayAimingEnabled`，结果元数据新增 `UseRayAiming`，与保留的 `UseExitPupilShape` 分开。应用的 Wavefront、Wavefront Map 和 Interferogram 复用这一个正式 Core 入口，无需另建计算路径。

`WavefrontEngine.GenerateChiefRay` 的六角环入口增加可选 `aimAtStop` 参数。未指定时仍为 `false`，保留其他调用方的原有通用默认行为；波前图调用方明确使用系统设置。未修改参考球、主光线光程、孔径接纳、去倾斜、显示最小值平移或已有采样坐标。

[OpticStudio 2026 R1 Wavefront Map](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html) 中的 Use Exit Pupil Shape 控制出瞳形状的显示，依据光束 X/Y F 数，不是停面瞄准开关。本次只修复其错误影响物理波前的问题；**按 X/Y F 数投影出瞳显示形状尚未实现**，当前该参数仍作为兼容设置及元数据保留，图坐标仍为归一化光瞳坐标。

## 实际波前图与原生精确共节点

使用未改动的三份官方镜头和六组冻结原生 OPD 光扇。实际产品计算 64×64 波前图，各有 3001 个有效二维样本；每组只比较图与光扇共有的 6 条精确节点记录，即 5 个独立坐标，中心在两个光扇各出现一次。下表共 36 条记录不是全部 492 个光扇节点，也不是完整原生二维 Wavefront Map 认证。

| 镜头与捕获设置 | 系统瞄准 | 修复前最大误差 waves | 修复后最大误差 waves |
| --- | --- | ---: | ---: |
| Cooke 轴上 550 nm | 关闭 | 0.010271390677793 | 2.260835963e-11 |
| Cooke 20° 480 nm | 关闭 | 364.929953861163 | 1.983404922e-5 |
| Double Gauss 轴上 587.6 nm | 关闭 | 0.021963776529311 | 1.632464719e-10 |
| Double Gauss 14° 486.1 nm | 关闭 | 384.154817243149 | 4.031837608e-5 |
| Relay 轴上 587.5618 nm | 开启 | 3.144181582e-10 | 3.144181582e-10 |
| Relay 2° 486.1327 nm | 开启 | 6.317280780e-7 | 6.317280780e-7 |

带符号 OPD 根据产品自身未舍入的平均光程与图值恢复显示最小值平移，不以原生值拟合活塞、比例或倾斜，也不插值到其他节点。整幅实际图与遵循系统瞄准的正式引擎路径最大差小于 1.8e-15 waves；显示形状开关开/关得到相同物理样本。小量残差仍保留，不宣称全部消除。

修复前[诊断结果](../artifacts/validation/pupil-comparison-20261008/run-03/summary.json)与修复后[结果](../artifacts/validation/pupil-comparison-20261008/repair-01/summary.json)独立保存，原始记录没有覆盖。11 个清单、快照、原生与 Tessar 源文件哈希前后完全一致。负 Y 渐晕和 RA32/64/128/256 控制结果也完全相同，说明这些待办未被本次修改改变。

## 回归与构建

新增 `WavefrontAimingRepairTests` 共 25 项：系统瞄准开/关与两种采样、显示开关物理不变性、六组冻结原生共节点在两种显示设置下的对照，以及六角引擎未指定参数的默认行为。原生节点断言使用原清单中的绝对加相对误差预算和 NRMSE 门槛，没有扩大容差。

同一批测试先在修复前运行，3 项通过、22 项失败；失败包含实际数值、有效节点和新增瞄准元数据检查。修复后 Release 单独运行 **25/25** 通过；Debug、Release 扩大回归均包含并通过这 25 项，零跳过。修复前后单独记录分别为 `wavefront-aiming-before-release.trx` 与 `wavefront-aiming-after-release.trx`，位于 `tests/OptilandWorkbench.Tests/TestResults`。

默认 `OptilandWorkbench.slnx` Debug、Release 均构建成功，零警告、零错误，默认桌面输出已更新。正式相邻回归两配置各 **743/743** 通过，零失败、零跳过，包含新增 25 项，以及波前、OPD、瞄准、此前 RMS 修复、Zernike、无焦、衍射、MTF 与 PSF。原 `123456.ZMX` 完整冻结 Wavefront Map 对照也在此集合内通过，沿用原网格与 NRMSE 门槛。记录位于同目录的 `wavefront-aiming-related-debug.trx` 和 `wavefront-aiming-related-release.trx`；新增测试不能再加到这 743 项之上。

比较工具的指标定义、参考完整性等相关回归两配置各 **45/45** 通过，零失败、零跳过；记录为 `tests/OptilandWorkbench.ZemaxComparison.Tests/TestResults/wavefront-aiming-comparison-debug.trx` 与 `wavefront-aiming-comparison-release.trx`。这是独立工具项目的定向验证，不是其全部 180 项的新完整结果。

实现轮未执行完整发布验收。此前正式完整 Release 4407 通过、2 失败、共 4409 项，以及比较工具 178 通过、2 失败、共 180 项，是修复前历史记录，不作为新增 25 项后的当前全量结果；后续本次完整 Release 已记录在上方验收链接中。原六文件 132 项外部矩阵或实验室全量仍未重跑，旧分类不更新，发布门禁仍未关闭。

## 后续待办

1. 拆分 FFT PSF 的单元中心节点与瞄准设置，以及零离焦辅助波前的固定瞄准，再用原生 PSF/MTF 验证。当前辅助路径的数值仍复现强制瞄准误差，不能据波前图修复宣布衍射整体修复。
2. 实现并验证 X/Y F 数对应的出瞳显示形状，保持物理波前不变。
3. 捕获负 Y 和混合符号 Y 的原生发射光线，确认渐晕插值变量及镜像语义；不直接推广现有正 Y 平方径向公式。
4. 捕获 RA 波前 32/64/128/256、chief/centroid、渐晕开/关，认证节点后再调整采样。

本机此前新原生捕获明确报 `ZOS-API not found`，本次使用已有冻结原生记录，不是新捕获。完整原生 Wavefront Map、PSF/MTF 及上述精确规则仍需要可用的 OpticStudio 原生环境；本机许可状态未检测。

诊断复现方式见[诊断工具说明](../tools/diagnostics/PupilComparison20261008/README.md)，修复前的问题定位保留在[诊断报告](ZEMAX_PUPIL_DIAGNOSIS_2026-10-08.md)。
