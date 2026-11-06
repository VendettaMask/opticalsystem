# 多配置联合公差本地验收

2026-11-06，Windows x64，.NET 10，Release。主算例用两配置 Cooke 三片镜头，制造误差、补偿约束和随机种子一并留存。

| 验收项 | 结果与证据 |
| --- | --- |
| 一件样件跨配置 | 固定种子的 ISOB／ISOC 在半径 6 mm 与 4 mm 的净口径内按相同物理坐标求矢高，重叠区域差值在 13 位小数内一致。TFRN 两配置采用基准检测口径换算曲率。表面实体不一致时预检定位配置、行和表面。 |
| 共用与独立补偿 | 主算例用种子 431、3 次试验、3 个补偿阶段。共用镜组调整量逐配置一致，独立像距在三次试验中相差约 0.44–0.48 mm。配置 1 通过 3/3，配置 2 通过 0/3，整机通过 0/3。另用四条受控记录核对 75%／50%／25% 良率。串行与双线程试验按同一顺序得到相同评价与余量。[工程](cooke-two-configurations.staropt) · [逐试验和阶段结果](cooke-joint-trials.json) |
| 逐视场 MTF | 用 30 cycles/mm 几何 MTF 检查配置 2 的第 2 视场。实测 0.1682589188，限值 1，验收余量 −0.8317410812。配置 1 通过，整机未通过。[视场原始结果](cooke-mtf-field-acceptance.json) |
| 联合反求 | 双配置分别偏离名义像距 −1.2 mm 和 +1.2 mm。允许 RMS 点列半径比各自名义值增大 0.0002 mm。TTHI 的 ±0.8 mm 收紧到约 [−0.002849684, +0.002849698] mm。复算时负端由配置 1 限制，正端由配置 2 限制，余量均非负。[端点与复验结果](cooke-joint-inverse.json) |
| 最差样件复算 | 样件文件 v2 保存最差试验 2。用 Tessar 工作区打开后，逐项比对配置与阶段记录一致。测试核对源处方、活动配置、修订以及取消保护。[样件](cooke-worst.startolsample.json) |
| TMCO／CMCO | 配置 2 的间隔通过比例 2 拾取配置 1。TMCO 增量分别为 +0.2 与 +0.4 mm，实际评价与两个直接改间隔的单配置算例一致。CMCO 补偿亦按拾取比例传播。测试覆盖只读目标、错行和重复补偿冲突。[拾取工程](cooke-thickness-pickup.staropt) · [试验结果](cooke-mce-pickup.json) |
| 研究文件与界面 | 640 DIP、150% 缩放离屏核对设置页与 TMCO／CMCO。v7 的限值、配置补偿和厚度行保存后解析一致。覆盖旧版单配置研究与样件兼容。[v7 研究](cooke-joint-settings.startol.json) · [配置设置](joint-setup-light-640.png) · [厚度操作数](joint-mce-light-640.png) |

[初次运行](joint-configuration-initial.trx)保留了后焦求解预检误判造成的三项失败。检查改为仅限制实际受求解控制的单元格后，[针对性复跑](joint-configuration-recheck.trx) 4/4 通过。[扩展回归](joint-configuration-regression.trx) 103/103 通过。[新增对应关系与条纹口径检查后](joint-configuration-final.trx) 105/105 通过。[最终验收运行](joint-configuration-acceptance.trx) 105/105 通过，含整机良率图及不同实际焦点的双配置验证。中英日词库检查为 3152 条译文、2885 处引用，零缺失、零错误。

下列命令在仓库根目录运行。

```powershell
$env:OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR = Join-Path (Get-Location) 'validation/rechecks/2026-11-06/joint-configuration-tolerancing'
dotnet test tests/OpticalSystemDesign.Tests -c Release --no-restore -m:1 --filter 'FullyQualifiedName~JointConfigurationTolerancingTests|FullyQualifiedName~MtfTolerancingPanelTests|FullyQualifiedName~StagedTolerancingTests|FullyQualifiedName~IsoIrregularityTolerancingTests|FullyQualifiedName~TolerancingWorkflowTests|FullyQualifiedName~MtfTolerancingTests' --logger 'trx;LogFileName=joint-configuration-repeat.trx' --results-directory $env:OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR
```

## OpticStudio 原生数值对照

当前环境未安装 OpticStudio，以上结果来自本系统及独立单配置构造。原生对照需用等效处方，按相同物理矢高、间隔误差、装调自由度、补偿界限、评价和视场权重逐配置导出结果，再按同一样件求联合通过率。逐样件对照以确定的物理扰动为配对依据。可先从确定的 TMCO 间隔及配置拾取算例开始，再比较含共享面形和分步补偿的算例。
