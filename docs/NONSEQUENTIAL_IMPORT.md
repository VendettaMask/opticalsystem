# 非序列文件导入

在非序列模式选择“文件 → 示例”或“导入 Zemax NSC”，也可用“打开”选择 ZMX。支持的文件直接完整载入当前场景，操作支持撤销。无法导入时仅显示简短提示和文件名，当前工程保持原样。取消文件选择也保留当前工程。

## 当前支持

- 矩形、椭圆、点光源及单光线；固定值、优化变量的保存值、位姿和参数拾取。同列拾取及跨列拾取均重新求值。
- 正数绝对参考、负数相对参考和包含关系。保留对象备注，并沿用主程序参考坐标链。
- 矩形体、球体、透明圆柱及圆台、标准透镜、偶次非球面透镜、空对象和平面矩形。标准透镜允许前后口径不同，各面净口径与机械口径保持一致。实体材料使用原文件目录中的材料身份；矩形体保留反射/吸收行为。
- POB 多边形资源的三角面、四边面、缩放、面编号及闭合性检查。支持统一反射、吸收或闭合折射交互；几何嵌入 STAROPT，删除原始 POB 后仍可重开和追迹。
- 普通及彩色矩形探测器的已有偏振分量。光源随机偏振与固定 Jones 状态分别导入。
- 已实现的波片、显式光谱和真彩色设置见[彩色波片](NSC_COLOR_FRINGES.md)。绘图分辨率保留在原始记录中，求交使用共享 Core 几何。
- 光源支持系统波长、XYZ、xy、u′v′、D65、均匀功率光谱及黑体光谱。选定源光谱后以其波段发射，覆盖原系统波长编号。流明输入按实际波谱的 CIE 1931 明视觉响应转换为瓦特保存。

透明圆柱/圆台由两个平面及精确侧面组成，原点位于前端面；偶次非球面使用已有的逐面光学组件，保留系数、独立口径和前后材料。POB 中的面号 n 对应 STARMESH 面号 n+1，原始记录保留供应方编号。其他几何、端口及活动属性按文件逐项列出。

## 106 个内置文件的检查结果

2026-11-23 批量检查后，以下四个文件可导入并执行追迹：

| 分类 | 文件 |
| --- | --- |
| Birefringence | Color Fringes.ZMX |
| Colorimetry | Example 1  Two-color mixing gives white.ZMX |
| Miscellaneous | Even asphere.zmx |
| Sources | sobol sampling.ZMX |

接入用户提供的 `Objects` 后，当前示例引用的 26 份资源随程序交付，共 3,304,662 字节。可映射对象由资源接入前的 311 增至 325；完整可应用的文件仍为 4 个。其余文件的对象类型、活动属性及端口问题见[最新逐文件结果](../validation/rechecks/2026-11-23/nsc-objects-layout/files.md)。当前缺少 `LETTERF.BMP`、`BARCHART.BMP`、`ALEX200.BMP`，分别被三个示例引用，见[资源清单](../validation/rechecks/2026-11-23/nsc-objects-layout/resources.md)。

POB 读取同时支持旧资源省略默认交互标记的写法。原始 ZMX 和资源保留字节及文件名大小写；`asset-index.json` 记录资源来源、哈希及引用示例。[本轮验收](../validation/rechecks/2026-11-23/nsc-objects-layout/README.md)保存接入后的检查结果；[首轮](../validation/rechecks/2026-11-23/nsc-import-repair/README.md)和[第二轮](../validation/rechecks/2026-11-23/nsc-import-resources-spectra/README.md)保留各自的历史结果。

## 后续文件的目录约定

设计文件与配套资源放在同一个数据根目录下，资源沿用原来的子目录和文件名。例如：

```text
DataRoot/
  Samples/
    Prisms/
      design.zmx
  Objects/
    Polygon Objects/
      Prism45.pob
    Apertures/
      hexagon.uda
    Sources/
      IESNA/
        Projector.ies
      Source Files/
        source.dat
    Bitmaps/
      image.bmp
  Images/
    image.bmp
```

读取器从设计文件所在目录向上查找，最多检查八层目录。每层先检查同目录文件，再按扩展名查找上述资源子目录；IES 和源光线数据也可放在该层的 `Sources/IESNA`、`Sources/Source Files`。文件名按大小写不敏感匹配，磁盘上的原始名字保持不变。新增文件沿用相同规则，`asset-index.json` 用于来源核对，不参与运行时文件筛选。

内置目录构建后位于 `Examples/NonSequential`，保留相同结构。POB 成功映射后的网格嵌入 `.staropt`，工程重开从工程内读取几何。

## 维护检查

维护工具支持单文件检查及批量检查：

```text
dotnet run --project tools/OpticalSystemDesign.NscValidation -- source.zmx result.json
dotnet run --project tools/OpticalSystemDesign.NscValidation -- --audit source-directory result.json
```

批量结果保存每个源文件的 SHA-256、对象映射数量和全部诊断。[资源接入检查](../validation/rechecks/2026-11-23/nsc-objects-layout/README.md)记录资源原字节与构建输出、独立目录查找、棱镜实际命中与全反射、移除资源后的保存重开。[直接导入检查](../validation/rechecks/2026-11-23/nsc-direct-import/README.md)记录现行菜单、文件选择、简短失败提示及撤销流程。

## 定义依据

参数与坐标映射依据官方说明：[参考对象和坐标顺序](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Object_Placement.html)、[拾取列编号](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/SOLVETYPE.html)、[标准透镜](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Standard_Lens.html)、[偶次非球面透镜](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Even_Asphere_Lens.html)、[探测器矩形](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Detector_Rectangle_Object.html)。

资源与光谱依据：[POB 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Polygon_Object.html)、[源颜色与光谱](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Sources.html)、[CIE 普朗克定律](https://cie.co.at/eilvterm/17-24-005)、[光度学定义](https://www.cie.co.at/publications/basis-physical-photometry-3rd-edition)。
