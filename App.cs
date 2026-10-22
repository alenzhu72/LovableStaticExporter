using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Article {
    public string title { get; set; } public string slug { get; set; } public string date { get; set; }
    public string summary { get; set; } public string keywords { get; set; } public string content { get; set; } public string image { get; set; }
    public override string ToString() { return String.IsNullOrWhiteSpace(title) ? "（未命名新闻）" : title; }
}
public class Settings {
    public string keywords { get; set; } public string description { get; set; } public string siteUrl { get; set; }
    public bool newsEnabled { get; set; } public string newsTitle { get; set; } public string newsIntro { get; set; }
    public int pageSize { get; set; } public List<Article> articles { get; set; }
    public Settings() { newsTitle = "新闻动态"; newsIntro = "了解我们的最新动态与行业资讯。"; pageSize = 6; articles = new List<Article>(); }
}
class Exporter : Form {
    TextBox directory = new TextBox(), log = new TextBox();
    TextBox keywords = new TextBox(), description = new TextBox(), siteUrl = new TextBox(), newsTitle = new TextBox(), newsIntro = new TextBox();
    TextBox title = new TextBox(), slug = new TextBox(), summary = new TextBox(), articleKeywords = new TextBox(), body = new TextBox(), image = new TextBox();
    CheckBox newsEnabled = new CheckBox(); NumericUpDown pageSize = new NumericUpDown(); DateTimePicker date = new DateTimePicker();
    Button browse = new Button(), load = new Button(), save = new Button(), export = new Button(), open = new Button(), publish = new Button();
    SftpPanel sftpPanel;
    ListBox articles = new ListBox(); TabControl tabs = new TabControl(); Panel edit = new Panel();
    Settings settings = new Settings(); Article current; string loadedRoot; bool running, binding;
    JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };
    Exporter() {
        Text = "Lovable 静态网站导出工具 3.0 · SEO / 新闻 / SFTP 发布"; Size = new Size(1080, 820); MinimumSize = new Size(1080, 780);
        Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var label = new Label { Text = "项目目录（包含 package.json）", Left = 18, Top = 14, AutoSize = true };
        directory.SetBounds(18, 40, 755, 29); directory.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ButtonAt(browse, "选择目录", 785, 38, 115); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        ButtonAt(load, "加载项目", 910, 38, 130); load.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += delegate { using(var d = new FolderBrowserDialog()) { d.Description = "选择本地 Lovable 网站项目"; if(d.ShowDialog() == DialogResult.OK && CanSwitch()) { directory.Text = d.SelectedPath; LoadProject(); } } };
        load.Click += delegate { if(CanSwitch()) LoadProject(); };
        ButtonAt(export, "保存并导出 HTML + ZIP", 18, 83, 250); export.Click += async delegate { await StartExport(); };
        ButtonAt(save, "保存设置和新闻", 282, 83, 180); save.Click += delegate { try { SaveProject(); MessageBox.Show("已保存，重新加载项目可继续编辑。"); } catch(Exception ex) { MessageBox.Show(ex.Message); } };
        ButtonAt(open, "打开 dist", 476, 83, 140); open.Click += delegate { var p = Path.Combine(directory.Text.Trim(), "dist"); if(Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\""); };
        ButtonAt(publish,"构建并上传服务器",630,83,220); publish.Click += async delegate { await sftpPanel.Upload(true); };
        tabs.SetBounds(18, 138, 1022, 615); tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        var seoTab = new TabPage("SEO / 栏目设置"); var newsTab = new TabPage("新闻内容管理"); var logTab = new TabPage("导出日志"); tabs.TabPages.AddRange(new TabPage[] {seoTab, newsTab, logTab});
        var sftpTab = new TabPage("SFTP 服务器发布"); sftpPanel = new SftpPanel(CurrentProject,() => StartExport(true),SetBusy,Append); sftpTab.Controls.Add(sftpPanel); tabs.TabPages.Add(sftpTab);
        Field(seoTab, "全站 SEO 关键词", keywords, 20, 23, 800, 30); Field(seoTab, "首页描述", description, 20, 76, 800, 70); description.Multiline = true;
        Field(seoTab, "网站正式地址", siteUrl, 20, 171, 800, 30);
        seoTab.Controls.Add(new Label { Text = "例：https://example.com（可选；填写后生成 canonical 与新闻 sitemap）", Left = 180, Top = 207, Width = 780 });
        newsEnabled.Text = "启用首页新闻栏目和独立新闻页"; newsEnabled.SetBounds(180, 245, 600, 30); seoTab.Controls.Add(newsEnabled);
        Field(seoTab, "栏目名称", newsTitle, 20, 294, 800, 30); Field(seoTab, "栏目简介", newsIntro, 20, 347, 800, 55); newsIntro.Multiline = true;
        Field(seoTab, "列表每页篇数", pageSize, 20, 428, 100, 30); pageSize.Minimum = 1; pageSize.Maximum = 50; pageSize.Value = 6;
        seoTab.Controls.Add(new Label { Text = "关键词用逗号分隔。首页展示最新 3 篇；新闻列表按日期倒序分页。\n文章正文为纯文本，空行分段；每篇可配一张图片。\n设置和图片保存在项目 .static-exporter 目录，请随项目一起备份。", Left = 180, Top = 480, Width = 780, Height = 80 });
        articles.SetBounds(12, 50, 230, 510);
        articles.SelectedIndexChanged += delegate { if(binding) return; StoreCurrent(); current = articles.SelectedItem as Article; BindArticle(); };
        var add = new Button(); ButtonAt(add, "添加新闻", 12, 10, 108); var remove = new Button(); ButtonAt(remove, "删除新闻", 130, 10, 108);
        add.Click += delegate { StoreCurrent(); var a = new Article { title = "新新闻", slug = "post-" + Guid.NewGuid().ToString("N").Substring(0,12), date = DateTime.Today.ToString("yyyy-MM-dd"), content = "" }; settings.articles.Add(a); RefreshArticles(a); };
        remove.Click += delegate { if(current != null && MessageBox.Show("删除这篇新闻？导出后会从新站点移除。", "删除新闻", MessageBoxButtons.YesNo) == DialogResult.Yes) { settings.articles.Remove(current); current = null; RefreshArticles(null); } };
        edit.SetBounds(254, 10, 745, 560);
        Field(edit, "标题", title, 0, 5, 588, 29, 100); Field(edit, "页面文件名", slug, 0, 48, 588, 29, 100);
        edit.Controls.Add(new Label { Text = "仅小写英文、数字、短横线；生成 news/文件名.html", Left = 100, Top = 79, Width = 600 });
        date.Format = DateTimePickerFormat.Custom; date.CustomFormat = "yyyy-MM-dd"; Field(edit, "发布日期", date, 0, 110, 220, 29, 100);
        Field(edit, "SEO 关键词", articleKeywords, 0, 153, 588, 29, 100); Field(edit, "摘要 / 描述", summary, 0, 196, 588, 52, 100); summary.Multiline = true;
        Field(edit, "图片（1 张）", image, 0, 263, 370, 29, 100); image.ReadOnly = true;
        var choose = new Button(); ButtonAt(choose, "选图", 480, 261, 90); var clear = new Button(); ButtonAt(clear, "移除", 580, 261, 90);
        choose.Click += delegate { using(var d = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.gif" }) { if(d.ShowDialog() == DialogResult.OK) image.Text = d.FileName; } }; clear.Click += delegate { image.Text = ""; }; edit.Controls.AddRange(new Control[] {choose, clear});
        Field(edit, "正文", body, 0, 310, 588, 230, 100); body.Multiline = true; body.ScrollBars = ScrollBars.Vertical; body.AcceptsReturn = true;
        edit.SizeChanged += delegate { body.Width = Math.Max(200,edit.ClientSize.Width - 116); body.Height = Math.Max(80,edit.ClientSize.Height - 326); };
        newsTab.Controls.AddRange(new Control[] {articles, add, remove, edit}); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Both; log.WordWrap = false; log.Font = new Font("Consolas", 9); log.Dock = DockStyle.Fill; logTab.Controls.Add(log);
        newsTab.Resize += delegate { articles.Height = Math.Max(300,newsTab.ClientSize.Height - 62); edit.SetBounds(254,10,Math.Max(700,newsTab.ClientSize.Width - 266),Math.Max(400,newsTab.ClientSize.Height - 22)); body.SetBounds(100,310,edit.ClientSize.Width - 116,Math.Max(80,edit.ClientSize.Height - 326)); };
        Controls.AddRange(new Control[] {label,directory,browse,load,export,save,open,publish,tabs}); BindSettings(); tabs.TabPages[0].Enabled = tabs.TabPages[1].Enabled = tabs.TabPages[3].Enabled = publish.Enabled = false;
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if(running) { e.Cancel = true; MessageBox.Show("正在导出，请等待完成后关闭。"); } else if(!CanSwitch()) e.Cancel = true; };
    }
    void ButtonAt(Button b, string text, int x, int y, int w) { b.Text = text; b.SetBounds(x,y,w,34); }
    void Field(Control panel, string text, Control input, int x, int y, int w, int h, int labelWidth = 160) { panel.Controls.Add(new Label { Text = text, Left = x, Top = y + 4, Width = labelWidth, Height = 26 }); input.SetBounds(x + labelWidth,y,w,h); panel.Controls.Add(input); }
    bool CanSwitch() {
        if(loadedRoot == null) return true;
        var result = MessageBox.Show("是否保存当前项目的设置和新闻？", "保存内容", MessageBoxButtons.YesNoCancel);
        if(result == DialogResult.Cancel) return false;
        if(result == DialogResult.Yes) { var requested = directory.Text; try { directory.Text = loadedRoot; SaveProject(); } catch(Exception ex) { MessageBox.Show(ex.Message); return false; } finally { directory.Text = requested; } }
        return true;
    }
    void LoadProject() {
        try {
            var folder = Path.GetFullPath(directory.Text.Trim()); if(!File.Exists(Path.Combine(folder,"package.json"))) throw new Exception("目录中没有 package.json，请选择项目根目录。");
            var file = Path.Combine(folder,".static-exporter","settings.json"); var next = File.Exists(file) ? json.Deserialize<Settings>(File.ReadAllText(file,Encoding.UTF8)) : new Settings(); if(next == null) throw new Exception("项目设置文件为空。");
            settings = next; if(settings.articles == null) settings.articles = new List<Article>(); loadedRoot = folder; current = null; BindSettings(); sftpPanel.LoadProfile(folder); tabs.TabPages[0].Enabled = tabs.TabPages[1].Enabled = tabs.TabPages[3].Enabled = publish.Enabled = true;
        } catch(Exception ex) { MessageBox.Show("加载失败：" + ex.Message); }
    }
    void BindSettings() { keywords.Text = settings.keywords ?? ""; description.Text = settings.description ?? ""; siteUrl.Text = settings.siteUrl ?? ""; newsEnabled.Checked = settings.newsEnabled; newsTitle.Text = settings.newsTitle ?? "新闻动态"; newsIntro.Text = settings.newsIntro ?? ""; pageSize.Value = Math.Max(1,Math.Min(50,settings.pageSize)); RefreshArticles(null); }
    void RefreshArticles(Article selected) { binding = true; articles.Items.Clear(); foreach(var a in settings.articles) articles.Items.Add(a); articles.SelectedItem = selected ?? (articles.Items.Count > 0 ? articles.Items[0] : null); current = articles.SelectedItem as Article; binding = false; BindArticle(); }
    void BindArticle() { edit.Enabled = current != null; title.Text = current == null ? "" : current.title; slug.Text = current == null ? "" : current.slug; articleKeywords.Text = current == null ? "" : current.keywords; summary.Text = current == null ? "" : current.summary; body.Text = current == null ? "" : current.content; image.Text = current == null ? "" : current.image; DateTime parsed; date.Value = current != null && DateTime.TryParse(current.date,out parsed) ? parsed : DateTime.Today; }
    void StoreCurrent() { if(current == null) return; current.title = title.Text.Trim(); current.slug = slug.Text.Trim(); current.date = date.Value.ToString("yyyy-MM-dd"); current.keywords = articleKeywords.Text.Trim(); current.summary = summary.Text.Trim(); current.content = body.Text; current.image = image.Text; articles.Invalidate(); }
    void SaveProject() {
        var folder = Path.GetFullPath(directory.Text.Trim()); if(loadedRoot == null) { LoadProject(); if(loadedRoot == null) throw new Exception("请先加载项目。"); }
        if(!String.Equals(folder,loadedRoot,StringComparison.OrdinalIgnoreCase)) throw new Exception("目录已更改，请先点击“加载项目”。");
        StoreCurrent(); settings.keywords = keywords.Text.Trim(); settings.description = description.Text.Trim(); settings.siteUrl = siteUrl.Text.Trim(); settings.newsEnabled = newsEnabled.Checked; settings.newsTitle = newsTitle.Text.Trim(); settings.newsIntro = newsIntro.Text.Trim(); settings.pageSize = (int)pageSize.Value;
        var dataDir = Path.Combine(folder,".static-exporter"); var imageDir = Path.Combine(dataDir,"images"); Directory.CreateDirectory(imageDir);
        foreach(var a in settings.articles) {
            if(String.IsNullOrWhiteSpace(a.image)) continue;
            string source = Path.IsPathRooted(a.image) ? a.image : Path.Combine(folder,a.image); if(!File.Exists(source)) throw new Exception("图片不存在：" + source);
            if(new FileInfo(source).Length > 20 * 1024 * 1024) throw new Exception("图片不能超过 20 MB：" + source);
            if(!Path.GetFullPath(source).StartsWith(Path.GetFullPath(imageDir) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) { string filename = Guid.NewGuid().ToString("N") + Path.GetExtension(source).ToLowerInvariant(); File.Copy(source,Path.Combine(imageDir,filename)); a.image = ".static-exporter/images/" + filename; }
        }
        var target = Path.Combine(dataDir,"settings.json"); var temp = target + ".tmp"; File.WriteAllText(temp,json.Serialize(settings),new UTF8Encoding(false));
        if(File.Exists(target)) File.Copy(target,target + ".bak",true); File.Copy(temp,target,true); File.Delete(temp); RefreshArticles(current);
        sftpPanel.SaveProfile();
    }
    string CurrentProject() { if(loadedRoot == null || !String.Equals(Path.GetFullPath(directory.Text.Trim()),loadedRoot,StringComparison.OrdinalIgnoreCase)) throw new Exception("请先选择项目并点击“加载项目”。"); return loadedRoot; }
    void SetBusy(bool value) { running = value; export.Enabled = browse.Enabled = load.Enabled = save.Enabled = directory.Enabled = open.Enabled = !value; publish.Enabled = !value && loadedRoot != null; tabs.TabPages[0].Enabled = tabs.TabPages[1].Enabled = tabs.TabPages[3].Enabled = !value && loadedRoot != null; if(value) tabs.SelectedIndex = 2; }
    void Append(string line) { if(line == null || IsDisposed) return; BeginInvoke((Action)(() => log.AppendText(line + Environment.NewLine))); }
    async Task<bool> StartExport(bool quiet = false) {
        try { SaveProject(); } catch(Exception ex) { MessageBox.Show(ex.Message); return false; }
        string folder = loadedRoot; var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"export.mjs");
        if(!File.Exists(script) || !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"news.mjs"))) { MessageBox.Show("缺少导出脚本，请完整解压工具。"); return false; }
        SetBusy(true); log.Clear();
        try {
            int code = await Task.Run(() => {
                var info = new ProcessStartInfo("node.exe", "\"" + script + "\" \"" + folder + "\""); info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true; info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
                using(var p = new Process()) { p.StartInfo = info; p.OutputDataReceived += (s,e) => Append(e.Data); p.ErrorDataReceived += (s,e) => Append(e.Data); p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit(); return p.ExitCode; }
            });
            if(!quiet || code != 0) MessageBox.Show(code == 0 ? "导出完成！请上传 dist 内的全部文件，或使用 dist.zip。" : "导出失败，请查看日志；修正后可重新导出。", "导出结果");
            return code == 0;
        } catch(Exception ex) { Append(ex.Message); MessageBox.Show("无法运行：" + ex.Message + "\n请先安装 Node.js 并重新打开工具。"); return false; }
        finally { SetBusy(false); }
    }
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Exporter()); }
}
