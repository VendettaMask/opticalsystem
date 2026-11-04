# 界面语言与本地词库

更新日期：2026-11-04。

界面支持简体中文、English、日本語。运行时从应用程序集内置的 `Assets/Localization/ui.catalog.json` 读取译文，从 `ui.invariants.json` 读取保留内容声明；不联网、不调用翻译服务、不读取用户文件来生成翻译。异世界主题继续遵循已确认的 English 锁定规则，普通主题保留用户语言选择。

## 开发流程

```powershell
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- extract . ui-extracted.json
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- sync .
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- check .
```

`extract` 使用当前 .NET SDK 自带的 Roslyn 解析 C# 源码，记录原文、文件、行号和模板。覆盖原生 Text/Content/Header/Title/PlaceholderText、工具提示、可访问名称、命令/Ribbon、直接字符串下拉选项、条件/切换分支和可识别的本地标签辅助函数；拼接与插值共同编号，避免串接多段插值时重复使用 `{0}`。只扫描 App 源码，不扫描工程、材料数据库、用户文档、构建输出或注释。任意外部返回值、反射生成文字和跨文件不透明辅助函数无法仅靠语法穷举，应在定义处显式使用本地化接口；提取清单零漏项不代表穷举了所有运行时状态。

词库生成输入：

- 现有 `UiLocalization*.cs` 的三语词条及 `DesignReviewLabels`，按原 `BuildTexts` 调用顺序提取，保留原有覆盖顺序。这些方法只作为兼容的生成输入，运行时不再执行 `BuildTexts`。
- `localization/ui-translations.tsv`：补齐的三语界面文字。列为原文、英文、日文；英文/日文源词条可用第四列指定中文。字面 `\n` 表示换行。
- `localization/ui-translations.json`：适合长说明的同一三语结构。
- `localization/optical-terms.tsv`：统一光学术语，明确区分辐照度 Irradiance / 放射照度、照度 Illuminance / 照度及辐射亮度 Radiance / 放射輝度。普通补充词条不得覆盖术语表。
- `localization/invariant-text.tsv`：材料型号、单位、公式示例、品牌和技术标识等保留内容，必须有逐条原因；不能用它放行待翻译的普通提示。

生成器自动复用没有歧义的已有三语译文，并派生访问键、冒号和已知前缀模板。无法可靠复用的新句子列入待译清单，需补齐译文后再同步；工具不会猜测专业句子或把原文复制成假译文。此次新增文字已补齐，后续也可由翻译人员或代码助手完成待译条目。

`sync` 在存在漏译、空译文、占位符不一致、数值/单位/链接丢失、已知公式变化或术语冲突时拒绝更新词库。`check` 同时验证生成资源未过期，失败返回非零。普通 CI 在 Windows、Linux、macOS 检查同一生成结果；没有新增发布依赖。

## 界面文字与原始数据

```csharp
button.Content = UiLocalization.Text("保存");
status.Text = UiLocalization.Format("已保存：{0}", originalPath);
UiLocalization.Preserve(materialNameLabel);
```

`Format` 只翻译模板，参数不递归翻译。已有动态提示可匹配本地模板，直接保留每个捕获值，不解析或重新格式化数值，保留路径、前导零、有效位数、公式与单位。模板支持三语往返，不能以普通词语替换用户数据中的片段。

材料名、文件名、厂商原文、公式和物理值在显示边界使用 `Preserve`；该声明对子控件继承。TextBox 编辑内容、绑定的数据文字、数值字体角色和数值编辑器内部文字默认保留。明确属于界面选项的绑定标签通过 `LocalizeChoiceLabel` 选择翻译；材料选择器保持原始字符串和选中项身份。不要对用户数据调用 `Text`，也不要从显示译文推断命令或数据身份。

语言变更更新现有、随后加载和已预先翻译的控件，保留原始来源和数据绑定；无歧义的反向词条支持再次切换。处方、材料、命令标识、计算修订、数值后端和单位定义不参与语言切换。

本轮实际数量、构建与定向验证见[当前状态](CURRENT_STATUS.md)和[验证记录](../validation/rechecks/2026-11-04/local-ui-catalog/README.md)。
