# 分析结果页与公差编辑器复核

| 范围 | 实现与复核样例 |
| --- | --- |
| Y–Ybar | 图签列出实际波长和表面范围。文本按表面并排给出 Y–Bar 和 Y。[绘图](yybar-plot.png) · [文本](yybar-text.png) |
| 渐晕 | 按视场绘制未渐晕光线分数。[绘图](vignetting-plot.png) |
| 入射角与像高 | 文本按像高并排给出三类光线的实际入射角。[文本](incident-angle-text.png) |
| 表面与系统数据 | 表面报告按所选表面给出厚度、材料、折射率和相关物理量。系统报告使用文本或数据视图。[表面报告](surface-data-text.png) |
| 网格畸变、对比度损失 | 图签列出物理视场、最大畸变和 SMIA TV 畸变。对比度损失图在光瞳内显示采样细节和实际频率、子午与弧矢 MTF。[网格畸变](grid-distortion.png) · [对比度损失](contrast-loss-map.png) |
| 赛德尔与 Zernike | 赛德尔报告分组对齐系数，支持窄窗口阅读。Fringe、Standard、Annular 系数报告使用正文展示系数和公式。[赛德尔报告](seidel-coefficients.png) · [窄窗口](seidel-narrow.png) · [波长系数](seidel-waves.png) |
| 惠更斯 PSF 截面 | 图签使用波长范围、视场、像宽、斯特列尔比及图心。[截面](huygens-psf-cross-section.png) |
| 傅里叶 MTF 随视场 | 默认 10 至 60 cycles/mm 六个独立输入。单项 0 停用，六项均为 0 时显示零频。旧单频预设保留原频率。20 cycles/mm 单频与多频结果逐点一致。[设置](fourier-mtf-vs-field-settings.png) · [绘图](fourier-mtf-vs-field.png) |
| 四项 MTF 曲面与场图 | 曲面按选定视场显示双空间频率，场图按固定空间频率扫描 X、Y 视场。场图可固定 0–1 色标，也可按数据范围着色。[三维 FFT](fft-surface-mtf.png) · [二维惠更斯](huygens-surface-mtf.png) · [二维视场 FFT](fft-mtf-map.png) · [二维视场几何](geometric-mtf-map.png) |
| 二维视场 RMS | 默认 21 × 21 个波前采样点形成 20 × 20 个完整色块。颜色取相邻四个节点的平均，原始采样点仍可读。MS-L7 自动半宽为 10.6066 mm。[绘图](rms-field-map.png) |
| 公差操作数 | 在主窗口用 MS-L7 运行公差向导，验证生成的 73 条操作数在 DataGrid 模板中显示分类底色。[编辑器](tolerance-wizard-docked-light.png) |

新增四项 MTF 的比较工具条目记录二维网格类型及原生分析身份，当前无数值比较适配器。原有 95 支菜单镜头冻结验收保持独立。本页截图为 MS-L7 定向复核。

图库、功能绑定表、五主题总图和 SVG 包核对了 225 款图形、336 处绑定、13 个 MTF 入口及 1,125 份主题 SVG。[图标目录](../../../../docs/assets/icons/README.md)

同步前两个 Release 测试项目均编译通过。首轮通过 50 项桌面用例，原有图标基线对比用例更新后补跑通过，覆盖共 51 项桌面用例。比较契约与注册表审计 7 项通过。原 1,989 个亮色、黑洞、像素图标像素哈希全部一致，新增四个入口另有三种尺寸的图标回归覆盖。
