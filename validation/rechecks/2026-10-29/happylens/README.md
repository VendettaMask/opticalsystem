# 初始结构生成与搜索：本轮证据入口

- [方法、支持域与结果说明](../../../../docs/HAPPYLENS_SEARCH_2026-10-29.md)
- [可直接打开的 Delano / 群体搜索计划](delano-population.topology-plan.json)
- [最佳组合案例的 STAROPT](comparison-final/Delano-73-population.staropt)：50 mm、F/12.5、半视场5°，RMS 11.5298 µm，最大半径34.0889 µm；不是原广角减片任务。
- [原库小规模运行](upstream/summary.json)：Delano与随机物理初始化，各四个候选，原Adam/SHADE交替完成。
- [最终18次对照](comparison-final/summary.json)：三个种子，三种起点，各自配局部或群体/局部；四个更新/决策与相同总预算上限，实际成本分别记录。
- [核验和文件指纹](verification.json)：26个不同用例通过，原目标、每阶段/总配额和追加账目检查。
- [模块定向结果](happylens-focused-final.trx)、[调度与界面补查](happylens-scheduling-ui.trx)、[Core结果](happylens-core-final.trx)。补查有一个重复UI方法，计数去重。
- [480 DIP日语离屏图](descriptor-controls.png)：已检查新生成控件可见、文字及数值布局。

`comparison`是初步阶段分配，`comparison-equal-updates`是修正更新次数后的中间版本。它们连同失败尝试保留，不作为最终表格或独立成功率数据。Delano加局部在本小例三种子均改善；随机处方均值未改善，群体策略九组中仅一组改善，未默认开启。

产品始终使用共享Core的C#实现。研究用Python源码和隔离环境在工作区外层tmp目录，未成为产品依赖。运行只使用后台CPU和Headless，无桌面操作、全套测试、跨平台实机或发布。STAROPT导出做了处方快照回读核验，未宣称外部原生光学认证。
