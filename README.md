# Lovable Static Exporter

Windows 图形界面工具：选择本地 Lovable 网站项目，一键生成 `dist` 静态目录与 `dist.zip`，方便上传 IIS。

## 下载和运行

[下载工具 ZIP](./LovableStaticExporter.zip)，完整解压后运行 `LovableStaticExporter.exe`。
请先安装 Node.js（包含 npm），并保持 `export.mjs` 与 EXE 在同一目录。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

使用系统 .NET Framework C# 编译器生成 EXE，并打包运行所需文件。
已使用 BriteCard Lovable TanStack 项目完成构建、HTML、CSS、二维码和 ZIP 导出验证。

## 使用说明与支持范围
Lovable 静态网站导出工具

1. 电脑上安装 Node.js（包含 npm）。服务器不需要安装 Node.js。
2. 完整解压工具，保持 LovableStaticExporter.exe 与 export.mjs 在同一目录。
3. 运行 EXE，选择包含 package.json 的项目根目录，点击“一键导出 HTML + ZIP”。
4. 完成后项目目录下生成 dist 和 dist.zip。将 dist 内全部内容上传到 IIS 网站根目录。

支持范围：普通 Vite 项目、使用 @lovable.dev/vite-tanstack-config 的 TanStack Start 项目。
工具通过临时构建配置开启预渲染，不修改原始 Vite 配置。
安装缺失依赖时会执行 npm ci / npm install；构建会执行项目自带的 build 脚本，请仅处理可信项目。
旧 dist 保存在 .dist-backup-时间戳目录；失败时恢复旧 dist。成功后可自行清理备份。
项目配置的自定义 outDir、其他框架、动态服务器功能不保证可导出。后端 API、登录和数据库不会变成静态 HTML。
Lovable 图片从 README.md 中已发布的 lovable.app 地址下载；未发布或无法下载时会报错。
本地 CSS 会内嵌，支持双击预览页面样式；JavaScript 模块请通过 HTTP/IIS 访问验证。
外部字体、第三方 API 等外部服务仍需网络。工具不会自动配置服务器、域名或 HTTPS。

开发者命令行用法：node export.mjs "D:\你的项目目录"
源码：App.cs、export.mjs。


