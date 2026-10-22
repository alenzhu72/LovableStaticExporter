# Windows 启动警告与正式代码签名

当前仓库的主程序与服务器配置工具尚未进行发行者代码签名。Logo、版本号、文件属性、ZIP 校验码不等于签名，也不会自动解除 Windows 的拦截。

## 先区分提示类型

| 提示 | 含义与处理 |
|---|---|
| “Windows 已保护你的电脑 / 无法识别的应用” | 通常是 SmartScreen 下载信誉不足；未签名的新程序容易出现。核对实际提示后判断。 |
| “未知发布者”与管理员授权窗口 | UAC 没有识别到可信发行者。服务器安装步骤需要管理员授权，正式签名后该授权窗口仍可能出现。普通客户端声明为 asInvoker，不主动要求管理员权限。 |
| Defender 检出具体威胁名或隔离文件 | 属于另一个检测结果，不能只解释为签名问题。先停止运行并查看检测记录，确认是否误报；开发者可通过微软文件提交渠道申请分析。 |
| Smart App Control 或企业策略阻止运行 | 可能有更严格的签名与信誉要求，需要遵循设备或组织管理策略。 |

## 正式发行的处理方式

1. 使用你本人或公司真实身份申请可信代码签名证书，或使用符合资格的签名服务。
2. 给客户端 EXE、服务器工具 EXE 签名，并添加可信时间戳；此项目还支持给服务器配置 PowerShell 脚本签名。
3. 签名后验证，再打包；以后每次重新编译后都重新签名，保持发行者身份一致。
4. GitHub 下载属于 Store 外分发，新文件即使已签名也可能暂时出现 SmartScreen 提示。自签名证书不适合解决面向普通用户的公开发行警告；EV 证书目前也不保证首次下载绕过 SmartScreen。

在地区和身份符合要求时可考虑 Azure Artifact Signing；其资格有地域限制，不能假定新加坡个人或公司都能申请。其他情况可咨询可信 CA 的代码签名证书及硬件 / 云密钥服务。若希望采用微软商店的信誉体系，可另做 MSIX 打包和 Store 提交；此仓库的 ZIP/EXE 不会因放到 GitHub 就获得 Store 身份。

微软依据：[SmartScreen 信誉说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)、[代码签名方案与资格](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)、[Smart App Control](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)。

## 已准备的签名入口

仓库完整源码中的 `sign-release.ps1` 使用 Windows 证书存储中的证书指纹；不要求将私钥、PFX 密码或服务凭据写进源码。运行签名流程需下载 / 克隆完整源码，运行版 ZIP 只包含工具及使用文档。

准备 Windows SDK Signing Tools 和真实代码签名证书（通常由令牌/HSM/云提供程序管理私钥），然后在项目根目录运行：

```powershell
.\sign-release.ps1 -CertificateThumbprint "你的真实代码签名证书指纹"
```

证书位于本机证书存储时加 `-MachineStore`；找不到 SignTool 时使用 `-SignToolPath`。云签名服务可能需要它自己的 SDK、插件或命令，该脚本的证书存储模式不会代替 Azure 账户和身份审核。

脚本执行：构建和测试 → 检查证书用途、私钥和信任链 → SHA-256 签名和时间戳 → 验证 → 仅重新打包 → 更新 SHA-256 校验文件。它不会安装自签名根证书，也不会关闭安全防护。签名需要已有真实证书；当前环境没有为这个项目执行签名。

## 下载校验

仓库提供 `LovableStaticExporter.zip.sha256` 和包内 `SHA256SUMS.txt`。在 PowerShell 中查看下载包 SHA-256：

```powershell
Get-FileHash .\LovableStaticExporter.zip -Algorithm SHA256
Get-AuthenticodeSignature .\LovableStaticExporter.exe
```

将散列与可信来源发布的值比较可检查文件一致性，但散列不能证明发行者身份，也不代表病毒扫描已通过。请不要通过关闭 Defender、关闭 SmartScreen 或添加整个目录的杀毒排除来代替解决方案。
