# ISO B、C、D 面形不规则度回归

日期为 2026-11-05。范围为居中圆形口径顺序公差，覆盖真实曲面、单位和统计、灵敏度与反求、种子确定性、指定频率 MTF、离屏编辑器和旧文件迁移。

| 项目 | 独立控制 |
| --- | --- |
| 连续极值 | 四重根的径向四次函数，以及 `t(1−t)` 和 `√t(1−t)` 的包络。后二者的峰值位于口径内部，单取边缘会得到零。 |
| ISOB | 对每个角频率 0–7，用实际追迹几何的矢高独立扫描 8193 个径向位置。两个相位方向恢复非零频率的包络。以 1600 nm 为输入，检查测得 P–V 的相对低估不超过 10⁻⁶，同时核对相反相位及顶点基准。 |
| ISOC | 用四个 Legendre 多项式的显式表达核对 Standard 11、22、37、56 的组合。输入 632.8 nm、0.6328 µm 或 2 个 0.6328 µm 条纹，得到同一连续 P–V。核对旋转对称性、解析法线、真实交点、缩放负端点及恢复。12 次固定幅值试验的形状不同。 |
| ISOD | 对实际矢高做径向 Gauss–Legendre 与等角积分。去均值 RMS 为 0.0012 mm，未去均值 RMS 包含轴上基准的偏移。12 次固定幅值试验保持指定 RMS；384 次截断正态试验的幅值落在开区间内，检查均值和标准差。 |
| 产品工作流 | Cooke 的三个不同表面分别配置 B/C/D，跑指定频率几何 MTF，比较单工作线程和双工作线程。运行 RMS 点列的反向增量，检查幅值保持对称、ISO 独立统计选项不丢失，原处方和工作区修订保持原值。 |
| 编辑器与文件 | 选择 ISOC，修改条纹、检测波长和截断正态，报告显示 P–V。换为 ISOD 后显示 RMS。验证 v5 往返、v4 版本边界，以及缺少 ISO 统计字段的 v5 默认使用固定最大值。 |

## 实际运行

Windows 隔离 Release 构建通过，零警告、零错误。ISO 定向测试 17/17 通过。扩大到完整公差筛选、SpecialSagSurfaceTests 和 ZemaxZernike 后，126/126 通过，无跳过。后一组已包含 ISO 定向用例。

中英日词库 sync/check 验证 3116 条译文、2869 处静态界面文字，漏译和错误均为零。

## 商业定义与本地模型

Ansys 文档给出 ISO 操作数的项号、单位、灵敏度端点和幅值分布。独立 B 使用单一角频率的 Standard 5–37 项族，C 使用四个径向项，D 使用 Standard 5–37 项的去均值 RMS。现有产品测试核对这些本地模型的解析量与真实追迹结果。OpticStudio 内部随机系数和逐值性能仍待对照。B/C 同面需要同时标定两者，当前验证明确拒绝该组合。偏轴或非圆口径的 ISO 面形仍待实现。[Ansys ISOB](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/ISOB.html) · [Ansys ISOC](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/ISOC.html) · [Ansys ISOD](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/ISOD.html)
