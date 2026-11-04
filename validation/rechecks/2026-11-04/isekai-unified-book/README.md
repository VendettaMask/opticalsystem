# 异世界统一棕金书封与小配饰

用户于 2026-11-04 确认以[棕金母版](../../../../docs/assets/themes/isekai/references/04-approved-gold-master-20261104.png)共用配色、边框和页签，只更换小配饰；并授权接入程序后查看。此前不同颜色的四套提案不是当前要求。

## 已实现

- 同一暖象牙纸、棕皮、旧金和深褐墨色；纸纹浓度降低，内角百合/藤蔓保留。
- 实际绘图/数据/文本页签在连续书封内，使用切角双金线铭牌与底栏金饰。原生 TabControl/TabItem 的选择、键盘和双内容呈现器保留，切换主题不移除或重建绘图内容。
- 类型化元数据选择四类同色配饰：星象、炼金、符文、水晶/书卷；名称和译文不参与判断。图例下方没有空位时省略右侧摆件，摘要仍可使用对应书册。
- 书册避开实际 TextLayout 行边界，无空白矩形则省略。装饰不接收输入，不改参数、图例布局或科学色标。
- 原源图 1200×900、160 DIP 图签、字体、光学结果与品牌轮廓保持。实时内层源图不叠第二道装饰框；PNG 仍输出完整 28 DIP 窄框与源图，不输出程序页签/工具栏，1×1256×956、默认配置1920×1080；报告保持原比例/物理字高。

## 实际程序截图

均由真实 C# 控件和共享 Core 的 Cooke 示例结果离屏渲染，非生成概念图。以下固定时间用于可重复截图，不表示另一次光学精度认证。

| 分析 | 当前实际界面 | 当前源图导出 |
| --- | --- | --- |
| 惠更斯 MTF / 星图配饰 | [界面](previews/Huygens-MTF-live.png) | [源图](previews/Huygens-MTF-source.png) |
| 惠更斯 PSF / 炼金配饰 | [界面](previews/Huygens-PSF-live.png) | [源图](previews/Huygens-PSF-source.png) |
| 波前 / 法阵配饰 | [界面](previews/Wavefront-Map-live.png) | [源图](previews/Wavefront-Map-source.png) |
| 点列 / 书卷配饰 | [界面](previews/Spot-Diagram-live.png) | [源图](previews/Spot-Diagram-source.png) |

## 验证结果与范围

默认 Release App、两个内置模块依赖与测试构建完成。最终常规集合 [final-scoped.trx](final-scoped.trx) 为 **112 通过、0 失败、6 按设计跳过**；在独立 Skia 导出进程中启用这类检查，[exports-final.trx](exports-final.trx) 为 **23 通过、0 失败、0 跳过**。两个最终进程按完整用例名去重为 **131 个不同用例通过**，重复用例不累加；导出类的跳过占位不当成通过。进一步强化四个实际画面必须出现书册的断言见 [folio-presence-final.trx](folio-presence-final.trx)。

覆盖原生键盘选页、数据与完整文本、图例筛选的保留/恢复、600×440 窄窗、fit/100%、源图和图签布局、背景空位避让、2D/3D/实体模型相机/光线状态、四个其他主题往返、线宽、光扇/点列、PNG边框/编码/取消及表格默认页。原先严格断言必须正好是基类 TabControl/TabItem 的测试改为验证原生基类契约，原内容和几何断言保留。

四个非 Isekai 主题的 **8 项 PNG/report 像素哈希**与改动前记录一致，当前收据为 previews 下各主题的 *-pixels.json；初始收据在本任务早先保留的 artifacts/validation/isekai-multi-concepts-20261104/unchanged-pixels.json，当前核对摘要见 [verification.json](verification.json)。实时往返在每个主题独立创建的实际控件树中比较，避免串联不同非材质主题时继承 Headless 首帧缓存。没有重新生成历史基线来消除差异。

中间失败保留于 focused-initial.trx、focused-repair.trx、roundtrip-diagnostic.trx、stable-tree.trx 和 roundtrip-final.trx：首轮主题换模板造成内容已有视觉父级，随后固定两个原生呈现器与内容树；Pixel 图例的重复测量差异随该修复消失。另修正了新增实时往返夹具的冷启动/首帧时序。诊断构建曾遇到另一后台测试进程短时锁定测试输出，未关闭该进程；后续正常默认输出构建完成。最初装饰图集背景不透明，已由内置图像工具移除背景，最终 PNG 透明比例和源矩形见[素材元数据](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/accessory-assets.json)。

本批只在后台 Headless/Skia 验证，没有控制或重启用户应用、打包、推送远端或发布安装包。功能图标没有修改；工作区并行的非序列功能不属于本批 UI 验证范围。本记录不代表完整光学精度认证。现行规范已同步到 AGENTS、UI_DESIGN_SPEC、THEME_PACKAGES、UI_DESIGN_REVIEW、素材记录和 CURRENT_STATUS。
