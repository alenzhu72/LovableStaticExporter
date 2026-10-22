using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class SftpTests {
    static void Check(bool value, string message) { if(!value) throw new Exception(message); }
    static void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
    static int Main(string[] args) {
        try {
            foreach(var p in new[] {"/", "C:\\", "/site/../other", "relative", "/site/*"}) Reject(() => SftpPublisher.NormalizeRoot(p),"Unsafe remote path accepted: " + p);
            Check(SftpPublisher.NormalizeRoot("C:\\inetpub\\site") == "/C:/inetpub/site","Windows path normalization failed");
            Check(!SftpPublisher.SafeRelative("../secret") && !SftpPublisher.SafeRelative("news/../../secret"),"Manifest traversal accepted");
            string root = Path.GetFullPath(args[0]); var ready = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"ready.json")));
            var project = Path.Combine(root,"project"); var dist = Path.Combine(project,"dist"); Directory.CreateDirectory(Path.Combine(dist,"news","images"));
            Environment.SetEnvironmentVariable("LSE_PROFILE_DIRECTORY",Path.Combine(root,"profiles"));
            var p1 = new SftpProfile {host = "127.0.0.1", port = Convert.ToInt32(ready["port"]),user = "fixture",remoteRoot = "/site",rememberSecret = true};
            SftpProfiles.Save(project,p1,"local-test-pass"); var stored = File.ReadAllText(SftpProfiles.ProfilePath(project)); Check(!stored.Contains("local-test-pass"),"Plaintext password was persisted"); Check(SftpProfiles.Secret(SftpProfiles.Load(project)) == "local-test-pass","DPAPI round trip failed");
            p1.rememberSecret = false; SftpProfiles.Save(project,p1,"local-test-pass"); Check(SftpProfiles.Secret(SftpProfiles.Load(project)) == "","Password was retained without consent");
            p1.fingerprint = SftpPublisher.Scan(p1); Console.WriteLine("Scanned loopback fixture fingerprint."); SftpPublisher.Test(p1,"local-test-pass");
            var correct = p1.fingerprint; p1.fingerprint = "ssh-rsa 2048 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; Reject(() => SftpPublisher.Test(p1,"local-test-pass"),"Wrong host key accepted"); p1.fingerprint = correct;
            Reject(() => SftpPublisher.Test(p1,"wrong-password"),"Wrong password accepted");
            File.WriteAllText(Path.Combine(dist,"index.html"),"home-v1"); File.WriteAllText(Path.Combine(dist,"news","index.html"),"list-v1"); File.WriteAllText(Path.Combine(dist,"news","old.html"),"old news"); File.WriteAllText(Path.Combine(dist,"news","images","old.png"),"image");
            File.WriteAllText(Path.Combine(root,"site","unrelated.txt"),"keep this");
            var lines = new List<string>(); SftpPublisher.Upload(project,p1,"local-test-pass",s => {lines.Add(s);Console.WriteLine(s);});
            Check(File.ReadAllText(Path.Combine(root,"site","index.html")) == "home-v1","Homepage upload failed"); Check(lines.FindIndex(s => s.EndsWith("news/old.html")) < lines.FindIndex(s => s.EndsWith("] index.html")),"Homepage was not uploaded last");
            File.Delete(Path.Combine(dist,"news","old.html")); File.Delete(Path.Combine(dist,"news","images","old.png")); File.WriteAllText(Path.Combine(dist,"news","new.html"),"new article"); File.WriteAllText(Path.Combine(dist,"index.html"),"home-v2");
            SftpPublisher.Upload(project,p1,"local-test-pass",Console.WriteLine); Check(File.Exists(Path.Combine(root,"site","news","old.html")),"Default publish deleted stale news");
            p1.cleanNews = true; SftpPublisher.Upload(project,p1,"local-test-pass",Console.WriteLine);
            Check(!File.Exists(Path.Combine(root,"site","news","old.html")) && !File.Exists(Path.Combine(root,"site","news","images","old.png")),"Managed news cleanup failed"); Check(File.Exists(Path.Combine(root,"site","unrelated.txt")),"Unrelated remote file was deleted");
            p1.useKey = true; p1.privateKey = Path.ChangeExtension((string)ready["privateKey"],"ppk"); SftpPublisher.Test(p1,"local-key-pass"); Console.WriteLine("PASS: password/key authentication, host key rejection, encrypted profiles, nested uploads, homepage-last ordering, repeat publish and scoped cleanup.");
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
