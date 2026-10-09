# 同源 N01 原生传播方法控制

2026-10-09，OpticStudio 26.1.0 / build 260127、有效 Enterprise API 许可。两份未保存 MS-L7 副本的 SHA-256 均为 `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`；仅将系统高级 `HuygensIntegralMethod` 分别设为 Auto、Planar、Spherical，读取确认后捕获，最后恢复原 Auto。未保存处方。

保留原 N01 配置 1、轴上视场 1、波长 1、瞳孔/像面各 32、间距 0.25 µm、偏振和质心关闭、峰值归一化关闭。`cross-section` 为原 X_Linear 截面设置，其 API 数值数组为空，实际可用原生文本含 33 点（包括正边界），强度六位小数。`grid` 仅将分析显示改成二维 Linear，直接取得 32×32 双精度强度和完整坐标。

Auto 与 Planar 的 1024 个二维像素逐值完全相同；Spherical 与 Auto 的相对 L2 为 0.0999197655786755，最大绝对差为 0.0516070576497783。Auto 的 33 个文本值同时与 Planar 及原冻结 N01 文本相同。这支持该同源控制的 Auto 实际采用 Planar，不能外推所有镜头的 Auto 分支。

`manifest.json` 对每份原始文件保留字节数和 SHA-256；空数组比较的早期无效诊断没有收入。上述比较属于诊断说明，原始数据没有替换成推断值。正式 Core 本次未改变 Huygens 模型；完整方向、参考和权重仍需进一步隔离，不能直接把一个传播分支替换当作已通过认证的修复。
