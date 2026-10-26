# 无焦分析修正与停止点（2026-10-26）

按用户“别跑了，差不多就停，同步到远端”“所有本地修正”的要求，停止进一步计算，整理当前全部本地修正。此记录与[修正前的完整复采](AFOCAL_RECOMPARE_2026-10-26.md)分开；原始捕获、失败记录和容差不改写。

## 已落入代码的修正

- Application 保留无焦惠更斯图像间距的 mrad 单位，有焦系统仍按 µm 转为 mm；补齐显式 `ImageDeltaMilliradians` 的正间距判断。
- 非主波长 OPD 尊重显式选择的参考波长，避免被主波长强制覆盖。
- 高斯标准点列按截断高斯能量分布分配瞳孔位置，避免重要性采样后重复加权。
- 几何/衍射包围能量、PSF 截面、RMS 随视场发布正确的无焦角坐标与量纲；FFT 截面使用同一瞳孔无像差峰值归一化。
- 无焦惠更斯采用既有原生兼容瞳孔网格；离焦参数传入共享 Core，按无焦屈光度的符号计算相位，非偏振传播保留入射瞳权重。
- 混合模式数值单光线报告不再被不可用的布局绘制阻断，单独发布复合端口光程；比较工具修正 NSC 内部明细行解析，诊断映射显式区分透明坐标断点、端口与几何路程。
- 一阶报表区分零光焦度的无限焦距与混合 NSC 无通用近轴等价的情形；后者标为不适用，不伪造数值通过。

显式波长参考与无焦参考平面可参照 [Ansys OPD 文档](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html)；高斯点列的光线密度约定见 [Ansys Gaussian beam 文档](https://optics.ansys.com/hc/en-us/articles/43071099342483-How-to-create-a-Gaussian-beam-profile-in-OpticStudio)。无焦正离焦对应朝镜头方向移动，见 [Afocal Image Space](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Afocal_Image_Space.html)。混合 NSC 的近轴限制见 [Ansys mixed-mode 文档](https://optics.ansys.com/hc/en-us/articles/42661747249043-How-to-model-a-mixed-sequential-non-sequential-system)。

## 已完成的验证与边界

最终已完成的 Release 无焦定向比较测试为 **79 通过、0 失败、0 跳过**。覆盖新增的十组非主波长 OPD 原生曲线、九组高斯因子/密度原生 RMS/GEO、角度轴、Application 间距、离焦非恒定及混合模式数值报告。第一次测试的三项失败及其后修正保留，不累计重复测试。该测试完成后仅补齐显式 mrad 间距正值分支与诊断工具；随后诊断项目 Release 构建成功，零警告、零错误。

停止前的固定角网格阶段 `fixed-v6` 中，FFT PSF、Huygens PSF、Huygens MTF 各五配置均 Pass；Huygens Through Focus 为两项 Close、三项 Difference，不能算全部通过。该阶段具有独立二进制身份，不能冒充最后源码的完整认证。

停止时仍开放：

- 三个几何包围能量离轴/质心比较差异；参考中心的原生定义仍在诊断中。
- 惠更斯离焦 MTF 的剩余数值差异；自动角网格下部分二维 PSF 的采样坐标尚未严格对齐。
- 衍射包围能量和 FFT 截面的少量 Close；五个相对照度原生结果无有效数值。
- 新发布的复合端口光程与最新解析组合尚未完整重放八个双筒单光线案例。
- **本轮没有完成最后源码的 150 项统一复跑，也没有重跑历史有焦 1159 + 52 守卫。** 全局比较策略和原始参考未放宽；不能声称全部精度问题已关闭或有焦通过项已重新认证。

实际出瞳坐标和完整圆边界两种实验因结果变差已撤回。诊断中将质心改成主光线的结果只用于定位，不计为同设置通过。最终源码未启用这些实验。

## 可追溯资料

- [停止点记录、测试和阶段结果](../validation/rechecks/2026-10-26/afocal-analysis-repair/README.md)。
- [独立原生数值夹具](../validation/zemax/2026-r1/afocal-analysis-repair-2026-10-26/)与 `AfocalAnalysisRepairTests`。
- `tools/diagnostics/AfocalRepair20261026` 保留新建输出目录的重放入口；`focal-guards` 的历史通用 Zernike 请求直接使用原共享 Core 定义，不恢复已移除的桌面重复入口。该守卫本轮未执行。
- 本机详细中间数据保留在 `artifacts/validation/afocal-repair-20261026`，修正前原始数据的已归档副本见 [afocal-recompare](../validation/rechecks/2026-10-26/afocal-recompare/README.md)。

本轮按停止指示不再启动计算、桌面构建或安装流程；代码同步不代表已安装应用或云端安装包更新。
