# 本地矢量图标库

Avalonia 应用内嵌固定版本 `lucide-static 1.25.0`，运行时不需要图标字体、JavaScript、NuGet 包装器或网络。

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

新增图标名前先调用 `LocalIconLibrary.Contains`。未知名称显示本地 `circle-question-mark`，不会产生空白控件。

更新目录时必须下载明确固定版本，只替换 `Assets/Icons` 下的三个上游文件，保留许可并运行完整构建与测试。禁止使用浮动版本或运行时联网获取图标。
