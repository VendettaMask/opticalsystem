# Objectives 全部 19 支镜头精度修复与对比

日期：2026-10-23。正式 C# Core/Application 重新导入并计算全部原请求；原始输入、冻结 Zemax 数组和容差未修改。

原 638 项：**586 Pass / 52 不可比较 / 0 Close / 0 Difference / 0 Error**。全部 638 项产生新数据。74/74 组逐面光线通过，13320 条输入光线，145841 次有效表面比较；接受/首次裁光差异均为零。

保留原 2026-10-20 的 564/564 Pass，以及修复前本次审计的 568/568 Pass。89 个原设置独立重采集，与软件得到 37 Pass / 52 不可比较，结论与冻结参考完全一致；19 个一阶控制的原生标量最大变化为 0。26 个另列高采样控制得到 22 Pass / 4 不可比较，没有 Close 或 Difference；不替换原请求。

| 镜头 | 原请求 P/C/D/I/E | 光线组通过 |
|---|---:|---:|
| Apochromat.zmx | 49/0/0/1/0 | 8 |
| Apochromat2.zmx | 37/0/0/1/0 | 5 |
| Apochromat3.zmx | 29/0/0/1/0 | 3 |
| Apochromat4.zmx | 43/0/0/27/0 | 13 |
| Cooke 40 degree field.zmx | 30/0/0/0/0 | 3 |
| Double Gauss 28 degree field.zmx | 29/0/0/1/0 | 3 |
| Double Gauss 5 degree field.zmx | 35/0/0/3/0 | 5 |
| Doublet using MIL number glasses.zmx | 37/0/0/1/0 | 5 |
| Doublet.zmx | 30/0/0/0/0 | 3 |
| Even Asphere.zmx | 20/0/0/2/0 | 1 |
| Gaussian Quadrature over Annular Pupils.zmx | 21/0/0/1/0 | 1 |
| Gaussian Quadrature Over Wavelength.ZMX | 29/0/0/1/0 | 3 |
| Inverse telephoto lens.zmx | 30/0/0/0/0 | 3 |
| Petzval.zmx | 29/0/0/1/0 | 3 |
| Relay lens.zmx | 29/0/0/1/0 | 3 |
| Tessar lens using vignetting factors.zmx | 28/0/0/2/0 | 3 |
| Wide angle lens 100 degree field.zmx | 29/0/0/1/0 | 3 |
| Wide angle lens 200 degree field.zmx | 26/0/0/4/0 | 3 |
| Wide angle lens 210 degree field.zmx | 26/0/0/4/0 | 3 |

## 保留限制

- 原请求的 30 项光扇/OPD 有效性掩码差异、20 项无效/低采样/空原生输出、2 项超广角场曲覆盖不足继续记为不可比较。原生场曲只到 89°，不能证明完整 100°/105° 半视场。
- 零视场通过仅认证轴上单点。光扇缺点不会被补零、跨缺点插值或伪造为实际裁光。即使批量光线通过，原生曲线没有相应有效数据时也不升级为精度通过。
- Even Asphere 的能量在 512 采样下原生仍警告取样太低。其余低采样原请求也保持原状态，高采样结果另列。
- 仅覆盖这 19 支镜头与明确请求，未跑全测试、全部 95 支目录、所有采样/视场组合，未发布安装包。

## 证据

- [修复说明](../../../../docs/OBJECTIVES_PRECISION_REPAIR_2026-10-23.md)、[机器汇总](summary.json)、[原请求矩阵](analysis-matrix.csv)、[请求及数据哈希](request-reference-manifest.json)、[输入完整性](source-integrity.json)。
- [原生重采集结果](native-repeat-results.json)、[补充采样结果](supplemental-results.json)、[逐面光线结果](ray-results.json)。
- [原生完整证据](native-evidence.zip)及[逐文件哈希](native-evidence.zip.manifest.json)保留 132 次原生请求尝试，包括无效与拒绝控制。软件、光线和失败阶段分开归档；派生图和 API/可执行二进制不进入归档。
- [完整软件计算与测试](software-evidence.zip)、[逐面光线](ray-evidence.zip)、[最终比较合同和失败阶段](contract-and-stages-evidence.zip)各附同名 `.manifest.json`，均逐文件回读校验。最终比较合同复用 `final-frozen` 的完全相同软件字节，未伪称为另一次 Core 计算。
- [最终源码与日志](logs-and-source-final-evidence.zip)保留源码快照、补丁、构建/失败日志和[严格文档审计](documentation-audit.json)；初次归档也保留在目录内。
- 完成的定向测试为 Core 82/82、比较与原生回归 29/29，共 111 项，包含实际 CUDA；默认 Release 构建成功并启动。
- 当前 Debug Core SHA-256：`6c7d79e3207c2c5c54d2633b05644f030d031a7d51cf19467dabbf1c8908a470`；原容差策略 SHA-256：`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。
