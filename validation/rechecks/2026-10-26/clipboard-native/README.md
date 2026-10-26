# Windows 剪贴板粘贴崩溃：后台复现、修复与回归

日期：2026-10-26。修复范围是剪贴板读取失败导致应用退出；不把剪贴板提供者自身的枚举失败写成已消除，也不把本地构建写成安装包已发布。

## 修复前证据

已安装载荷 `1.0.4-win-x64-824c7b3e674e` 的 App 信息版本为 `1.0.4+45cce15b2949ce66168341bbfab22c00a3a1e60e`，App DLL SHA-256 为 `51F61A719670B279BD85C04BD39558433B0F20612CCE98CA900A295744532962`。加载原安装文件中的 `App.Initialize`、`CommandSearchControl`、Avalonia 12.1.0 和 Win32 后端，在从不显示的原生 Window 中执行实际 `TextBox.Paste()`。

- 当前系统剪贴板只读粘贴 **5/5 成功**；未自然触发历史格式枚举失败。这不代表缺陷关闭。
- 隔离 COM 数据对象通过真实 `IDataObject.EnumFormatEtc` 返回 `0x8007000E`，再由原 Avalonia Win32 MicroCom 代理和 `OleDataObjectToDataTransferWrapper` 读取。**2/2** 产生 `COMException (0x8007000E): EnumFormatEtc failed → TextBox.Paste()`，诊断进程都因未处理异常退出，退出码 `-532462766`。
- 对照和故障数据对象只绑定在诊断进程内；没有调用 `OleSetClipboard` 或任何系统剪贴板写入 API。此为受控原生 COM 故障复现，不是重新制造一个用户全局剪贴板故障。

原结果为 [background-summary.json](background-summary.json)、[第一次故障](fault-unhandled-01.jsonl)和[第二次故障](fault-unhandled-02.jsonl)。当时宿主源和运行脚本冻结为 [baseline-Program.cs.txt](baseline-Program.cs.txt)、[baseline-run-background.ps1.txt](baseline-run-background.ps1.txt)。此前前台验证被用户 Esc 中止，没有完成粘贴，`start.json` 和 `ui-observations.json` 只属于那个未完成阶段。

## 实现

功能搜索通过 `PastingFromClipboard` 受控检查读取；COM、占用超时和访问拒绝只取消本次粘贴，查询、选区和搜索结果保留。等待期间查询、选区、窗口或最新粘贴请求发生变化时，旧请求不再提交。成功后仍调用原生 `TextBox.Paste()`，保留原生选区替换、长度约束及独立撤销/重做；不写回剪贴板。

预检查与原生粘贴之间仍可能发生提供者状态变化，因此应用启动时安装 Dispatcher 异常边界。边界同时核对 COM 异常类型、原 Avalonia Win32 剪贴板栈及原 `TextBox.Paste` 栈；只取消这条失败路径，不吞掉无关 UI/COM 错误。失败通过状态栏提示，诊断仅保存时间、异常类型、HRESULT 和调用栈，不保存剪贴板内容或提供者控制的异常消息。日志按大小轮换，日志写入失败不再触发粘贴崩溃。

该保护不替代更新安装包；本轮没有替换现有安装或操作用户的工程窗口。

## 修复后验证

默认 Release App/测试编译完成，定向回归 **28/28 通过、0 失败、0 跳过**，覆盖同步/异步 COM、超时、拒绝访问、空读取、非预期异常、查询/选区保留、延迟读取取消、只读框、命令搜索和启动守卫。见 [clipboard-focused.trx](fixed/clipboard-focused.trx)及[运行日志](fixed-test-retry.log)。首轮测试进程连接超时属于中止，保存在 [first-aborted.trx](fixed/clipboard-focused-first-aborted.trx)，不计为通过。

最终隐藏 Win32 进程矩阵 **13/13 符合预期**，见 [完整机器账目](fixed/native-final/background-summary.json)。其中 12 个正常/保护场景正常退出；另一个负面对照按要求保留未处理异常，证明未广泛吞错。

| 场景 | 实际结果 |
| --- | --- |
| 正常中英混合文本 | 文本一致，原生撤销/重做通过 |
| 当前系统剪贴板 | 只读粘贴 5/5，未记录其文本 |
| 搜索框原生枚举失败 | 查询/选区不变，提示一次，进程正常结束 |
| 普通 TextBox 绕开搜索框局部保护 | 全局边界取消原生失败，进程正常结束 |
| 预检查成功、第二次原生读取失败 | 全局边界生效，查询/选区不变 |
| 失败后重新粘贴 | 先安全取消，再正常替换选区；撤销/重做通过 |
| 只读搜索框 | 未读取提供者，查询/选区不变 |
| 空剪贴板文本 | 不修改查询/选区，不报告错误 |
| Ctrl+V | 隐藏控件内路由事件，粘贴及撤销/重做通过 |
| Shift+Insert | 隐藏控件内路由事件，粘贴及撤销/重做通过 |
| Ctrl+V 下原生枚举失败 | 安全取消并保留查询/选区 |
| 最大文本长度 | 原生长度限制及撤销/重做通过 |
| 无关 COM 错误 | 不被全局边界吞掉；诊断进程按预期异常退出 |

矩阵中的 `WindowVisible` 始终为 false；没有显示/激活窗口、发送物理键鼠输入、截图或改写系统剪贴板。快捷键测试是目标隐藏控件内部的路由事件，不是前台键盘端到端验收。诊断进程自行禁用崩溃对话框；本轮没有新增匹配的 Windows 事件，结论依据退出码、已刷盘的异常调用栈和控件状态，不能把事件计数零写成没有故障。

全部矩阵进程记录加载的 App SHA-256 与模块 ID。默认输出被其他正在进行的工作重新生成后，另在当前默认输出上复查正常粘贴、全局兜底、第二次读取失败和失败后恢复，见 [default-output-recheck](fixed/default-output-recheck/background-summary.json)；它与此前矩阵按各自二进制身份保留，不混写哈希。

验证宿主最早的正常对照因反射重载选择错误中止，保留为 `isolated-control-harness-error-01.*`；首轮修复版宿主缺少 Skia 动态库搜索路径，保留于 `fixed/native-first/`，没有将其当作剪贴板产品失败。修正宿主后 `fixed/native-second/` 及最终矩阵通过。

本轮没有运行全仓库测试、进行安装/OTA 或前台原生窗口验收，不能据此认证所有第三方剪贴板提供者。历史 [2026-10-24 实机 OTA 记录](../../../../docs/OTA_NO_SAVE_NATIVE_2026-10-24.md)及其 `ClipboardPasteCrashFixed: false` 保持原阶段事实。

汇总断言由 [audit-results.ps1](audit-results.ps1)复核，全部 12 项通过；结果、当前默认输出身份和修复相关源码哈希见 [repair-verification.json](repair-verification.json)。仓库严格文档检查完成：断链、Markdown 围栏错误和只在本机存在的链接均为 0，见 [documentation-check.json](documentation-check.json)。

## 后台复查

先构建 `background-probe/ClipboardBackgroundProbe.csproj`。运行 `run-background.ps1` 时，通过 `ComponentRoot` 指定要加载的实际默认 App 输出，通过 `OutputDirectory` 指定独立证据目录，通过 `Modes` 选择表中的场景。默认参数指向修复前的安装载荷，用于历史对照；最终修复版矩阵的组件目录及运行模式在机器账目中记录。不要把诊断宿主加入产品启动或打包路径。
