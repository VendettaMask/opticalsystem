# 镜头数据求解入口

更新日期：2026-09-07。

## 已实现的交互

镜头数据表的曲率半径、厚度和净口径使用同一种单元格求解入口。数值右侧保留 24 DIP 标记区；点击标记区或右键数值单元格打开当前列的求解设置。独立的“R 变量”“T 变量”和净口径“固定”复选框列均不再显示，表面属性页也不再提供另一套净口径固定开关。

标记遵循 Zemax Lens Data Editor 规则：

| 列 | 默认/固定 | 变量或用户固定 | 拾取 |
|---|---|---|---|
| 曲率半径 | 固定为空白 | 变量显示 `V` | `P` |
| 厚度 | 固定为空白 | 变量显示 `V` | `P` |
| 净口径 | 自动为空白 | 用户固定显示 `U` | `P` |

- 曲率和厚度处于固定状态时，首次打开默认选择变量；净口径处于自动状态时，首次打开默认选择固定。所有变化只有点击“确定”才写入工程。
- 曲率、厚度和净口径拾取均只允许前序面。曲率与净口径提供比例因子；厚度提供比例因子和偏移量。拾取目标数值只读，并随源面变化更新。
- 新插入表面的厚度默认为固定，净口径默认为自动；自动净口径不显示字符。取消拾取后保留当前结果值，并切换到所选的固定、变量或自动状态。
- 表面行的插入、删除菜单仍在非求解单元格上使用右键打开；在三类求解数值单元格上右键只打开相应求解设置。

## 计算与保存

曲率拾取遵循 Ansys [Curvature Solves](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Curvature_Solves.html) 的曲率比例：`C_target = factor × C_source`，因此非零比例时 `R_target = R_source / factor`。厚度拾取遵循 [Thickness Solves](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Thickness_Solves.html)：`T_target = offset + factor × T_source`。净口径的自动、用户固定、拾取及字符含义参考 [Clear Semi-Diameter Solves](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Clear_Semi_Diameter_or_Semi_Diameter_Solves.html) 与 [Summary of Solves](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Summary_of_Solves.html)。

三类拾取均保存到 `.staropt`，支持撤销、重做、表面插入/删除重编号和多配置快照。拾取链先完整求值再写入目标，循环、无效引用和不可表示的结果会使当前工作区事务整体回滚。优化器不会把拾取目标作为独立变量，源变量变化后会先更新全部拾取结果再计算评价函数。

ZMX 的净口径 `DIAM` 代码 `0/1/2` 分别导入为自动、用户固定和拾取；净口径拾取的源面与比例可再次导出。当前 ZMX 导出器没有足够的原始求解字段来无损写回厚度拾取，因此厚度拾取的完整持久化以 `.staropt` 为准。

## 验证记录

2026-09-07：默认解决方案 Debug 构建为 `0` 警告、`0` 错误；求解界面、计算、撤销保存、快照迁移、ZMX 导入、表面插入删除、多配置、优化及相邻回归组合测试 `207/207` 通过。Release 解决方案也完成过 `0` 警告、`0` 错误构建；本次没有运行全量测试、安装包或 OpticStudio 实机对标。
