using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

/// <summary>
/// P1-1 session library (see docs/roadmap.md).
///
/// Data sources
///   * ~/.dsh/storages/session_projcache.json  -> titles, turns, tokens, cost
///   * ~/.dsh/sessions/&lt;workspace&gt;/&lt;session-id&gt;/session.jsonl.zstd
///
/// Operations: list (merged view of cache + disk), export to a zip that keeps
/// the original relative layout (so it can be restored verbatim), import such
/// a zip, and delete a session folder to the Recycle Bin.
/// </summary>
internal sealed class SessionEntry
{
    public string Id = "";              // session-<uuid> (projcache key == folder name)
    public string Title = "";
    public string Folder = "";          // full path of the session folder ("" when cache-only)
    public bool OnDisk;
    public long Bytes;
    public DateTime LastPromptAt = DateTime.MinValue;
    public DateTime CreatedAt = DateTime.MinValue;
    public int Turns;
    public int Steps;
    public long In, Out, CacheRead, CacheWrite;
    public double Cost;
    public string CostText = "";
    public string PermissionPreset = "";
    public string Approval = "";
    public bool PlanActive;
    public int TodoCount;

    public long Tokens { get { return In + Out + CacheRead + CacheWrite; } }

    /// <summary>Last activity, falling back to creation time.</summary>
    public DateTime When { get { return LastPromptAt != DateTime.MinValue ? LastPromptAt : CreatedAt; } }

    public string SizeText
    {
        get
        {
            if (!OnDisk) return "缓存中无文件";
            double mb = Bytes / 1024.0 / 1024.0;
            return mb >= 1 ? mb.ToString("F1", CultureInfo.InvariantCulture) + " MB"
                           : Math.Max(1, Bytes / 1024) + " KB";
        }
    }
}

internal static class Sessions
{
    internal static string Root
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "sessions");
        }
    }

    private static string DshHome
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"); }
    }

    /// <summary>Merged view: every session known to the cache or present on disk.</summary>
    internal static List<SessionEntry> List(Pricing pricing)
    {
        var byId = new Dictionary<string, SessionEntry>(StringComparer.OrdinalIgnoreCase);

        // 1) projection cache (titles / usage)
        List<SessionUsage> cached = new List<SessionUsage>();
        try { cached = Usage.ReadSessions(); } catch { }
        try { if (pricing != null) Usage.ComputeCosts(pricing, cached); } catch { }
        foreach (SessionUsage s in cached)
        {
            var e = new SessionEntry();
            e.Id = s.Id;
            e.Title = s.Title;
            e.Turns = s.Turns;
            e.Steps = s.Steps;
            e.In = s.UncachedInput; e.Out = s.Output; e.CacheRead = s.CacheRead; e.CacheWrite = s.CacheWrite;
            e.Cost = s.Cost;
            e.LastPromptAt = s.LastPromptAt;
            e.CreatedAt = s.CreatedAt;
            e.PermissionPreset = s.PermissionPreset;
            e.Approval = s.Approval;
            e.PlanActive = s.PlanActive;
            e.TodoCount = s.TodoCount;
            byId[e.Id] = e;
        }

        // 2) disk: ~/.dsh/sessions/<workspace>/<session-id>/
        try
        {
            if (Directory.Exists(Root))
            {
                foreach (string ws in Directory.GetDirectories(Root))
                {
                    foreach (string dir in Directory.GetDirectories(ws))
                    {
                        string key = Path.GetFileName(dir);
                        SessionEntry e;
                        if (!byId.TryGetValue(key, out e))
                        {
                            e = new SessionEntry();
                            e.Id = key;
                            try { e.CreatedAt = Directory.GetCreationTime(dir); } catch { }
                            byId[key] = e;
                        }
                        e.OnDisk = true;
                        e.Folder = dir;
                        e.Bytes = FolderBytes(dir);
                    }
                }
            }
        }
        catch { }

        var list = new List<SessionEntry>(byId.Values);
        if (pricing != null)
            foreach (SessionEntry e in list) e.CostText = Usage.Money(e.Cost, pricing);
        list.Sort(delegate(SessionEntry a, SessionEntry b)
        {
            int c = b.When.CompareTo(a.When);
            if (c != 0) return c;
            return String.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static long FolderBytes(string dir)
    {
        long n = 0;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { n += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return n;
    }

    /// <summary>
    /// Exports the session folder as a zip that mirrors the layout under
    /// ~/.dsh (sessions/&lt;workspace&gt;/&lt;id&gt;/...), so
    /// <see cref="Import"/> can restore it byte-for-byte.
    /// </summary>
    internal static string Export(SessionEntry e, string zipPath)
    {
        LastError = "";
        if (e == null || !e.OnDisk || String.IsNullOrEmpty(e.Folder) || !Directory.Exists(e.Folder))
        {
            LastError = "该会话在磁盘上没有文件";
            return null;
        }
        try
        {
            string ws = Path.GetFileName(Path.GetDirectoryName(e.Folder));
            string prefix = "sessions/" + ws + "/" + Path.GetFileName(e.Folder) + "/";
            using (FileStream fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string f in Directory.GetFiles(e.Folder, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(e.Folder.Length).TrimStart('\\').Replace('\\', '/');
                    ZipArchiveEntry entry = za.CreateEntry(prefix + rel, CompressionLevel.Optimal);
                    using (Stream es = entry.Open())
                    using (FileStream input = File.OpenRead(f))
                    {
                        byte[] buf = new byte[128 * 1024];
                        int n;
                        while ((n = input.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, n);
                    }
                }
            }
            return zipPath;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>Restores sessions from an exported zip; returns the file count.</summary>
    internal static int Import(string zipPath)
    {
        return Import(zipPath, DshHome);
    }

    /// <summary>Same, but into an explicit ~/.dsh equivalent (used by the smoke test).</summary>
    internal static int Import(string zipPath, string baseDir)
    {
        LastError = "";
        int count = 0;
        try
        {
            using (FileStream fs = File.OpenRead(zipPath))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in za.Entries)
                {
                    string rel = entry.FullName.Replace('/', '\\');
                    if (rel.EndsWith("\\")) continue;
                    if (!rel.StartsWith("sessions\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                    if (Path.IsPathRooted(rel)) continue;
                    string target = Path.Combine(baseDir, rel);
                    string dir = Path.GetDirectoryName(target);
                    if (!String.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    using (Stream es = entry.Open())
                    using (FileStream outFs = new FileStream(target, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[128 * 1024];
                        int n;
                        while ((n = es.Read(buf, 0, buf.Length)) > 0) outFs.Write(buf, 0, n);
                    }
                    count++;
                }
            }
        }
        catch (Exception ex) { LastError = ex.Message; return -1; }
        if (count == 0) LastError = "压缩包里没有 sessions\\... 条目";
        return count;
    }

    internal static string LastError = "";

    // ---- delete (Recycle Bin) ---------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>Moves the session folder to the Recycle Bin (undo is possible).</summary>
    internal static bool DeleteToRecycleBin(SessionEntry e)
    {
        LastError = "";
        if (e == null || !e.OnDisk || String.IsNullOrEmpty(e.Folder) || !Directory.Exists(e.Folder))
        {
            LastError = "该会话在磁盘上没有文件";
            return false;
        }
        return DeletePathToRecycleBin(e.Folder);
    }

    /// <summary>Generic Recycle Bin delete for a file or folder (used for backups too).</summary>
    internal static bool DeletePathToRecycleBin(string path)
    {
        LastError = "";
        try
        {
            if (String.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                LastError = "路径不存在";
                return false;
            }
            var op = new SHFILEOPSTRUCT();
            op.wFunc = FO_DELETE;
            op.pFrom = path + "\0\0";            // double-null terminated list
            op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT);
            int rc = SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted)
            {
                LastError = "SHFileOperation 返回 " + rc;
                return false;
            }
            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    /// <summary>
    /// Drops the session row from the projection cache so the web session list
    /// no longer shows it. Only safe while the backend is stopped (otherwise
    /// the backend can write the row back from memory).
    /// </summary>
    internal static bool PruneCache(string id)
    {
        return PruneCacheFile(Usage.ProjCacheFile, id);
    }

    /// <summary>Same, on an explicit cache file (used by the smoke test).</summary>
    internal static bool PruneCacheFile(string file, string id)
    {
        LastError = "";
        try
        {
            if (!File.Exists(file) || String.IsNullOrEmpty(id)) return false;
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) return false;
            object tablesObj;
            if (!root.TryGetValue("tables", out tablesObj)) return false;
            var tables = tablesObj as Dictionary<string, object>;
            if (tables == null) return false;
            object sessionsObj;
            if (!tables.TryGetValue("sessions", out sessionsObj)) return false;
            var sessions = sessionsObj as Dictionary<string, object>;
            if (sessions == null) return false;
            if (!sessions.Remove(id)) return false;
            string json = ser.Serialize(root);
            try { ser.DeserializeObject(json); }          // never write something we cannot read back
            catch (Exception ex) { LastError = "序列化结果无效: " + ex.Message; return false; }
            File.WriteAllText(file, json, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }
}
