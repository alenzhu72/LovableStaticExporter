using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using WinSCP;

public class SftpProfile {
    public string host { get; set; } public int port { get; set; } public string user { get; set; }
    public string remoteRoot { get; set; } public string fingerprint { get; set; } public string privateKey { get; set; }
    public bool useKey { get; set; } public bool rememberSecret { get; set; } public string protectedSecret { get; set; }
    public bool cleanNews { get; set; }
    public SftpProfile() { port = 22; }
}
public static class SftpProfiles {
    static JavaScriptSerializer Json() { return new JavaScriptSerializer(); }
    public static string ProfilePath(string project) {
        string hash; using(var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(project).TrimEnd('\\').ToLowerInvariant()))).Replace("-", "");
        var overrideDir = Environment.GetEnvironmentVariable("LSE_PROFILE_DIRECTORY");
        return !String.IsNullOrEmpty(overrideDir) ? Path.Combine(overrideDir,hash + ".json") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LovableStaticExporter","connections",hash + ".json");
    }
    public static SftpProfile Load(string project) { var file = ProfilePath(project); return File.Exists(file) ? Json().Deserialize<SftpProfile>(File.ReadAllText(file,Encoding.UTF8)) ?? new SftpProfile() : new SftpProfile(); }
    public static string Secret(SftpProfile p) {
        if(!p.rememberSecret || String.IsNullOrEmpty(p.protectedSecret)) return "";
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p.protectedSecret),null,DataProtectionScope.CurrentUser));
    }
    public static void Save(string project, SftpProfile p, string secret) {
        p.protectedSecret = p.rememberSecret && !String.IsNullOrEmpty(secret) ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret),null,DataProtectionScope.CurrentUser)) : null;
        var file = ProfilePath(project); Directory.CreateDirectory(Path.GetDirectoryName(file));
        var temp = file + ".tmp"; File.WriteAllText(temp,Json().Serialize(p),new UTF8Encoding(false)); File.Copy(temp,file,true); File.Delete(temp);
    }
}
public class SftpManifest { public string format { get; set; } public List<string> files { get; set; } }
public static class SftpPublisher {
    const string ManifestName = ".lovable-export-manifest.json";
    public static string NormalizeRoot(string raw) {
        var value = (raw ?? "").Trim().Replace('\\','/');
        if(Regex.IsMatch(value,"^[A-Za-z]:/")) value = "/" + value;
        if(!value.StartsWith("/") || value.Contains("//") || value.Any(Char.IsControl) || value.IndexOfAny(new char[] {'*','?','[',']'}) >= 0) throw new Exception("远程目录必须是绝对 SFTP 路径，例如 /C:/inetpub/britecard。");
        var parts = value.Split('/'); if(parts.Any(p => p == "." || p == "..")) throw new Exception("远程目录不能包含 . 或 ..。");
        value = value.TrimEnd('/'); if(value.Length == 0 || Regex.IsMatch(value,"^/[A-Za-z]:$")) throw new Exception("请选择具体的网站目录，不能上传到服务器根目录或磁盘根目录。");
        return value;
    }
    public static bool SafeRelative(string value) {
        if(String.IsNullOrEmpty(value) || value.StartsWith("/") || value.Contains("\\") || value.Any(Char.IsControl) || value.IndexOfAny(new char[] {':','*','?','[',']'}) >= 0) return false;
        return value.Split('/').All(p => p.Length > 0 && p != "." && p != "..");
    }
    static bool ManagedNews(string value) { return SafeRelative(value) && (Regex.IsMatch(value,"^news/[a-z0-9-]+\\.html$") || Regex.IsMatch(value,"^news/images/[a-z0-9-]+\\.(png|jpg|gif|webp)$")); }
    static void ValidateHost(SftpProfile p) {
        if(String.IsNullOrWhiteSpace(p.host) || p.host.Any(Char.IsWhiteSpace) || p.host.Any(Char.IsControl) || p.host.IndexOfAny(new char[] {'/','\\','@','?','#'}) >= 0) throw new Exception("请填写服务器 IP 或主机名，不包含 sftp:// 或路径。");
        if(p.port < 1 || p.port > 65535) throw new Exception("端口必须为 1–65535。");
    }
    public static void Validate(SftpProfile p) {
        ValidateHost(p); NormalizeRoot(p.remoteRoot);
        if(String.IsNullOrWhiteSpace(p.user)) throw new Exception("请填写 SFTP 用户名。");
        if(String.IsNullOrWhiteSpace(p.fingerprint) || p.fingerprint.Contains("*") || p.fingerprint.Contains(";")) throw new Exception("请获取并核对服务器 SHA-256 指纹后再连接。");
        if(p.useKey) {
            if(!File.Exists(p.privateKey)) throw new Exception("私钥文件不存在。请选择 PuTTY .ppk 私钥。");
            using(var reader = new StreamReader(p.privateKey)) if(!(reader.ReadLine() ?? "").StartsWith("PuTTY-User-Key-File-")) throw new Exception("私钥需要 PuTTY .ppk 格式；OpenSSH 私钥请先使用 PuTTYgen 转换。");
        }
    }
    static SessionOptions Options(SftpProfile p, string secret) {
        return new SessionOptions { Protocol = Protocol.Sftp, HostName = p.host.Trim(), PortNumber = p.port, UserName = p.user, SshHostKeyFingerprint = p.fingerprint, Password = p.useKey ? null : secret, SshPrivateKeyPath = p.useKey ? p.privateKey : null, PrivateKeyPassphrase = p.useKey ? secret : null, Timeout = TimeSpan.FromSeconds(25) };
    }
    static Session NewSession() {
        var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WinSCP.exe"); if(!File.Exists(exe)) throw new Exception("缺少内置 WinSCP.exe，请完整解压新版工具。");
        return new Session { ExecutablePath = exe, Timeout = TimeSpan.FromSeconds(60) };
    }
    public static string Scan(SftpProfile p) { ValidateHost(p); using(var session = NewSession()) return session.ScanFingerprint(new SessionOptions { Protocol = Protocol.Sftp, HostName = p.host.Trim(), PortNumber = p.port, Timeout = TimeSpan.FromSeconds(20) },"SHA-256"); }
    public static void Test(SftpProfile p, string secret) {
        Validate(p); using(var session = NewSession()) { session.Open(Options(p,secret)); var target = session.GetFileInfo(NormalizeRoot(p.remoteRoot)); if(!target.IsDirectory) throw new Exception("远程目标不是目录。"); }
    }
    static IEnumerable<string> Walk(string dir) {
        foreach(var item in Directory.GetFileSystemEntries(dir)) {
            if((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new Exception("dist 中不能包含符号链接或目录联接：" + item);
            if(Directory.Exists(item)) { foreach(var f in Walk(item)) yield return f; } else yield return item;
        }
    }
    static int Order(string relative) { if(relative == "index.html") return 9; if(Regex.IsMatch(relative,"^news/(index|page-[0-9]+)\\.html$")) return 7; if(relative.EndsWith(".html",StringComparison.OrdinalIgnoreCase)) return 5; return 1; }
    static void EnsureDir(Session session, string root, string relative, HashSet<string> known) {
        string next = root;
        var parts = relative.Split('/'); for(int i = 0; i < parts.Length - 1; i++) { next += "/" + parts[i]; if(known.Contains(next)) continue; if(session.FileExists(next)) { if(!session.GetFileInfo(next).IsDirectory) throw new Exception("远程路径不是普通目录：" + next); } else session.CreateDirectory(next); known.Add(next); }
    }
    public static void Upload(string project, SftpProfile p, string secret, Action<string> log) {
        Validate(p); var root = NormalizeRoot(p.remoteRoot); var dist = Path.GetFullPath(Path.Combine(project,"dist"));
        if(!Directory.Exists(dist) || !File.Exists(Path.Combine(dist,"index.html"))) throw new Exception("没有可上传的 dist/index.html，请先导出。");
        if((File.GetAttributes(dist) & FileAttributes.ReparsePoint) != 0) throw new Exception("dist 不能是符号链接。");
        var local = Walk(dist).ToList(); var relative = local.ToDictionary(f => f, f => f.Substring(dist.Length + 1).Replace('\\','/'));
        if(relative.Values.Any(f => !SafeRelative(f) || f == ManifestName)) throw new Exception("dist 包含不支持的文件路径。");
        local = local.OrderBy(f => Order(relative[f])).ThenBy(f => relative[f],StringComparer.Ordinal).ToList();
        var current = new HashSet<string>(relative.Values,StringComparer.Ordinal); var previous = new List<string>();
        using(var session = NewSession()) {
            log("连接 " + p.host + ":" + p.port + "，目标 " + root); session.Open(Options(p,secret));
            if(!session.GetFileInfo(root).IsDirectory) throw new Exception("远程目标不是普通目录，请先创建并授权。");
            string manifestPath = root + "/" + ManifestName;
            if(session.FileExists(manifestPath)) {
                var info = session.GetFileInfo(manifestPath); if(info.Length > 2 * 1024 * 1024 || info.FileType == 'L') throw new Exception("远程部署清单无效。");
                using(var stream = session.GetFile(manifestPath)) using(var reader = new StreamReader(stream,Encoding.UTF8)) {
                    var manifest = new JavaScriptSerializer().Deserialize<SftpManifest>(reader.ReadToEnd());
                    if(manifest == null || manifest.format != "LovableStaticExporter/1" || manifest.files == null || manifest.files.Any(f => !SafeRelative(f))) throw new Exception("远程部署清单格式无效，请检查目标目录。");
                    previous = manifest.files;
                }
            }
            var transfer = new TransferOptions { TransferMode = TransferMode.Binary, PreserveTimestamp = false, FilePermissions = null, OverwriteMode = OverwriteMode.Overwrite };
            transfer.ResumeSupport.State = TransferResumeSupportState.On;
            var known = new HashSet<string>(StringComparer.Ordinal) { root };
            int uploaded = 0;
            foreach(var f in local) {
                string rel = relative[f]; EnsureDir(session,root,rel,known);
                string target = root + "/" + rel; if(session.FileExists(target) && session.GetFileInfo(target).FileType == 'L') throw new Exception("拒绝覆盖远程符号链接：" + rel);
                var folder = target.Substring(0,target.LastIndexOf('/'));
                session.PutFileToDirectory(f,folder,false,transfer); log("[" + (++uploaded) + "/" + local.Count + "] " + rel);
            }
            var retained = new HashSet<string>(current,StringComparer.Ordinal);
            foreach(var old in previous.Where(f => ManagedNews(f) && !current.Contains(f))) {
                if(!p.cleanNews) { retained.Add(old); continue; }
                string target = root + "/" + old; EnsureDir(session,root,old,known);
                if(session.FileExists(target)) { var info = session.GetFileInfo(target); if(info.IsDirectory || info.FileType == 'L') throw new Exception("拒绝删除非普通新闻文件：" + old); session.RemoveFile(target); log("已移除旧新闻：" + old); }
            }
            // Never mirror-delete arbitrary server files, application data, or old JS assets.
            var temp = Path.Combine(Path.GetTempPath(),"lse-publish-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try { var manifest = new SftpManifest { format = "LovableStaticExporter/1", files = retained.OrderBy(f => f,StringComparer.Ordinal).ToList() }; var file = Path.Combine(temp,ManifestName); File.WriteAllText(file,new JavaScriptSerializer().Serialize(manifest),new UTF8Encoding(false)); session.PutFileToDirectory(file,root,false,transfer); }
            finally { Directory.Delete(temp,true); }
            log("上传完成，共 " + uploaded + " 个文件。首页最后上传。");
        }
    }
}
