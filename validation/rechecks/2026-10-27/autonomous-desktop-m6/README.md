# M6 内置界面与定向验证（2026-10-27）

本轮从同步后的 M0–M5 实现继续 M6，不恢复旧AutoLens长训练。新增自主设计内置窗口、显式CPU有限调度、计划/检查点文件、谱系/失败/实耗和独立验收查看、取消保存/精确续跑、保存失败保留界面，以及由Core核对的STAROPT导出。入口和支持域见[模块说明](../../../../labs/InitialStructure/README.md)，工程门槛见[实施计划](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)。

## 实际结果

最终32个不同用例通过，未跑全仓：

- `m6-scoped.trx`：27/27（自主界面6、旧搜索8、扩展13）。覆盖旧检查点身份、CPU硬配额、保存续跑、未知预约与达标归档等。
- `m6-desktop-save-guard.trx`：7/7，包含上述自主界面6项与新增保存失败1项；六项重复不相加。实际以存储租约占用触发失败，关闭不显示保存成功或强行退出。
- `m6-shared-ui.trx`：4/4。提取既有安全离屏会话为两个测试项目共享源，原有多语言和绘图回归保持。
- 六项界面/文件测试验证设计计划校验、打开零追迹、未编辑身份保持、有限物距拒绝、实际控件开始/停止/继续、只读任务、无覆盖复制、普通关闭、未达标导出和Core回读；策略用例验证A不做结构操作、B不请求恢复矩阵、空新字段保持旧序列化。
- `screenshots/` 为真实Avalonia离屏Skia渲染，查看了中文宽窗口、日文480宽度和未达标终点；覆盖中英日语言切换、窄窗重排及输入滚动。截图中的严格1e-9mm RMS只是流程失败夹具，不是16次同预算任务的目标。

[16次对照及失败记录](../autonomous-comparison-m6/README.md)使用原50µm门槛，每组预算上限一致，输入/训练权限/终验设置一致。全部完成；四组各2/4保留合格、均无合格二片，D两次渐进过渡拒绝。不能把已合格起点保留视为从零设计成功。

## 中间失败与界面范围

初次新增测试使用不支持的1视场训练密度，被正式验证拒绝；修正测试夹具为3，未改生产采样边界。随后修正反序列化不可变集合的深值比较、逻辑树同控件重复枚举、布局变更后的点击等待，以及新增列宽的编译类型错误。原日志和TRX保留。

两次窄窗测试中止/早期运行挂起来自直接使用Avalonia会话时续体在其工作线程同步析构，导致线程等待自身；提取复用根测试项目既有安全会话，不屏蔽异常。阶段日志、超时序列和最后成功用例保留。只在布局模式改变时重排，避免重复创建网格定义。

本轮曾尝试通过computer-use启动默认输出作原生验收，但工具被用户物理Escape中止；没有完成窗口选择、交互或截图，不能计作原生通过。用户随后要求不影响电脑使用，已记录协作约束，后续仅后台/离屏测试，不继续控制键鼠、抢焦点或启动真实窗口。没有在本机安装/更新应用；不以离屏测试声称原生实机认证。

## 复现

```powershell
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M6_EVIDENCE = '<后台截图输出目录>'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests -c Release --no-restore -m:1 --filter 'FullyQualifiedName~AutonomousDesktopTests|FullyQualifiedName~AutonomousSearchTests|FullyQualifiedName~TopologyExpansionTests'
dotnet test tests/OpticalSystemDesign.Tests -c Release --no-restore -m:1 --filter 'FullyQualifiedName~WavefrontSurfaceRenderTests|FullyQualifiedName~UiLocalizationTests'
dotnet build src/OpticalSystemDesign.App -c Release --no-restore -m:1
```

默认Release构建、改动C#范围格式与文档检查日志同目录保存。当前输出二进制身份见 `verification.json`；对照运行当时的计算二进制另在对照 `summary.json`，后续格式与界面保护修正不重写旧指纹。当前工作区有另一云发布任务提交的锁文件修复，本轮保留，不提交或推送任何内容，也不操作发布。
