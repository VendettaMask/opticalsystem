# #555、#2499 固定档 FFT PSF 修复

两条原请求都使用固定 64×64 瞳采样和 128×128、间距 0.25 µm 的像面网格。旧 Workbench 在按像元间距拉伸瞳孔采样时，至少一轴只剩不足两个瞳内点，两个请求均以错误退出。修复后，入口沿用原采样档追迹完整瞳孔，直接在原物理像面频点做分离式傅里叶求值。该分支沿用通光掩码和入射振幅，直接使用追迹所得相位，并检查相邻受光点的相位步长。 [Ansys FFT PSF 的瞳孔拉伸定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html)

| 请求 | 原固定档复跑 | 覆盖像点 | NRMSE | 最大绝对差 |
| --- | --- | ---: | ---: | ---: |
| #555 Singlet eyepiece | 通过 | 16,384 | 0.00684962 | 0.000759965 |
| #2499 Unobscured Gregorian | 通过 | 16,384 | 0.00129249 | 0.00158547 |

两条的 X、Y 坐标均为 −15.75 至 +16.00 µm，保留原处方、配置、波长、视场、瞄准和相对辐照度定义。两条都以完整覆盖和原 0.01 NRMSE 判据通过，逐点绝对与相对替代条件未全数通过。独立参考分别从原生出瞳波前重建，以及从原生光线、波前和锥面反射重建。旧冻结原生空网格仍单列。 [Singlet 参考](../singlet-physical-fft/README.md) · [Gregorian 参考](../gregorian-fft-psf-reference/README.md) · [本轮比较值](verification.json)

#555 的固定 32 档对照测得最大相邻相位差 0.583606 波，继续返回相位欠采样。对 427 条原队列中的全部 64 个可执行固定 PSF 请求回归，除两条修复外，另外 62 条数值载荷逐字节一致。同一 Core 的 67 项定向测试通过。 [正式请求、逐点值、参考阵列和回归结果](evidence.zip)
