# Optical System Design 私有 OTA 密钥仓库

本仓库只管理签名密钥的加密备份与恢复，不发布软件，也不替代公开主仓库。**保持 Private，不增加不必要的协作者。** 能修改工作流的人也能使用工作流中的 Secret。

## 首次启用：只需一个恢复密码

1. 打开本仓库的 [Actions Secrets 设置](https://github.com/VendettaMask/opticalsystem-ota-vault/settings/secrets/actions/new)。名称填写 `OPTICAL_OTA_RECOVERY_PASSWORD`，值填写你自己选的唯一强密码：至少 20 字符，建议六个随机单词或密码管理器生成的长密码。不要使用 GitHub 登录密码，不要发到聊天、Issue、代码、普通 Variables 或工作流输入中。
2. 打开 [云端备份工作流](https://github.com/VendettaMask/opticalsystem-ota-vault/actions/workflows/vault.yml)，点击 **Run workflow**，分支 `main`、操作 `Backup`。
3. 等 `self-test` 和 `backup` 都成功，确认仓库 `backups/` 内出现 `ota-<公钥指纹>.json`。工作流会把密钥在内存中加密、提交文件、重新下载并解密验证。仅有 Secret 或绿色 self-test 不代表已经生成持久备份。
4. 如需独立验证恢复，使用同一工作流选择 **Verify**。它只读取加密文件和恢复密码，不读取原始签名 Secret、不替换任何密钥。

日常主仓库打包发布不需要输入恢复密码，不需要旧电脑开机。你只需要保管这个恢复密码（以及能正常登录 GitHub）；软件用户不需要它。

## 主仓库签名 Secret 误删后的纯网页恢复

先确认公开主仓库已交付支持恢复的发布脚本；没有该功能的旧工作流不能按本节恢复。

1. 打开本私有仓库 `backups/ota-<公钥指纹>.json`，复制完整 JSON（这是加密内容，不是明文私钥）。
2. 在主仓库 [Actions Secrets](https://github.com/VendettaMask/opticalsystem/settings/secrets/actions) 中新增 `OPTICAL_OTA_ENCRYPTED_BACKUP`，值为刚才的完整 JSON；新增 `OPTICAL_OTA_RECOVERY_PASSWORD`，值为同一个恢复密码。
3. 主仓库 `OPTICAL_OTA_SIGNING_KEY` 缺失时，发布脚本从这两个 Secret 在内存中解密并校验同一公钥后签名；不需要生成新密钥，不需要额外个人访问令牌或旧电脑。原签名 Secret 若存在但无效，则拒绝，不静默使用恢复路径。
4. 发布是否成功以主仓库 Actions 结果为准。不要把私钥明文打印出来、复制进代码或上传到 Release。

## 安全边界

- 加密文件使用随机盐、随机 nonce、PBKDF2-HMAC-SHA256（600000 次）和 AES-256-GCM；产品身份、公钥指纹和解密后的公钥全部校验。不依赖 Windows DPAPI、电脑硬件或本机账户。
- 持久备份是私有仓库中的版本化文件，不是会过期的 Actions artifact/cache，也不会出现在公开 Release 下载区。
- Backup 不覆盖已有加密文件；已有备份密码不符会停止，不自动更换密码或密钥。密码更换须另行受控操作。
- `OPTICAL_OTA_SIGNING_KEY` 是用于创建加密备份的受保护源 Secret，不是源码文件。恢复 Verify 不使用它。
- 如果只忘记密码但原签名 Secret 仍正常，日常发布不受影响；加密文件不能因此直接解密。若签名 Secret、可用恢复密码都丢失，只能换签名密钥并让用户手动安装内置新公钥的版本，不能绕过验签。
- 仅靠恢复密码不能找回失去访问权限或被删除的 GitHub 账户/仓库。请为 GitHub 开启两步验证，并安全保管账户恢复方式。全部放同一云服务不是异地灾备；平台不可用时无法发布或恢复。

源码、软件 OTA 客户端进度和实际发布验收以 [主仓库](https://github.com/VendettaMask/opticalsystem) 为准。准备好密钥备份不等于客户端 OTA 已发布或真实升级已验收。
