# 本地矢量图标库

Avalonia 应用内嵌固定版本 `lucide-static 1.25.0` 作为 `StandardLucide` 图标包，运行时不需要图标字体、JavaScript、NuGet 包装器或网络。异世界主题使用从 Game-icons.net 官方仓库筛选的 `GameIconsFantasy` 图标包并保留相同语义名；目录包含 86 个上游 SVG 语义映射并覆盖当前界面全部图标用法，不使用自绘轮廓、自动符文或 Lucide 几何。主题选择由 `ThemeIconResolver` 完成，中央解析器不包含任何具体主题名称分支。

仓库文件：

- `Assets/Icons/lucide-icon-nodes.json`：完整节点目录；
- `Assets/Icons/lucide-package.json`：固定上游元数据；
- `Assets/Icons/LUCIDE-LICENSE.txt`：ISC 许可和 Feather 署名；
- `Controls/LocalIcon.cs`：加载、解析、缓存和 Avalonia 几何渲染。

熟悉的工具栏命令可使用纯图标，但必须提供 Tooltip：

```csharp
var resetButton = new Button
{
    Content = new LocalIcon { IconName = "rotate-ccw", Width = 18, Height = 18 }
};
ToolTip.SetTip(resetButton, "重置视图");
```

需要可见文字时使用：

```csharp
var saveButton = new Button
{
    Content = new LocalIconLabel("save", "保存")
};
```

新增图标名前先调用 `LocalIconLibrary.Contains`。未知名称显示当前主题处理后的 `circle-question-mark`，不会产生空白控件。业务代码不得按主题替换 `IconName`，也不得直接读取某个主题包。

普通与暗夜主题必须继续直接使用原始 Lucide 几何；异世界主题只加载经过人工筛选、带上游来源记录的 Game-icons.net 资源，未映射语义使用同一套图标中的 `help.svg` 回退。运行时主题切换必须触发现有图标重绘。

`Assets/Icons/game-icons-isekai.json` 记录每个语义的上游作者目录、SVG 文件、路径数据及固定提交号；`scripts/import-game-icons.ps1` 可从官方仓库重新导入。资源按 CC BY 3.0 分发，完整署名和许可位于 `Assets/Icons/GAME-ICONS-LICENSE.txt`。

2026-08-30 语义复核后，网格、列表树、画中画分别使用 `divided-square.svg`、`checkbox-tree.svg`、`window.svg`；搜索、放大、缩小分别使用 `magnifying-glass.svg`、`expand.svg`、`contract.svg`；新建文件与文字输入分别使用 `scroll-quill.svg`、`quill-ink.svg`；包搜索与扫描搜索分别使用 `archive-research.svg`、`radar-sweep.svg`。这些易混淆操作必须保持不同上游来源，相关契约测试同时验证来源和实际渲染。

更新目录时必须下载明确固定版本，只替换 `Assets/Icons` 下的三个上游文件，保留许可并运行完整构建与测试。禁止使用浮动版本或运行时联网获取图标。
