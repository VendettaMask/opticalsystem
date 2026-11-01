# 商业 BSDF / XYZ 校准检查

本目录保留 2026-10-31 的后台原生采集、失败调查、共享 Core 修正和 NSC→RAW/ISP 定向回归。结论与适用边界见[实施说明](../../../../docs/COMMERCIAL_BSDF_CALIBRATION_2026-10-31.md)。White Specular 完整方向对标与真实 Zemax XYZ 校准均未通过。

最终产品定向回归 **245/245**，新增16项，零失败/跳过；默认完整Release App与测试驱动零警告/错误。一次光子换算最大差9.10e−13 e−，当前源/校准删除后RAW/ISP精确重放，并保留旧grid4 Speos重放。内部回归通过不改变下列商业/实测失败。

## 执行与证据

- `native-signed-components.json`：有效 ANSYS 签名的原生组件版本/SHA；相同版本组件的数值 DLL 并非字节相同。
- `native-isolated-active-v11.json` / `native-core-comparison.json`：原控制文件的 15 个系数与当前 Core 比较，最大绝对误差 7.22e−16；开启 White Specular 的合并方向图 NRMSE 0.1876946，保留失败。
- `synthetic-v11-rpc-capture.json`、各 `*.anisotropicbsdf`、`*-enhancement.pb`、`*.xmp`：本机回环 RPC、原生保存文件及关闭/开启/重设/自动生成锥的实际输出。
- `native-no-white-reference.json` / `native-no-white-comparison.json`：603 个原生方向点，400 nm、三个已测入射角、White Specular 关闭；最大 BSDF 绝对误差 3.40e−9 sr⁻¹，通过该范围显式数值门槛。
- `synthetic-v11-loaded.txt`：独立原生 COM ExportTXT。解码后的完整合并 XMP 与其最大差 4.99e−11。
- `native-map-convergence.json`：64/128/256/512 分辨率的分层半球积分；开启时的原生系数/图积分差距不能由本次加密解释。未将其指定为厂商缺陷或原生追迹行为。
- `native-valid-control*`、`trace-valid-*`：由文本经原生生成的控制、65536光线Interactive和每组1048595光线带Weight Direct MC；保存实际请求、主要原生几何回读、引擎身份、原生XMP/HTML和工作量。开启White的探测器积分0.269664549 W对Core0.251318301 W，差0.018346248 W；关闭时原生逸出0.199999 W、有限探测器约0.199957 W，对Core全半球0.2 W。明确使用Core手工锥覆盖；原生White开/增强标志0组合未自动兼容。不是完整商业通过。
- `mystique*.astm*`、`cornell-paired-fit.json`：Cornell 配对实测输入和 959/480 方向训练/留出。31 波段均失败；最差 NRMSE 0.6083908，最大绝对误差 0.1043304 sr⁻¹，观察者/照明条件未核实，正式导出拒绝。原件 SHA 见 JSON。此下载未作为产品材质或测试夹具收录。
- `derived-xyz-candidates/`：CIE官方原表及许可/校验和，Cornell Krylon Blue原始实测光谱、数学推导的受限光源XYZ及预定550 nm校准；不是独立XYZ实测。留出NRMSE0.129854、最大绝对差0.190910 sr⁻¹，仍失败，没有合格校准导出或材质全链路认证。Cayman原始下载未用于本次拟合。
- `theta-first.trx`、`theta-repair.trx`：修复过程中失败记录；后一次含测试源码更新前的旧测试 DLL，不作为最终判定。
- `before-theta/`：本阶段较早的 239 项回归和首次校准失败，冻结保留；随后新增了协议5，不能把旧运行当成最终代码验证。
- `theta5-chain/`：协议5 的完整链路、旧 XYZ 路径与离屏控件证据；文件删除后冻结重算，不重新启动 NSC。

最终运行使用 `run-theta5-focused.ps1`，运行前后身份在 `before-theta5-final-run.json` / `after-theta5-final-run.json`，结果为 `theta5-final-focused.trx`。83项不同范围源码/输入中81项不变，`SpeosIsolatedCapture` 的Program/RpcCapture在测试期间扩展了后台探针；12项二进制中11项不变，采集工具DLL变化。Core/Application/App/测试驱动未漂移，测试不执行此外部采集工具。后续诊断补证及实际工具执行身份见 `post-investigation-identity.json`；没有因诊断扩展把旧239项或中间245项当成新产品代码的通过。没有全仓测试、安装包发布或原生桌面交互验收。

后续共享构建改变了默认 `src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll`；测试复制App仍保留245项使用的原哈希，Core/Application/测试驱动也未变。补充身份明确列出当前默认App与测试副本的差异，以及之后后台采集/分析工具的变化；当前默认App不代表本次冻结执行。

## 重放

默认 Release App 构建后，以 `-p:BuildProjectReferences=false` 构建测试驱动，再执行 `run-theta5-focused.ps1`。脚本只写本目录与其 `theta5-chain`，不更新之前阶段的证据。拟合工具：`dotnet run --project tools/diagnostics/CommercialBsdf20261031 -c Release -- <本目录>`；原生比较用 `--native <本目录>`，依赖已经捕获的数值文件，不启动商业引擎。

原生重采工具为 `tools/diagnostics/SpeosIsolatedCapture20261031`：`rpc <证据目录> <输入文件>` 仅启动自己的本机回环后台服务；`broker <结果JSON> <输入文件>` 使用不切换显示的独立 desktop；`broker-xmp` 仅在没有现有查看器时导出本次自有 XMP。已有查看器不连接/关闭。RPC 运行时由本机官方介质的 speosrpc/WINX64.7z 提取，当前位于被忽略的 `native-runtime/`；不分发其 EXE/DLL/安装负载或任何许可组件。产品 Core 没有这些依赖。

`trace <证据目录> <输入文件> <子目录> direct` 运行带Weight Direct MC，省略direct则为无权重Interactive控制；只有本次私有RPC数据库、工作区合成几何与材料被使用。结果原生写入本次Temp job后复制到证据目录，保留原路径与SHA，未改用户工程。`CommercialBsdf20261031 --direct <控制目录> <输入文件> white` 对开启结果作独立积分，关闭时省略white；这两项Core比较都明确指定了相同手工锥。`--derived-green <derived-xyz-candidates目录>` 重放已预定550nm的推导XYZ校准，失败保持失败。

研究期间的网络失败、404 HTML、未使用的数据页面和早期 COM 注册失败均为调查记录，不是合格参考，也没有进入光学计算。没有调整原验收门槛、改写实测值或用合成校准冒充实测成功。
