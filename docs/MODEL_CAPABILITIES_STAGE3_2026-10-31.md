# 模型能力补齐第三批，2026-10-31

> 接续提示：本批 302 项剩余模型阻塞此后已解除；网格矢高面一阶量等精度差异没有因此自动通过。按类别接续见[当前交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。

上批剩余 409 项中，**107 项解除模型能力阻塞，302 项仍未支持**。用户指定的 751 项累计解除 449 项；原始 1,098 项累计解除 796 项。解除模型阻塞不代表精度通过。

纯 C# 共享 Core 新增嵌入式 Grid Sag 的双三次 Hermite/线性插值、真实节点导数、完整序列化和 ZMX 往返；有限网格外返回无交点。原生线性网格的矢高和法向场分别插值，经独立方向反推验证。保存文件的全零导数保持零值，不擅自重新估计。当前仅激活经过验证的毫米单位、完整 GDAT/GARR、普通变换标志和零 Zernike 扩展；其余格式保留不透明边界。

PARAXIAL OPD 模式 0 仅在无限物距、上游平面均匀介质且保持平面波时激活，并在每次实际计算时复核。有限物距、上游像差及模式 3 不采用别的模式近似。逆向理想近轴作用修正有符号传播方向，增加折射率变化下互易控制。

相关 4 支示例、110 项原请求后台重算：**59 Pass、15 Difference、30 Incomparable、6 Error**。原生参考缺口、非有限一阶结果和分析执行边界如实保留；原比较门槛未修改，原 95 支总账未覆写。Grid Sag 原生点控制四种数据各 193 行，最大误差小于 7.8e-11；网格一阶焦距的直接原生控制仍有实际差异，尤其线性插值网格。原生曲率估计与口径相关，当前顶点导数规则尚未对齐，**不宣称网格全部分析精度通过**。

157 项针对性测试通过，默认 Release App 构建零警告、零错误。冻结 Core：4bd425b9eb1d32709d941abf83c27398eb87e986a33b54b9586339d0ff0a66ea。后续剩余模型为双折射入/出面 208 项、Binary 3 44 项、Grid GRIN 29 项、Paraxial XY 21 项；此处计数按请求去重，双折射相关近轴面阻塞仍保留。

[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage3/results.json)、[能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage3/capability-progress.json)、[网格直接原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage3/grid-native-controls.json)、[一阶量未关闭差异](../validation/rechecks/2026-10-31/model-capabilities-stage3/grid-firstorder-controls.json)。证据中的源码为运行前冻结快照。后台原生捕获不占桌面，不使用截图。
