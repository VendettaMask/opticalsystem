# 示例中文分类与精度文档交接验证

日期：2026-11-01。变更范围为菜单显示、语言词条、现有菜单回归及相关文档；没有修改物理计算公式、处方、原始证据或验收门槛。

## 定向检查

使用默认 Release 输出构建并执行：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SequentialExampleTests|FullyQualifiedName~UiLocalizationTests" --logger "trx;LogFileName=example-categories-final.trx" --results-directory artifacts/validation/example-category-chinese-20261101/tests -v minimal
```

最终 **7/7 通过，0 失败、0 跳过**；默认 Release 主程序、两个内置模块及测试驱动构建成功。用例覆盖原 14 类/95 个处方哈希与菜单打开动作、中文分类及辅助功能名、英文词条映射、常规文档导入/配置和现有语言切换。Avalonia 使用离屏测试平台，没有打开用户桌面窗口。首轮和末次为同一用例集合，不相加为 14 项。末次 TRX 另存本目录。

## 文档核对

已更新交接页、状态页、问题清单、精度契约、用户指南、文档索引、双语首页、示例来源说明、模型补齐各批和相关专题入口；历史数值与冻结证据保持原身份。仓库严格文档检查首轮扫描 1,757 份文档，无围栏错误、无现行指南正文重复；另发现两处镀膜指南指向尚不存在的 `COATING_DESKTOP_WORKSPACE_2026-11-01.md`，不属于本轮改动，未覆盖其他正在进行的工作。本轮修改文档的链接没有报错。最终审计状态以本目录验证清单为准，不把全仓检查失败写成通过。

文档检查结果及本轮源文件/二进制指纹见本目录 `verification.json`，不替代精度重算。文档权威入口为[中文交接](../../../../docs/EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。历史全量矩阵、冻结光学参考、证据包与原失败记录均保留；本轮未运行 Zemax、全仓测试、安装、发布、提交或推送。
