# 用户指南：瞄准规则与现有算法保留

本次同步仓库用户指南和内置中、英、日三语章节，未修改光学计算代码。

- 镜头表：三角形全选表头、键盘激活、屏幕外行，以及反射镜/坐标参考/材料默认行色和自定义覆盖规则。
- 分析：工程瞄准状态的继承、显式单光线选项与专用积分/参考探针边界；有焦像方离焦点列的默认五面及同条件结果对照。
- 计算原理：保留原有章节与公式，补充瞄准状态贯穿派生计算的要求。
- 修正内置指南中过时的“视图没有所有结构”说明，保留未实现的平移排布、光瞳显示等边界。

## 内容保留证据

`source-preservation.json` 对照当前任务前已提交的两份计算原理源码：原计算章节 ID、公式 ID、节标题以及全部 **585 个不同字符串字面量**均保留。新增一个三语段落，不删除原算法解释。

`probe` 读取实际编译后的完整指南数据，并以真正的离屏阅读控件定位新增段落；`content.json` 保存实际章节、公式及三语正文，`captures/updated-*.png` 保存三种语言的宽窗 Light 与窄窗 Dark。内置 PDF 使用同一章节数据，本轮不另导出或核验 PDF 页面。

## 定向检查入口

`focused/Guide.csproj` 只链接现有 `UserGuidePanelTests`、`UserGuideMathematicsTests` 及共用安全离屏会话，使用过滤器运行阅读器与算法覆盖检查；没有运行完整测试工程、完整光学精度矩阵或用户桌面交互。

```powershell
$env:DOTNET_PROCESSOR_COUNT='2'
$env:OPTICAL_SYSTEM_DESIGN_GUIDE_READER_CAPTURE_DIR="$PWD/validation/rechecks/2026-11-21/user-guide-aiming-update/captures"
$env:OPTICAL_SYSTEM_DESIGN_GUIDE_PDF_AUDIT_DIR="$PWD/validation/rechecks/2026-11-21/user-guide-aiming-update/audit"
dotnet test validation/rechecks/2026-11-21/user-guide-aiming-update/focused/Guide.csproj -c Release --filter 'FullyQualifiedName~UserGuidePanelTests|FullyQualifiedName~EveryActualAnalysisHasAnExistingCalculationChapterAndEveryFormulaHasOutlines' --logger 'trx;LogFileName=guide-content.trx' --results-directory validation/rechecks/2026-11-21/user-guide-aiming-update/results -m:1 -nr:false
dotnet run --project validation/rechecks/2026-11-21/user-guide-aiming-update/probe/Capture.csproj -c Release -p:BuildProjectReferences=false -- validation/rechecks/2026-11-21/user-guide-aiming-update
```

## 已完成的检查

- `results/guide-content.trx`：11项现有定向检查全部通过，0失败、0跳过。覆盖三语搜索与章节导航、宽窄阅读器、截图解码，以及全部分析与公式资源的实际关联。
- `audit/coverage.json`：实际编译目录为80项分析，对应27章计算原理、39组公式；仓库三个当前指南文件的旧76项计数同步修正，覆盖表的80行已与实际目录逐项核对，没有缺失或退役条目。
- `default-release-build.log`：默认App输出完整Release构建成功，0警告、0错误。
- `captures/updated-*.png`：18张实际新增段落画面，覆盖三种语言、三个更新章节及宽窗Light/窄窗Dark。脚本逐页检查章节身份与横向溢出；人工核对中文宽窗、中文/英文/日文窄窗和含原公式的英文计算页，文字按原字号换行，长章保留正常纵向滚动。

默认输出初次完整构建遇到并行会话正在编辑的非序列导入方法缺失（`NativeGlobalMetadata`、`MapNativeObjects`），后续名称统一时又遇到导入器局部变量重名（保留在`default-release-build-import-edit.log`）；未修改这些功能，也未操作用户程序。独立画面脚本最初把全文关键字当作唯一章节ID、且保留了默认操作教程筛选，导致定位检查失败；后改为“全部内容”筛选并选取实际章节节点，未修改产品搜索行为。最后仅统一实体模型的英日名称；可用`probe`的第二参数`audit-only`刷新最终编译正文，不重复截图或运行测试。
