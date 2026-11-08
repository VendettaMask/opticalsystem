# GRIN interconnect 一阶量的独立原生复验

原请求 #825 的验收量是有效焦距和 F 数。对菜单处方的字节副本加载配置 1，使用原 0.55 µm 波长、物方 NA 0.2、4 mm Gradient 1 体段和原孔径。副本与冻结处方的 SHA-256 一致。原生 OpticStudio 2026 R1 的无偏振直接光线从物面发射，在 GRIN 前后表面取截点和方向。96 条光线共返回 192 个表面记录，全部无光线错误且未截光。四档只改在内存中用于积分的最大步长，处方副本未保存。捕获已完整写盘，随后原生应用关闭时报告 FRU 内部错误。 [版本、关闭状态和文件身份](capture-receipt.json) [原生处方回读](model.json) [逐光线原始结果](direct-grin.json) [采集代码](GrinFirstOrderControl.cs)

用六组轴平行光线的正负入射高度差求 C = Δ(dy/dz) / Δy，再按相邻高度减半做 Richardson 外推。焦距为 −1/C。另以实际 NA 的正负边缘光线在第 1 面的截点间距确定入瞳直径，得到 1.0372361263751149 mm。该面是系统的光阑，其前方只有物方均匀介质。 [Ansys 直接实光线接口](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/IRayTraceDirectUnpolData.html) [Ansys Gradient 1 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v242/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_1.html)

| 对照 | 有效焦距（mm） | F 数 |
| --- | ---: | ---: |
| 原生实光线外推，最大步长 0.003125 mm | 2.063212815077623 | 1.989144769077842 |
| 原生实光线外推，最大步长 0.0015625 mm | 2.063212815077636 | 1.989144769077855 |
| Workbench 原请求重算 | 2.063212815089806 | 1.989144769089587 |
| 最细步长原生 EFFL 和 EFFL/EPDI | 2.063210254801252 | 1.989142300713787 |

最终两档体段积分步长的焦距变化为 1.33×10⁻¹⁴ mm。最细步长的最后两组入射高度外推变化为 3.68×10⁻¹³ mm，最后一组使用 0.00125 和 0.000625 mm 两档高度。精确轴上解析焦距为 2.063212815077614 mm，与最终原生实光线外推的差为 2.18×10⁻¹⁴ mm。

沿用原比较器和 #825 的两项门槛，两种原生加密检查及 Workbench 对收敛参考均通过。Workbench 焦距与 F 数误差分别为 1.22×10⁻¹¹ mm 和 1.17×10⁻¹¹，相对误差均约 5.90×10⁻¹²。原生 EFFL 操作数在加密后仍低约 2.56 nm。原冻结报告复核仍为 Close，独立的原生轴上导数对照结论为 Pass。 [原请求、量级、门槛和逐档比较](verification.json) [复验代码](Compare-Grin.cs) [冻结 EFFL 步长研究](../../2026-11-07/residual-reference-followup/README.md)
