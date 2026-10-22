# Windows SFTP 服务器配置

把本目录完整复制到 Windows Server，运行 `SftpServerSetup.exe`。它打开管理员 PowerShell 执行同目录的配置脚本；新增账号时在控制台输入密码，密码不会作为命令行参数传递。

## 首次配置

1. 在服务器下载微软项目的 [OpenSSH-Win64.zip](https://github.com/PowerShell/Win32-OpenSSH/releases)，选择适合该服务器系统的、带有效签名的版本。
2. 打开配置工具，选择 ZIP。已有 sshd 服务时可以不选 ZIP，工具复用已安装服务。
3. 网站目录填写 IIS 实际物理路径，例如 `C:\inetpub\britecard`；账号建议 `lse_deploy`，使用专用的非管理员本地账号。
4. 填写你的电脑公网 IP 或 CIDR。动态公网 IP 变化后需要更新规则；明确允许所有来源时可填写 `Any`。
5. 端口默认 22。点击安装 / 配置，授权 UAC；创建新账号时在管理员 PowerShell 中设置密码。
6. 完成后控制台会显示远程目录、端口和各类主机公钥的 SHA-256 指纹。保留这些信息供客户端核对。
7. 在云服务器安全组或外层防火墙也放行同一 TCP 端口。

Windows Server 2016 应使用官方 Win32-OpenSSH 安装方式，不能套用仅适用于较新 Windows 的 `Add-WindowsCapability` 方法。参考 [官方安装文档](https://github.com/PowerShell/Win32-OpenSSH/wiki/Install-Win32-OpenSSH)。请先核实所选 OpenSSH 版本仍支持你的系统。

## 配置工具会做什么

- 未安装时，将所选官方 ZIP 中的程序安装到 `C:\Program Files\OpenSSH` 并注册 sshd 服务。
- 创建或复用专用本地账号，为网站目录增加该账号的 Modify 权限；检测到 IIS 用户组时增加读取权限。
- 备份 `C:\ProgramData\ssh\sshd_config`，添加该账号的 `ForceCommand internal-sftp` 和初始网站目录，关闭该账号的转发能力。
- 校验 sshd 配置，为所选端口和来源添加一条 Windows 防火墙规则，设置服务自动启动并重启 sshd。
- 已有 SSH 服务时要求端口与现有配置一致；检测到同账号自定义规则等冲突时停止，留待人工检查。

配置不是整台服务器的事务回滚。sshd 配置校验或启动失败时恢复配置文件备份；此前已创建的账号、目录权限和防火墙规则需要按错误信息检查。其他已有防火墙规则保持不变，可能仍允许更广的访问。

该账号被限制为 SFTP，不提供 SSH 命令行；此工具没有建立文件系统 chroot。其他目录能否访问取决于 Windows NTFS 权限，不应将其当成严格目录沙箱。

## 连接客户端

在导出工具“ SFTP 服务器发布”中输入：

| 项目 | 示例 |
|---|---|
| 服务器 | 公网 IP 或主机名 |
| 端口 | 22 |
| 用户名 | lse_deploy |
| 远程目录 | /C:/inetpub/britecard |
| 密码 | 服务器创建账号时输入的密码 |

点击“获取并核对指纹”，将 SHA-256 内容与服务器控制台输出逐一核对，确认后保存；再测试连接。客户端也支持 PuTTY `.ppk` 私钥；公钥安装、authorized_keys 权限设置需另行完成，可参考 [微软公钥认证说明](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_keymanagement)。

服务器配置工具只做上述服务配置，不创建 IIS 网站、绑定域名或申请 HTTPS 证书。
