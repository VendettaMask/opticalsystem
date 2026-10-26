# CompanyLogo.svg 制作来源

2026-10-26 按用户要求，以现有 `CompanyLogo.png` 的品牌拼写、无横杠 A、横向构图与深蓝色为参照重新制作标准矢量字标。字形来自 Nasalization Regular 的实际字体轮廓；横向排版保留约 11.79:1 的原标识比例。旧 PNG 只保留作历史视觉参照，不嵌入产品或 SVG，也不参与运行时绘制。

- 字体作者：[Typodermic / Nasalization](https://typodermicfonts.com/nasalization/)。
- 获取来源：[作者免费桌面字体页](https://typodermicfonts.com/desktop/)，下载包 `typodermic-free-fonts-2026i.zip`。
- 制作字体：`Nasalization Rg.otf`，67,384 字节，SHA-256 `aefedadd5c49e84537ba3add3c487e1e8c2d1e27b4dbf4fefecd19000374a9d1`。
- 随下载包取得 `Typodermic Desktop EULA 2026.pdf`。作者区分[完整固定图形与字体软件嵌入](https://typodermicfonts.com/license/)：本项目分发完整字标轮廓，不分发字体软件、可重组字母集或运行时排字功能。

SVG 仅含一条完整组合的 `path`，以及标题与说明；没有 `image`、`text`、外部链接、Base64 图片或字体数据。Avalonia 与 Skia 从这一资源绘制相同轮廓；工程图 PDF 的默认 Logo 直接保留路径。

字体和随包协议保留在本机忽略目录 `.tmp/brand-vector/font-source/`，不进入 Git 或发布载荷。维护者需自行从作者获取适用的制作字体后，在仓库根目录执行：

```powershell
python tools/branding/build-company-logo.py --font ".tmp/brand-vector/font-source/Nasalization Rg.otf"
```

生成工具仅供离线制作，需要 Python/fontTools；普通构建和产品运行只使用已经提交的 SVG，不安装或查找 Nasalization，不依赖 Python 或网络。
