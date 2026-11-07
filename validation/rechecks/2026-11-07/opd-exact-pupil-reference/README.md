# 单色 OPD 的原瞳节点复验

复验覆盖原显式批量 OPD 未通过的 22 条请求。原模型、配置、视场、主波长、选波长、裁光、像面及 OPD 参考全部按原生读回核对。原图发布的 1,565 个物理通光节点使用冻结原值。44 次补采将每侧 20 条射线提高到 40 和 80 条。对其中 9 条另补采每侧 160、320 和 640 条，共 27 次。28 个原图省略的通光节点均在至少两档密采图中直接发布，坐标与原请求节点重合。冻结已报告节点在每档密采逐值一致，补点也在各自可用档逐值一致。 [逐请求](results.csv) [逐节点及读回](results.json)

22 条曲线的 1,593 个物理通光节点按冻结规则全部通过，最高 NRMSE 为 0.001779257，阈值为 0.003。28 个新取得的节点中有 26 个单独通过点误差门槛，最大误差 4.3534×10⁻⁵ 波。余下两个点超出逐点门槛，曲线仍满足原 NRMSE 规则。冻结矩阵、原生图表文件及原 `CurrentAndChief` 补充判定单独保留。 [原物理裁光与批量 OPD](../fan-physical-coverage/README.md)

## 两个仍超逐点门槛的结果

两项均为 Sample Spectrometer、配置 1、视场 2、弧矢坐标 −0.30。逐点门槛为 `1e−5 + 0.001 × |原生值|` 波。

| 请求 | 选波长 / 主波长 | 单色图表 / Core（波） | 绝对差 / 点门槛（波） | 光程差残差 | 逐面位置 / 方向最大差 | 几何 OPL 最大差 | 原生 OPDC 与批量（波） |
| --- | --- | --- | --- | --- | --- | --- | --- |
| #458 | 0.400 / 0.550 µm | −5.61726554 / −5.60632055 | 0.01094499 / 0.00562727 | 4.378 nm | 8.49×10⁻¹⁶ mm / 5.56×10⁻¹⁶ | 7.11×10⁻¹⁵ mm | 100.63727456 |
| #466 | 0.700 / 0.550 µm | −3.23667422 / −3.23037041 | 0.00630381 / 0.00324667 | 4.413 nm | 1.37×10⁻¹⁵ mm / 4.45×10⁻¹⁶ | 7.11×10⁻¹⁵ mm | 28.41819283 |

位置与方向由五面实际追迹逐面核对，几何 OPL 用相对本波长主光线的差值。相邻弧矢坐标 −0.29375、−0.28750、−0.28125 的有符号残差分别为 4.191、4.008、3.829 nm 和 4.224、4.040、3.861 nm。逐面一致性和连续残差把问题定位到衍射相位与波前参考处理，现有证据尚不能分离两项贡献。 [逐面追迹与密采邻域](spectrometer-controls.json)

## 批量 OPD 参考

原生手册规定单色 OPD 图表使用选定波长的参考球及主光线。原生 `OPDC` 在 22 条和一条主波长通过控制的通光节点上与 `CurrentAndChief` 逐值相同。将选波长设为主波长的独立控制中，Sample Spectrometer #458 与 Houghton #2316 每条射线的像面位置、方向和原始几何 OPL 完全相同，批量 OPD 却分别改变最多 236.70575 波和 0.08483586 波。#458 的 −0.30 点在此控制中为 −5.48074634 波，原单色图表值为 −5.61726554 波。Apochromat4 #1542 的换主波长控制伴随最大 1.51×10⁻⁵ mm 的几何位置改变，单列记录。实际验收采用原设置的单色图表读回。 [OPD 图表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html) [批量接口](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/IRayTraceNormUnpolData.html) [原生控制](opdc-controls.json)

## 重放

展开脚本从 [本轮原生与 Core 原始结果](opd-references.zip) 及 [冻结证据](../../../repairs/2026-10-21/sequential-corpus-full-evidence.zip) 复原 1,088 个逻辑文件。相同内容仅保存一份，全部按 [清单](manifest.json) 核对 SHA-256。`verify` 使用与 [原显式复验](../fan-physical-coverage/summary.json) 对应的已编译 `OpticalSystemDesign.Core.dll`、`OpticalSystemDesign.ZemaxComparison.dll` 和 `comparison-settings.json`。在证据目录运行以下命令，其中 `$comparisonBin` 指向这些文件所在目录，`$raw` 指向空工作目录，`$baseline` 指向 `fan-physical-coverage` 证据目录。

```powershell
./Expand-Evidence.ps1 -OutputDirectory $raw
dotnet build ./verify/verify.csproj -c Release "-p:ComparisonBin=$comparisonBin" -o "$raw/verify"
dotnet "$raw/verify/verify.dll" $raw $baseline
node ./inspect.cjs $raw
```

离线验证逐条核对原设置、模型、实测轴、裁光和原阈值。`prepare-dense.ps1` 可从原生请求生成补采清单，准备重新原生采集时使用 OpticStudio 2026 R1 和归档请求所指的原模型。
