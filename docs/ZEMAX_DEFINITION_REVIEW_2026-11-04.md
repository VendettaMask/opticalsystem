# 对照Zemax说明的定义核查，2026-11-04

本轮新增确认**两处设置没有按其定义生效的实现问题**，以及一个波前图入口的功能差距。只进行了官方文档、源码核查和后台控制复现，未修改产品计算代码、处方、原比较数据或容差，也没有新的原生捕获。此前图像模拟52项新增Pass保持其原执行身份。

## P1：中间面分析没有完整生效

[OpticStudio 2026 R1 OPD说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html)规定，中间面计算在原系统副本上进行：依共轭类型转换像高视场；面在光阑之前时处理临时光阑及孔径；有焦OPD、PSF、MTF等在选定面之后建立临时近轴像面。无焦及几何点列等有各自不同规则，不能统一按“截到这一面”处理。文档同时提示需要光线瞄准的系统有适用限制。

[Wavefront Map说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)也明确Surface选择计算面，并指向上述中间面规则。

当前实现及复现：

| 入口 | 实际代码/输出 | 结论 |
|---|---|---|
| OPD | `_surfaceNumber`只写结果标注，`ResolveAnalysisOptic`只处理孔径开关；仍追迹完整系统 | 选定面未生效 |
| 波前图 | 用原Optic生成波前，面号仅写SurfaceNumber/SurfaceLabel | 选定面未生效 |
| FFT PSF、MTF | 复制并截断面列表，没有临时近轴像面和前光阑处理 | 中间面准备缺口 |

以未改变的图像模拟示例2为控制：OPD选择最终面、面2、面12、甚至不存在的面999，均Success；每次82个点的物理轴、单位和数据哈希完全相同。波前图同样四种选择均Success，709个点哈希相同。**999只是错误标注，没有验证或拒绝。**

FFT PSF/MTF选择光阑前面2会抛出“Float by stop size requires an aperture stop”；面12的截断结果因约393.536波局部步进而Unavailable。它们说明当前准备方式的实际后果，不证明原生同一请求必定能算出有效结果；没有取得这些中间面的原生数值认证。

源码：[OPD入口](../src/OpticalSystemDesign.Core/Analysis/Wavefront/OpticalPathDifferenceAnalysis.cs)、[波前入口](../src/OpticalSystemDesign.Core/Analysis/Wavefront/WavefrontAnalyses.cs)、[FFT PSF/MTF入口](../src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs)。建议用共享Core的分析副本准备能力统一处理，保留原处方及结果相关设置；不能只修改标签或一律增加传播距离。

## P1：有焦OPD参考模式被忽略

[官方参考设置说明](https://optics.ansys.com/hc/en-us/articles/43071050784147-How-do-OPD-Reference-settings-in-System-Explorer-Advanced-section-affect-wavefront-calculation)区分出瞳参考球与像面参考；对有焦系统，[Absolute/Absolute 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Absolute_Absolute_2.html)不加回溯参考球的修正，应直接比较主光线与瞳孔光线到像面的光程。

导入和保存已经保留ReferenceOpd，但[共享波前路径](../src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs)只在无焦分支区分模式；有焦分支始终减去参考球回溯光程。因此有焦Absolute、Absolute2、Infinity也会沿用出瞳结果。

实际控制使用同一镜头的复制对象，分别选择四种模式，原系统不变：四份OPD曲线哈希完全相同。对主波长、归一化视场(0,1)、瞳孔(0,0.8)，当前Absolute返回 **3.68595191395 waves**；依据官方Absolute定义，用共享追迹器的像面含相位光程作差得到 **−15.0818884596 waves**，相差 **18.7678403735 waves**。

后一个值是独立定义探针，**不是新增的Zemax原生捕获值**。不能把这个公式用于ExitPupil或Infinity的验收，也不能把有焦结论外推到已经分支处理的无焦模式。修复时需明确OPD/波前显示参考与物理衍射相位之间的关系，避免连带改变既有PSF/MTF契约。

## P2：波前图缺少主波长参考入口

官方Wavefront Map的Reference To Primary决定使用所选波长还是主波长的参考球，以显示横向色差。当前波前图入口未接入这个选项；现有ReferenceChiefRay只影响Reference/ReferenceChiefRay结果标注，没有传入波前生成，不能当作主波长参考球开关。共享波前方法已有参考波长参数，但当前波前图调用传null。

这是入口能力差距，不等于当前默认单色波前都算错。未新增原生勾选/不勾选对照，后续应独立验证选项语义和完整输出。

## 与现有差异的关系和建议顺序

95份菜单源文件逐份核查，全部为 `ROPD 2`（Exit Pupil），哈希与上一轮源审计一致。因此非默认参考模式问题不能直接解释当前默认参考下的原矩阵差异。示例2/5剩余六项离轴OPD也都请求最终像面；这轮没有证据将它们归因于中间面缺口。

这些六项仍为4 Close/2 Difference，最大差约0.0286–0.0364波；应继续区分近轴面Mode1相位积分、参考球与光瞳定义。[近轴面官方说明](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html)说明模式及适用精度限制，但不能仅凭限制文字将残差判为正常或宣告根因。原生已报采样不足的两项能量仍不升级。19项旧Pass采样适用性问题继续按[既有规则核查](FFT_SAMPLING_RULE_AUDIT_2026-11-04.md)处理，不通过调阈值恢复。

下一步建议先修中间面副本准备，再补全有焦参考模式，随后补波前主波长参考选项；它们分别有明确的失败复现和文档定义。现有六项离轴OPD需继续单独定位，不能合并宣称已解决。

最终执行Core仍为 `1af8b46ae62b581a85b05b3954f0d8dd1a1414e989d1c826feaed272836036a3`。本轮20次Workbench请求、4组共享波前/像面光程探针均为诊断控制，不计新增通过测试或原生认证。见[完整数据、来源及复算入口](../validation/rechecks/2026-11-04/zemax-definition-review/README.md)。
