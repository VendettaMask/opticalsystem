# Lambertian BSDF 与 NSC 相机链路

2026-10-31。本阶段为共享 C# Core 增加正式方向散射，接入现有 NSC → 分波长探测器功率 → RAW/ISP。本文的解析与统计验收不代表 Speos、Keysight 或 Zemax 的商业数值认证。

## 物理与接口

`IBsdf` 提供 `Evaluate / Pdf / Sample`，方向都指向离开交互点的世界方向，法线指向入射侧；入射方向也使用背向传播的约定。波长以 nm 显式传入。值和 PDF 都按立体角计量，接口不包含面积、像元、曝光或 QE。

首个实现 `LambertianReflectionBsdf` 的 `f = ρ/π`、`p(ω) = cosθ/π`，以两个独立 `[0,1)` 随机数进行余弦半球采样。每次出射功率为 `Pin × f × cosθ / p`，Lambertian 下等于 `Pin × ρ`；其余吸收。`ρ` 必须有限且在 `[0,1]` 内，越界拒绝，不夹紧用户输入。模型定义参见 [PBRT Diffuse Reflection](https://pbr-book.org/4ed/Reflection_Models/Diffuse_Reflection)，采样测度参见 [PBRT Sampling Multidimensional Functions](https://pbr-book.org/4ed/Sampling_Algorithms/Sampling_Multidimensional_Functions)。

对象可选 `SurfaceBsdf`。本阶段支持不透明反射/吸收的矩形面、球、圆柱、方盒与网格，设置覆盖整个对象表面；不支持逐面覆盖。此模型替代整个表面交互，三个 Fresnel 分裂选项都不会再给它叠加镜面反射或二次吸收。折射表面、标准镜片、光源与探测器声明该模型会明确失败。

每次碰撞采样一个散射方向，正功率沿同一分支继续，可以多次反弹；`ρ=0` 完全吸收，只计一次提议采样，不产生虚假的 R/S 路径事件。仍遵守原段数、分支数、源光线和能量阈值预算。缺失 `SurfaceBsdf` 的场景不消耗新增散射随机数，保留原追迹行为。旧 `IScatteringModel`、`MainRayScatterLossApproximation`、`MeanMeasuredScatterLoss` 及过时 Lambertian/BSDF 别名仍为主光线损耗兼容接口，与新 `IBsdf` 分开。

## 操作与来源

1. 在非序列对象属性中选不透明“反射”或“吸收”，开启“Lambertian 方向散射”，输入反射率并应用。属性表、复制粘贴和 STAROPT 保存保留模型；非法反射率不更新对象。
2. 运行非序列追迹。`S<n>` 只匹配方向散射；`R<n>` 包括镜面、全反射和漫反射。路径摘要以 `S` 保存漫反射事件，场景 DTO 也带独立散射类型。`SEQ(H2,S2,D3)` 可按有序事件筛选。
3. 在相机选择“非序列探测器”，读取例如 `S2 & D3` 的冻结功率，导入覆盖波长的绝对 QE 后运行。只执行一次波长光子换算、曝光、QE、噪声、ADC 和 ISP，不叠加顺序主像、PSF、像元面积或系统波长权重。

`NonSequentialBsdfStatistics` 记录筛选前的全部交互次数、入射/反射/吸收功率之和、出射余弦之和及平方和、PDF 最小/最大值。多次反弹分别计数，功率和也是按交互累计，不能再当作唯一源功率或探测器功率。追迹控制显示次数、平均余弦和反射功率；会话累积合并统计；相机显示次数和平均余弦，快照保留同一统计及模型/反射率来源。统计不是单像元误差条，也不是传感器噪声估计。外部数据库不存该运行摘要，打开后统计未知；未知历史不补造数值。

有界 `DirectCameraScene` 继续使用其独立直射材质，明确拒绝夹带 NSC `SurfaceBsdf`，避免以另一套材质静默覆盖方向模型；此类场景使用完整 NSC 探测器入口。

## 格式兼容

- STAROPT 容器仍 v2，最大工程负载 v8。声明任何 `SurfaceBsdf`（含禁用对象）的工程写 v8，普通工程继续写 v7。v1–7 仍可读；低版本负载夹带 BSDF 会拒绝，旧程序面对 v8 会拒绝，避免静默镜面化。
- STARRDB 二进制仍 v1；新追迹头按需保存 `SurfaceBsdfProtocol = LambertianReflection/1`，分段交互追加 `DiffuseReflection`。未知协议拒绝。旧数据库可读；旧程序不能读取新增交互事件。没有 BSDF 的头和场景哈希不添加新字段/片段。
- 相机仍 `CameraLabV9-NscDetector/1`：传感器开发算法没变，冻结功率已包含散射。新快照按需保存 `SurfaceBsdfs / SurfaceBsdfProtocol / BsdfStatistics`，空值不序列化，保留旧 V9 与采样报告指纹。没有当前场景和数据库也能复算该冻结功率；不等于从实验包重新追迹整个 NSC 场景。

## 验证范围

测试覆盖半球反射率和 PDF 积分、互易性、上下半球、边界输入、旋转法线；65,536 个固定种子样本的 CDF `P(cosθ≤z)=z²`、一阶矩 `2/3`、二阶矩 `1/2` 和均匀方位角。统计阈值依据解析方差的六倍标准差；浮点界限是内部正确性保护，不是新商业精度容差。

NSC 控制用独立的微小面元到中心正方形视因子（半边长/距离均为 1），检查 8,192 条光线、`ρ=0 / 0.25 / 1` 的接收统计和完整能量账本；另验证三个分裂模式一致、多次反弹的吸收/截断、数据库筛选、哈希、工程往返、取消。相机验证逐像元光子换算、CFA/行方向、曝光倍增、排除散射后全黑、RAW/ISP 有效输出以及独立冻结重开。桌面属性与相机链路使用离屏测试，不占用用户桌面。

最终构建、通过数、原始 TRX、采样摘要、实验包、截图和哈希见 [本轮证据](../validation/rechecks/2026-10-31/lambertian-bsdf/README.md)。旧 V9 回归夹具直接复制上一阶段冻结实验包，未重写内容；上一阶段记录见 [NSC 相机链路](CAMERA_NSC_CHAIN_2026-10-31.md)。

## 后续边界

本阶段为无偏振、非相干、与波长无关反射率的不透明 Lambertian 表面散射。阶段结束时的实测 BSDF 文件、谱/角度相关模型及 BTDF 缺口已在 [后续阶段](MEASURED_BSDF_BTDF_2026-10-31.md)补上自有 CSV 网格实现；本页保留原阶段格式和验证身份。ABg/Gaussian、delta 镜面混合瓣、逐面材质、体散射、重要性指向探测器和 MIS 仍未实现。低概率杂散路径依然需要足够源样本；不以采样未命中断言物理路径不存在。
