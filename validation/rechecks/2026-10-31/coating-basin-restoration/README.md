# 镀膜生成认证证据（2026-10-31）

> 重复资料整理（2026-11-26）：逐字相同的历史源码、脚本或项目文件只保留一份原件，原目录角色通过别名表保存；需要完整源码目录时先在仓库外还原。见[合并记录](../../2026-11-26/duplicate-consolidation/README.md)。结果和失败分类保持。


> 存储状态更新（2026-11-26）：大型历史派生 JSON 已无损压缩，原路径、原字节哈希及仓库外还原方法可查。详见[精简账本](../../2026-11-26/repository-trim/README.md)。原验收结论保持不变。


本目录保存本轮基线、最终生成、独立复算与未采用试验；产品结论见[报告](../../../../docs/COATING_GENERATION_CERTIFICATION_2026-10-31.md)。Generate 0/6→2/6，旧完整Search已能通过同两组；98%真实高反无新增通过。

- paired-generation.json：6组逐量、原门槛与预算守卫。reference-inputs为实际相同热起点，inputs保留61个原始输入。
- baseline-generate-all / final-generate-all及各自-cold：最终两臂。final-generate-primary/a/b保存实际调用时长和工作量；不是等成本比较。
- baseline-search / baseline-cross：旧完整Search六组控制。verification.json核对最终Search路径保持、源身份、测试实际发现数及独立物理检查。
- reference-results.json / reference-native：固定参考18组原始输出与本地原门槛复验；点平方目标不同，不是等目标竞赛。
- hr-cold：19组旧HR最终膜系冷复算；curvature.json / curvature-reference-comparison.json及curvature-trial-final仅为离线诊断，未进入产品。
- family / feedback / allocation / certify / budgeted等目录是中间试探，未完成的已在停止JSON中记录，不能计作最终验证。rejected-restoration-source保留未采用源码。旧边界差分诊断明确以before-boundary标记，最终使用修正后的证据。
- tests保存最终构建日志、共享输出依赖覆盖失败、仅2项旧程序集和最终6项运行；首次测试断言写法导致的编译失败另记入execution-ledger.json，按实际身份分别计数。final-source及final-tests为本轮最终源码快照；final-manifest.json只封存本目录，运行时目录忽略但二进制摘要另存。

未重写此前非线性约束修正证据，未全仓测试、操作原生桌面、提交或推送。
