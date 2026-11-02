# 偏心出瞳与FFT频率复核

结果与边界见[本轮报告](../../../../docs/BIOCULAR_PUPIL_FREQUENCY_REPAIR_2026-11-02.md)。

- [汇总](summary.json)：216项196 Pass/20 Incomparable；426项304/16/16/87/3，分类保持。417项定向测试、14项MTF对照与收敛、默认双模块App构建通过。
- [原请求216项](results-final.json)、[两类别426项](categories-results-final.json)、[最终MTF14项](mtf-results-final.json)、[编译源绑定](source-verification.json)。最终Core为`ea7bed78eeaa95f9ac6e56e49fa112039ca6e814c52342b3b563dbb803366118`。
- [归档索引](archive-index.json)逐项给出逻辑路径、SHA256、长度、实际容器及entry。`entry: null`表示直接文件；否则从指定ZIP的entry读取。不能假设所有内容都位于本目录ZIP。
- [归档校验](archive-verification.json)：新ZIP 18,495,228字节，SHA256 `a28f7fde85c046b878a3314fc964c375b1dda888ead1559e46cd98e33400ff67`，4,877个新载荷、5,177个旧载荷读回验证，52份高采样原生文件唯一展开保留。`mtf-convergence/{1013,1043}/{128,256}/native`供当前驱动重放使用；其他已归档展开副本退休。

`replay-candidate`是仅修相位的194 Pass/2 Close版本，`replay-projection`先关闭5 Close；`categories`暴露辅助光线遮挡回归，`categories-fixed`及`replay-final`为最终同Core结论。`mtf-convergence`记录新原生捕获，`mtf-convergence-accepted`是最终Core对这些原生输入的重放；`mtf-convergence-final`的权限失败不是成功运行。

`native-phase`操作数参数错误，不能作为逐波长相位/路径认证；`native-phase-corrected`按实际schema修正。`phase-controls-final`对齐路径基准和chief参考，仅为独立诊断，不替代正式图表比较。早期诊断可执行体已被最终驱动替代；最终PDB源绑定不声称恢复每个早期临时驱动的原始源码。

成功测试为`tests/geometric-pupil-final.trx`；中止、编译失败、分类回归和其他有用诊断各保留一次。通过[封存脚本](../../../../tools/diagnostics/BiocularReference20261102/Seal-Evidence.ps1)完成校验和退休，脚本不应向已封存目录重跑写入。
