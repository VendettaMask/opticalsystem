# 实测角表、BTDF 链路与三种 XYZ 校准的未通过结果

后续进展见[全入射角对数重建与绝对XYZ校准](XYZ_LOG_RECONSTRUCTION_2026-11-01.md)：新插值下550 nm全部八入射角通过，Mystic/Cayman新增通过原开发留出门槛，Blue Acrylic仍开放。下文保留本阶段原范围和失败结论。

2026-11-01。本阶段**没有解决剩余三种材料的XYZ校准门槛**，不能报告该目标完成。已增加Cornell光谱角表的明确支持域导入，修复法向单旋转角的合法四维Zemax表导入，完成五种材料10°/20°切片和三份VIAVI实测BTDF共13组完整链路验证。校准失败、非被动角表和早期执行问题全部保留。[证据入口](../validation/rechecks/2026-11-01/xyz-angular/README.md)

## 三种材料的校准排查

原550 nm、959训练/480验证、完整四角键每第三项留出、NRMSE≤0.01及最大绝对差≤0.01 sr⁻¹保持。[上一阶段明确的观察条件](XYZ_KNOWN_CONDITIONS_2026-11-01.md)仍为官方CIE1931 2°/D65、400–700 nm、范围外为零、白Y=1；原发布XYZ观察者仍未核实。

从[Cornell官方数据入口](https://bowers.cornell.edu/computer-graphics/data)所链接的原档取得三份全精度MAT。MAT与ASTM逐行配对的最大舍入差均约5e−7 sr⁻¹；当前共享Core正值模型在全精度数据上仍失败：

| 材料 | 原ASTM正值模型NRMSE | 全精度MAT正值模型NRMSE | 最佳非负探索候选NRMSE | 新正式批准 |
| --- | ---: | ---: | ---: | --- |
| Blue Acrylic | 0.0514012 | 0.0514055 | 0.0220069 | 无 |
| Mystic | 0.2658934 | 0.2659274 | 0.0118027 | 无 |
| Cayman | 0.0549350 | 0.0549327 | 0.0139164 | 无 |

[全精度原文件身份](../validation/rechecks/2026-11-01/xyz-angular/full-precision/sources.json)、[全精度结果](../validation/rechecks/2026-11-01/xyz-angular/final-precision/)、[正式原模型复算](../validation/rechecks/2026-11-01/xyz-angular/final-calibration/summary.json)。MAT诊断沿用显式标记的来源SHA占位符，只作精度排查，不正式导出。正式配对CSV/SHA拟合仍只有上一阶段的Garnet Red和Krylon Blue两例通过，其校准内容未改变。

独立诊断比较固定近邻数量/幂次、正值重心插值、局部径向基、对数/平方根响应、绝对XYZ强度，以及明确加入角度条件的不同模型。剩余三种没有非负、有限且同时通过原门槛的候选。Cayman某有符号候选NRMSE虽约0.011996，仍超限且53项负预测，不裁剪为零。溢出、奇异和失败也保留；严格JSON汇总将非有限值编码为失败字符串，未丢弃原实验输出。[探索清单与失败分类](../validation/rechecks/2026-11-01/xyz-angular/exploration-finite.json)

这些是使用原划分的开发诊断，不是新盲测；探索模型没有进入产品，也没有按材料名称选择运行时分支。结果证明这些尝试未达标，不能由此断言任何未来模型均不可能成功。XYZ只提供三项光谱积分，缺少一般唯一反演保证；下一步需要检验更有依据的光谱先验、测量不确定度或额外独立测量，不将新增先验伪装成XYZ自身的信息。

## 真实角表接入与能量限制

新增 `CornellAstmSpectral/1` 仅接受已核实约定的Cornell/Kodak光谱BRDF配置，严格验证实验室/系统、列、波长、方向、行数与非负有限值；不宣称通用ASTM标准兼容。原始角度明确为弧度，波长为nm，输入为辐射度BRDF。XYZ/RGB列、其他实验室配置、未知波段和未测入射角拒绝。

每个实测极角环的周期分段线性方位函数在方位节点并集上保留，所有原节点不降采样；不同极角之间按余弦线性插值。单样本环使用周期常数，这是明确插值假设，不是新测量。新增 `BsdfAngularSupport.MeasuredPolarRangeZeroOutside`：仅在实测极角范围计算密度与积分，范围外为零支持，不向未测极角外推，不添加镜面峰。它不意味着真实材料在未测区域散射为零。

重要新发现：采用此明确插值时，五份Cornell完整八入射角文件均有切片的R积分超过1，实际导入拒绝，不自动缩放：

| 材料 | 某个超限切片 | 该切片积分 |
| --- | ---: | ---: |
| Garnet Red | 80° | 1.9900866 |
| Krylon Blue | 70° | 1.3030699 |
| Blue Acrylic | 50° | 1.0824145 |
| Mystic | 80° | 24.5486 |
| Cayman | 80° | 24.2655 |

[独立逐切片积分](../validation/rechecks/2026-11-01/xyz-angular/angular-power-audit.json)、[产品整表拒绝](../validation/rechecks/2026-11-01/xyz-angular/third-import/import-audit.json)。这是**当前插值对稀疏角表的非被动结果**，不等于直接证明原材料发能，也不等于测量已做被动性认证。不能忽略缺测、扩大峰宽或改变功率门槛来宣布整表通过。

新增显式选原测量入射角的选项，本轮对每材料统一固定10°、20°、550 nm。每个选中切片保留其全部原出射节点及方位变化，不用均匀合成分布替换。原六位小数弧度到度的5e−5°容差只用于唯一匹配原入射节点，不做最近角外推；全部入射角仍可请求，并继续因上述非被动切片拒绝。波段留空时请求全谱；展开超过原节点预算则拒绝，不自动减采样。

桌面同一BSDF入口增加`.astm`，提供“单色波长 / Cornell波段”和“Cornell入射角（空白全部）”。Core/Application API为 `AstmWavelengthNanometers`、`AstmIncidentThetaDegrees`。完整支持域、选中节点、原文件SHA和转换说明随工程/相机冻结。新支持域使用 **MeasuredBsdfGrid/9 / STAROPT v17**，旧Grid/1–8及其摘要不升级，Grid/8仍v16；容器2和顺序快照6保持。低版本夹带新模型拒绝，包括禁用对象。

## 实测 BTDF

[VIAVI官方BSDF数据页](https://www.viavisolutions.com/en-us/osp/resources/bsdf-data)提供代表性样本的实测Zemax表。本轮取得EDC50、EDS40、HiLam原ZIP、表格及配套说明/示范ZMX；每表361×86=31,046个原角节点，无降采样。法向入射的`ASymmetrical4D`单旋转角0°具有唯一固定材料方向分布；现已允许该合法特例，倾斜入射仍需完整旋转覆盖，缺测角不外推。新特例沿用Grid/3/v11，不改变已有文件计算。

单色链路显式使用供方示范ZMX所声明的EDC50/EDS40 550 nm、HiLam 632 nm，未声称独立核实测量激光谱宽或完成波长响应校准。文件仅提供该入射切片；不是全入射/全旋转范围认证。产品BTDF对象要求两面数据，本轮明确声明背面相同，仅实际测试前面入射；不将背面称为另一份实测。[下载身份](../validation/rechecks/2026-11-01/xyz-angular/measured-btdf/source-downloads.json)

## NSC→RAW/ISP 与回归

最终13组为10个Cornell实测出射切片加3个VIAVI BTDF法向切片，均直接使用实测辐射度角表，**不意味着三份XYZ校准被批准**。独立对角节点分片积分计算功率、方向均值/方差及4×8二维角直方图；每组32,768方向样本，416个分箱全部通过六西格玛加一个计数的离散余量，方向均值按原六西格玛检查，`f|cosθ|/pdf`与积分一致。NSC每组另追迹8,192光线并核对方向矩与能量账本。

源功率固定1e−11 W，探测器3.2 mm方形/32×32，像元100 µm，10 ms和解析QE用于独立电子换算；QE不是实测传感器。电子、非零RAW/ISP、v17或v11工程保存重开、删除临时源后空场景冻结重放均验证。文件中`FullMeasuredAngularTableRetained=true`仅指该**选定入射切片的完整出射表**，不是八个入射角或未测半球区域均通过。[最终13组](../validation/rechecks/2026-11-01/xyz-angular/final-measured-chain/summary.json)

最终307/307项定向回归（新增12项，2分19秒、0失败/跳过），另23组旧White捕获功率复算最大差仍1.45344 µW；不运行全仓或新原生商业GUI。13组链路最大相对源功率能量差2.58435e−13、账本闭合差2.58460e−13，最大电子差7.27596e−12 e⁻。默认Release主程序含两个内置模块构建成功，测试/诊断构建零警告错误；早期共享工作区其他模块的编译错误保留，其后正常构建成功。

第一次307项通过期间，默认App输出被其他构建更新；测试结束后共享测试目录也被覆盖，原快照和差异保留。因此最终使用从默认构建复制的专属执行目录与源/输入快照复验，9个执行程序集和144份相关文件快照前后无漂移，不将原共享路径后来覆盖的字节称为已测版本。执行SHA、最终计数、能量/电子最大差和旧1,212份冻结文件核查集中在[机器汇总](../validation/rechecks/2026-11-01/xyz-angular/final-verification-summary.json)和[身份检查](../validation/rechecks/2026-11-01/xyz-angular/final-run-integrity.json)。早期像元尺寸控制配置错误与沙箱测试宿主中断也保留，未改产品像元范围或物理一致性规则来放行。

未完成项仍为：三种XYZ校准达标、原发布观察者直接证据、Cornell全入射角的合格物理角分布、真实背面BTDF和更多波长/入射/材料轴，以及完整商业逐值认证。本阶段不能报告用户整项要求已全部完成。
