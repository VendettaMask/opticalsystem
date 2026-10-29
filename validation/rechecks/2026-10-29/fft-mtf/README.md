# FFT MTF 复色二次插值修复（2026-10-29）

针对[10月28日完整复核](../../2026-10-28/precision-check/README.md)中瞄准镜FFT MTF最大差0.01117663797。保持原处方、配置、视场、波长、采样、频率范围、参考和调制度容差，重算原五个FFT MTF请求。机器账目见[verification.json](verification.json)；[evidence.zip](evidence.zip)只包含红/绿TRX、拆分摘要及完整性报告，不复制原生数组。

## 复现与原因

原最大差出现在Afocal Riflescope第二视场子午曲线，频率0.08623110629 cycles/mrad。原比较器因NRMSE合格仍判Pass，新增原逐点断言实际确认瞄准镜198点、双筒两个配置各220点超限；扩束镜和Apochromat棱镜对照原来已逐点通过。

同一Core导出的单波长复数OTF经离线重放，可在4.45e-16以内复现原产品输出。Sampling64、实际FFT缓冲128及300个显示频率保持，以下对照仅用于诊断，不改变原验收请求：

| 瞄准镜对照 | 最大绝对差 | 对齐超限点 |
| --- | ---: | ---: |
| 原粗网格合成后自然样条 | 0.0111766380 | 198 |
| 省略标量透过率，其他保持 | 0.0111837954 | 不作为关闭依据 |
| 采用此前PSF振幅插值，其他保持 | 0.0111837954 | 不作为关闭依据 |
| 另一频率分母控制 | 0.0149070503 | 未接入产品 |
| 原粗网格合成后改为not-a-knot样条 | 0.0066810864 | 200 |
| 各波长直接到显示频率，仍用原自然样条 | **0.0003824057** | **0** |
| 各波长直接线性插值 | 0.0112198158 | 372 |
| 各波长样条后相加模长 | 0.1265070714 | 780 |

**根因是复色合成前的粗网格线性重采样丢失曲率。** 各波长的物理频率节点不同，先投到某一波长的粗节点再作显示样条，无法恢复原有曲率。修复保持既有自然三次样条，对每个单波长的复数OTF直接查询原显示频率，再按原权重合成，最后求调制度/实部/虚部/相位。没有改变原OTF节点、光学追迹、频率轴、归一化或拟合系数。

网络核对采用[2026 R1 FFT MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_MTF.html)及[Ansys采样说明](https://optics.ansys.com/hc/en-us/articles/42661791331859-What-does-the-sampling-correspond-to-in-wavefront-based-calculations)：计算基于出瞳FFT，无焦截止频率与出瞳直径/波长有关，光谱的物理频率尺度需要分别处理。官方说明不等于公开本例的全部曲线插值细节；本轮顺序缺陷由同请求控制和独立解析回归证明。

## 实现及独立验证

产品只修改[共享Core的MtfAnalysis](../../../../src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs)。兼容模式普通FFT响应改用每个波长自身频率轴的复数样条后再加权；单色、零权重排除、权重全零时的既有等权语义及轴外归零保持。方波沿用原谐波路径，通用非兼容分析和其他MTF扫描入口不切换。既有PSF修复、光瞳/FFT采样及300个显示频率保持。

[独立数学测试](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/FftMtfSpectrumInterpolationTests.cs)用两套不同三节点频率轴和带虚部的OTF，由自然边界`M0=M2=0`推导中点二阶导数与分段闭式期望。它验证四种普通响应、T/S共轭相位、1:3光谱权重和短轴外不外推，不调用产品样条构造期望。另保护单色五种响应及零/未指定权重。所有合成都先处理复数，再求模，不能相加单色模长来匹配参考。

## 五份原请求的结果

读取原归档的每份Workbench请求和原生data/model/captured-settings，指纹与旧账目一致。原门槛为`|ΔMTF| ≤ 0.001 + 0.001 × |reference|`；NRMSE0.01、最小覆盖0.95均保持，实际30条曲线覆盖率全部为1。

| 原文件 / 配置 | 曲线数 | 最大绝对差：前 → 后 | 超限点：前 → 后 |
| --- | ---: | ---: | ---: |
| Afocal Riflescope / 1 | 6 | 0.0111766380 → **0.0003824057** | 198 → **0** |
| Beam Expander / 1 | 2 | 0.0000336081 → 0.0000336081 | 0 → **0** |
| Binocular System / 1 | 10 | 0.0101718827 → **0.0000481166** | 220 → **0** |
| Binocular System / 2 | 10 | 0.0101718827 → **0.0000481166** | 220 → **0** |
| Apochromat prism / 1 | 2 | 0.0014707875 → 0.0014711760 | 0 → **0** |

共30条曲线，638个对齐超限点降为0。“点”指原比较器在物理频率并集上的节点，不是新增光线或镜头。Apochromat最大差增加3.88532e-7但仍满足同一绝对+相对门槛；不能声称所有点误差都下降、与原生逐位相同或任意FFT MTF均高精度。原五项NRMSE分类前后仍为Pass，关闭依据是原逐点门槛，不改写旧完整矩阵。

## 已完成的检查

| 执行 | 通过 / 失败 / 跳过 | 范围 |
| --- | --- | --- |
| mtf-before.trx | 2 / 3 / 0 | 原五请求，实际复现638点超限 |
| mtf-after.trx | 5 / 0 / 0 | 同五请求，全部逐点通过 |
| mtf-targeted.trx | 88 / 0 / 0 | 五原MTF、10项独立光谱控制、既有PSF/振幅/相位/物理与能量保护 |
| mtf-protection.trx | 53 / 0 / 0 | 原有有焦/偏振/采样/取消及FFT MTF最大频率、五种类型保护 |

最终按测试名称去重141个不同用例通过，不与前两轮116/119项相加。默认Release App构建零警告、零错误；两组测试及App使用同一Core二进制。原2668份冻结文件HEAD和工作副本完整性通过。策略LF身份仍为`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。

首次诊断引用内部视场助手导致编译失败，改为诊断反射调用后才执行光学测试；一次离线脚本的权重数组误用整数类型，修正为浮点后才收集拆分。未把这些工具错误算作光学Pass，没有修改产品视场选择。诊断用的原始数组只留本机生成目录，仓库引用旧归档成员与哈希。

## 复现入口与边界

[原请求回归](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftMtfPointwiseTests.cs)可用以下过滤器运行：

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AfocalFftMtfPointwiseTests|FullyQualifiedName~FftMtfSpectrumInterpolationTests'
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore /m:1 /nr:false
```

完整88/53项的TRX身份在机器账目中。[诊断导出](../../../../tests/OpticalSystemDesign.ZemaxComparison.Tests/FftMtfDiagnostics.cs)仅在显式`OPTICAL_FFT_MTF_DIAGNOSTICS`环境下运行。[离线重放](../../../../tools/diagnostics/FftMtf20261029/Diagnose.py)与[证据汇总](../../../../tools/diagnostics/FftMtf20261029/Collect-Evidence.py)均不进入产品计算路径。

未重新采集Zemax、操作桌面、执行完整150项/95文件、原生GUI/CUDA、安装或发布；未关闭其他OPD、惠更斯离焦、自动PSF坐标或相对照度缺口。共享工作区其他功能不据此获得光学认证。
