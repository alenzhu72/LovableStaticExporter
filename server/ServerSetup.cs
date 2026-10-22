using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
class ServerSetup : Form {
    TextBox archive = new TextBox(), website = new TextBox(), user = new TextBox(), address = new TextBox(); NumericUpDown port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22 };
    ServerSetup() {
        Text = "Lovable SFTP 服务器配置 · Windows Server"; Icon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(ServerSetup).Assembly.Location); ClientSize = new Size(790,440); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI",10);
        Field("官方 OpenSSH ZIP",archive,20); var browse = new Button { Text = "选择 ZIP", Left = 650, Top = 18, Width = 115, Height = 33 }; browse.Click += delegate { using(var d = new OpenFileDialog { Filter = "OpenSSH-Win64 archive|*.zip" }) if(d.ShowDialog() == DialogResult.OK) archive.Text = d.FileName; }; Controls.Add(browse);
        Field("IIS 网站本地目录",website,78); website.Text = "C:\\inetpub\\britecard";
        Field("SFTP 专用用户名",user,136); user.Text = "lse_deploy";
        Field("允许的客户端公网 IP",address,194);
        Controls.Add(new Label { Text = "填写电脑公网 IP/CIDR；明确允许所有来源时填 Any。", Left = 205, Top = 230, Width = 550 });
        Field("SFTP 端口",port,266); port.Width = 130;
        var help = new LinkLabel { Text = "打开官方 OpenSSH 下载页", Left = 205, Top = 315, Width = 480 }; help.LinkClicked += delegate { Process.Start("https://github.com/PowerShell/Win32-OpenSSH/releases"); }; Controls.Add(help);
        var install = new Button { Text = "以管理员权限安装 / 配置", Left = 205, Top = 356, Width = 350, Height = 42 }; install.Click += delegate { Install(); }; Controls.Add(install);
    }
    void Field(string text, Control c, int y) { Controls.Add(new Label { Text = text, Left = 20, Top = y + 5, Width = 180 }); c.SetBounds(205,y,430,29); Controls.Add(c); }
    string Quote(string value) { if(value.Contains("\"") || value.Contains("\r") || value.Contains("\n")) throw new Exception("输入不能包含引号或换行。"); return "\"" + Regex.Replace(value,@"(\\+)$","$1$1") + "\""; }
    void Install() {
        try {
            if(String.IsNullOrWhiteSpace(website.Text) || String.IsNullOrWhiteSpace(user.Text) || String.IsNullOrWhiteSpace(address.Text)) throw new Exception("请填写网站目录、专用用户名和允许的公网 IP。");
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Install-SftpServer.ps1"); if(!File.Exists(script)) throw new Exception("缺少配置脚本，请完整复制 server 文件夹到服务器。");
            string command = "-NoProfile -NoExit -ExecutionPolicy Bypass -File " + Quote(script) + " -PublishPath " + Quote(website.Text.Trim()) + " -UserName " + Quote(user.Text.Trim()) + " -Port " + (int)port.Value + " -AllowedClientAddress " + Quote(address.Text.Trim());
            if(!String.IsNullOrWhiteSpace(archive.Text)) command += " -OpenSSHZip " + Quote(archive.Text.Trim());
            Process.Start(new ProcessStartInfo("powershell.exe",command) { UseShellExecute = true, Verb = "runas" });
        } catch(Exception e) { MessageBox.Show(e.Message); }
    }
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new ServerSetup()); }
}
