# 计算公式附录

本文件由指南公式定义生成，适用条件、变量与单位见[计算指南](COMPUTATION_GUIDE.md)及软件内对应章节。公式为数学排版；软件使用预先生成的矢量轮廓，PDF 复用同一资源。

## 材料色散与单位换算

$$
\begin{aligned}n^2(\lambda)&=1+\sum_j\frac{B_j\lambda^2}{\lambda^2-C_j}\\1\,\mathrm{mm}&=10^3\,\mu\mathrm{m}=10^6\,\mathrm{nm}\end{aligned}
$$

## 光线与光程

$$
\begin{aligned}\mathbf p(t)&=\mathbf o+t\mathbf d\\\mathrm{OPL}&=\sum_j n_j(\lambda)L_j\end{aligned}
$$

## 折射与反射

$$
\begin{aligned}n_1\sin\theta_1&=n_2\sin\theta_2\\\mathbf d_r&=\mathbf d-2(\mathbf d\cdot\mathbf N)\mathbf N,\quad |\mathbf d|=|\mathbf N|=1\end{aligned}
$$

## 高度与斜率的近轴矩阵

$$
\begin{gathered}\begin{pmatrix}y_2\\u_2\end{pmatrix}=\begin{pmatrix}A&B\\C&D\end{pmatrix}\begin{pmatrix}y_1\\u_1\end{pmatrix},\quad u=\frac{dy}{dz}\\T(d)=\begin{pmatrix}1&d\\0&1\end{pmatrix}\\R=\begin{pmatrix}1&0\\-\dfrac{n_2-n_1}{n_2R_s}&\dfrac{n_1}{n_2}\end{pmatrix}\\\mathrm{EFL}=-\frac1C\quad\text{(air to air)}\end{gathered}
$$

## 质心与点列 RMS

$$
\begin{aligned}\bar x&=\frac{\sum_iw_ix_i}{\sum_iw_i},\quad\bar y=\frac{\sum_iw_iy_i}{\sum_iw_i}\\\mathrm{RMS}&=\sqrt{\frac{\sum_iw_i[(x_i-\bar x)^2+(y_i-\bar y)^2]}{\sum_iw_i}}\end{aligned}
$$

## 本项目 FFT 的 OPD 相位约定

$$
\begin{aligned}\varphi&=-\frac{2\pi\,\mathrm{OPD}}{\lambda}\\P(\xi,\eta)&=A(\xi,\eta)\exp[i\varphi(\xi,\eta)]\end{aligned}
$$

## FFT、Huygens 与多色 PSF

$$
\begin{aligned}I_{\mathrm{FFT}}&\propto|\mathcal F\{P\}|^2\\I_{\mathrm{Huygens}}&\propto\left|\sum_j a_j e^{ik\,\mathrm{OPL}_j}\right|^2\\I_{\mathrm{poly}}(x,y)&=\frac{\sum_\lambda w_\lambda I_\lambda(x,y)}{\sum_\lambda w_\lambda}\end{aligned}
$$

## OTF 与 MTF

$$
\begin{aligned}\mathrm{OTF}(f_x,f_y)&=\mathcal F\{I(x,y)\}\\\mathrm{MTF}(f_x,f_y)&=\frac{|\mathrm{OTF}(f_x,f_y)|}{|\mathrm{OTF}(0,0)|}\end{aligned}
$$

## 衍射圈入能量

$$
\mathrm{EE}(r)=\frac{\displaystyle\iint_{(x-x_c)^2+(y-y_c)^2\le r^2}I(x,y)\,dx\,dy}{\displaystyle\iint I(x,y)\,dx\,dy}
$$

## 反射、透射与吸收功率

$$
\begin{aligned}R&=|r|^2\\R+T+A&=1\end{aligned}
$$

## 优化目标残差

$$
\begin{aligned}r_i&=\sqrt{\frac{w_i}{\sum_j|w_j|}}(v_i-t_i),\quad w_i>0\\\mathrm{merit}&=\sum_{i:w_i>0}r_i^2\end{aligned}
$$

## Monte Carlo 估计良率

$$
\widehat Y=\frac{N_{\mathrm{pass}}}{N_{\mathrm{trials}}}
$$

## 非序列功率账目

$$
P_{\mathrm{source}}=P_{\mathrm{detector}}+P_{\mathrm{absorbed}}+P_{\mathrm{escaped}}+P_{\mathrm{truncated}}
$$

## 探测器像素功率与辐照度

$$
\begin{aligned}P_{\mathrm{pixel}}&=\sum_iP_i\\E_{\mathrm{pixel}}&=\frac{P_{\mathrm{pixel}}}{A_{\mathrm{pixel}}}\end{aligned}
$$

## 相机光电子数

$$
N_e=E_{\mathrm{ref}}\,L\,A\,t\,\frac{\lambda}{hc}\,\mathrm{QE}
$$

## 圆锥与偶次非球面矢高

$$
z(r)=\frac{cr^2}{1+\sqrt{1-(1+k)c^2r^2}}+\sum_{m=2}^{M}A_{2m}r^{2m},\quad c=\frac1R
$$

## GRIN 几何光学路径

$$
\frac{d}{ds}\left(n\frac{d\mathbf r}{ds}\right)=\nabla n
$$

## 光瞳加权积分

$$
\langle g\rangle\approx\frac{\sum_iw_i g_i}{\sum_iw_i},\quad\sum_i w_i\longrightarrow\int_{\mathcal P}dA
$$

## 相对畸变

$$
D=100\,\frac{h_{\mathrm{real}}-h_{\mathrm{reference}}}{h_{\mathrm{reference}}}\,\%,\quad h_{\mathrm{reference}}\ne0
$$

## Zernike 未加权 QR 拟合

$$
\begin{aligned}W(\rho,\theta)&\approx\sum_{j=1}^{K}a_j Z_j(\rho,\theta)\\\mathbf a&=\underset{\mathbf a}{\operatorname{argmin}}\,\|Z\mathbf a-\mathbf W\|_2^2\end{aligned}
$$

## 干涉条纹相位与强度

$$
\begin{aligned}q&=s\left(W_1-W_2+\frac{\mathrm{OPL}_2-\mathrm{OPL}_1}{\lambda}\right)+\frac{t_xx+t_yy}{2}\\I&=\frac{1+\cos(2\pi q)}2\end{aligned}
$$

## 焦面刀口与瞳再成像

$$
\begin{aligned}U_{\mathrm{out}}&=\mathcal F^{-1}\{M\,\mathcal F[P]\}\\I&=\sum_c|U_{\mathrm{out},c}|^2,\quad\tau=\frac{P_{\mathrm{out}}}{P_{\mathrm{in}}}\end{aligned}
$$

## 标量角谱传播

$$
U(z)=\mathcal F^{-1}\!\left[\mathcal F[U(0)]\exp\!\left(iz\sqrt{k^2-k_x^2-k_y^2}\right)\right]
$$

## 扫描最大值归一化的相对照度

$$
\mathrm{RI}(H)=\frac{Q(H)}{\displaystyle\max_{H'\in\mathcal H_{\mathrm{sampled}}}Q(H')}
$$

## 辐照度与辐射强度

$$
\begin{aligned}E_{ij}&=\frac{\Phi_{ij}}{\Delta A_{ij}}\quad[\mathrm{W/mm^2}]\\J_{ij}&=\frac{\Phi_{ij}}{\Delta\Omega_{ij}}\quad[\mathrm{W/sr}]\end{aligned}
$$

## 线扩散与边缘扩散

$$
\begin{aligned}\mathrm{LSF}(x)&=\int\mathrm{PSF}(x,y)\,dy\\\mathrm{ESF}(x)&=\frac{\displaystyle\int_{-\infty}^{x}\mathrm{LSF}(u)\,du}{\displaystyle\int_{-\infty}^{\infty}\mathrm{LSF}(u)\,du}\end{aligned}
$$

## 非相干与相干扩展像

$$
\begin{aligned}I_{\mathrm{incoherent}}(\mathbf x)&=\int I_o(\mathbf u)h_I(\mathbf x;\mathbf u)\,d\mathbf u\\U_{\mathrm{coherent}}(\mathbf x)&=\int U_o(\mathbf u)h_U(\mathbf x;\mathbf u)\,d\mathbf u\\I_{\mathrm{coherent}}(\mathbf x)&=|U_{\mathrm{coherent}}(\mathbf x)|^2\end{aligned}
$$

## 互强度及当前均匀相干模型

$$
\begin{aligned}I(\mathbf x)&=\iint J_o(\mathbf u_1,\mathbf u_2)h_U(\mathbf x;\mathbf u_1)h_U^*(\mathbf x;\mathbf u_2)\,d\mathbf u_1\,d\mathbf u_2\\J_{ij}&=(1-\gamma)I_i\delta_{ij}+\gamma a_i a_j^*,\quad 0\le\gamma\le1\end{aligned}
$$

## RMS 视场扫描节点

$$
H_i=\frac{i}{N}H_{\max},\quad i=0,1,\ldots,N
$$

## 阻尼线性化步进

$$
(J^{\mathsf T}J+\mu D)\,\Delta\mathbf x=-J^{\mathsf T}\mathbf r
$$

## 中心差分

$$
\frac{\partial r_i}{\partial x_j}\approx\frac{r_i(\mathbf x+h_j\mathbf e_j)-r_i(\mathbf x-h_j\mathbf e_j)}{2h_j}
$$

## 薄膜相位与稳定反射递推

$$
\begin{aligned}\delta_j&=\frac{2\pi}{\lambda}\widetilde n_jd_j\cos\theta_j\\r_j&=\frac{r_{j,j+1}+r_{j+1}e^{2i\delta_j}}{1+r_{j,j+1}r_{j+1}e^{2i\delta_j}}\end{aligned}
$$

## 三阶波像差基底示意

$$
W(\rho,\theta;H)=W_{040}\rho^4+W_{131}H\rho^3\cos\theta+W_{222}H^2\rho^2\cos^2\theta+W_{220}H^2\rho^2+W_{311}H^3\rho\cos\theta
$$

## 装调导数外推与共轭位置

$$
\begin{aligned}A&=\frac{\partial f}{\partial h},\quad B=\frac{\partial f}{\partial u}\\D_{\rm ext}&=\frac{4D(h/2)-D(h)}{3}\\z_{\rm image}&=z_{\rm launch}+\sigma\frac{B}{A}-z_{\rm datum}\end{aligned}
$$

## Adam 动量、偏差修正与更新

$$
\begin{aligned}m_t&=\beta_1m_{t-1}+(1-\beta_1)g_t\\v_t&=\beta_2v_{t-1}+(1-\beta_2)g_t^2\\\widehat m_t&=\frac{m_t}{1-\beta_1^t},\quad\widehat v_t=\frac{v_t}{1-\beta_2^t}\\x_{t+1}&=x_t-\alpha_t\frac{\widehat m_t}{\sqrt{\widehat v_t}+\epsilon}\end{aligned}
$$

## 几何与采样 OTF

$$
\begin{aligned}\mathrm{OTF}_{\rm geometric}(\mathbf f)&=\frac{\sum_jw_j\exp[-2\pi i\,\mathbf f\cdot\mathbf x_j]}{\sum_jw_j}\\Q(\Delta)&=\sum_{p\in\mathcal P\cap(\mathcal P+\Delta)}I_p e^{2\pi i[W(p)-W(p-\Delta)]}\\\mathrm{OTF}_{\rm sampled}(\Delta)&=\frac{Q(\Delta)}{Q(0)}\end{aligned}
$$

## 高度视场的主光线归一化

$$
\bar y_j^{\rm scaled}=\bar y_j\frac{h_{\rm selected}}{\bar y_{\rm image}},\quad\bar y_{\rm image}\ne0
$$

## Moore–Elliott 局部损失

$$
L(p)=\frac{1-\cos(2\pi\Delta W(p))}{2}
$$

## Jones 瞳矩阵

$$
\begin{pmatrix}E_x^{\rm out}\\E_y^{\rm out}\end{pmatrix}=\begin{pmatrix}J_{xx}&J_{xy}\\J_{yx}&J_{yy}\end{pmatrix}\begin{pmatrix}E_x^{\rm in}\\E_y^{\rm in}\end{pmatrix}
$$
