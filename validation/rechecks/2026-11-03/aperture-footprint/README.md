# 通光孔径与相邻分析修复

完整根因、实现与认证边界见[本轮报告](../../../../docs/CLEAR_APERTURE_REPAIR_2026-11-03.md)。

- [汇总](summary.json)：14支420项154→165 Pass，原通过无失守；[修复前](results-before.json)、[修复后](results-final.json)。
- [跨类别426项](categories-results-final.json)：307 Pass/18 Close/15 Difference/85 Incomparable/1 Error，与420项有重叠，不合并为全量通过率。
- [固定光线](fixed-ray-results.json)及[修复前](fixed-ray-results-before.json)：72条输入，25条接受/首裁光差异全部消除；可生成69条的541组逐面对照无超限，原3条瞄准失败仍在。同一原生输入同时用于前后Core。
- [27项收敛](convergence-results.json)：Huygens PSF及FFT MTF的17项全部Pass；衍射能量4项同档Pass、6项双方采样收敛Difference，认证继续开放。原低采样分类不覆盖。
- 最终446项定向测试通过，默认双模块Release App零警告/错误；成功记录为逻辑路径`tests/clear-aperture-regression-final.trx`。此前编译、测试、映射和构建锁失败分别保留，不冒充成功运行。
- [当前编译源绑定](source-verification.json)、[修复前源绑定](before-source-verification.json)。最终Core为`15cc79d4582d0c6b1f244e4ecd51396b955143ed3ae181297172c4c5ee0b185f`；修复前为`07db76dd3a0cebcee8642bd8bce820f56bfaa042d1eaf62517699cc2c2529abb`，Git源版本`10a885a1`。

[归档索引](archive-index.json)为每个逻辑路径给出实际容器、entry、长度及SHA256。entry为null时直接读取archive字段指向的文件；否则从所指ZIP的entry读取，不假设都在本目录ZIP。`replay-after`是首次孔径候选，`replay-final`包含报表与FFT瞄准保护；`native-compare-complete`为最终固定输入核验。旧错误映射和未完成运行保持各自身份。

[归档校验](archive-verification.json)：ZIP为61,263,356字节，SHA256 `237fd7acda3d66a09eb7341a3d0ed642f437055b38542ee29933084fe3934627`；7,139个新载荷和3,637个复用载荷实际验证，140份原生文件唯一展开保留，其余已归档展开副本退休。原生捕获位于`native-controls`及`convergence`，不修改用户源文件或旧参考。

[封存脚本](../../../../tools/diagnostics/ApertureFootprint20261103/Seal-Evidence.ps1)记录完整校验和受限退休路径，不应向已封存目录重新执行写入。早期临时诊断的整套源码身份不由最终PDB绑定追溯替代；最终产品源与两版Core的绑定独立可核对。
