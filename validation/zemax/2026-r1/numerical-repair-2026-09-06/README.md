# 2026-09-06 数值修复补充捕获

此目录只供离线验证，正式 `src` 不读取或引用这里的文件。既有 `123456.ZMX` 主基准未修改。这里的 JSON 为本机 OpticStudio 2026 R1（API 260127，SP0）通过 C# ZOS-API 工具取得的原始输出；不是 Workbench 生成的期望值。

- `ms-l7-fft-psf-data.json`：高 NA 镜头的 128×128 原生 FFT PSF 网格。
- `ms-l7-spot-data.json`、`123456-spot-data.json`：两支镜头的原生 RMS/GEO 标量。
- 对应 `*-captured-settings.json`：API 实际设置及完整请求；`ms-l7-fft-psf-environment.json`：实际版本与许可证状态。
- `manifest.json`：镜头在仓库中的位置和 SHA-256、全部原始文件哈希、单位、采样来源与测试容差。

点列设置是配置 1、视场 1、波长 1、hexapolar 密度 20、主光线参考、非偏振；测试绝对容差为 `2e-8 µm`。FFT PSF 是光瞳 64×64、像面 128×128、0.25 µm 像面间隔、非偏振、Normalize=false；输出是相对理想峰值的无量纲强度，网格 NRMSE 容差为 `0.01`。坐标逐点核对，不拟合平移或做额外归一化。以上是这次文件捕获的设置，不能描述成通用 Zemax 默认值。

完整根因、修复与报告位置见 [修复记录](../../../../docs/NUMERICAL_REPAIR_2026-09-06.md)。Optiland 0.5.8 冻结历史目录与此目录分离，没有新增或重新生成 Optiland 对照。
