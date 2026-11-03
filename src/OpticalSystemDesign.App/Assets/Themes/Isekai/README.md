# Isekai 绘图材质素材

本次使用的三张[参考原稿及来源/哈希](../../../../../docs/assets/themes/isekai/references/README.md)已随仓库保存，包含用户上传的二维/分析参考和最初确认的生成概念稿；参考原稿不参与运行资源嵌入。

当前版本按用户 2026-11-03 两张附图修订：花饰位于羊皮纸**内部**，窄皮革边采用圆角旧金包角与星芒，另有双层细线内框和图例留白黄铜星盘。素材由内置 `image_gen` 生成，保留原 PNG/透明通道并嵌入 App。未使用 CLI/API 回退或第三方图库。新圆角框提示词见 [rounded-frame-prompt.json](rounded-frame-prompt.json)，内角素材见 [inner-prompts.json](inner-prompts.json)，纸纹/黄铜星盘及历史稿见 [prompts.json](prompts.json)。

| 当前资源 | 原始像素 | 用途 | SHA-256 |
| --- | --- | --- | --- |
| [rounded-gold-binding.png](rounded-gold-binding.png) | 1448×1086 RGBA | 圆角旧金包角、双金线和顶部星芒；窄皮革边不含花叶 | `d17f4ce78488777400f29734a6c0c07dfea3485feb74afe7bf99d7078c6e615c` |
| [inner-botanical.png](inner-botanical.png) | 1598×984 RGBA | 四角百合/藤蔓、天体星盘、炼金瓶、晶簇；等比画在纸内 | `62502bc8626eb9b1070fb1a6c31f6ab6e4e092bfda38cf8d1a2031f1629cd7ae` |
| [vellum.png](vellum.png) | 1448×1086 RGB | 羊皮纸，轻色中心区域作为阅读底纹 | `4a8b61331cb9e1c2a19bd92bc9fdff96b11355c7d1e721fe417d84c704fd5f5b` |
| [armillary.png](armillary.png) | 1086×1448 RGBA | 仅在分析图例实际留白中绘制的黄铜星盘 | `7e6658e0614bfa66f3813ce547b99c1c898e7bc95a11f2fce6ee6b3ae14dabf0` |

用户参考为 `codex-clipboard-f76f0cfc-29c7-4083-b6cc-283f05eb2d46.png`，仅用于风格和装饰位置，不取用其中镜头/光线/数值。新工具输出 ID 为 `exec-80f06fa6-4b4b-4f76-9675-2e851d1677e7.png`（窄框）、`exec-d80d9e5a-3332-488d-98f1-536d5b0b5536.png`（内角花饰）。纸纹输出为 `exec-565feee3-2809-45a0-94fa-840cf7eb9018.png`。产品只依赖本目录嵌入资源，不依赖个人生成缓存。

第二张参考为 `codex-clipboard-00a16ecf-835f-4a2c-9fd9-eab969fea1b3.png`；圆角框原始输出为 `exec-40cd07b3-6212-44bd-ba1d-ad1fd2ad440f.png`。同样只参考装饰，不使用示意图中的任何曲线、读数或控件。

实现入口为 `Theming/IsekaiDrawingMaterial.cs`。素材只作装饰，不包含科学数据、图表、公司标志或产品控件。实际曲线/光线/镜片仍由原 C# 绘制；公司标志仍使用 `Assets/Brand/CompanyLogo.svg` 轮廓。解码、四角画刷和图签背景按 UI 线程复用；局部高质量插值不改变科学数据栅格采样。

2D/3D 中花饰按 0.9 不透明度绘于数据下方；分析和图签使用 0.22，分析两侧 18 DIP 纸内窄边补充 0.55 的细线花饰。双层内框使用主题自有细墨线；黄铜星盘避开实际图例，无足够留白时不画。窄皮革边在原源图外增加 28 DIP，1× 普通图纸完整尺寸为 1256×956；报告复用内部图纸，保持原比例/物理字高，不加入外部皮革边。详见[当前实际截图与验证](../../../../../validation/rechecks/2026-11-03/isekai-rounded-border/README.md)。

历史资源 [grimoire-frame.png](grimoire-frame.png) 和 [leather-binding.png](leather-binding.png) 保留为之前已交付稿件的来源，但**不再嵌入或运行加载**。此前来源、用途与验证见[宽花框阶段](../../../../../validation/rechecks/2026-11-03/isekai-rich-frame/README.md)和[图内花饰阶段](../../../../../validation/rechecks/2026-11-03/isekai-inner-ornament/README.md)；不是当前外框。
