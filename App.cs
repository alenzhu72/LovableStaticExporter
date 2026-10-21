using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

class Exporter : Form {
    TextBox directory = new TextBox(), log = new TextBox();
    Button browse = new Button(), export = new Button(), open = new Button();
    bool running;
    Exporter() {
        Text = "Lovable 静态网站导出工具"; Size = new Size(820, 590); MinimumSize = new Size(660, 450);
        Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var header = new Label { Text = "选择从 GitHub 下载并解压的网站目录", Left = 20, Top = 20, AutoSize = true };
        directory.SetBounds(20, 55, 650, 30); directory.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        browse.Text = "选择目录"; browse.SetBounds(680, 53, 100, 32); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += delegate { using (var dialog = new FolderBrowserDialog()) { dialog.Description = "选择包含 package.json 的项目目录"; if(dialog.ShowDialog() == DialogResult.OK) directory.Text = dialog.SelectedPath; } };
        export.Text = "一键导出 HTML + ZIP"; export.SetBounds(20, 100, 230, 40); export.Click += async delegate { await StartExport(); };
        open.Text = "打开 dist"; open.SetBounds(265, 100, 130, 40); open.Click += delegate { var p = Path.Combine(directory.Text.Trim(), "dist"); if(Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\""); };
        var help = new Label { Text = "需要 Node.js（含 npm）和网络。支持普通 Vite 与 Lovable TanStack Start。\n输出：项目目录下 dist 文件夹和 dist.zip。旧 dist 会保留备份。", Left = 20, Top = 155, Width = 750, Height = 48, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        log.SetBounds(20, 215, 760, 310); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Both; log.WordWrap = false; log.Font = new Font("Consolas", 9); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.AddRange(new Control[] {header,directory,browse,export,open,help,log});
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if(running) { e.Cancel = true; MessageBox.Show("正在构建，请等待完成后关闭，以确保备份恢复。"); } };
    }
    void Append(string line) { if(line == null || IsDisposed) return; BeginInvoke((Action)(() => log.AppendText(line + Environment.NewLine))); }
    async Task StartExport() {
        string folder;
        try { folder = Path.GetFullPath(directory.Text.Trim()); } catch { MessageBox.Show("请选择有效的项目目录。"); return; }
        if(!File.Exists(Path.Combine(folder,"package.json"))) { MessageBox.Show("目录中没有 package.json，请选择项目根目录。"); return; }
        var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"export.mjs");
        if(!File.Exists(script)) { MessageBox.Show("缺少 export.mjs，请将工具完整解压后运行。"); return; }
        running = true; export.Enabled = browse.Enabled = directory.Enabled = open.Enabled = false; log.Clear();
        try {
            int code = await Task.Run(() => {
                var info = new ProcessStartInfo("node.exe", "\"" + script + "\" \"" + folder + "\"");
                info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
                using(var p = new Process()) { p.StartInfo = info; p.OutputDataReceived += (s,e) => Append(e.Data); p.ErrorDataReceived += (s,e) => Append(e.Data); p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit(); return p.ExitCode; }
            });
            MessageBox.Show(code == 0 ? "导出完成！请上传 dist 内的全部文件，或使用 dist.zip。" : "导出失败，请查看日志。", "导出结果");
        } catch(Exception ex) { Append(ex.Message); MessageBox.Show("无法运行：" + ex.Message + "\n请先安装 Node.js 并重新打开工具。"); }
        finally { running = false; export.Enabled = browse.Enabled = directory.Enabled = open.Enabled = true; }
    }
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Exporter()); }
}
