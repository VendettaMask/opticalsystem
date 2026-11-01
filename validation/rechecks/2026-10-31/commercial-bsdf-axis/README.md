# 商业 BSDF 与固定材料轴验证封存

2026-10-31，默认 Release，共享纯 C# Core。适配范围是 Speos v8.0 TEXT 的 BRDF/各向异性 BSDF 值模式与原生 CSV v2。测试输入为独立生成的合成数据，没有商业实测参考或 Speos 数值认证。

- `build-final-app.log`：默认 App（含两个内置模块）构建，零警告、零错误；`build-final-tests.log`：同输出图的测试驱动构建，零警告、零错误。
- `final-focused.trx`、`test-final-focused.log`：最终合并 **156/156**，零失败、零跳过，包含本阶段新增 24 项及前三阶段相关回归。集合相互覆盖，不重复累计。没有执行整个主测试套件。
- `before-final-run.json` / `after-final-run.json`：34 个范围内源码/夹具文件与 6 个二进制指纹在末次运行前后保持一致。来源是共享脏工作区，保留 BaseCommit，不将它当成干净提交。指纹为追溯信息，不是光学数值门限。
- `synthetic.brdf` / `synthetic.anisotropicbsdf`：带明确合成说明的格式夹具；`speos-*.json` 记录来源/数据摘要与积分结果。
- `fixed-axis-statistics.json`：seed 4901、32768 次联合采样。材料 X 均值 0.08701983，对照解析值 0.08882790；Y 均值 0.00168159；平均绝对余弦 0.66646429，对照 2/3；透射 21018 次。均通过六倍标准差控制，同时逐样本核对联合 PDF 权重。
- `nsc-covariance-False.json` / `True.json`：4096 条射线、完整参考坐标链刚体变换，分别覆盖主轴与平行法线的极点回退。逐射线方向、每像元功率与 R/T/A 能量一致；最大像元功率绝对误差 1.26218e−29 W。全能量相对误差小于 1e−12。
- `camera-chain-False.json` / `True.json`：商业反射与透射经现有 NSC 探测器到传感器；逐像元一次光子换算最大误差 2.84217e−14 e−。删除原文件、清空追迹/场景后冻结实验 RAW/ISP 精确重放；实验 zip 随证据保存。
- `commercial-bsdf-editor.png` / `commercial-bsdf-camera.png`：最终 Avalonia Headless/Skia 离屏帧，已查看。导入侧、局部轴 `(0,1,0)`、正确的 Measured BSDF 标签与材料方向统计均由真实控件路径验证。没有启动或控制用户桌面。
- `manifest.json`：封存产物摘要、结果范围及最终 Application 二进制位置一致性。

中间失败保留：`development-unit.trx` 是测试 CSV 列拼接错误；`development-chain.trx` 是新夹具把布局射线数误写为 0；`development-complete.trx` 的两个旋转检查要求功率逐位相等，差异为浮点末位。夹具已修正，旋转验证改为逐像元原物理量的明确数值容差，同时保留方向和能量断言。离屏检查还发现新协议被旧摘要标签误称 Lambertian，已修正并加入断言。最终回归包含全部修正。

`prior-focused.trx` 及 `prior-before/after-run.json` 是补充非节点参考角一致性断言之前的 156 项通过记录。最终代码将光谱参考角的积分缩放统一为运行时的 cosθ_i 插值，并断言 30° 非节点参考角处的 R+T 恰为文件光谱比例；末次回归覆盖该修正，不将前后两次运行数相加。

运行入口为 `run-focused.ps1`。它覆盖本目录中的末次运行/统计/图像产物；封存后要保留历史证据时应先复制目录。指南更新脚本仅记录本次文档编辑，不是产品运行依赖。算法、格式限制及官方依据见[实现说明](../../../../docs/COMMERCIAL_BSDF_AXES_2026-10-31.md)。
