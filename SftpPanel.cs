using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

public class SftpPanel : UserControl {
    TextBox host = new TextBox(), user = new TextBox(), remote = new TextBox(), fingerprint = new TextBox(), privateKey = new TextBox(), secret = new TextBox();
    NumericUpDown port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22 };
    CheckBox useKey = new CheckBox(), remember = new CheckBox(), clean = new CheckBox();
    Label status = new Label();
    Func<string> projectPath; Func<Task<bool>> build; Action<bool> busy; Action<string> log;
    public SftpPanel(Func<string> projectPath, Func<Task<bool>> build, Action<bool> busy, Action<string> log) {
        this.projectPath = projectPath; this.build = build; this.busy = busy; this.log = log; Dock = DockStyle.Fill; AutoScroll = true;
        Field("服务器 IP / 主机名",host,20,20,610); Field("端口",port,20,66,110); Field("用户名",user,350,66,280);
        Field("网站远程目录",remote,20,112,750); remote.PlaceholderCompat("/C:/inetpub/britecard");
        Controls.Add(new Label { Text = "例：/C:/inetpub/britecard 或 /var/www/britecard；目录需已存在。", Left = 190, Top = 148, Width = 780 });
        Field("服务器 SHA-256 指纹",fingerprint,20,188,610);
        var scan = Button("获取并核对指纹",815,186,170); scan.Click += async delegate { await Scan(); };
        useKey.Text = "使用 PuTTY .ppk 私钥登录（否则使用密码）"; useKey.SetBounds(190,236,580,30); Controls.Add(useKey);
        Field("私钥文件",privateKey,20,279,610); var choose = Button("选择私钥",815,277,170); choose.Click += delegate { using(var d = new OpenFileDialog { Filter = "PuTTY 私钥|*.ppk|所有文件|*.*" }) { if(d.ShowDialog() == DialogResult.OK) privateKey.Text = d.FileName; } };
        Field("密码 / 私钥口令",secret,20,325,610); secret.UseSystemPasswordChar = true;
        remember.Text = "在本机记住密码 / 口令（Windows 当前用户加密）"; remember.SetBounds(190,368,780,30); Controls.Add(remember);
        clean.Text = "同步删除已移除的新闻（仅本工具部署清单内的旧新闻，不清理其他服务器文件）"; clean.SetBounds(190,406,800,32); Controls.Add(clean);
        var save = Button("保存连接设置",190,453,180); var test = Button("测试连接",385,453,150); var upload = Button("上传现有 dist",550,453,180); var publish = Button("构建并上传",745,453,180);
        save.Click += delegate { try { SaveProfile(); status.Text = "连接设置已保存在本机。"; } catch(Exception e) { MessageBox.Show(e.Message); } };
        test.Click += async delegate { await Test(); }; upload.Click += async delegate { await Upload(false); }; publish.Click += async delegate { await Upload(true); };
        status.SetBounds(190,505,790,65); status.Text = "首次配置请先获取指纹并与服务器核对，再测试连接。上传覆盖同名站点文件。"; Controls.Add(status);
    }
    void Field(string label, Control input, int x, int y, int width) { Controls.Add(new Label { Text = label, Left = x, Top = y + 4, Width = 170, Height = 26 }); input.SetBounds(x + 170,y,width,29); Controls.Add(input); }
    Button Button(string text, int x, int y, int width) { var b = new Button { Text = text }; b.SetBounds(x,y,width,35); Controls.Add(b); return b; }
    public void LoadProfile(string project) {
        var p = SftpProfiles.Load(project); host.Text = p.host ?? ""; port.Value = Math.Max(1,Math.Min(65535,p.port)); user.Text = p.user ?? ""; remote.Text = p.remoteRoot ?? ""; fingerprint.Text = p.fingerprint ?? ""; privateKey.Text = p.privateKey ?? ""; useKey.Checked = p.useKey; remember.Checked = p.rememberSecret; clean.Checked = p.cleanNews;
        try { secret.Text = SftpProfiles.Secret(p); status.Text = "连接设置已加载；私钥只保留本地路径。"; } catch { secret.Text = ""; status.Text = "无法解密原口令，请重新输入（仅原 Windows 用户可解密）。"; }
    }
    SftpProfile Snapshot() { return new SftpProfile { host = host.Text.Trim(), port = (int)port.Value, user = user.Text.Trim(), remoteRoot = remote.Text.Trim(), fingerprint = fingerprint.Text.Trim(), privateKey = privateKey.Text.Trim(), useKey = useKey.Checked, rememberSecret = remember.Checked, cleanNews = clean.Checked }; }
    public void SaveProfile() { SftpProfiles.Save(projectPath(),Snapshot(),secret.Text); }
    async Task Scan() {
        try {
            projectPath(); var p = Snapshot(); busy(true);
            var found = await Task.Run(() => SftpPublisher.Scan(p));
            if(MessageBox.Show("请通过服务器控制台核对该 SHA-256 指纹：\n\n" + found + "\n\n与服务器的 ssh-keygen -lf 输出一致后，是否保存并信任该指纹？", "核对服务器身份", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { fingerprint.Text = found; SaveProfile(); status.Text = "已保存核对后的指纹；后续连接会严格验证。"; }
        } catch(Exception e) { status.Text = "获取指纹失败：" + e.Message; MessageBox.Show(status.Text); }
        finally { busy(false); }
    }
    async Task Test() {
        try { projectPath(); var p = Snapshot(); var password = secret.Text; SftpPublisher.Validate(p); SaveProfile(); busy(true); log("测试 SFTP 连接..."); await Task.Run(() => SftpPublisher.Test(p,password)); status.Text = "连接成功，目标目录可读。写入权限会在实际上传时验证。"; MessageBox.Show(status.Text); }
        catch(Exception e) { status.Text = "连接失败：" + e.Message; log(status.Text); MessageBox.Show(status.Text); }
        finally { busy(false); }
    }
    public async Task Upload(bool rebuild) {
        bool uploading = false;
        try {
            var root = projectPath(); var p = Snapshot(); var password = secret.Text; SftpPublisher.Validate(p); SaveProfile();
            if(rebuild && !(await build())) return;
            busy(true); uploading = true; status.Text = "正在上传，请等待完成。";
            await Task.Run(() => SftpPublisher.Upload(root,p,password,log));
            status.Text = "发布完成：" + p.host + " → " + p.remoteRoot; MessageBox.Show(status.Text,"SFTP 发布");
        } catch(Exception e) { status.Text = "发布失败：" + e.Message; log(status.Text); MessageBox.Show(status.Text + (uploading ? "\n上传按文件执行，已完成的文件可能已更新；修正问题后可重新发布。" : "")); }
        finally { busy(false); }
    }
}
// Keep the UI compatible with the classic .NET Framework TextBox.
static class SftpTextBoxExtensions { public static void PlaceholderCompat(this TextBox box, string example) { box.AccessibleDescription = example; } }
