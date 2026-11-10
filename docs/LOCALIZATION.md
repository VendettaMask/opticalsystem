# 界面语言与本地词库

更新日期：2026-11-10。

界面支持简体中文、English、日本語。运行时从应用程序集内置的 `Assets/Localization/ui.catalog.json` 读取译文，从 `ui.invariants.json` 读取保留内容声明；不联网、不调用翻译服务、不读取用户文件来生成翻译。异世界主题继续遵循已确认的 English 锁定规则，普通主题保留用户语言选择。

## 开发流程

```powershell
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- extract . ui-extracted.json
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- sync .
dotnet run --project tools/OpticalSystemDesign.Localization -c Release -- check .
```

`extract` 使用当前 .NET SDK 自带的 Roslyn 解析 C# 源码，记录原文、文件、行号和模板。覆盖原生 Text/Content/Header/Title/PlaceholderText、工具提示、可访问名称、命令/Ribbon、直接字符串下拉选项、条件/切换分支和可识别的本地标签辅助函数（含局部函数、LocalIconLabel 和 CompactLabel）；拼接与插值共同编号，避免串接多段插值时重复使用 `{0}`。同一命名空间、同一 partial 类型的辅助函数会跨源文件分析，保留调用处的原文件和行号，不借用其他类型的同名函数。只扫描 App 源码，不扫描工程、材料数据库、用户文档、构建输出或注释。任意外部返回值、元组中的标签、反射生成文字和其他类型的不透明辅助函数无法仅靠语法穷举，应在定义处显式使用本地化接口；提取清单零漏项不代表穷举了所有运行时状态。

词库生成输入：

- 现有 `UiLocalization*.cs` 的三语词条及 `DesignReviewLabels`，按原 `BuildTexts` 调用顺序提取，保留原有覆盖顺序。这些方法只作为兼容的生成输入，运行时不再执行 `BuildTexts`。
- `localization/ui-translations.tsv`：补齐的三语界面文字。列为原文、英文、日文；英文/日文源词条可用第四列指定中文。字面 `\n` 表示换行。
- `localization/ui-presentation-translations.tsv`：分析元数据、工程图、报告和跨窗口绑定的三语补充，使用同一生成/校验管线。
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

2026-11-09的[跨功能离屏审计](../validation/rechecks/2026-11-09/english-ui-audit/README.md)进一步确认：Dock模型Title、DataGrid表头绑定、ComboBox占位符/选中项、Application/Core生成的结果标签、直接绘制文字和工程图/报告输出仍有漏译。静态词库零缺失不是运行时英文完整性验收；新增界面审查必须覆盖这些呈现边界，同时保留用户名称、原始厂商注释与物理数据。这些呈现边界已在2026-11-10接入共享绑定，见[统一绑定复验](../validation/rechecks/2026-11-10/english-ui-binding/README.md)。

## 光学装调的动态显示

装调窗口的表格像类型、Core 返回的固定诊断、测量模式与叉丝详情在呈现边界翻译。固定模式前缀与结果说明分别翻译；组合详情显式翻译标签后再填入原始表面名和数值，不递归处理用户数据。窗口监听语言变化，原位刷新行属性和详情；保留表格选择、共轭结果、叉丝像素、仪器参数和处方，不发起计算。

2026-11-09 根据实际英文截图修复，并扩大 partial 辅助函数检查，补齐相机、镀膜、公差等界面的同类遗漏。英文校验也覆盖花括号内的说明性标签，只有数字格式占位符可豁免。静态词库检查与装调/共享语言的离屏回归范围分别记录，不能据此声称全产品所有动态状态都已穷举。见[英文残留复验](../validation/rechecks/2026-11-09/english-ui-localization/README.md)。

## 动态呈现及导出语言

`BindChoices` 为固定选项提供显示模板，不改 ItemsSource、枚举或保存键；`TranslateBinding` 只绑定明确的只读界面字段，不能用于用户名称、材料或数值。Dock 标题保留规范描述；DataGrid 表头跟踪列增加及标题变化。`BindLanguage` 持有弱生命周期关系，直接绘图在语言变化时失效重绘，不创建新计算结果。控件保存原文、上次显示及语言，重复布局可复用已翻译结果。

工程图默认输入通过 `SetDefaultText` 绑定；用户编辑后停止自动改写。重绘已接受图纸时，只有仍对应同一默认来源的显示文字可更新。动态模板通过 `Format` / `Template` 保留参数的原值与格式；计算记录仅翻译固定键和明确的分类值，原始报告 DTO 不改。

报告入口捕获一次语言，透过 `ReportImageRequest.Language` 和报告模型传入同源图像及四种写入器。`UseLanguage` 是异步局部输出作用域，已存在的 UI 控件继续使用界面语言，导出创建的控件保持自己的输出语言。不得用全局切换 UI 来实现导出翻译。新增界面需同时核对已打开窗口、后来创建的控件、主题往返和实际导出，而非仅检查词库是否存在键。
