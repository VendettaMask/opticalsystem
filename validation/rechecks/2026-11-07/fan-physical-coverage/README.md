# 光扇、OPD 与原生图表裁光边界

对冻结矩阵里 60 组 Ray Fan 和 OPD 配对请求，用原处方、配置、视场、波长和采样节点补采 OpticStudio 显式批量光线。9,840 个曲线节点的物理通光判定全部一致。冻结图表的 206 个额外空值都落在裁光间隙后首个实际通光节点。补采结果保留原始状态、坐标和光程；冻结图表及其不可比判定保留。 [OpticStudio 光扇设置](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Ray_Aberration_rays_and_spots.html) [原生显式光线与哈希](native-explicit-rays.zip)

Zemax 兼容的 Ray Fan 使用选定波长主光线定义横向像差零点。主光线被遮挡时，使用绕开物理孔径得到的几何交点作为坐标原点。该光线仍保持裁光状态，通光曲线和传输功率中均无此样点。用 Schmidt-Cassegrain spider obscuration 与 Mirror zoom 的冻结图表数值验证了零点修正。

补充比较区分两种范围。显式批量比较核对整个物理节点格；图表比较只核对 OpticStudio 实际报告数值的节点，并独立确认省略点的物理通光状态。OPD 批量接口的 `CurrentAndChief` 与原生图表参考在部分处方上给出不同数值，两个范围分别记账。

| 补充范围 | 通过 | 接近 | 差异 |
| --- | ---: | ---: | ---: |
| 显式批量 Ray Fan，全物理节点 | 60 | 0 | 0 |
| 显式批量 OPD，全物理节点 | 38 | 2 | 20 |
| 冻结图表实际报告的 Ray Fan 和 OPD 节点 | 120 | 0 | 0 |

显式光扇的最大横向像差差值为 1.37×10⁻⁶ µm。22 条未通过的显式 OPD 与其冻结图表对照均单列。 [逐请求对照](results.json) [范围和汇总](summary.json)
