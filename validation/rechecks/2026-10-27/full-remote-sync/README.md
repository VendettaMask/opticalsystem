# 全量项目同步：合并核对（2026-10-27）

本地全部产品代码、文档、截图、交付 PDF 与开发验证记录先保存为 `259318f002f3e8a791b3ceda633159dcd60e5750`；随后合并远端 `88aa58f8` 及其之前的 16 个新提交。没有强制推送或丢弃双方改动。

冲突处理：

- `MainWindow.OnClosing` 同时保留本地离屏工作区隔离返回与远端 `_closeAfterPersistence || _macOtaPrepared` 的关闭准备状态。
- `docs/CURRENT_STATUS.md` 保留本地全部专题记录及远端云发布、Mac/Windows OTA、README 更新记录，移除被远端正式记录替代的旧标题。

合并后的默认 Release 输出参与构建，后台定向 **29/29 通过，0 跳过**：`UserGuidePanelTests`、`MacOtaPanelTests`、`WorkspaceSessionTests`、`OtaUpdatePanelTests.ActualUpdateEntryHonorsSaveDiscardAndCancelWithoutSavingTwice`。见 [TRX](merge-integration.trx) 与[日志](merge-integration.log)。采用两处理器与隔离设置目录，所有界面均为离屏；不运行安装器、不操作桌面，不宣称 Mac 原生安装验收或全仓测试通过。

[严格文档检查](documentation.json)通过；[冻结核对](frozen-integrity.json)确认 2668 份参考在提交与工作目录均一致。原始日志/图保留字节，日期目录沿用仓库已有的原始证据空白检查规则。下载教材、标准原件和本地检查运行时仍在被忽略的 `/tmp/`，未删除、未上传。常规 `bin/obj/.tmp` 等继续沿用原忽略规则。

此记录是主线合并核对，不等于重新发布安装包或全部数值认证；原各专题测试范围保持。机器汇总见 [verification.json](verification.json)。
