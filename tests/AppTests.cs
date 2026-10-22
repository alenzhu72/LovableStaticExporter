using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

class AppTests {
    static object Field(object form, string name) { return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    static void Call(object form, string name) { form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null); }
    static void Check(bool ok, string message) { if(!ok) throw new Exception(message); }
    [STAThread] static int Main(string[] args) {
        string fixture = Path.Combine(Path.GetTempPath(), "lse-app-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(fixture); Environment.SetEnvironmentVariable("LSE_PROFILE_DIRECTORY",Path.Combine(fixture,"local-profiles")); File.WriteAllText(Path.Combine(fixture,"package.json"),"{}");
            string sourceImage = Path.Combine(fixture,"cover.png"); using(var image = new Bitmap(16,16)) image.Save(sourceImage,System.Drawing.Imaging.ImageFormat.Png);
            string settingsPath = Path.Combine(fixture,".static-exporter","settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllText(settingsPath,"{\"newsEnabled\":true,\"newsTitle\":\"测试新闻\",\"pageSize\":2,\"articles\":[{\"title\":\"旧标题\",\"slug\":\"hello\",\"date\":\"2026-10-22\",\"content\":\"正文\"}]}",Encoding.UTF8);
            Type appType = Assembly.LoadFrom(args[0]).GetType("Exporter");
            using(var form = (Form)Activator.CreateInstance(appType,true)) {
                ((TextBox)Field(form,"directory")).Text = fixture; Call(form,"LoadProject");
                Check(((TextBox)Field(form,"newsTitle")).Text == "测试新闻", "Saved settings were not loaded");
                ((TextBox)Field(form,"title")).Text = "更新后的标题"; ((TextBox)Field(form,"body")).Text = "第一段\r\n\r\n第二段"; ((TextBox)Field(form,"image")).Text = sourceImage;
                ((TextBox)Field(form,"keywords")).Text = "全站词, SEO"; Call(form,"SaveProject");
                Check(File.ReadAllText(settingsPath).Contains("更新后的标题"), "Article edits were not persisted");
                Check(Directory.GetFiles(Path.Combine(fixture,".static-exporter","images")).Length == 1, "Image was not copied to project storage");
                Call(form,"SaveProject"); Check(File.Exists(settingsPath + ".bak"), "Settings backup missing");
                Call(form,"LoadProject"); Check(((TextBox)Field(form,"title")).Text == "更新后的标题", "Reload lost article title");
                Check(((TextBox)Field(form,"image")).Text.StartsWith(".static-exporter/images/"), "Image is not portable");
                var sftp = Field(form,"sftpPanel"); ((TextBox)Field(sftp,"host")).Text = "fixture.invalid"; ((TextBox)Field(sftp,"remote")).Text = "/C:/inetpub/britecard"; ((TextBox)Field(sftp,"secret")).Text = "ui-test-secret"; ((CheckBox)Field(sftp,"remember")).Checked = true; Call(form,"SaveProject");
                Check(!File.ReadAllText(settingsPath).Contains("ui-test-secret"), "Server credential leaked into project settings");
                Call(form,"LoadProject"); Check(((TextBox)Field(Field(form,"sftpPanel"),"secret")).Text == "ui-test-secret", "Remembered SFTP password did not reload");
                if(args.Length > 1) {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-3000,-3000); form.Show(); Application.DoEvents(); var tabs = (TabControl)Field(form,"tabs");
                    using(var bmp = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,form.ClientRectangle); bmp.Save(Path.Combine(args[1],"app-seo.png"),System.Drawing.Imaging.ImageFormat.Png); }
                    tabs.SelectedIndex = 1; Application.DoEvents();
                    using(var bmp = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,form.ClientRectangle); bmp.Save(Path.Combine(args[1],"app-news.png"),System.Drawing.Imaging.ImageFormat.Png); }
                    tabs.SelectedIndex = 3; Application.DoEvents();
                    using(var bmp = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,form.ClientRectangle); bmp.Save(Path.Combine(args[1],"app-sftp.png"),System.Drawing.Imaging.ImageFormat.Png); }
                    Type setupType = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(args[0]),"server","SftpServerSetup.exe")).GetType("ServerSetup");
                    using(var setup = (Form)Activator.CreateInstance(setupType,true)) { setup.ShowInTaskbar = false; setup.StartPosition = FormStartPosition.Manual; setup.Location = new Point(-3000,-3000); setup.Show(); Application.DoEvents(); using(var bmp = new Bitmap(setup.Width,setup.Height)) { setup.DrawToBitmap(bmp,setup.ClientRectangle); bmp.Save(Path.Combine(args[1],"server-setup.png"),System.Drawing.Imaging.ImageFormat.Png); } }
                }
            }
            Console.WriteLine("PASS: Windows form load/edit/save/reload/image copy/settings backup and separate encrypted SFTP profiles."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if(Directory.Exists(fixture)) Directory.Delete(fixture,true); }
    }
}
