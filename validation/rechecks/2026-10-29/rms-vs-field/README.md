# 无焦 RMS 随视场：高斯角向节点修复（2026-10-29）

瞄准镜原请求的最大差 **0.002218114630527568 → 4.137330811282425e-9 mrad**，逐点超限 **1 → 0**。另外四份原无焦 RMS 随视场请求也全部满足原逐点和 NRMSE 门槛，没有控制项误差增加。此次是针对该计算路径的修复及定向验证，没有再次重跑全部170项。

## 原因和修复

原非偏振 Gaussian RMS 积分把12条角向臂放在从 +X 开始的周期等分节点上。按半瞳孔 Gauss-Chebyshev 节点及镜像展开后，12臂应采用相差15°的中点布置。旧节点产生的角向积分混叠在瞄准镜边缘视场最明显；6臂和10臂的两种布置对应相同节点集合，而8臂、12臂需要半步偏移。

共享 Core 的 `ApertureSampler` 增加显式角向规则，仅由非偏振 Gaussian RMS 点列积分路径选用。一般周期采样默认、显式传入瞳孔节点、波前积分、偏振及 RA 路径继续遵守各自原契约。没有按镜头名称分支，没有调整原生曲线或拟合补偿。

**原处方、采样设置、参考与容差保持**：GQ 6环/12臂、72节点、径向位置和权重、Field Density=15（实际计算16视场）、选中波长及质心定义保持。修复会纠正角向节点坐标，不能把它表述为“实际节点坐标完全未变”。零最大视场的扩束镜和 Apochromat 在比较器中各合并为一个独立坐标，五请求共50个可对齐值。

瞄准镜在六个诊断视场的72条光线全部有效，各保留19面历史，最大光阑瞄准残差 **小于3.3e-13 mm**。无需丢光线补救或改动追迹。独立控制只把原72个节点旋转π/12，保留权重，再由同一 Core 追迹；离线按导出角坐标独立重算加权质心和 RMS，边缘视场得到0.870019440824315 mrad，对比原生0.870019443199354 mrad，差约−2.37504e-9 mrad。轴上结果保持至浮点舍入精度，支持角向节点而非角度换算或质心公式的归因。

最差旧点在**归一化视场1，即物理2.5°**；[完整170项报告](../final-core-170/README.md)原先将它文字误写为1°，此次只修正文句，原数字账目保持。原门槛仍为绝对1.7453292519943293e-5 mrad + 原生值的0.001倍；原 NRMSE 门槛0.003、CloseFactor及覆盖门槛也不变。瞄准镜原整体 NRMSE 已为 Pass，不能据此忽略那个超限点。

## 官方依据及推断边界

OpticStudio 2026 R1 的 [RMS vs. Field 文档](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html)描述 Gaussian 积分、径向密度及光瞳参考；[Gaussian Quadrature 文档](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gaussian_Quadrature.html)说明环/臂、离轴左右半瞳孔对称和积分阶数。官方页面没有公开具体角向节点公式；这里的半步约定是结合数学节点条件和既有原生捕获验证得到的推断，不能声称公开文档逐字规定了15°。

新增独立控制验证6/8/10/12臂、环形瞳孔的 Chebyshev 根及 arcsine 角向矩至高斯阶数，同时校验原径向权重、一般默认、奇数臂行为和缓存身份。原生文本明确为 Centroid；ZOS-API读回的枚举名在该版本存在别名，不据此将原质心请求改成主光线参考。

## 五份原请求

| 文件/配置 | 修复前最大差 / mrad | 修复后最大差 / mrad | 原逐点超限前→后 |
| --- | ---: | ---: | ---: |
| 瞄准镜 c1 / w2 | 0.00221811463053 | 4.13733081128e-9 | 1→0 |
| 扩束镜 c1 / w1 | 1.36479369472e-15 | 1.18015406475e-16 | 0→0 |
| 双筒 c1 / w2 | 2.24005999305e-8 | 5.73874281429e-13 | 0→0 |
| 双筒 c2 / w2 | 2.24005366478e-8 | 5.71542813077e-13 | 0→0 |
| Apochromat c1 / w3 | 1.89326617253e-28 | 1.89326617253e-28 | 0→0 |

瞄准镜修复后的最大差在轴上；物理2.5°边缘差约2.37504e-9 mrad。原处方、请求、有效门槛、原生文件的字节哈希及前后全部指标见[机器账目](verification.json)。原生数据继续引用既有唯一[证据包](../../2026-10-27/afocal-fft-energy/evidence-current.zip)，不新增一份参考数组。

## 验证与重放

- 最终比较项目112项通过，另专用RMS/GQ保护47项通过；按测试名称去重 **159项**，零失败、零跳过。涵盖原五份 RMS、既有 RMS 采样契约，以及此前 FFT PSF/MTF、OPD、Huygens离焦修复的定向保护。原有 Cooke/Tessar 回归仅作为保护重放，没有新增其独立残差调查或认证。
- 三处最终 Core 二进制 SHA-256 一致：`a71aa3339d02ced1a327a64c30e1aa1c43c9ebaf13a8fe696b706ae7fecb749b`。默认 Release App 构建零警告、零错误，未启动应用。
- 原2668份冻结证据在 HEAD 和工作副本均完整。比较策略与 HEAD 保持一致，五份请求指纹与此前完整170项账目一致；离线独立复核全部50个对应值及四个旋转节点控制。
- 初轮五项参考路径错误、修复前真实1点失败及诊断重复失败均保留，未计入最终通过数。[封存包](evidence.zip)含这些TRX、诊断光线、前后源码、实际Core及构建日志；原生数组以原包成员哈希引用。

在仓库根目录重放：

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --filter 'FullyQualifiedName~AfocalRmsFieldPointwiseTests'
dotnet test tools/diagnostics/RmsField20261029/FocusedRmsRegression.csproj -c Release
```

原始运行保存在 `artifacts/validation/rms-field-20261029`。`tools/diagnostics/RmsField20261029/Collect-Evidence.py` 只核对和封存已完成运行，不为产品提供光学计算。此次未新采原生、重跑完整170项/95文件、运行GUI/GPU或发布；剩余扩束镜衍射包围能量、双筒配置2 FFT PSF截面等仍按[当前问题清单](../../../../docs/OPEN_ISSUES_2026-10-08.md)保留。
