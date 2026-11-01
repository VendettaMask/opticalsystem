# 已知观察条件的 XYZ 校准与完整相机链路

后续进展见[全入射角对数重建与绝对XYZ校准](XYZ_LOG_RECONSTRUCTION_2026-11-01.md)：新插值下550 nm全部八入射角通过，Mystic/Cayman新增通过原开发留出门槛，Blue Acrylic仍开放。下文保留本阶段原范围和失败结论。

后续[实测角表与BTDF验证](XYZ_MEASURED_ANGULAR_2026-11-01.md)增加独立支持域9/v17，完成13组明确入射切片的真实角表链路；剩余三种XYZ校准仍未达标。本页保留正值模型8/v16的原结果与范围。

2026-11-01。共享 C# Core 新增显式观察条件的光谱投影和非负、强度齐次的经验颜色校准，使用 **MeasuredBsdfGrid/8 / STAROPT v16**。按明确 CIE 1931 2° / D65 条件从 Cornell 实测光谱重新推导的 XYZ，Garnet Red 和 Krylon Blue 在 550 nm 通过原留出门槛并成功导出；另三种材料失败，保留拒绝。最终 **295/295 定向测试**、四组采用上述正式校准的 NSC→探测器→电子→RAW/ISP 控制、23组旧 White 功率复算通过。[完整证据与执行身份](../validation/rechecks/2026-11-01/xyz-conditions/README.md)

## 原测量条件与本轮声明

[Cornell 测量说明](https://bowers.cornell.edu/computer-graphics/data)明确：发布 XYZ 是由实测光谱在 D65 下计算，非独立 XYZ 仪器测量；该说明没有明确观察者角度。原 Krylon MATLAB v4 文件也没有观察者/照明元数据。[同团队 MDL 颜色约定](https://www.graphics.cornell.edu/online/formats/mdl/chunks/color.html)使用 CIE 1931，仅为间接线索，不能代替本数据的直接元数据。直接下载该 MDL 页得到空正文，已记录而未用作证据。

从五材料中每次用四材料恢复31维光谱→发布XYZ核，留出材料 RMS 为3.44e−7至8.01e−7，支持共同线性转换的推断，仍不能核实历史观察者与归一化。**原发布 XYZ 的观察者保持未核实，不重新贴上已核实标签，也不解除原导出限制。** [条件审计](../validation/rechecks/2026-11-01/xyz-conditions/source-conditions-audit.json)

本轮创建新的派生XYZ，条件由计算本身确定：官方 [CIE 1931 2°名义色匹配函数](https://cie.co.at/datatable/cie-1931-colour-matching-functions-2-degree-observer)、[CIE D65](https://cie.co.at/datatable/cie-standard-illuminant-d65)，明确400–700 nm、范围外为零、该范围白色Y=1。它不是完整可见光范围的默认标准色值，也不是新独立仪器实测XYZ。官方原表SHA分别为：

```
CMF  fa663e3535a7e0763a745993a1f0a192eb0275ac46ad2d1befd7626841e713c1
D65  e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb
```

新增 `XyzSpectralProjector` 接收调用方明确提供的CMF/SPD表，检查严格波长轴、非负有限值及全范围覆盖，拒绝外推。对实测光谱、CMF、SPD的合并分段用两节点Gauss准确积分三个线性因子的三次乘积；Kahan累加。`XyzColorimetricConditions` 保存名称、原文件SHA、实际数值表SHA、波段和 `PiecewiseLinearProductGauss2WhiteY/1`。产品不下载外部表，也不依赖商业运行时。

1 nm与半nm细分结果最大差1.47793e−12；旧已知条件线性拟合仍失败。原MAT与ASTM光谱最大舍入差4.99981e−7 sr⁻¹；换用MAT全精度后，旧线性模型NRMSE仍为0.0337871、最大差0.0481493 sr⁻¹。积分细分和文本精度不是该失败的主要原因。[全精度诊断](../validation/rechecks/2026-11-01/xyz-conditions/final-mat/mat-fit.json)带显式占位来源标记，不用于正式导出。

## 正值颜色映射及功率约定

`FitPositiveColorTransfer` 使用归一化三维XYZ颜色选择近邻，用非负权重平均训练节点的 `fλ/(X+Y+Z)`，再乘输入总XYZ。它保证输出非负、输入强度缩放时输出同比缩放；黑色映射零。零XYZ对应非零训练辐射度明确拒绝，相同颜色精确命中时平均全部同色节点，排序和并列处理确定。没有裁剪负预测、改变实测值或材料名称分支。

邻居数量是显式参数，本轮所有材料固定6；初始诊断同时记录1/6/12，未按材料自动选优。这是有声明颜色域的经验映射，不声称任意XYZ唯一恢复光谱，固定近邻集合交界也不承诺光滑。只保存训练标签；适用域在拟合前由未标注输入颜色确定，可包含验证输入特征，不读取留出辐射度标签。原秩3、跨波段样本角色和数量限制继续检查。修改验证标签不改变模型摘要，但会使验证失败、禁止导出；更改条件、颜色域或模型节点也不能沿用旧证据。

```csharp
var fit = XyzRadiometricCalibrator.FitPositiveColorTransfer(
    pairedObservations, provenance, policy, gamut,
    projector.Conditions, neighbourCount: 6, cancellationToken);
var calibration = fit.CreateCalibration(name,
    projector.Conditions.Observer, projector.Conditions.Illuminant);
```

非线性映射不能直接作用于总XYZ TIS。导入先按各通道独立TIS恢复绝对角表节点，检查域并映射每个节点，**以校准后辐射度角表的余弦投影积分得到新TIS**。超出被动功率1或出现非法值明确拒绝，不重新缩放修正。方向密度、积分、Sample/Pdf、波长凸混合及介质处理复用普通Zemax网格实现。非恒定角分布测试用独立逐节点映射和分段积分验证，也明确证明“转换总TIS”会给出不同错误结果。近邻搜索在构建辐射度网格时执行，不在每条光线采样时执行。

模型最多4,096节点、1–32波段，节点×波段不超过65,536并计入冻结载荷预算；CMF/SPD各最多4,096点，派生角网格沿用131,072节点预算。正值校准JSON上限4 MiB，旧线性JSON仍为64 KiB；不把新容量放宽到旧文件。模型、条件、颜色域和来源均进入新摘要。

## 留出结果及适用范围

550 nm、原门槛 **NRMSE≤0.01、最大绝对差≤0.01 sr⁻¹、信号底1e−6** 保持。每材料1,439方向，按完整四角G17键排序，每第三项留出；959训练/480验证。结果为本轮新派生XYZ：

| 材料 | 留出NRMSE | 最大绝对差 sr⁻¹ | 正式导出 |
| --- | ---: | ---: | --- |
| Garnet Red | 0.00531860 | 0.00730652 | 通过 |
| Krylon Blue | 0.00642337 | 0.00801225 | 通过 |
| Blue Acrylic | 0.05140121 | 0.00189514 | 拒绝 |
| Mystic（文件mystique） | 0.26589344 | 0.07720890 | 拒绝 |
| Cayman | 0.05493500 | 0.01269899 | 拒绝 |

[逐材料原始指标、配对CSV、校准JSON](../validation/rechecks/2026-11-01/xyz-conditions/final-positive/summary.json)。通过两例的最大相对误差仍分别4.70076和37.95693；原门槛没有该项，未隐藏暗信号误差。插值式模型的训练误差接近零不作为独立证据。原划分参与开发诊断，不是未参与开发的新盲测；通过范围仅为上述材料、550 nm及声明颜色域。其余三例、历史发布XYZ和先前线性失败均保留。

## 协议与完整链路

新 `PositiveModel` 明确区分非线性正值模型，Grid/8贯穿对象、STARRDB反射/透射、相机来源冻结、界面协议标签及STAROPT v16。工程v15或更低夹带Grid/8拒绝，包括禁用对象。旧Grid/1–7不升级，Grid/7仍v15、White Grid/6仍v14；容器2、顺序快照6保持。桌面复用校准JSON入口，拟合仍为Core/API能力，没有新增桌面拟合向导。

四组最终Workbench控制使用两份已通过校准，各做BRDF/BTDF、8,192条方向样本；颜色取未用于训练的实测方向输入。角形状为**合成均匀控制**，总XYZ固定0.1 sr⁻¹、550 nm源功率1e−9 W，不能称完整实测角分布或真实BTDF测量认证。BTDF探测器明确双面接收，BRDF为正面接收；此前透射零信号是控制探测器朝向配置问题，相关失败保留。

四组预测散射份额分别约0.09154527和0.09010161；最大相对源功率能量差5.49144e−14、账本闭合差4.03011e−14，方向均值通过原六西格玛统计门槛。按普朗克常数、光速、550 nm、10 ms和明确解析QE独立计算，最大电子差9.09495e−13 e⁻；RAW/ISP非零。保存工程和实验后删除控制源/临时校准副本，重新打开v16检查数据摘要，再在空场景重放冻结实验，电子/RAW/ISP四组逐值一致。[最终四组结果](../validation/rechecks/2026-11-01/xyz-conditions/final-real-chain-retry/summary.json)

## 验证身份与剩余范围

最终定向295/295、0失败/跳过（1分33秒），新增15项，覆盖解析精确积分、条件/摘要篡改、域外拒绝、训练/验证隔离、齐次性/同色并列、非被动拒绝、非恒定角能量与方向统计、固定轴、BRDF/BTDF、STARRDB、v16防降级及Grid/7冻结ZIP重放。默认Release App含两个内置模块，App/测试/新诊断/White诊断最终构建均零警告错误。

15个二进制、136个相关源/输入文件运行前后无漂移，App/测试/诊断执行副本一致。前297+533+163共993份冻结文件逐一SHA核对未变。23组旧White原生捕获功率复算仍通过，最大场景差1.45344 µW，没有本轮新原生GUI追迹或完整二维方向商业对标。[汇总](../validation/rechecks/2026-11-01/xyz-conditions/final-verification-summary.json)、[身份检查](../validation/rechecks/2026-11-01/xyz-conditions/final-run-integrity.json)

保留本轮诊断构建磁盘不足失败；只清理本任务失败构建的Skia副本，并让诊断复用已构建Application程序集，正式App依赖保持。最终独立链路首次受系统临时目录重命名权限限制，记录异常；仅把该进程TEMP/TMP指向工作区内专属目录后，同一产品/驱动字节四组通过。没有为环境失败改动产品或放宽门槛。全程后台，未运行全仓测试、安装/发布、原生桌面验收。

仍开放：原发布XYZ观察者直接证据、其余材料的合格校准、独立新盲测及更多波长、完整实测角分布和实测BTDF端到端，以及White完整二维方向、固定轴/非等介质的商业逐值认证。本轮两例通过不覆盖这些范围。
