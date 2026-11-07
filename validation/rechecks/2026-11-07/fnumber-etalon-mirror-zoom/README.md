# 出瞳 F 数、Etalon 与反射变焦精度复验

## 单片目镜的出瞳 F 数

OpticStudio 2026 R1 的 API 回读原处方为 `PupilSizePosition`。用 API 保存两种模式时，`GLRS` 的第一项全局参考面保持一致，第二项在 `0` 和 `1` 之间切换。单片目镜原模式的出瞳距离为 1000 mm，直径为 5 mm，原生和 Core 都使用 F/200。切换到 `TracingRays` 后两边都得到约 F/7507.09。独立的近轴模型与反射变焦控件也分别回读了两种模式。该设置现在随 ZMX 和光学快照保存，旧文件缺省时沿用追迹光线模式。 [原生回读、控制处方和报告](native-controls.zip) · [Ansys 出瞳 F 数定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Pupil_Size_Position.html)

#557 的 FFT MTF 按原档通过。#559 的两种模式在 128、512、1024 档均通过同档对照，共同覆盖 100%，最大强度差 5.17×10⁻⁷。原模式的 Zemax 512→1024 档按原阈值收敛，Core 512 档对 Zemax 1024 档也通过。冻结 32 档的相邻受光节点相位差约 0.528 波，原档保留相位保护。 [逐档对照](fnumber-etalon-controls.json) · [冻结请求结果](focused-results.json)

## Etalon 的有焦与无焦参考

原处方是无限远入射、均匀镀膜平面与像面重合的系统。#2203 的有焦 MTF 从 64 到 256 档最大差约 0.6505。#2212 的报告使用 10⁹ mm 生成畸变参考高度，而实际主光线在像面处的横向高度恒为零。原处方核验和 Core 类型状态均确认这两项缺少有限焦面参考，冻结结果归为不可比。 [有焦报告与处方](native-controls.zip)

同处方的独立无焦控件中，MTF 的 128→256 档收敛，256 档 Core 对 Zemax 的最大差约 2.11×10⁻⁴。均匀圆瞳的解析角频率 MTF 也通过。无焦场曲及畸变全部通过，最大场曲差约 3.55×10⁻¹² 屈光度。 [无焦控制结果](fnumber-etalon-controls.json) · [Ansys 有焦和无焦 MTF 频率定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_MTF.html)

## Mirror zoom 的内缘截光

显式原生光线对照覆盖轴向及斜向的环形口径内缘。内半径为 50 mm 时，49.9999995 mm 和恰好 50 mm 均被挡住，50.0000005 mm 透射。按两个配置的实际 APMN 内半径分别做微扰，增大内半径的五份 32 档报告与原报告逐值一致。缩小内半径的五份报告复现了旧 Core 的差异。居中的 ZMX 环形口径现在保留内缘遮挡规则，并在光学快照和 ZMX 往返后继续生效。 [显式光线、控制输入和报告](native-controls.zip) · [Ansys 环形口径定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Aperture_surface_properties.html)

原 32 档的 #2766、#2769、#2770、#2773、#2858 全部通过，最大绝对差小于 5×10⁻⁷。#2857 FFT PSF 截线也由接近变为通过。独立的高采样原生对照中，#2770 Huygens PSF 截线的 1024→2048 档仍判为接近，归一化均方根差约 0.00662。其余四项的最高两档通过。 [原档逐项结果](focused-results.json) · [独立原生加密](mirror-native-controls.json)

## 95 支菜单镜头复验

先全量重算 95 支镜头的原请求。最后加入环形口径规则后，新旧 114 份配置快照仅有 14 份增加内缘标记，这些配置下的 300 项可执行原请求全部用最终版本重算。其余配置快照逐字节相同。全部原请求、阈值及 30,700 个冻结参考载荷均通过身份核验。最终版本的固定光线检查有 143 组通过、108 组不可比。原独立无焦范围 15 项通过。 [版本与重算范围](identity.json) · [统计](summary.json)

| 判定 | 上一轮 | 本轮 |
| --- | ---: | ---: |
| 通过 | 2,501 | 2,508 |
| 接近 | 23 | 22 |
| 数值差异 | 44 | 38 |
| 不可比 | 426 | 428 |
| 求值错误 | 30 | 28 |

新增 7 项通过，原通过项无回退。扩展回归有 177 项通过，5 个导入测试失败在上一轮程序集复现，测试名称和报错逐条一致。516 项冻结余项保留镜头、配置、原请求和判定依据。 [逐镜头](by-lens.csv) · [判定变化](verdict-changes.csv) · [逐项余项](remaining-requests.csv) · [既有导入测试失败](existing-import-test-failures.json)
