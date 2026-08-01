# 打包镜头库

## 运行时模型

桌面应用不下载、同步、解压或转换镜头文件。发布前生成只读原生库：

```text
LensLibrary/
  index.json
  projects/*.staropt
```

应用启动时读取 `index.json`，参数和二维预览只打开 `.staropt`。镜头库页面位于“数据库 > 镜头库”，与材料库分离。选中条目只更新预览，不改变当前设计；双击条目打开打包工程并激活镜头编辑器。

实像高系统若无法生成预览光线，仍显示镜头几何，不因光线预览失败而放弃整个条目。

## 离线构建

维护工具 `tools/OptilandWorkbench.LensLibraryBuilder`：

1. 在临时目录安全解压 ZIP；
2. 扫描本地 ZMX；
3. 使用 Workbench 玻璃数据库解析材料；
4. 导入全部支持的配置；
5. 写入带校验的 `.staropt` 和紧凑索引。

```bash
dotnet run \
  --project tools/OptilandWorkbench.LensLibraryBuilder \
  -- \
  tools/lens-library-public-sources.json \
  src/OptilandWorkbench.App/Assets/LensLibrary
```

当前打包库共 849 项：56 个独立显微物镜、5 个工业示例和 788 个可转换公开 Zemax 设计。显微类别只允许独立物镜；筒镜、聚光镜、Fourier 成像链和完整显微系统排除。

## 来源与 Git 策略

下载和展开的数据位于忽略目录：

```text
local-data/lens-library/originals/user-zmx/public/
```

仓库内测试样例位于相邻 `user-zmx/project/`。只有审核后的 `index.json` 与 `.staropt` 随应用打包。来源许可必须记录；不支持的结构离线构建时明确失败，不能近似替代。

## 单文件转换与安装

Windows 可把 `.zmx` 拖到 `Convert-Zemax-Lens.cmd`，或执行：

```powershell
.\Convert-Zemax-Lens.cmd "D:\lenses\example.zmx"
```

工具会导入支持配置、写入并重读 STAROPT 校验、发布到 `samples/lenses` 和打包镜头库，并按稳定 ID 更新索引。相同来源重新导入会更新条目，不创建重复项。全部输出先暂存，任一步失败都保留旧库。

可通过 `--name`、`--category`、`--source-name` 和 `--license` 提供元数据；运行 `--help` 查看完整参数。

## 公开语料同步

`tools/Sync-Public-ZemaxCorpus.ps1` 从 Figshare、Zenodo 和已知 Mendeley 数据集同步声明开放许可的真实 ZMX，并记录来源、MD5/SHA-256 和许可。

`tools/Sync-DanReileyLensExchange.ps1` 镜像 Dan Reiley Lens Design Exchange 的公开目录，并记录文件 ID、原名、哈希、重复关系和失败。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-Public-ZemaxCorpus.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-DanReileyLensExchange.ps1
```

批量转换：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Import-Public-ZemaxCorpus.ps1
```

2026-07-29 同步语料包含 1,050 个 ZMX 清单项：788 个成功、256 个保留明确失败报告、6 个重复内容跳过。失败条目仍保留在下载语料中，不会静默近似。
