# 冻结证据的 Git 字节完整性修复

2026-10-11，E04 的原件与仓库保存缺口已定位并修复。此电脑仍保留 2026-10-09 清单对应的原始文件；逐项重读 2668 个文件，全部符合原 SHA-256。修复后的 Git 索引与从索引生成的全新检出同样为 **2668/2668**。没有重算参考、改写旧清单或覆盖此前另一处检出的失败账本。

## 原因与修复

以 `fc69b86a06ccc42c0bf5b6736e13f5828ad3ad94` 为修复前提交，直接读取 Git blob，只能精确匹配 **2236/2668**，有 **432** 个差异。旧工作区检查只能证明本机文件完整，不能证明提交中保存了相同字节。10 月 10 日另一处检出的 2660/2668 是该宿主的历史结果，也不能替代 Git blob 的完整检查。

逐项将当前原件与 2026-10-09 清单核对后，用 `git add --renormalize` 保存原始字节，而不是转换原件去匹配旧 Git blob。432 个文件中 431 个位于 `artifacts/zemax`，另一个是冻结清单中的 `tools/OptilandWorkbench.ZemaxComparison/comparison-settings.json`。`git diff --cached --ignore-cr-at-eol` 对这些文件没有任何内容差异：恢复只涉及行末 CR/LF，包括原报告中的混合换行，没有改变数值或报告文字。既有 artifacts 原字节属性保留；捕获设置另补 `-text`，避免再次归一化。

此前无法解释的 `COMPARISON_REPORT.md` 原件现已直接取得，SHA-256 为 `f3a05d5c0d9c56fcfbd9b27609effc38182b4cd819de3fba6072ecf094329fb7`，与冻结清单一致。这里保留的是现有原件，不是猜测编码、生成报告或从曲线拟合参考。

新增 `tools/validation/GitFrozenIntegrity.ps1`：先沿用原工作区清单检查，再通过 `git cat-file --batch` 原始字节流逐项核对提交或索引。它不经文本解码读取 blob，缺失 Git 对象、工作区文件变化或任一 SHA 不符均失败；可固定已发布聚合身份，拒绝通过更换清单掩盖差异。CI 为 Linux/macOS/Windows 增加独立完整性任务；本机仅验证 Windows 执行，远端 CI 结果另行查看。

## 验证

| 范围 | 结果 |
| --- | --- |
| 修复前提交的 Git blob | 2236/2668，432 个差异，检查正确失败 |
| 原工作区文件 | 2668/2668，原件未改 |
| 修复后 Git 索引 | 2668/2668 |
| 全新独立检出目录 | 2668/2668 |
| 新 Git 检查脚本回归 | 9/9，覆盖 CRLF 归一化、索引/提交区分、工作区变化、缺失 blob、二进制和固定聚合身份 |
| 既有工作区检查脚本回归 | 8/8，独立集合，不计入正式产品测试 |

三处完整匹配的聚合 SHA-256 均为 `3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10`，完全沿用原规则：保留路径大小写的 ordinal 排序、`path=小写哈希`、UTF-8、CRLF 与最后 CRLF。原清单字节、历史账本、165 份新增原生捕获仍按原哈希保留。详细来源、原件路径、修复前 432 个 Git blob、索引和全新检出结果见[机器记录](validation/FROZEN_GIT_INTEGRITY_REPAIR_2026-10-11.json)。

本轮没有改变产品计算代码、分析设置含义或容差，也没有重跑正式全量、完整比较、实验室或新增 Zemax 采集。4608/4608 与 179/1/180 仍是 10 月 10 日原验收范围，不能改标为本轮计算结果。N01、N02 及完整发布验收继续开放；关闭 E04 不代表数值残差已经解决。

## 复验

```powershell
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.ps1 -RepositoryRoot . -ManifestPath artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json -ExpectedAggregateSha256 3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.Tests.ps1 -OutputDirectory <全新夹具目录>
```

提交前可增加 `-Source Index`。默认检查 HEAD；仅暂存原件而未提交时，不能把索引通过声称为旧提交通过。脚本不会修改冻结文件。
