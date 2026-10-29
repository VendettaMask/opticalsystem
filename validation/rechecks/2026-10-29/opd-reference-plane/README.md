# 无焦 OPD 参考平面位置修复（2026-10-29）

在原处方、原40区间/41节点扇形、原选择器、原生参考和全局容差下，复现瞄准镜边缘视场486 nm的最大差 **0.03917309038750538 wave**。共享Core修复后降为 **8.10698273490118e-6 wave**；656 nm同类残差从 **0.006425972535672786** 降至 **1.9170150731273594e-6 wave**。原20个OPD请求/40条扇形全部满足原逐点门槛和NRMSE门槛，11个对齐超限点降为0。

## 根因与反证

[Ansys 2026 R1 OPD说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html)区分单波长与全部波长的参考主光线；[无焦参考说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Afocal_Image_Space.html)规定参考平面垂直于参考主光线，并说明改变平面位置通常会改变非理想准直波束的OPD。

本批冻结原生数组进一步确认：**参考主光线的波长决定朝向，但系统近轴出瞳的轴向位置保持以主波长计算**。这是从所列原生捕获与分离变量试验得到的结论，不把文档未明确描述的内部算法当作通用规格。

原`WavefrontEngine.GenerateAfocalChiefRay`把二者绑定，按`referenceWavelengthNanometers`计算出瞳位置。瞄准镜主波长587 nm的出瞳位置约为-5.59734e-11 mm，486/656 nm的色散出瞳位置分别为-0.3979186812/+0.2758259955 mm。显式单波长OPD因此错误移动了参考平面，误差在偏离准直较大的边缘节点放大。

保持实际光线和所选主光线朝向，仅将平面平移回主波长出瞳位置即可预测几乎全部残差。对单位主光线方向`c`、单位样本方向`u`、主光线方向上的平移量`δs`，几何独立诊断使用：

\[
\Delta W=\frac{n\,\delta s}{\lambda_{mm}}\left(1-\frac{1}{u\cdot c}\right),
\qquad \delta s=\frac{z_{p,primary}-z_{p,selected}}{c_z}.
\]

| 边缘视场 | 原最大差/wave | 只修正轴向位置的离线预测 | 错改为主波长主光线的反例 | 最终共享Core |
| --- | ---: | ---: | ---: | ---: |
| 486 nm | 0.03917309039 | 8.10696454e-6 | 1.0044638564 | 8.10698273e-6 |
| 587 nm | 3.24435869e-6 | 3.24435869e-6 | 3.24435869e-6 | 3.24435869e-6 |
| 656 nm | 0.00642597254 | 1.91704037e-6 | 0.05850881247 | 1.91701507e-6 |

直接换成主波长主光线会显著增大误差，排除了“全部统一成主波长参考”的错误修法。离线投影仅供诊断，不进入产品计算，也没有拟合原生系数。

## 实现与范围

产品修改仅把普通无焦ExitPupil分支的轴向位置计算改为现有无参`EstimateExitPupilLocation()`，由该方法使用系统主波长。保留所选参考主光线、参考点在该主光线上的交点构造、入射相位及OPD符号。Absolute、Absolute2、有焦参考球和混合棱镜原有分支继续保留。

原OPD门槛仍为绝对 **1e-5 wave** 加相对 **0.001×|参考值|**，NRMSE **0.003**，最低覆盖率 **0.95**。20个请求均覆盖1.0，全体最大差为8.10698273e-6 wave。原历史聚合NRMSE已是Pass，本次新增逐点守卫才显式阻止局部超限被整体指标掩盖。最大差并未降为零。

新增测试直接读取原归档中的canonical请求和原生数组，逐个核对相同配置、每侧20条/每扇41点、覆盖率及原策略。独立几何控制覆盖所选/默认参考、三种参考模式、源处方不变和有色散的正反例。

## 验证与证据

- OPD及相关FFT PSF/MTF/能量定向回归 **117/117**；既有有焦OPD、瞄准、渐晕和默认主波长参考保护 **54/54**，共 **171个不同用例**，零失败/跳过。不累计中间重复运行。
- 默认输出的Release App构建成功，零警告/错误。两组测试与App的Core SHA-256一致：`bb32743a712035a0e5e618370ed353940bd226dbcd3bef48358c5114382680ff`。
- 2668份冻结文件在Git HEAD和工作副本均保持；全局策略LF SHA-256仍为`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。
- [权威验证账目](verification.json)记录20组请求/指纹、处方和原生归档成员哈希、前后指标、二进制身份及限制；[证据归档](evidence.zip)保存TRX、诊断分解、追迹重放数据、源代码快照、构建日志和完整性结果。原生数组继续引用既有归档，不复制或改写冻结捕获。
- 中间诊断工具的坐标/JSON序列化失败、首次控制编译问题及修正后的两项真实逐点失败均保留。`opd-baseline.trx`才是干净的修复前光学基线；`opd-final.trx`和`opd-protection.trx`是最终通过结果。

后台验证没有启动或操作桌面。没有重新捕获原生数据、重跑完整150项或95文件、执行GUI/CUDA验收、提交发布或安装。其他开放项见[当前问题清单](../../../../docs/OPEN_ISSUES_2026-10-08.md)。

复跑入口：比较测试中的`AfocalOpdPointwiseTests`/`AfocalOpdReferencePlaneTests`及既有OPD、FFT守卫；保护项目为`tools/diagnostics/Opd20261029/FocusedOpdRegression.csproj`。`OPTICAL_OPD_DIAGNOSTICS`可保存诊断，封存脚本`tools/diagnostics/Opd20261029/Collect-Evidence.py`只读取已完成运行，不执行产品光学计算。
