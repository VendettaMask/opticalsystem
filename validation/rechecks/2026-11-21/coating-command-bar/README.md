# 镀膜设计命令栏（2026-11-21）

用户指出上方命令栏是白底、下方一整排计算按钮多余。此次沿用光学工作台已有的字体、图标、原生菜单和五主题材质，保持参数、膜层表、光谱与 Dock 内容为阅读主体。

原命令栏绑定正文 `Surface`，遮住了 `MainWindowTitleBar.Wrap` 提供的窗口级蓝色渐变。改为共享 `RibbonPageBackground`：普通 Light 透出原渐变，其他主题使用自己的命令区资源，不新建局部蓝色渐变或硬编码底色。

移除正文顶部的“1 生成膜系 / 2 优化 / 结构搜索 / 计算/复验 / 取消”整排按钮及其布局行。相同计算函数移入“设计 / 优化”原生菜单，保留既有设置入口；计算时禁用重复执行命令。取消继续由运行期间显示的进度条提供，同时保留 Escape；进度条取消处理保留原按钮的修订失效、会话取消及绘图取消。参数、公差入口、物理计算与结果数据没有变化。

默认路径 Release 主程序构建成功。最终 **6/6 离屏检查通过，0 失败**，包括五主题与 820-DIP / 18-DIP 字号的日文窄窗口。检查实际工具栏蓝色像素、仅有一个正文工作区、四个原生菜单动作、忙碌时禁用、进度条取消的修订失效，以及主题往返后实验指纹保持。[最终原始结果](results/coating-command-bar-verified.trx) · [实际截图](captures/)

目视核对普通 Light 的工具栏、原按钮行腾出的空间与其他主题；保留原有菜单横向滚动和原生状态。新增“生成膜系”的中英日运行词条，原运行资源逐项保留，用户操作说明已同步到主指南和镀膜模块指南。

最初沙箱内测试宿主连接超时，属于中止而不是通过；后在自动审核允许下重跑离屏检查。首轮 4/6 通过，2 项因测试误将实际 RGBA 像素按 BGRA 解释而失败；修正为读取位图格式后，6 项最终全部通过，重复运行不累计。过程原件保留在 results 中。构建只在命令行关闭不可达的 NuGet 漏洞审计，不修改产品配置或宣称漏洞检查通过。未启动/控制用户的桌面窗口，也未运行光学精度矩阵。

```powershell
$env:OPTICAL_SYSTEM_DESIGN_COATING_CHROME_CAPTURE_DIR = "$PWD\validation\rechecks\2026-11-21\coating-command-bar\captures"
dotnet test validation/rechecks/2026-11-21/coating-command-bar/focused/CoatingChrome.csproj -c Release -m:1 -p:NuGetAudit=false --logger 'trx;LogFileName=coating-command-bar-verified.trx' --results-directory validation/rechecks/2026-11-21/coating-command-bar/results
```
