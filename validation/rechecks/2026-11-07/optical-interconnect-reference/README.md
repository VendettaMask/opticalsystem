# Optical interconnect 往返 GRIN 精度复验

## 两条逐面光线

OpticStudio 在同顶点、同曲率、同 Gradient 9 介质的镜面与回程界面之间使用轴上折射率报告方向。对照器先核实处方、物理交点、法线和实际追迹给出的折射率比，再验证原生两个相邻方向满足该界面的切向光学动量关系。验证通过后将方向和入射角换算到实际体内介质。848、865 的已报告数据按原阈值全部通过。换算只作用于对照，保留原报告和实际追迹结果。[原生控件和报告](native-controls.zip) · [Gradient 9 折射率定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Gradient_9.html)

两条光线的冻结报告均未提供第 3、5 面的非零体段弧长。84 组原生截点覆盖 0.001、0.0005 mm 两档积分步长，两段各用 8、16、32 条弦复核收敛。往程弧长为 2.600125523 mm，回程为 2.634648579 mm，Core 最大误差约 1.92×10⁻⁹ mm。补充弧长均通过，冻结整表保留不可比。[逐段弧长和误差](arcs.json)

## 一阶焦距

847 的原生 EFFL 报告值为 2.424207244 mm，Core 为 2.424613029 mm。保留原处方的轴上近邻光线测量在原积分步长和细步长分别收敛到 2.424613291 和 2.424613617 mm。独立移除两段净长度为零的空气传播，再从无穷远输入小光线，得到 2.424613031 mm。三者与 Core 的最大差约 5.87×10⁻⁷ mm，小于原 2.43×10⁻⁶ mm 容差。冻结 EFFL 仍记录原数值差异，独立收敛焦距通过。原处方 EFFL 报告还随物方数值孔径改变，具体读数与两套近轴光线均已归档。[焦距收敛与口径控制](first-order.json) · [Ansys 有效焦距定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Effective_Focal_Length.html)

## 场曲、畸变和衍射包围能量

864 的普通 F-Tan 畸变以近轴物高建立参考。原生界面将参考视场分别设为 1 和 2 并回读，101 点的五列数据完全一致。旧对照适配器将该字段解释为用第一条离轴视场建立畸变零点。修正设置翻译后原请求通过。[原生参考视场控制](field-reference.json) · [Ansys 畸变定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)

860 的共轴 GRIN 反射系统沿用共轴出瞳参考，消除了长虚拟传播将参考探针推到折射率有效域外的求值错误。原 32 档判为接近。原生 256→512 档和两档积分步长均按原门槛收敛。Core 在 256、512 两档对实际曲线及衍射极限均通过，512 档最大差为 6.14×10⁻⁴。原生 32→64 档判为接近，保留原档结论和补充收敛结果。[五档采样对照](energy-refinement.json)

## 200° 与 210° 广角镜头

两份冻结原生报告均计算到 89°。普通 F-Tan 的扫描上限按原生定义应用到组合报告的场曲和畸变两张图。实际视场表及光线瞄准的归一化基准仍按处方读取。1998、2028 各有 101 个场点，原阈值下场曲与畸变全部通过，共同覆盖 100%。最大场曲差为 1.72×10⁻⁸ mm，最大畸变差为 2.27×10⁻⁹ 个百分点。[Ansys F-Tan 扫描范围](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)

## 两项 FFT MTF 的补充收敛

1989 的 200° 广角镜头与 2679 的全反射棱镜均保持原视场、波长和 50 cycles/mm 频率范围。两项原生 512→1024 档均通过收敛门槛，Core 512 档对原生 1024 档的子午、弧矢曲线也均通过。全反射棱镜的原生 128→256 档仍有差异，冻结 64 档求值判定单列。[采样梯度与逐曲线结果](mtf-refinement.json)

## 95 支菜单镜头复验

3,024 项原请求中有 2,904 项可执行，94 支导入成功，共 114 个配置。系统盘写满后按原快照、请求和 Core 哈希补跑 134 项。最后的角度扫描上限修改命中 1998、2028，两项用最终 Core 定向重算，114 份镜头配置快照哈希一致。30,700 个冻结参考载荷哈希通过，固定光线 143 组通过、108 组不可比，独立无焦补充 15 项通过。[版本与恢复清单](identity.json)

| 冻结判定 | 上一轮 | 本轮 |
| --- | ---: | ---: |
| 通过 | 2,496 | 2,499 |
| 接近 | 22 | 23 |
| 数值差异 | 47 | 44 |
| 不可比 | 424 | 426 |
| 求值错误 | 35 | 32 |

新增 3 项原请求通过，原通过项无回退。六项补充验证独立于冻结矩阵。首次定向测试筛选误带入一条已排除的历史用例，按确认范围重跑后 Core 163 项、对照工具 34 项全部通过。冻结矩阵的 525 项余项逐项保留。[逐镜头结果](by-lens.csv) · [判定变化](verdict-changes.csv) · [余项明细](remaining-requests.csv) · [Core 测试](core-scoped-release.trx) · [对照工具测试](comparison-release.trx)
