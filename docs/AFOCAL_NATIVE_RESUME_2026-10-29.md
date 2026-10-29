# 无焦精度：本机 Zemax 后台复核（2026-10-29）

用户确认这台电脑有 Zemax 后，恢复此前暂停的原生核验。实际通过本机
OpticStudio **2026 R1 / build 260127 / EnterpriseEdition** 完成后台 API
连接，`IsValidLicenseForAPI=true`。此前“没有本机运行环境”只属于
[暂停阶段](../validation/rechecks/2026-10-29/fft-profile-paused/README.md)，已不再是当前阻断。

## 完成了什么

共完成 **20 个独立原生采集任务**，不是20项新增光学Pass：两组瞳孔/NSC
设置、两组33条顺序面探针、两组33条内部NSC路径、两组26条接缝扫描、两组
26条Glue Distance控制、六组独立质心/主光线能量控制、瞄准镜瞳孔操作数，
以及配置2的原FFT截面和Real/Imaginary各一组。各组有重叠探针，不能相加成
独立覆盖数。每任务120秒硬限，顺序执行；本批没有超时。

冻结ZMX的前后SHA-256保持。原生实际ObjectsDir中的两份POB与冻结字节匹配，
没有覆盖、安装或修改用户文件。两个配置实际读回
`SystemData.NonSequentialData.GlueDistanceInLensUnits=1e-6`，模型单位为mm。
这些读数取代此前关于实际设置的缺失，仍不等于获取了内部三角片ID或FFT像素
到发射坐标的私有映射。

## 新证据与结论

| 核验 | 实际结果 | 意义 |
| --- | --- | --- |
| 原FFT截面、Real、Imaginary重新采集 | 三份新输出的完整dataSeries/dataGrids分别与原冻结参考完全相等 | 该残差不是旧数组或本次版本漂移 |
| Field=2、Wavelength=2，三模型/配置各开两个独立实例 | ReferTo分别读回Centroid/ChiefRay，选择器正确，三对完整dataSeries仍完全相等 | 已排除简单的分析实例复用；尚不能据此修改物理质心定义 |
| 两配置各33条中心/边界顺序面探针 | 原生全部error=0/vignette=0；Core可比较各29条，4条超出归一化API域明确单列 | 不把未比较或被拒输入算成通过 |
| 原Core与这些探针对照 | 配置1最大方向差约1e-15；配置2约1.57e-9，集中于瞳孔X=0 | 新原生数据再次复现接缝差异 |
| NSC内部路径与入射/出射方向 | 配置2第二块棱镜的一次反射平面与当前Core选择不同，几何法向可由反射方向独立推回 | 比原先只看组出口更精确；不是原生直接返回的三角片ID |
| Glue Distance临时改为1e-9/1e-3的独立控制 | 两组26条完整路径分别与原1e-6控制相等 | 在这些探针范围内，调Glue Distance不能解释差异；不能推广为所有边缘不受它影响 |

官方2026 R1的[分片物体说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Special_Considerations_for_Faceted_Objects.html)
讨论异法向公共边及Glue Distance吸收；[Polygon Object](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Polygon_Object.html)
说明R四边形内部剖分。它们没有公开本例近共面折痕的完整选片规则。实际探针全部
接纳，不能套用一个猜测的接缝吸收掩码来关闭误差。

## 已否决的修复假设

试验将点在三角片内的边界判断由原始有向面积改为归一化重心面积，并在舍入
等距时保留文件顺序。配置2的29条方向差降至约2e-15，但配置1由约1e-15
恶化至约1.57e-9；配置2FFT截面仍335点超限，最大差约0.00119205。
因此这项Core修改已经完整撤回。改善一组接缝光线并不足以证明FFT网格残差关闭。

撤回后再次执行原五份FFT截面请求，所有数值与本轮修改前完全一致：

| 原请求 | 最大归一化强度差 | 超限点 |
| --- | ---: | ---: |
| Riflescope c1 | 1.01559650353e-5 | 0 |
| Beam Expander c1 | 1.38534394489e-6 | 0 |
| Binocular c1 | 2.45039864263e-5 | 0 |
| Binocular c2 | **0.00119466103820** | **335** |
| Apochromat Prism c1 | 0.000112028888338 | 0 |

采用原逐点门槛`1e-5 + 0.002 × |原生参考强度|`，五份请求指纹、原坐标、
采样和参考不变。整体NRMSE的Pass不能替代逐点通过。本批没有重跑170项，
三项质心Difference、六项自动网格Incomparable、五项相对照度缺参考及两项
mixed NSC一阶不适用均未关闭。

## 交付与下一步

当前产品Core源码与起始提交相同，纯C#和共享Core保持。本批修复原生host中
阻止Framework编译的局部变量重名，增加受限NSC路径采集器及有界C#调度/核对
工具。初次沙箱IPC拒绝、原生面型本地化守卫失败和编译修正均留存；它们不是
光学数值失败。

最终诊断工具、共享Core/Application及比较工具Release构建零警告/零错误；
POB字节保护与原生配置选择定向回归 **12/12** 通过，约0.7秒测试执行时间。
这些守卫测试不增加光学通过数，未重跑桌面App或全仓测试。

下一步证据目标已从“有没有Zemax/实际Glue Distance/是否用同一POB”缩小为：
同一原处方下，解释近共面接缝的原生选片规则及FFT内部X/Y出瞳网格定义；
质心能量需解释独立实例中选择器不同而曲线相等的语义。不能通过交换轴、
原生掩码、挑选步长或放宽验收门槛补齐。

[本批证据与检查结果](../validation/rechecks/2026-10-29/afocal-native-resume/README.md)
包含20项执行账目、新原生数组、失败记录、反证及最终Core复核。
没有占用桌面、启动GUI截图、操作现有会话、重跑全仓或发布软件。
