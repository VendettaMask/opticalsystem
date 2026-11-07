# 倾斜像面的相对照度复验

范围为 95 支菜单镜头的 #2752 Tilted object。冻结设置为 0–10 mm 物高、波长 0.55 µm、瞳密度 20、21 个场点、关闭偏振和光线瞄准。旧版最大照度差为 0.00828509，NRMSE 为 0.00637432，覆盖 100%。独立控制使用密度 40，保留其他分析设置。 [原请求与加密结果](../residual-reference-followup/README.md) · [原生控制设置和哈希](controls.json)

## 分离物高、像面几何和原生坐标约定

原 #2738 和 #2755 的轴上与 10 mm 物高逐面光线均通过，纵向坐标最大差约 5×10⁻¹¹ mm。将物高上限独立改为 4 mm，原生端点有效 F 数与原曲线 4 mm 的内点完全相同。仅保留物面倾斜、像面归零时，旧版与 Zemax 的照度最大差为 1.93×10⁻¹⁰。

将同一物理像面改用 `COORDBRK` 旋转加 `STANDARD` 平面表示，仍扫描 0–10 mm 物高。54 条光线、162 个对应面显示相同的启动位置、入射方向和遮挡，最大交点差为 1.42×10⁻¹⁴ mm，法向差为 1.11×10⁻¹⁶。旧版 Core 和原生对此处方的照度最大差为 1.21×10⁻¹¹。原生使用 `TILTSURF` 表示时会改变有限像点校正，即使物理像面相同。 [逐面等价证据](plane-equivalence.json) · [Ansys 倾斜面坐标定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Tilted.html)

原生有限配置以本地 X 轴投影建立像面切面基底。对 `TILTSURF`，它以未旋转局部坐标报告的截点横向差量 `ΔX, ΔY` 作为切面分量，再施加现有一阶像点修正。真三维偏移会在陡倾面上拉长该差量。保留现有一阶修正时，将偏移写为 `tangentX × ΔX + tangentY × ΔY`，独立单轴和双轴控制均与 Zemax 一致。`AdaptivePupil` 与 `UniformImageCosine` 继续采用物理像面投影，不受原生有限坐标约定影响。 [第一组映射控制](local-offsets.json) · [混合和陡倾基底控制](basis.json)

## 原设置复验

通过正式 Workbench 入口重放原 #2752 和 11 个独立控制。原 20 档结果按冻结门槛通过，21 点最大照度差为 **8.512×10⁻¹²**，NRMSE 为 6.024×10⁻¹²，覆盖 100%。11 个控制也全部通过，最大照度差为 1.935×10⁻¹⁰。原处方与旋转坐标的等价处方在 `AdaptivePupil` 和 `UniformImageCosine` 下分别相差 7.63×10⁻¹⁷ 和 2.08×10⁻¹⁷ 的投影余弦面积。 [正式入口、原阈值和物理积分结果](final-verification.json)

最终默认源构建的相关照度测试 38 项全部通过，其中包括六个原生数值回归和两个物理积分坐标等价性回归。
