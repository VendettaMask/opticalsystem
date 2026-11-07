# 固定像面窗口 FFT PSF 补采

本轮覆盖证据台账中仍缺同合同高档原生参考的 34 条 FFT PSF。OpticStudio 2026 R1 按原处方、配置、视场、波长和像面，分别以 256 和 512 的瞳采样复验。68 次调用均完成。原生请求和验收采用的 API 数据网格均为 128×128、0.25 µm 间距。回读中有 23 次附“采样太低”或“结果无效”，这些网格作为诊断保存。 [逐项判定](results.csv) [度量与回读](results.json) [证据清单](manifest.json)

8 条达到两档原生收敛、独立 Core 相位采样收敛和原容差跨软件对照。使用原矩阵每项的容差，跨软件比较覆盖各自完整的 16,384 个像面像素。 [原请求台账](../incomparable-coverage/by-request.csv)

| 请求 | Core 返回瞳采样 | 原生档间 NRMSE | 跨软件 NRMSE |
| --- | ---: | ---: | ---: |
| #309 | 1024 | 0.009024 | 0.004946 |
| #787 | 2048 | 0.001312 | 0.000237 |
| #1181 | 2048 | 0.001979 | 0.000989 |
| #2301 | 1024 | 0.003139 | 0.000618 |
| #2357 | 1024 | 0.008408 | 0.002724 |
| #2401 | 512 | 0.000351 | 4.15×10⁻¹¹ |
| #2431 | 1024 | 0.000971 | 0.000342 |
| #2721 | 512 | 0.000494 | 1.75×10⁻¹² |

#2357 原 64 瞳节点的自适应入口无法验证邻接。将独立控制从 256 开始后，512 和 1024 的密瞳物理相位均已解析，整窗相对 RMS 差为 0.000354。两次 Core 记录均在证据中单列。

| 其余请求的状态 | 数量 |
| --- | ---: |
| 两档原生均有无效诊断 | 10 |
| 单档有有效原生网格 | 3 |
| 两档原生比较 Close | 6 |
| 两档原生比较 Difference | 5 |
| 两档原生通过，Core 未建立收敛 | 2 |

#967 和 #2157 的 Core 常规分支在 2048 预算内未达到相位采样收敛，密瞳替代分支又触发全瞳覆盖保护。两条的原生档间通过，Core 仍保留未收敛状态。#1291 的 Core 已有独立收敛，跨软件结果为 Close，原生两档为 Difference。单侧收敛和只有一份原生数值的情况单独记录。

用 `Expand-Evidence.ps1` 核对和展开 [固定窗口原始结果](fixed-window-references.zip) 与 [清单](manifest.json) 指向的既有原始数据，再用 `verify/verify.csproj` 重放。`$comparisonBin` 为本次 Core、比较程序集及比较设置所在目录，`$raw` 为证据工作目录。

```powershell
./Expand-Evidence.ps1 -OutputDirectory $raw
dotnet build ./verify/verify.csproj -c Release "-p:ComparisonBin=$comparisonBin" -o "$raw/verify"
dotnet "$raw/verify/verify.dll" $raw
```

重放产出 `psf-results.json`。原生文本头及全文哈希随归档保存，正式数值来自固定的 API 像面窗口。 [OpticStudio FFT PSF 说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)
