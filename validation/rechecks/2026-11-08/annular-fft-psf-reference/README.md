# 环形光瞳 FFT PSF 的独立参考

Image Simulation Example 4 #653 和 Hubble #2335 使用各自原始处方与请求，配置 1、轴上视场 1、0.5 µm 波长、原像面、标量线性相对辐照度。保持 128×128 的物理像面网格与 0.25 µm 像元间距，逐点验证 16,384 个坐标，范围为两个轴各 −15.75 至 +16.00 µm。比较覆盖率均为 100%。原始 64 档原生 FFT 空网格仍记为 Incomparable。 [原始设置和结论](../../2026-11-07/incomparable-meter/meter-comparison.json)

各原处方在主镜处限定均匀环形光瞳。入瞳外半径 1.2 m，内半径 0.200000003 m。独立求交和两次反射沿径向重建出射方向与参考球 OPD，再积分带相位的零阶 Hankel 核。各处方原生档案含 83 条轴上测试光线，其中 68 条透过，所有通光掩码与环形边界相符，透过光线的强度均为 1。出射方向正弦最大差为 7.22×10⁻¹⁶，OPD 最大差为 3.31×10⁻⁸ 波。归一化采用同一未像差环形光瞳的轴上峰值。以实际非线性方向映射替代 FFT 的线性方向余弦标尺，全部像元 NRMS 变化为 2.66×10⁻⁸。将径向积分由 128 段增至 256 段，像面 NRMS 变化不超过 1.46×10⁻¹⁰。 [原生光线存档](../../2026-11-07/incomparable-meter/meter-explicit-fan-native.zip) [光线与处方哈希](../../2026-11-07/incomparable-meter/meter-explicit-fan-results.json) [独立计算](AnnulusReference.mjs)

| 案例 | 1024 档对参考 NRMS | 2048 档对参考 NRMS | 1024→2048 NRMS | 2048 档最大绝对差 | 独立验证 |
| --- | ---: | ---: | ---: | ---: | --- |
| #653 Example 4 | 0.000170830 | 0.000190067 | 0.000282459 | 0.000434486 | Pass |
| #2335 Hubble | 0.000170830 | 0.000190067 | 0.000282459 | 0.000434486 | Pass |

沿用原比较门槛。Pass 要求 NRMS ≤0.01，或每点绝对差 ≤0.00001 + 0.001×参考绝对值。最低覆盖率 0.95，Close 的 NRMS 门槛为 0.03。Workbench 正式入口在这两档均返回完整原网格。工作 F 数 23.99994207561692，与原生边缘方向给出的 23.999942075602817 相符。2048 档读取同一显示窗的裁剪变换。 [逐档指标、请求身份和二进制身份](annulus-verification.json) [原始 Workbench 数据及请求](core-numeric-evidence.zip)

复算时，从 Workbench 存档解出两个 `original-*-request.json` 文件，按 `manifest.json` 将各档内容还原为 `<档位>/data.json`、`request.json` 和 `provenance.json`，校验各内容文件名中的 SHA-256。另从既有原生存档解出 `650/point-reference.json` 与 `2332/point-reference.json`，在同一临时目录分别命名为 `native-650.json` 与 `native-2332.json`。运行 `node AnnulusReference.mjs <仓库目录> <临时目录>` 重新生成逐档指标。两个处方的 512、1024 与 2048 档 Workbench 数值文件各自具有相同 SHA-256，存档按内容只保留一份。

## 自适应正式入口

两份原请求只启用 Adaptive，保持采样起点 64、原像面网格与默认相对 RMS 精度 0.001。64 档记录无相邻受光点，保留完整像面坐标，并从数值比较中排除该档。默认上限 1024 时，由密瞳 256→512 建立收敛。上限 2048 时，常规 1024→2048 同样通过。两条路径的档间 NRMS 均为 0.000282459，对独立参考的 NRMS 均为 0.000190067。两例均返回原 16,384 像元，请求档误差字段为空。Hubble 的固定、验证原采样，以及仅预算到 128 的自适应路径均报告不可用。 [自适应逐档指标](annulus-adaptive-verification.json) [自适应及固定模式对照](adaptive-numeric-evidence.zip)

50 项定向测试通过，覆盖含 1 个相位通过档的预算、可收敛预算、验证原采样、现有采样与投影约定。测试和正式入口使用同一 Core 程序集 SHA-256 `c9fbc3db79ee5d557d50f9b4f0ca6ce92cad33bf2cb9dd2c7e29da65306481d0`。复算自适应结果时，按自适应存档的 `manifest.json` 恢复对应档位目录，再给前述复算命令附加 `--adaptive`。两份报告均已从归档数据重建并匹配 SHA-256。 [回归测试回执](annulus-retry-focused.trx)
