# 旧双筒 335 点工作树清理报告，2026-11-01

用户明确要求：“这属于多余内容，不需要了，你保留一个报告就可以删了。”本次只保留本报告，不另建代码快照、压缩包或存档分支。

## 来源与退出原因

- 删除目标：`D:/Projects/opticalsystem-afocal-335`，为独立 Git 工作树，基于旧提交 `cf61c001dc1a5f36afdd84a4d24d7d020b130d99`。
- 用途：2026-10-29 双筒配置 2 的 FFT PSF 截面 335 点差异调查，包含瞳孔尺度/发射、三角片交点、平面折射方向及原生诊断工具。
- 26 个变更文件是 4 个已跟踪文件的实验修改和 22 个未跟踪文件，不是 26 个新的精度问题。部分早期假设曾改善配置 2 但损害配置 1，不能作为当前产品代码重新发布。
- 后续正式修复和验收已在主项目完成：[335 点修复](AFOCAL_FFT_SEAM_REPAIR_2026-10-30.md)、[无焦完整结果](AFOCAL_ANALYSIS_RESULTS_2026-10-31.md)。原 335 点超限已归零。
- 主项目同步提交为 `30908879e46fb78e81f8e17862f6a06fc06aab75`。本次没有合入旧实验或改变主项目物理计算。
- 删除前目录共 16,825 个文件、7,472,999,075 字节（约 7.47 GB）；包含旧工作树检出文件、历史资料、构建输出和诊断缓存。该数字是目录逻辑大小，不等于磁盘净释放量。

## 保留资料核查

旧工作树 validation 下 7538 个文件逐文件核对：与主项目同路径、同长度、同 SHA-256 的有 7538 个（4850666131 字节）；同路径不同版本 0 个，主项目无同路径文件 0 个。当前主项目的证据与既有 Git 历史保持；用户授权丢弃旧工作副本和旧实验状态，不为其再造档案。

主项目活动源码、工具、测试、脚本和 CI 中未发现对旧工作树绝对目录的引用。旧目录根和后代没有符号链接或重解析点。共享 Git 对象库位于主项目 `.git`，不在删除目标内。

## 删除前的 26 个变更文件

下表记录原相对路径、原字节数和 SHA-256，仅用于识别已丢弃状态；不包含旧代码载荷，也不据此增加数值通过范围。

| 状态 | 相对路径 | 字节 | SHA-256 | 与主项目关系 |
|---|---|---:|---|---|
| M | `src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs` | 14567 | `55343d5f7371787835bb2a4dff71bce79ca417ecaabdebd82b1f65bdfb8cb8ac` | 主项目已有不同版本 |
| M | `src/OpticalSystemDesign.Core/Geometries/PolygonGroupGeometry.cs` | 11753 | `f15ac02c8df2ea7ff2f32c700e69dfb857aec7e447e52fd0d61be771858f4d2e` | 与主项目同字节 |
| M | `src/OpticalSystemDesign.Core/Interactions/InteractionModels.cs` | 16901 | `5148915d3d9e8676e852f97053425aa309505ffac0e6ec86a71c98ddef69c1f7` | 与主项目同字节 |
| M | `tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs` | 25929 | `0727bf71c050bce78b24503c738b8b3b608af7627292d4752450621f66f23350` | 主项目已有不同版本 |
| ?? | `tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftProfilePointwiseTests.cs` | 4355 | `331474d5ca8544db265a09df02dd588ad75e00ec8f0c20be07e2cfa61f8c828c` | 与主项目同字节 |
| ?? | `tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativeFacetPathCapture.cs` | 5036 | `353c868da48f554558c97f5b5ffe0bd63a6a940cac40654415f11dafa0986cff` | 与主项目同字节 |
| ?? | `tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativeNormalsCapture.cs` | 4263 | `d329b6774dddd0f8f21459b68a853303a19c7943a074a74615a3e55500e8b8e3` | 与主项目同字节 |
| ?? | `tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativePolygonControl.cs` | 3102 | `b7ad0f71785ca082894aa331e81ed1b042a5043abe087d0817efe8c22e9627b4` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/AutomaticGridDiagnostics.cs` | 6521 | `cfb5dc1b182248535bb0a37d26b72eeff7012f2c9f7c603700fed9b156fb36f7` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/Collect-Evidence.ps1` | 5296 | `d59117c476ae08581186ca250f316e51a50b5d4087ca2dd145f39b2ed4e0eb75` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/CoreComparison.cs` | 8542 | `d04d260fd69e9b0a3888388a2dad99301a26a9d559cf171969ef30c7203eb3ae` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/FftProfileNative20261029.csproj` | 621 | `aedab527b0e9ec167db6bad60969ce7f7e3013775f8a0966c8a88209648ff63b` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/GaussianGridDiagnostics.cs` | 3923 | `3a7bac37f5fca07a3cd37ece9d78c240f016f07df10957043d633b9316609a48` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/NativeAssessment.cs` | 7920 | `4e25d29fb6d93a3514934d0b670cf767c08967c2445d181977864db6b86daddf` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/NormalFormulaDiagnostics.cs` | 3802 | `153cfb763b4374e3643b39261062758f719b0f88f1e68c63d5c418dc24d7efa2` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/Program.cs` | 4913 | `0a6de914418b8539db89aeb71c175348816e084bb217918f26eff495bb3d6764` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/PupilGridDiagnostics.cs` | 5473 | `0e4bef6050b4fbc129181ef1b9991de21b0873aa34bef59ca7dd54cc6c395faf` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/PupilLineDiagnostics.cs` | 3053 | `3348135e8d3667ab933034c7df3163641c5f8770d57abcbb63344610a3dd867e` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/PupilMapDiagnostics.cs` | 2184 | `583b9c624f671a0a76182f242712837568710414ae578f63f9ffe5e05eebd5ff` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/PupilProjectionDiagnostics.cs` | 3915 | `d36edeb9df2f29ebd387e1bb5d2f96686102c97e9ca05ab338b2c5da31773f9a` | 与主项目同字节 |
| ?? | `tools/diagnostics/FftProfileNative20261029/README.md` | 3568 | `a2f74018854453021da06aa316b45a02dc83a5cec9c46a43ab9a743388865e4a` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/TaggedFacetDiagnostics.cs` | 8031 | `4d960f327f8036d05ab75aef461d1c56e6b9a402bc0baa129f516200bcb95074` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/TaggedFacetRuleScoring.cs` | 3286 | `6b63c347adfb476d3f40e1074b298ed2eb1732bc9881f885c306b7c3e0ca8fd1` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/WorkingPupilDiagnostics.cs` | 3941 | `11437917260609b6711a339ee265dfbfd315540022806e3673646b27e3b08f4b` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/packages.lock.json` | 2278 | `0265287f11a1806b181eb60d2cc56cabb9e59bb42ba8b4430a6aa3136f1c008d` | 主项目已有不同版本 |
| ?? | `tools/diagnostics/FftProfileNative20261029/rejected-hypothesis.patch` | 1056 | `69595ecdf4ff5468eb69363c8f0964d2b49f7e9b22f959d921a5ac5d37c685bb` | 与主项目同字节 |

## 执行与核验

报告已在删除前写入主项目并回读，26 个变更文件的身份已记录，并在实际移除前再次核对哈希。

Git 已移除工作树登记；首轮因 Windows 长路径限制留下目录残留，核对绝对目标、父目录及无重解析点后，使用 PowerShell 原生长路径删除完成清理。没有停止用户应用，也没有删除主项目或共享 Git 对象库。

- `D:/Projects/opticalsystem-afocal-335` 目录已不存在。
- 主项目 `.git/worktrees/opticalsystem-afocal-335` 登记目录已不存在，`git worktree list` 只列主工作区。
- 清理时主项目 HEAD 仍为 `30908879e46fb78e81f8e17862f6a06fc06aab75`，当前物理计算源码及原精度验收保持。
- 没有创建或推送 `archive/afocal-335-20261101` 分支。旧工作树的存档确认已由用户“只保留报告后删除”的新决定关闭。

不运行额外光学测试或打开用户桌面；本次仅清理不再需要的旧工作副本。
