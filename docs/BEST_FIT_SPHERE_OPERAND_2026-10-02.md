# BFSD 最佳拟合球面接入

本页保留第二十八批 BFSD 阶段记录；当前操作数计数与后续合并回归见[第二十九批](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。

2026-10-02，第二十八批。新增共享 `SurfaceBestFitSphere` 及 BFSD 本地评价、编辑保存和优化路径。目录变为 **301/383 项受限执行、82 项兼容保留**：75 项已知功能待接通、2 项定义待核实、5 项官方 Unused。另 4 项本程序扩展不计入官方目录。这里的“执行”不代表全部工作模式和原生数值已验收。

## 当前定义与范围

[官方 BFSD 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Best_Fit_Sphere_Data.html)指定最小体积准则和八类结果；[矢高表说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Sag_Table_1.html)说明材料侧及拟合方向。当前目标版本仍为项目固定的 2026 R1；引用页面版本与捕获版本分别记录，不能从网页推定实机行为。

本地六槽为 `Int1=Surf`、`Int2=Data`、`Data1=MinR`、`Data2=MaxR`、两项未用。MinR/MaxR 同时为零时用 `0..净半口径`，否则要求有限 `0≤MinR<MaxR`。坐标是表面局部径向坐标，不是法向距离或光瞳归一坐标；指定环带用于拟合，物理通光孔径不再次裁切这一加工轮廓。

| Data | 当前返回值 | 单位 |
| --- | --- | --- |
| 0 | 拟合球面曲率 | mm⁻¹ |
| 1 | 拟合球面半径 | mm |
| 2 | 球面顶点沿局部 +Z 的偏移 | mm |
| 3 | 最大去除深度 | mm |
| 4 | 去除体积 | mm³ |
| 5 | 最大绝对径向斜率差 | 无量纲 |
| 6 | 去除深度 RMS | mm |
| 7 | 径向斜率差 RMS | 无量纲 |

当前支持平面、标准圆锥、偶次与奇次非球面。材料方向必须由一侧 AirMaterial、另一侧材料明确确定；反射、两侧均空气或均材料、非旋转面明确报错。没有用折射率阈值、材料名称或 Z 顺序猜测加工方向。平面曲率为零；无限半径不是有限评价结果，Data=1 明确失败。

官方 BFSD 表格对 Data=4 使用 material to use 的措辞，而相邻矢高表说明及捕获给出 Volume To Remove。本地当前计算去除体积；原生 BFSD 行的确切列、正负偏移约定和各数据值仍待捕获核对，不能用分析文本替代这一验证。

## 共享算法与计算边界

`SurfaceBestFitSphere.MinimumVolume` 直接读取正式几何矢高和 `SurfaceDifferentialMetrics` 的解析导数。每个候选曲率先找到轮廓与球面差值的包络偏移，使球面毛坯位于空气侧；材料去除深度由有符号差值计算。

去除体积复用 `ElementVolumeMetrics` 的正式径向面积积分：球面使用解析积分，非球面使用有误差控制和预算的自适应 Simpson。对实际表面和候选球面分别积分再结合偏移，不使用实验室局部公式。

候选曲率用外径归一化，64 区间扫描后细化离散局部极小区间。1000 个径向区间用于寻找导数变号区间，再二分细化差值驻点；深度和斜率 RMS 采用等径向梯形权重，最大斜率差取这组径向样点。曲率候选最多 4096 个，半球边界、无有限面形、负体积异常和取消均明确失败。已知纯球面/平面的零体积解直接保留，避免数值搜索扰动精确解。

这是有预算的数值搜索，尚不能证明任意高阶、窄环带或强振荡轮廓的全局最优，也没有证明原生 RMS 权重、采样及符号完全相同。工作预算和当前取样是本程序实现细节，不是 Zemax 通用规格。不存在失败后切换到基准球面或零结果的近似回退。

## 编辑与文件

帮助页发布中文定义、参数、单位和上述限制。原始槽位为权威，未用槽位锁定并保存；本地行可以经过编辑器、快照与 STAROPT 往返。原生 ZMX BFSD 行保留原记录、禁用且逐行标为兼容，保存再读不会自动升级。强行启用保留行仍返回错误，不能产生成功零值。

## 验证

新增 26 项测试，包括正负球面和材料侧、平面无限半径、四次非球面的独立解析包络/数值体积检查、奇偶面形反号对称、长度/面积/体积尺度关系、环形范围、输入错误、取消、编辑与文件往返，以及 BFSD 曲率驱动正式 DLS 半径优化。首次定向 24/26，两个失败来自测试报告数值解析包含句号、合成 ZMX 缺少孔径字段；修正夹具后 **26/26**。

默认 Debug/Release 输出均构建成功，合并回归各 **2497/2497**（前批 2471 项与本批 26 项的并集），零失败/跳过，无编译警告或错误；格式检查与 git diff --check 通过。29 份选定基线资产与已提交 HEAD 逐字节一致，两个配置实际执行的源文件和矢高表副本也逐字节相同。命令与证据见[构建记录](BUILD_AND_RELEASE.md#顺序操作数第二十八批复验2026-10-02)及[机器摘要](../artifacts/validation/sequential-operands-twentyeighth-20261002/verification.json)。

固定 `123456.ZMX / OpticStudio 2026 R1` 的 `078-sagtable/data.txt` 捕获仅针对第 1 面球面：原生文本 BFS 半径 `40.02622 mm`，当前值 `40.02621921556379 mm`，差约 `−7.84e−7 mm`，落在原文本末位半单位 `5e−6 mm` 内。偏移为零，去除量及斜率差接近舍入零。该证据验证球面退化情况，不验证任意非球面最小体积拟合，也不是原生 MFE 行捕获。没有改写捕获。

原先 EFNO 的约 1.227% 轴上差异、默认 RI 的既有门槛和 FCGT 的 1e−5 mm 目标及 Difference 状态均保留。没有新的 Zemax 捕获、全产品/实验室验收或 Optiland 对照，也没有通过界面截图宣称光学数值准确。

## 仍需完成

BFSD 的原生列、非球面捕获、RMS 权重、偏移与体积语义，以及更完整的加工方向和面形支持仍待核实。共享拟合后续还需用于 SSAG/SSLP/SCRV 和 DSAG/DSLP/DCRV 的移除最佳拟合球面模式，本批没有把那些未完成模式标为完成。

本次还复核了两个实际光线项：官方 [HHCN](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)需要比较真实交点和常规矢高分支，但当前 StandardGeometry 求交明确验证常规矢高分支，不能据此返回恒零并称已实现。HYLD 的公开分类页说明所需光线量，尚不足以确定完整公式；需要进一步依据原始论文或原生捕获核实，不能用入射角平方冒充高良率贡献。两项继续保留在实现任务中。

源码：[共享球面拟合](../src/OptilandWorkbench.Core/Services/SurfaceBestFitSphere.cs)、[共享体积积分](../src/OptilandWorkbench.Core/Services/ElementVolumeMetrics.cs)、[评价入口](../src/OptilandWorkbench.Core/Optimization/MeritFunction.ManufacturingProfiles.cs)、[验证用例](../tests/OptilandWorkbench.Tests/BestFitSphereOperandTests.cs)。
