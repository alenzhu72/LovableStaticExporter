# Lovable Static Exporter 2.0

Windows 桌面工具：选择从 GitHub 下载的 Lovable 网站目录，一键导出 `dist` 和 `dist.zip`，上传到 Windows IIS。新增 SEO 设置与本地新闻 / Blog 管理。

## 下载与运行

[下载完整工具 ZIP](./LovableStaticExporter.zip)，解压后运行 `LovableStaticExporter.exe`。请保留同目录的 `export.mjs` 和 `news.mjs`。

运行电脑需要 Node.js（含 npm）及 .NET Framework 4.5+；推荐 Node.js 22.12+ 或 24，并满足网站自身依赖要求。Windows 网站服务器只需 IIS。

## SEO 与新闻操作

1. 选择含 `package.json` 的项目目录，或输入目录后点击“加载项目”。已有设置和新闻会自动加载。
2. 在“SEO / 栏目设置”填写全站关键词（逗号分隔）、首页描述。留空保留原有元信息。
3. 可填写正式网站地址，例如 `https://example.com`。填写后生成 canonical；启用新闻时生成 `news-sitemap.xml` 并在 `robots.txt` 追加地址。此处必须填实际部署地址。
4. 勾选启用新闻，编辑栏目名称、简介、列表每页篇数（1–50）。首页展示最新 3 篇，并添加“查看全部”和首页导航入口。
5. 在“新闻内容管理”点击“添加新闻”，输入标题、页面文件名、日期、该页 SEO 关键词、摘要、正文，选择一张图片。图片可选，支持 PNG/JPEG/GIF/WebP，最大 20 MB。
6. 正文是纯文本，空行分段、单行换行保留；不执行输入的 HTML。摘要留空时使用正文前 160 字；文章关键词留空时继承全站关键词。
7. 点击“保存设置和新闻”可保存草稿；点击“保存并导出 HTML + ZIP”构建整个站点。导出要求每篇都有标题、正文和有效日期。

文章文件名只能使用小写英文、数字、短横线，必须唯一，不允许 `index`、`page-N` 和 Windows 保留文件名。新增文章自动生成稳定文件名；已发布后建议保持不变，以保留原有链接。

## 输出结构

```text
项目/
  .static-exporter/
    settings.json         # SEO、栏目和文章内容
    settings.json.bak     # 上一次保存的设置
    images/               # 从电脑复制的文章图片
  dist/
    index.html            # 首页，含新闻区块
    news-home.js          # 框架加载后维持新闻区块与导航
    news/
      index.html          # 新闻列表第一页
      page-2.html         # 超出每页篇数后自动生成
      post-xxx.html       # 独立新闻详情
      images/             # 文章图片
    news-sitemap.xml      # 设置正式网址后生成
    web.config            # IIS 默认首页
    assets/               # 原网站资源
  dist.zip
```

设置按项目保存，不写入网站源码。图片会复制到项目内，移动或备份项目时请带上 `.static-exporter`。在工具中编辑或删除新闻后需重新导出；上传时同步替换整个 `dist` 内容，清理服务器上的旧新闻文件，避免删除后的页面继续存在。

## 样式与部署

新闻页继承首页导出 CSS、字体及主题变量（支持常见 HSL、OKLCH 主题），使用自适应卡片、列表和文章布局。首页原布局保留，新闻区块加在页面底部；存在 `header nav` 时添加栏目链接。特殊主题可能需要进一步适配。

CSS 内嵌便于双击查看样式。新闻页可直接打开；原站点 JavaScript 仍需通过 HTTP/IIS 验证。将 ZIP 内容完整解压到 IIS 网站根目录，让 `index.html` 与 `web.config` 直接位于根目录，不需要 URL Rewrite 来访问新增的 `.html` 新闻页。

## 支持范围

- 普通 Vite 构建，以及使用 `@lovable.dev/vite-tanstack-config` 的 Lovable TanStack Start 项目。
- TanStack 使用临时配置启用静态预渲染，不修改原来的 Vite 配置。
- 安装缺失依赖时执行 `npm ci` 或 `npm install`，构建执行项目自带脚本，请仅处理可信项目。
- 旧 `dist` 备份到 `.dist-backup-时间戳`，失败时恢复旧目录；成功后可自行清理备份。
- 若原站已有 `news` 目录，工具报错避免覆盖，请先调整原栏目路径。
- 登录、数据库和后端 API 不会变成静态服务。自定义构建输出和其他框架可能需要适配。
- Lovable 图片通过 README 中的已发布 `lovable.app` 地址下载；未发布或下载失败时会报错。
- 写入关键词标签不代表搜索排名或收录保证。工具还生成页面标题、描述、canonical 和新闻结构化数据；外部字体及第三方服务仍需网络。

## 从源码构建与验证

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本编译 EXE、检查 JS、运行新闻生成测试及 Windows 窗口保存测试，然后生成工具 ZIP。

```powershell
node --test tests/news.test.mjs
node export.mjs "D:\你的项目目录"
```

`tests/browser-check.mjs` 为可选浏览器验证，需要 Playwright 和 Edge；测试截图与示例项目在本地隔离目录生成，不属于实际网站内容。
