# 真空腔逐面光线复验

对照 11 月 4 日 [原固定光线复验](../../2026-11-04/all-example-accuracy/ray-results.json) 中的 `046-vacuum-cell-c1-w1`。沿用 [10 月 21 日冻结归档](../../../repairs/2026-10-21/sequential-corpus-full-evidence.zip) 的 `run/rays/046-vacuum-cell-c1-w1/native/ray-audit.json`，以及菜单中的原始 `vacuum cell.zmx`（SHA-256 `fd2a3063b4ed4aab4f50aee4786faa4b498f5731fb6746985b81066aae869911`）。第一配置，0.55 µm，两视场，144 条光线，8 个实际面。冻结样本和历史结果均在原路径维护。

原追迹在第 3 面之前吻合。从第 3 面进入真空后，离轴代表光线的 Y 方向余弦差约 `7.06774e-5`，后续坐标误差累积。原导入忽略配置中位于两个空白 `GLSS` 行之前的 `PRES 0`。现在按每个配置的操作数顺序处理。末次 `PRES` 设定全局参考，后续 `GLSS` 使用当前压力。局部气体折射率是其绝对折射率与系统参考空气绝对折射率之比，真空绝对折射率为 1。见 [Ansys 多环境说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Defining_Multiple_Environments_within_a_Single_Configuration.html)和[折射率公式](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Index_of_Refraction_Computation.html)。

| 执行 | 接受、首次裁光差异 | 坐标超限 | 方向超限 | 最大坐标误差 mm | 最大方向误差 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 11 月 4 日记录 | 0 | 1094 | 1732 | 0.009460795362279839 | 7.067741388355753e-5 |
| 标量 CPU | 0 | 0 | 0 | 1.0658141036401503e-14 | 8.881784197001252e-16 |
| 批量 CPU | 0 | 0 | 0 | 1.4210854715202004e-14 | 8.881784197001252e-16 |

两条执行路径各比较 1152 个逐面结果。沿用坐标 `1e-6 mm + 1e-6 × |参考|`、方向余弦 `1e-8 + 1e-6 × |参考|`。独立的双配置控制还检查了真空和半个大气压、压力分段终止、关闭折射率环境调整、非法压力、快照与 STAROPT 重开、ZMX 往返。

当前支持 20 °C、1 atm 系统参考下的固定局部气压。非标准全局参考、热光学求解和局部气压作为 NSC 外部介质仍有明确限制。结果来自冻结光线重放，范围是这组逐面光线与压力导入控制。
