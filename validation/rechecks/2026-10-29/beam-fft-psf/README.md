# 扩束镜 FFT PSF 参考峰值修复（2026-10-29）

基于`4ec6abab`的共享工作区。仅修复二维标量FFT PSF兼容模式的无像差参考峰值；冻结原生数据、处方、坐标、请求、掩码和原比较容差保持。没有启动Zemax或控制用户桌面。机器账目见[verification.json](verification.json)，红/绿TRX及完整性报告集中在[evidence.zip](evidence.zip)，不重复保存原生数组。

本页保留先行峰值阶段的原数值。后续已在相同两请求上关闭下述48/140点形状残差，见[振幅插值修复](../beam-fft-psf-shape/README.md)；本页“仍开放”描述的是先行阶段，不代表当前状态。

## 官方依据与根因

查阅Ansys **2026 R1** 官方[FFT PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)及[Apodization Type](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Apodization_Type.html)（访问日期2026-10-29）：未勾选Normalize时，PSF强度以无像差PSF峰值为参考；勾选时才把实际峰值归一为1。高斯照明的因子描述振幅沿归一化瞳孔半径的衰减，G=1的圆边缘振幅为1/e、强度为1/e²。手册支持上述定义，不视为公开了原生内部全部离散实现。

冻结扩束镜`GFAC 2 1`表示高斯振幅`A=exp(-2ρ²)`。共享Core先以有效瞳孔平均功率规格化振幅，再把FFT强度除以有效节点数`M²`。这使同一高斯瞳孔即使没有像差也只有`(ΣA)²/(M ΣA²)`的峰值：它度量了相对均匀瞳孔的相干效率，不能当作相对同一高斯瞳孔的Strehl。二维PsfAnalysis遗漏了这项参考换算；截面原来另算ignoreOpd的理想PSF后相除，因此此前截面可通过而二维幅度仍错。

独立零像差平面孔径控制直接复现：G=0正常，G=0.5/2/4分别错误损失约2.05%/23.96%/51.93%。这不依赖对原生峰值拟合。实际扩束镜自动/固定网格的参考比例由各自追迹振幅直接得到，精确值见机器账目。

## 实现范围

- [共享FFT引擎](../../../../src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs)保留原低层强度数组，另发布`UnaberratedPeakRatio=(Σ|pupil|)²/M²`。相位仍进入实际FFT，理想参考只去掉相位、保留实际振幅和接纳节点；不追加追迹，不更改探针或插值。
- [二维分析](../../../../src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs)仅在`zemaxCompatible && !usePolarization`时，以选中波长的非相干加权理想峰值作参考。Normalize=true继续按实际峰值归一；无像差参考不会把真实像差损失抹成1。
- 通用低层FFT、非兼容分析、实验性偏振功率、MTF及圈入能量的输入强度契约保留。未修改比较器或容差文件。策略的LF内容SHA-256仍为`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`；Windows检出换行的单独字节身份也记录在账目中。

## 两份原请求的实际结果

同一`Beam Expander.ZMX`、配置1、视场1、波长1、Sampling=64、Display=128、Normalize=false、UsePolarization=false，分别使用原自动角网格与原固定0.005 mrad网格。每份16384点，坐标严格对齐、覆盖率1，无缩放网格、平移、峰值拟合或重采样。

| 项目 | 自动网格 | 固定角网格 |
| --- | ---: | ---: |
| 原Core峰值 | 0.766261270483 | 0.760325437203 |
| 冻结Zemax峰值 | 0.999979496401 | 0.999977644411 |
| 修复后Core峰值 | 0.999979500615 | 0.999977653781 |
| 修复后峰值绝对差 | 4.21412e-9 | 9.36971e-9 |
| 修复后全图最大绝对差 | 3.14182e-4 | 5.14197e-4 |
| 修复后NRMSE | 1.33997e-5 | 2.76100e-5 |
| 原逐点绝对+相对门槛超限点：前→后 | 529→48 | 853→140 |
| 修复后窗口积分相对差 | +0.08328% | +0.13609% |

**关闭的是23%参考峰值缺陷；非中心形状差异未关闭。** 两项正式比较前后均为原规则Pass，但没有因此宣布全图逐点达标。新增回归将原`absolute=1e-5`、`relative=0.001`明确用于峰值，同时输出全部点的超限计数和窗口积分；没有改动原`NRMSE=0.01`或其他门槛。首次尝试的更强“所有点均达标”断言仍在修复后失败，原TRX保留，不能将最终峰值专项通过冒充该更强目标通过。有限窗口积分也不是绝对辐射功率。

现有另一份32/64原生Real/Imaginary控制的离线逆变换显示非均匀振幅与简单圆内高斯节点间存在较小差异；它属于另一请求，不能据此改动当前两份请求的振幅或猜测原生插值。未为消除上述48/140点残差调整高斯因子、瞳孔边界、相位或归一化系数。

## 回归与失败记录

| 最终执行 | 通过 / 失败 / 跳过 | 范围 |
| --- | --- | --- |
| targeted.trx | 68 / 0 / 0 | 正式PSF契约、两份扩束镜峰值、解析FFT/Huygens、能量及6项新归一化测试 |
| spectral-final.trx | 8 / 0 / 0 | 上列6项重复验证，另2项多波长权重及截面无重复归一化控制 |
| protection-focused.trx | 46 / 0 / 0 | 原有有焦、原生输入、偏振、采样、取消及高NA回归 |

最终按完整测试名称去重 **116个不同用例通过**，不是116项新增Zemax对比。新测试包含独立离散Parseval、Gaussian振幅相干和、附加线性相位的独立傅里叶期望、通用功率契约、多波长和Normalize开关。原2668份冻结文件HEAD及工作副本全部匹配。同一Core二进制SHA-256见账目，不与旧Core的150项矩阵累计覆盖。

初轮`before.trx`的两个原生夹具源文件未物化；重新物化后`before-optical.trx`才复现真实23%差异。修复后`after.trx`保留全逐点门槛的两项失败。补充`spectral.trx`曾错误要求截面插值后每点与二维栅格原样相等；删除这一不符合现有截面接口的假设，改验独立理想Strehl及无重复归一化，未修改产品插值。所有中间记录随包保存。

原主测试项目首次构建被共享工作区另一项报告修改阻断（缺少DesignReportWordWriter/DesignReportPdfWriter、ReportExportResult.MarkdownPath），未执行该轮主测试；本次未修改相关文件。保护检查改用[独立工程](../../../../tools/diagnostics/BeamFftPsf20261029/FocusedFftRegression.csproj)直接编译同一5份原测试源码与断言，仅引用Core/Application；MS-L7夹具来自已提交的同字节原捕获。依赖锁定还原通过，未执行在线漏洞审计。该结果不称为默认App完整构建通过。

## 复现

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AfocalPsfContractTests|FullyQualifiedName~FftPsfNormalizationTests|FullyQualifiedName~AfocalIndependentPhysicsTests|FullyQualifiedName~AfocalFftEnergyTests'
dotnet restore tools/diagnostics/BeamFftPsf20261029/FocusedFftRegression.csproj --locked-mode
dotnet test tools/diagnostics/BeamFftPsf20261029/FocusedFftRegression.csproj -c Release --no-restore
```

完整矩阵、95文件、原生GUI、CUDA和安装器未在此轮重验；没有发布或推送本次修复。共享工作区另有报告/Delano工作，本账目不认证其功能。[汇总器](../../../../tools/diagnostics/BeamFftPsf20261029/Collect-Evidence.py)仅整理完成的TRX与哈希，不执行产品计算或产生参考数值。
