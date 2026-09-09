using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

/// <summary>
/// P1-2 backup / migration (see docs/roadmap.md).
///
/// Backup: one archive of ~/.dsh (config, credentials, sessions, attachments)
///         excluding things that can be rebuilt (installed node_modules under
///         profiles\, the projection cache, locks/temp/logs).
/// Restore: verifies the manifest, refuses foreign archives, and MERGES by
///         default (existing files are kept) so a restore never destroys work.
///         The caller creates a "pre-restore" backup first.
/// </summary>
internal sealed class BackupManifest
{
    public string App = "DSH 控制中心";
    public string BuildId = "";
    public string CreatedAt = "";
    public string DshVersion = "";
    public int Files;
    public int ShellFiles;          // files from %LOCALAPPDATA%\DSH (shell data)
    public bool Credentials;        // true only when the user opted into including the API key
    public long Bytes;
    public string[] Excluded = new string[0];
}

internal sealed class BackupResult
{
    public string Path = "";
    public int Files;
    public long Bytes;
    public string Error = "";
    public bool Ok { get { return Error.Length == 0 && Path.Length > 0; } }
}

internal sealed class RestoreResult
{
    public int Added, Skipped, Failed;
    public string PreBackup = "";
    public string Error = "";
    public BackupManifest Manifest;
    public bool Ok { get { return Error.Length == 0; } }
}

/// <summary>One archive on disk (manual backup or pre-restore snapshot).</summary>
internal sealed class BackupItem
{
    public string Path = "";
    public string Name = "";
    public string Kind = "手动";      // 手动 / 恢复前
    public DateTime CreatedAt = DateTime.MinValue;
    public long Bytes;
    public int Files;
    public string BuildId = "";

    public string SizeText { get { return Backup.SizeText(Bytes); } }
}

internal static class Backup
{
    internal static string LastError = "";

    internal static string DshHome
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"); }
    }

    internal static string DefaultTargetDir
    {
        get { return Path.Combine(Program.LogDir, "backups"); }
    }

    private static readonly string[] ExcludedNames = { "storages\\session_projcache.json" };

    /// <summary>
    /// Credential files. NEVER archived by default: the API key is too sensitive
    /// to sit inside a zip that users mail around or drop in a cloud folder.
    /// </summary>
    internal static bool IsCredential(string rel)
    {
        if (String.IsNullOrEmpty(rel)) return false;
        string name = Path.GetFileName(rel).ToLowerInvariant();
        return name.StartsWith(".credentials", StringComparison.OrdinalIgnoreCase)
            || name == "credentials.json"
            || name == "credentials.yaml"
            || name == "credentials.yml";
    }

    /// <summary>True when a path (relative to ~/.dsh) should stay out of the archive.</summary>
    internal static bool IsExcluded(string rel)
    {
        if (String.IsNullOrEmpty(rel)) return true;
        string low = rel.ToLowerInvariant();
        foreach (string e in ExcludedNames)
            if (low.StartsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        // installed plugin dependencies: rebuildable by the plugin installer
        if (low.StartsWith("profiles\\") && low.IndexOf("\\node_modules\\", StringComparison.Ordinal) >= 0) return true;
        if (low.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return true;
        if (low.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
        if (low.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Creates a backup archive of ~/.dsh into <paramref name="targetDir"/>.</summary>
    internal static BackupResult Create(string targetDir)
    {
        return Create(targetDir, DshHome, "dsh-backup-", ShellDataDir, false);
    }

    internal static BackupResult Create(string targetDir, string baseDir, string prefix)
    {
        return Create(targetDir, baseDir, prefix, ShellDataDir, false);
    }

    internal static BackupResult Create(string targetDir, string baseDir, string prefix, string shellDir)
    {
        return Create(targetDir, baseDir, prefix, shellDir, false);
    }

    /// <summary>
    /// The shell's own data (%LOCALAPPDATA%\DSH): usage history, all-time totals,
    /// pricing, notification history, UI preferences, pet position. Without it a
    /// restore on a new machine would lose the cost/usage ledger even though the
    /// conversations came back.
    /// </summary>
    internal static string ShellDataDir
    {
        get { return Notifications.DataDir; }
    }

    /// <summary>
    /// Creates the archive. <paramref name="includeCredentials"/> is OFF by
    /// default: the API key is never written into a backup unless the user
    /// explicitly asks for it.
    /// </summary>
    internal static BackupResult Create(string targetDir, string baseDir, string prefix, string shellDir, bool includeCredentials)
    {
        var res = new BackupResult();
        LastError = "";
        try
        {
            if (!Directory.Exists(baseDir)) { res.Error = "找不到 " + baseDir; return res; }
            Directory.CreateDirectory(targetDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zip = Path.Combine(targetDir, prefix + stamp + ".zip");
            int dup = 1;
            while (File.Exists(zip))
            {
                zip = Path.Combine(targetDir, prefix + stamp + "-" + dup + ".zip");
                dup++;
            }
            var excluded = new List<string>();
            int files = 0, shellFiles = 0;
            long bytes = 0;

            using (FileStream fs = new FileStream(zip, FileMode.Create, FileAccess.Write))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // 1) the DSH home: sessions (the conversations) + config
                foreach (string f in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(baseDir.Length).TrimStart('\\');
                    if (IsExcluded(rel) || (!includeCredentials && IsCredential(rel)))
                    {
                        if (excluded.Count < 20) excluded.Add(rel.Replace('\\', '/'));
                        continue;
                    }
                    if (String.Equals(Path.GetFullPath(f), Path.GetFullPath(zip), StringComparison.OrdinalIgnoreCase)) continue;
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    ZipArchiveEntry entry = za.CreateEntry("dsh/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    using (Stream es = entry.Open())
                    using (FileStream input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        byte[] buf = new byte[128 * 1024];
                        int n;
                        while ((n = input.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, n);
                    }
                    files++;
                    bytes += len;
                }

                // 2) the shell's own data, kept under shell/ so a restore knows where it goes
                if (!String.IsNullOrEmpty(shellDir) && Directory.Exists(shellDir))
                {
                    foreach (string f in Directory.GetFiles(shellDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        string rel = Path.GetFileName(f);
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { }
                        ZipArchiveEntry entry = za.CreateEntry("shell/" + rel, CompressionLevel.Optimal);
                        using (Stream es = entry.Open())
                        using (FileStream input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            byte[] buf = new byte[64 * 1024];
                            int n;
                            while ((n = input.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, n);
                        }
                        shellFiles++;
                        bytes += len;
                    }
                }

                var mf = new BackupManifest();
                mf.BuildId = Program.BuildId;
                mf.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                mf.DshVersion = DshVersion();
                mf.Files = files;
                mf.ShellFiles = shellFiles;
                mf.Credentials = includeCredentials;
                mf.Bytes = bytes;
                mf.Excluded = excluded.ToArray();
                ZipArchiveEntry me = za.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (Stream es = me.Open())
                {
                    byte[] b = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(mf));
                    es.Write(b, 0, b.Length);
                }
            }

            res.Path = zip;
            res.Files = files + shellFiles;
            res.Bytes = bytes;
            return res;
        }
        catch (Exception ex)
        {
            res.Error = ex.Message;
            LastError = ex.Message;
            return res;
        }
    }

    /// <summary>Reads the manifest of a backup archive (null when it is not ours).</summary>
    internal static BackupManifest ReadManifest(string zip)
    {
        LastError = "";
        try
        {
            if (!File.Exists(zip)) { LastError = "文件不存在"; return null; }
            using (FileStream fs = File.OpenRead(zip))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry e = za.GetEntry("manifest.json");
                if (e == null) { LastError = "缺少 manifest.json，不是 DSH 备份包"; return null; }
                using (Stream s = e.Open())
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    var mf = new JavaScriptSerializer().Deserialize<BackupManifest>(r.ReadToEnd());
                    if (mf == null || !"DSH 控制中心".Equals(mf.App)) { LastError = "manifest.json 不是 DSH 备份"; return null; }
                    return mf;
                }
            }
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    internal static RestoreResult Restore(string zip, bool overwrite)
    {
        return Restore(zip, overwrite, DshHome, ShellDataDir);
    }

    /// <summary>Merges a backup into <paramref name="baseDir"/> (default: never overwrite).</summary>
    internal static RestoreResult Restore(string zip, bool overwrite, string baseDir)
    {
        return Restore(zip, overwrite, baseDir, ShellDataDir);
    }

    /// <summary>
    /// Same, with an explicit shell-data target (used by the smoke probe):
    /// `dsh/...` entries land in <paramref name="baseDir"/>, `shell/...` entries
    /// in <paramref name="shellBase"/>.
    /// </summary>
    internal static RestoreResult Restore(string zip, bool overwrite, string baseDir, string shellBase)
    {
        var res = new RestoreResult();
        LastError = "";
        BackupManifest mf = ReadManifest(zip);
        if (mf == null) { res.Error = LastError; return res; }
        res.Manifest = mf;
        try
        {
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            using (FileStream fs = File.OpenRead(zip))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in za.Entries)
                {
                    string name = entry.FullName;
                    if (name.EndsWith("/")) continue;
                    string root, rest;
                    if (name.StartsWith("dsh/", StringComparison.OrdinalIgnoreCase))
                    { root = baseDir; rest = name.Substring(4); }
                    else if (name.StartsWith("shell/", StringComparison.OrdinalIgnoreCase))
                    { root = shellBase; rest = name.Substring(6); }
                    else continue;
                    if (String.IsNullOrEmpty(root)) continue;
                    string rel = rest.Replace('/', '\\');
                    if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                    if (Path.IsPathRooted(rel)) continue;
                    string target = Path.Combine(root, rel);
                    if (File.Exists(target) && !overwrite) { res.Skipped++; continue; }
                    try
                    {
                        string dir = Path.GetDirectoryName(target);
                        if (!String.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        using (Stream es = entry.Open())
                        using (FileStream outFs = new FileStream(target, FileMode.Create, FileAccess.Write))
                        {
                            byte[] buf = new byte[128 * 1024];
                            int n;
                            while ((n = es.Read(buf, 0, buf.Length)) > 0) outFs.Write(buf, 0, n);
                        }
                        res.Added++;
                    }
                    catch { res.Failed++; }
                }
            }
        }
        catch (Exception ex) { res.Error = ex.Message; LastError = ex.Message; }
        return res;
    }

    // ---- history / retention ----------------------------------------------

    /// <summary>All archives in <paramref name="dir"/>, newest first.</summary>
    internal static List<BackupItem> List(string dir)
    {
        var list = new List<BackupItem>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (string f in Directory.GetFiles(dir, "*.zip"))
            {
                string name = Path.GetFileName(f);
                bool pre = name.StartsWith("pre-restore-", StringComparison.OrdinalIgnoreCase);
                if (!pre && !name.StartsWith("dsh-backup-", StringComparison.OrdinalIgnoreCase)) continue;
                var it = new BackupItem();
                it.Path = f;
                it.Name = name;
                it.Kind = pre ? "恢复前" : "手动";
                try { it.CreatedAt = File.GetLastWriteTime(f); } catch { }
                try { it.Bytes = new FileInfo(f).Length; } catch { }
                BackupManifest mf = ReadManifest(f);
                if (mf != null) { it.Files = mf.Files; it.BuildId = mf.BuildId; }
                list.Add(it);
            }
            list.Sort(delegate(BackupItem a, BackupItem b) { return b.CreatedAt.CompareTo(a.CreatedAt); });
        }
        catch { }
        return list;
    }

    /// <summary>Deletes the oldest archives beyond <paramref name="keep"/>; returns how many went away.</summary>
    internal static int Prune(string dir, int keep)
    {
        if (keep < 1) keep = 1;
        int removed = 0;
        List<BackupItem> all = List(dir);
        for (int i = keep; i < all.Count; i++)
        {
            if (Sessions.DeletePathToRecycleBin(all[i].Path)) removed++;
        }
        return removed;
    }

    internal static string DshVersion()
    {
        try
        {
            string f = Path.Combine(Program.RootDir, "node_modules", "@deepseek-ai", "dsh", "package.json");
            if (!File.Exists(f)) return "";
            Match m = Regex.Match(File.ReadAllText(f), "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return ""; }
    }

    internal static string SizeText(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? mb.ToString("F1", CultureInfo.InvariantCulture) + " MB" : (bytes / 1024) + " KB";
    }
}
