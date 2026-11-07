# 三类重点镜头未通过项

P/C/D/I/E 分别表示通过、接近但未通过、数值差异、不可比、计算或求值错误。此表逐项列出 C/D/I/E。

| 请求 | 镜头 | 配置 | 范围 | 分析 | 结论 | 原因或最大误差 |
| ---: | --- | ---: | --- | --- | --- | --- |
| 489 | GRINs/Grid GRIN.ZMX | 1 | primary | First Order | Difference | FNumber 62566 Dimensionless，NRMSE 0.88065，EffectiveFocalLength 22274 Millimeter，NRMSE 0.88065 |
| 490 | GRINs/Grid GRIN.ZMX | 1 | primary | Single Ray Trace | Difference | m 0.0023505 Dimensionless，NRMSE 0.0086662，incidence 0.1394 Degree，NRMSE 0.0071764 |
| 493 | GRINs/Grid GRIN.ZMX | 1 | primary | Optical Path Difference | Difference | tangential 163.39 Wave，NRMSE 0.28468，sagittal 135.06 Wave，NRMSE 0.27762 |
| 494 | GRINs/Grid GRIN.ZMX | 1 | primary | Wavefront | Difference | grid 171.04 Wave，NRMSE 0.31588 |
| 495 | GRINs/Grid GRIN.ZMX | 1 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 496 | GRINs/Grid GRIN.ZMX | 1 | primary | Huygens PSF | Difference | grid 0.0093248 Dimensionless，NRMSE 0.20751 |
| 497 | GRINs/Grid GRIN.ZMX | 1 | primary | MTF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 498 | GRINs/Grid GRIN.ZMX | 1 | primary | Huygens MTF | Close | sagittal 0.050715 Dimensionless，NRMSE 0.025998，tangential 0.019634 Dimensionless，NRMSE 0.014143 |
| 499 | GRINs/Grid GRIN.ZMX | 1 | primary | FFT PSF Cross Section | Incomparable | {"type":"CaptureFailure","errorCode":"NativeInvalidResult","error":"CaptureFailure: OpticStudio returned a null data-series buffer; no numerical reference is available.\r\n   在 Host.\u003c\u003ec__DisplayClass21.\u003cCapture\u003eb__1e(Int32 i)\r\n   在 System.Linq.Enumerable.WhereSelectEnumerableIterator 2.MoveNext()\r\n   在 System.Linq.Buffer 1..ctor(IEnumerable 1 source)\r\n   在 System.Linq.Enumerable.ToArray[TSource](IEnumerable 1 source)\r\n   在 Host.Capture(IAR_ r)\r\n   在 Host.Run(String api)\r\n   在 Host.Main(String[] args)"} |
| 500 | GRINs/Grid GRIN.ZMX | 1 | primary | Huygens PSF Cross Section | Difference | curve:0 0.0054004 Dimensionless，NRMSE 0.69758 |
| 501 | GRINs/Grid GRIN.ZMX | 1 | primary | Encircled Energy | Difference | curve:0 0.0012209 Dimensionless，NRMSE 0.077318 |
| 502 | GRINs/Grid GRIN.ZMX | 1 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 503 | GRINs/Grid GRIN.ZMX | 1 | primary | Huygens Through Focus MTF | Difference | curve:1 0.12703 Dimensionless，NRMSE 0.15835，curve:0 0.11432 Dimensionless，NRMSE 0.066286 |
| 505 | GRINs/Grid GRIN.ZMX | 1 | primary | RMS vs Field | Close | curve:0 0.72787 Micrometer，NRMSE 0.0019188 |
| 506 | GRINs/Grid GRIN.ZMX | 1 | primary | Field Curvature and Distortion | Difference | tangential 0.027877 Millimeter，NRMSE 0.010788，sagittal 0.028791 Millimeter，NRMSE 0.010141 |
| 507 | GRINs/Grid GRIN.ZMX | 1 | edge | Single Ray Trace | Difference | m 0.0023505 Dimensionless，NRMSE 0.0086662，incidence 0.1394 Degree，NRMSE 0.0071764 |
| 510 | GRINs/Grid GRIN.ZMX | 1 | edge | Optical Path Difference | Difference | tangential 179.73 Wave，NRMSE 0.28468，sagittal 148.57 Wave，NRMSE 0.27762 |
| 511 | GRINs/Grid GRIN.ZMX | 1 | edge | Single Ray Trace | Difference | m 0.0023505 Dimensionless，NRMSE 0.0086662，incidence 0.1394 Degree，NRMSE 0.0071764 |
| 514 | GRINs/Grid GRIN.ZMX | 1 | edge | Optical Path Difference | Difference | tangential 163.39 Wave，NRMSE 0.28468，sagittal 135.06 Wave，NRMSE 0.27762 |
| 515 | GRINs/Grid GRIN.ZMX | 1 | edge | Single Ray Trace | Difference | m 0.0023505 Dimensionless，NRMSE 0.0086662，incidence 0.1394 Degree，NRMSE 0.0071764 |
| 518 | GRINs/Grid GRIN.ZMX | 1 | edge | Optical Path Difference | Difference | tangential 149.77 Wave，NRMSE 0.28468，sagittal 123.8 Wave，NRMSE 0.27762 |
| 1137 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 1138 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Huygens PSF | Incomparable | System.IO.InvalidDataException: No valid grid overlap |
| 1139 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | MTF | Incomparable | System.IO.InvalidDataException: Native focal FFT MTF has no image-plane frequency scale in the verified collimated contact-pupil prescription. |
| 1140 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Huygens MTF | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1142 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Huygens PSF Cross Section | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1145 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Huygens Through Focus MTF | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1146 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Relative Illumination | Incomparable | {"type":"CaptureFailure","errorCode":"NativeInvalidResult","error":"CaptureFailure: OpticStudio returned a null data-series buffer; no numerical reference is available.\r\n   在 Host.\u003c\u003ec__DisplayClass21.\u003cCapture\u003eb__1e(Int32 i)\r\n   在 System.Linq.Enumerable.WhereSelectEnumerableIterator 2.MoveNext()\r\n   在 System.Linq.Buffer 1..ctor(IEnumerable 1 source)\r\n   在 System.Linq.Enumerable.ToArray[TSource](IEnumerable 1 source)\r\n   在 Host.Capture(IAR_ r)\r\n   在 Host.Run(String api)\r\n   在 Host.Main(String[] args)"} |
| 1148 | Miscellaneous/Grid Sag Example.zmx | 1 | primary | Field Curvature and Distortion | Incomparable | System.IO.InvalidDataException: Native field curvature and distortion have no finite focal plane or nonzero field domain in the verified collimated contact-pupil prescription. |
| 1159 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 1160 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Huygens PSF | Incomparable | System.IO.InvalidDataException: No valid grid overlap |
| 1161 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | MTF | Incomparable | System.IO.InvalidDataException: Native focal FFT MTF has no image-plane frequency scale in the verified collimated contact-pupil prescription. |
| 1162 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Huygens MTF | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1164 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Huygens PSF Cross Section | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1167 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Huygens Through Focus MTF | Incomparable | System.IO.InvalidDataException: Clipped fans require explicit aperture checking and complete finite coordinate lattices |
| 1168 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Relative Illumination | Incomparable | {"type":"CaptureFailure","errorCode":"NativeInvalidResult","error":"CaptureFailure: OpticStudio returned a null data-series buffer; no numerical reference is available.\r\n   在 Host.\u003c\u003ec__DisplayClass21.\u003cCapture\u003eb__1e(Int32 i)\r\n   在 System.Linq.Enumerable.WhereSelectEnumerableIterator 2.MoveNext()\r\n   在 System.Linq.Buffer 1..ctor(IEnumerable 1 source)\r\n   在 System.Linq.Enumerable.ToArray[TSource](IEnumerable 1 source)\r\n   在 Host.Capture(IAR_ r)\r\n   在 Host.Run(String api)\r\n   在 Host.Main(String[] args)"} |
| 1170 | Miscellaneous/Grid Sag Example.zmx | 2 | primary | Field Curvature and Distortion | Incomparable | System.IO.InvalidDataException: Native field curvature and distortion have no finite focal plane or nonzero field domain in the verified collimated contact-pupil prescription. |
| 1181 | Miscellaneous/Grid sag surface.zmx | 1 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 2762 | Zoom systems/Mirror zoom.zmx | 1 | primary | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.7000000000000001 |
| 2763 | Zoom systems/Mirror zoom.zmx | 1 | primary | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.7000000000000001 |
| 2765 | Zoom systems/Mirror zoom.zmx | 1 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 2766 | Zoom systems/Mirror zoom.zmx | 1 | primary | Huygens PSF | Difference | grid 0.0050325 Dimensionless，NRMSE 0.1678 |
| 2769 | Zoom systems/Mirror zoom.zmx | 1 | primary | FFT PSF Cross Section | Difference | curve:0 0.0052271 Dimensionless，NRMSE 0.024909 |
| 2770 | Zoom systems/Mirror zoom.zmx | 1 | primary | Huygens PSF Cross Section | Difference | curve:0 0.0050323 Dimensionless，NRMSE 0.25862 |
| 2772 | Zoom systems/Mirror zoom.zmx | 1 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2773 | Zoom systems/Mirror zoom.zmx | 1 | primary | Huygens Through Focus MTF | Difference | curve:1 0.025422 Dimensionless，NRMSE 0.039292，curve:0 0.025422 Dimensionless，NRMSE 0.039292 |
| 2779 | Zoom systems/Mirror zoom.zmx | 1 | edge | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.7000000000000001 |
| 2780 | Zoom systems/Mirror zoom.zmx | 1 | edge | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.7000000000000001 |
| 2784 | Zoom systems/Mirror zoom.zmx | 2 | primary | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.6000000000000001 |
| 2785 | Zoom systems/Mirror zoom.zmx | 2 | primary | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.6000000000000001 |
| 2787 | Zoom systems/Mirror zoom.zmx | 2 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 2794 | Zoom systems/Mirror zoom.zmx | 2 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2801 | Zoom systems/Mirror zoom.zmx | 2 | edge | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.65 |
| 2802 | Zoom systems/Mirror zoom.zmx | 2 | edge | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.65 |
| 2806 | Zoom systems/Mirror zoom.zmx | 3 | primary | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.55 |
| 2807 | Zoom systems/Mirror zoom.zmx | 3 | primary | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.55 |
| 2809 | Zoom systems/Mirror zoom.zmx | 3 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"计算终止; 无效的结果!"}] |
| 2813 | Zoom systems/Mirror zoom.zmx | 3 | primary | FFT PSF Cross Section | Error | PSF: 光瞳相位存在混叠风险，相邻实际受光节点最大相位差 0.549012 波，超过本程序局部相位检查的 0.5 波界限。当前 FFT 数据不可用；需要独立加密验证，或使用适用的 Huygens 分析。此检查不代表 Zemax 的内部报错判据，也不构成收敛认证。 |
| 2816 | Zoom systems/Mirror zoom.zmx | 3 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2823 | Zoom systems/Mirror zoom.zmx | 3 | edge | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.6000000000000001 |
| 2824 | Zoom systems/Mirror zoom.zmx | 3 | edge | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.6000000000000001 |
| 2828 | Zoom systems/Mirror zoom.zmx | 4 | primary | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.45 |
| 2829 | Zoom systems/Mirror zoom.zmx | 4 | primary | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.45 |
| 2831 | Zoom systems/Mirror zoom.zmx | 4 | primary | PSF | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2835 | Zoom systems/Mirror zoom.zmx | 4 | primary | FFT PSF Cross Section | Error | PSF: 光瞳相位存在混叠风险，相邻实际受光节点最大相位差 0.658442 波，超过本程序局部相位检查的 0.5 波界限。当前 FFT 数据不可用；需要独立加密验证，或使用适用的 Huygens 分析。此检查不代表 Zemax 的内部报错判据，也不构成收敛认证。 |
| 2838 | Zoom systems/Mirror zoom.zmx | 4 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2845 | Zoom systems/Mirror zoom.zmx | 4 | edge | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.5 |
| 2846 | Zoom systems/Mirror zoom.zmx | 4 | edge | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.5 |
| 2850 | Zoom systems/Mirror zoom.zmx | 5 | primary | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.30000000000000004 |
| 2851 | Zoom systems/Mirror zoom.zmx | 5 | primary | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.30000000000000004 |
| 2853 | Zoom systems/Mirror zoom.zmx | 5 | primary | PSF | Error | PSF: 光瞳相位存在混叠风险，相邻实际受光节点最大相位差 0.565445 波，超过本程序局部相位检查的 0.5 波界限。当前 FFT 数据不可用；需要独立加密验证，或使用适用的 Huygens 分析。此检查不代表 Zemax 的内部报错判据，也不构成收敛认证。 |
| 2857 | Zoom systems/Mirror zoom.zmx | 5 | primary | FFT PSF Cross Section | Close | curve:0 0.0022475 Dimensionless，NRMSE 0.0031115 |
| 2858 | Zoom systems/Mirror zoom.zmx | 5 | primary | Huygens PSF Cross Section | Difference | curve:0 0.0023481 Dimensionless，NRMSE 0.011266 |
| 2860 | Zoom systems/Mirror zoom.zmx | 5 | primary | Diffraction Encircled Energy | Incomparable | System.IO.InvalidDataException: Native report emitted diagnostics; numerical reference requires review: [{"ErrorCode":"Success","Text":"取样太低, 数据不准确!"}] |
| 2867 | Zoom systems/Mirror zoom.zmx | 5 | edge | Ray Fan | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.35000000000000003 |
| 2868 | Zoom systems/Mirror zoom.zmx | 5 | edge | Optical Path Difference | Incomparable | System.IO.InvalidDataException: Checked fan validity masks differ at physical coordinate 0.35000000000000003 |
