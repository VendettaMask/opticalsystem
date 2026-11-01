# 望远镜与光学检测：遮挡与几何参考

结论和统计边界见[修复报告](../../../../docs/OBSCURED_REFERENCE_REPAIR_2026-11-01.md)。本轮原 7 支、13 项全部恢复 Pass；两个类别 426 个原可执行请求为 224 Pass / 20 Close / 28 Difference / 101 Incomparable / 53 Error。原低采样平场施密特 2278 的 Difference 保留，独立 512 档对原生以及双方 256→512 收敛分别通过。

[summary.json](summary.json) 是简要结果，[category-results.json](category-results.json) 为逐请求账目；原 13 项、固定光线和收敛记录分别在 [regression-results.json](regression-results.json)、[ray-results.json](ray-results.json)、[energy-convergence.json](energy-convergence.json)。

唯一数值载荷在 [evidence.zip](evidence.zip)。[archive-index.json](archive-index.json) 将原逻辑路径映射到包内 `payloads/<sha256>`，重复文件只存一份；读取相应成员恢复逻辑路径即可复核。[archive-verification.json](archive-verification.json) 记录全部成员验证和包哈希，[reference-verification.json](reference-verification.json) 记录复用原生数组对既有权威包的核验。参考包不重复复制，原始失败不覆盖。

101 项与 66 项两组回归通过，默认含两内置模块的 Release App 构建通过。全程后台，未进行前台交互或安装发布。
