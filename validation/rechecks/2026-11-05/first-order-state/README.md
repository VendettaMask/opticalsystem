# 一阶参数的零光焦度状态与 Chebyshev 镜

从菜单 95 支的首轮原设置复算取出七个 `First Order` 字段类型错误，用原始 ZMX 重新导入，经正式 Workbench 分析执行。逐项复用原请求指纹、OpticStudio 2026 R1 冻结参考和原比较门槛。七项原错误复验为 **4 通过、3 不可比**。见[结果及来源身份](results.json)。

软件输出有效焦距无穷大时，JSON 使用 `"Infinity"`，并同时给出零光焦度及无限状态。对标工具以前把该字符串直接传给 JSON 数字读取器。现按光焦度和状态核验，只比较有限/无限状态，两个有限数仍使用原数值门槛。冻结参考中的 `±1e10` 由同捕获的 EFFL、入瞳与 F 数关系以及独立平面处方确认后才视为零光焦度。Optimization demo 1、Etalon 和 Beamsplitter cube 的三项通过。两项 Grid Sag 与一项 MEMS DLL 的单一焦距编码不足以确认真实顶点光焦度，因此保留不可比。Grid Sag 的原生光焦度还取决于网格数据。 [Ansys Grid Sag](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Grid_Sag.html)

离轴 Chebyshev 反射镜的标准基面是平面，但二阶多项式提供光焦度。共享近轴矩阵现使用实际矢高在顶点沿 Y 的二阶导数。公式使用 `T''_j(0) = -j²T_j(0)` 并保留归一化半轴和基面曲率。原镜头的有效焦距由无穷大恢复为 **500.00000000700004 mm**，与同请求冻结 EFFL 相同。F 数也通过原门槛。该计算是局部顶点近轴量。 [Ansys Chebyshev 面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Chebyshev_Polynomial.html)

默认 Release 对标项目的 9 项测试通过，覆盖两种原生符号、无限与有限状态差异、未知面型与非法负载，以及原有物理约定。见[测试记录](first-order-comparison-final.trx)。
