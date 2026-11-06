# 同面 ISOB / ISOC 联合标定与固定扰动对照

2026-11-06。在同一圆形净口径面上，B 表示叠加后的总峰谷值，C 表示旋转对称分量峰谷值。联合计算使用一份实际追迹几何。灵敏度仍单独评价两行，反求后的范围保留物理单位约束。分布、单位和界面入口见[公差说明](../../../../docs/TOLERANCING.md)和 [Ansys ISOC 定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/ISOC.html)。

## 解析和工作流复验

- 角向阶次 1–7 分别生成联合面形。测试从真实矢高在 8193 个径向位置采样，每个位置用四个角度独立恢复角向平均和振幅。核对总峰谷值 1600 nm、旋转对称峰谷值 600 nm、顶点基准、实际法线和原曲面恢复。
- 固定最大值、双截断正态和混合设置覆盖 C 接近 B 的情况。验证不同单位的配对、冲突拒绝、逐行灵敏度和反求、同一随机种子的单/双线程 MTF、表格报告和 startol v5 往返。
- [初轮记录](joint-iso-initial.trx) 为 25 通过、2 失败。失败点是 1024 径向区间的独立测量漏掉部分连续极值。将该测量统一提高至 8192 区间，保留原数值阈值。
- [扩展回归](joint-iso-regression.trx) 为 146 通过、1 失败。失败项为工作区关页保存测试，在 Isekai 资源初始化时缺少 Avalonia 渲染接口，[单独运行](workspace-isolated.trx) 同样失败。仓库的[历史回归](../../2026-10-26/full-reliability/complete-regressions.log)记录了同测试类的同类错误。排除该工作区测试类后，光学计算、公差与配对界面定向复验 146/146 通过。[定向 TRX](joint-iso-focused-final.trx)

## 固定样件

[名义 Cooke 处方](nominal.zmx)和[扰动处方](fixed-joint-iso.zmx)使用同一镜头、波长、玻璃、视场和焦面。扰动处方在第 1 面加入 Fringe Sag。净口径半径为 6 mm，取归一化径向坐标平方 `t` 和方位 `θ`，附加矢高为 `0.0024(t²−t)+0.0006t cos(2θ)` mm。

- 旋转对称分量范围为 −600 至 0 nm。
- 组合最大值在边缘，为 600 nm。最小值位于 `t = 0.625` 的负角向分支，为 −937.5 nm。总峰谷值为 1537.5 nm。
- 固定 Fringe Sag ZMX 重载与联合扰动路径的矢高、法线和 128／256 瞳孔采样下的 FFT MTF 已在本机核对。在 256 采样、20 cycles/mm、轴上和 550 nm 时，名义 T/S 均为 0.868482，扰动 T/S 为 0.569669／0.736393。[原始结果](fixed-perturbation-workbench.json)

| 文件 | SHA-256 |
| --- | --- |
| nominal.zmx | `31410bc7e86add44331b2db3c064031a4cdd294964c550fb9c7ac5b68d4dcd1f` |
| fixed-joint-iso.zmx | `e476788cd8ab4c564259b270f8a62c287f1f84e1b05a5ac6caa5cbd7b4b2d172` |

## OpticStudio 原生对照待执行

当前机器未找到 OpticStudio 可执行文件或 ZOS-API 程序集。两份 ZMX 与 JSON 是本地工作台结果。原生对照需分别打开两份 ZMX，确认 SK16、F2 解析为同一玻璃数据。在 FFT MTF 使用像面、视场 1、波长 2（0.55 µm）、调制度、关闭偏振和重新调焦，分别采样 128、256。导出 T/S 曲线及原始设置，比较 20 cycles/mm 的名义值、扰动值、下降量和采样收敛。该固定控制隔离了面形追迹误差，原生随机系数和补偿良率另需实测。
