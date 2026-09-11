using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

/// <summary>
/// Persistent notification history (ADR-0009). One JSON object per line in
/// %LOCALAPPDATA%\DSH\notifications.jsonl.
///
/// Read state is PER NOTICE: each line carries an id (and an optional refId for
/// the thing it is about, e.g. an approval), and the read marker is a small JSON
/// file holding "everything at/before this moment" plus the ids read
/// individually. That is what lets an answered approval clear its own
/// notification (and the tray red dot) without wiping unrelated unread alerts.
/// </summary>
internal sealed class Notice
{
    public string Id = "";
    public string RefId = "";       // approvalId the notice is about
    public string SessionId = "";   // session the notice is about
    public DateTime Time = DateTime.MinValue;
    public string Title = "";
    public string Sub = "";
    public string Kind = "";        // warn / info / success
    public bool Unread;
}

internal static class Notifications
{
    internal const int Max = 500;

    internal static string DataDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSH"); }
    }

    /// <summary>Test seam: when set, the store uses this file instead of the real one.</summary>
    internal static string OverrideFile = "";

    internal static string FileName
    {
        get
        {
            return OverrideFile.Length > 0 ? OverrideFile : Path.Combine(DataDir, "notifications.jsonl");
        }
    }

    private static string ReadFile
    {
        get
        {
            return OverrideFile.Length > 0 ? OverrideFile + ".read.json" : Path.Combine(DataDir, "notifications-read.json");
        }
    }

    /// <summary>Old single-timestamp marker, migrated on first read.</summary>
    private static string LegacyReadFile
    {
        get
        {
            return OverrideFile.Length > 0 ? OverrideFile + ".read" : Path.Combine(DataDir, "notifications-read.txt");
        }
    }

    private sealed class ReadState
    {
        /// <summary>Ids of the notices the user has seen. Id-only on purpose.</summary>
        public readonly List<string> Ids = new List<string>();
    }

    // ---- write -------------------------------------------------------------

    /// <summary>Appends one notice; returns its id ("" when it could not be stored).</summary>
    internal static string Add(string title, string sub, string kind)
    {
        return Add(title, sub, kind, "");
    }

    internal static string Add(string title, string sub, string kind, string refId)
    {
        return Add(title, sub, kind, refId, "");
    }

    /// <summary>Appends one notice; returns its id ("" when it could not be stored).</summary>
    internal static string Add(string title, string sub, string kind, string refId, string sessionId)
    {
        string id = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(DataDir);
            var o = new Dictionary<string, object>();
            o["id"] = id;
            o["t"] = ToUnixMs(DateTime.Now);
            o["title"] = title == null ? "" : title;
            o["sub"] = sub == null ? "" : sub;
            o["kind"] = kind == null ? "" : kind;
            if (!String.IsNullOrEmpty(refId)) o["ref"] = refId;
            if (!String.IsNullOrEmpty(sessionId)) o["sid"] = sessionId;
            File.AppendAllText(FileName, new JavaScriptSerializer().Serialize(o) + Environment.NewLine, new UTF8Encoding(false));
            Trim();
            return id;
        }
        catch { return ""; }
    }

    /// <summary>Keeps the file at <see cref="Max"/> lines (only runs when it grows past it).</summary>
    private static void Trim()
    {
        try
        {
            string[] lines = File.ReadAllLines(FileName);
            if (lines.Length <= Max + 50) return;
            var keep = new List<string>();
            for (int i = lines.Length - Max; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0) keep.Add(lines[i]);
            File.WriteAllLines(FileName, keep.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    // ---- read --------------------------------------------------------------

    /// <summary>Newest first; the unread flag comes from the per-notice read state.</summary>
    internal static List<Notice> Load()
    {
        var list = new List<Notice>();
        try
        {
            if (!File.Exists(FileName)) return list;
            ReadState st = LoadRead();
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    var n = new Notice();
                    n.Time = FromUnixMs(ToL(d, "t"));
                    n.Id = Str(d, "id");
                    if (n.Id.Length == 0) n.Id = "t" + n.Time.Ticks;    // legacy line
                    n.RefId = Str(d, "ref");
                    n.SessionId = Str(d, "sid");
                    n.Title = Str(d, "title");
                    n.Sub = Str(d, "sub");
                    n.Kind = Str(d, "kind");
                    n.Unread = !IsRead(n, st);
                    list.Add(n);
                }
                catch { }
            }
        }
        catch { }
        list.Reverse();
        return list;
    }

    private static int _cachedUnread = -1;
    private static DateTime _cachedStamp, _cachedReadStamp;
    private static long _cachedLen = -1, _cachedReadLen = -1;

    internal static int Unread()
    {
        try
        {
            if (!File.Exists(FileName)) { _cachedUnread = 0; return 0; }
            // The tray tooltip asks for this every second; re-parsing the whole
            // history each time is pointless. Cache keyed on BOTH files: the
            // history (new notices) and the read set (mark-read writes).
            var fi = new FileInfo(FileName);
            var rf = new FileInfo(ReadFile);
            long rlen = rf.Exists ? rf.Length : -1;
            DateTime rstamp = rf.Exists ? rf.LastWriteTimeUtc : DateTime.MinValue;
            if (_cachedUnread >= 0 && fi.LastWriteTimeUtc == _cachedStamp && fi.Length == _cachedLen
                && rstamp == _cachedReadStamp && rlen == _cachedReadLen)
                return _cachedUnread;
            ReadState st = LoadRead();
            var ser = new JavaScriptSerializer();
            int n = 0;
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    var probe = new Notice();
                    probe.Time = FromUnixMs(ToL(d, "t"));
                    probe.Id = Str(d, "id");
                    if (probe.Id.Length == 0) probe.Id = "t" + probe.Time.Ticks;
                    if (!IsRead(probe, st)) n++;
                }
                catch { }
            }
            _cachedUnread = n;
            _cachedStamp = fi.LastWriteTimeUtc;
            _cachedLen = fi.Length;
            _cachedReadStamp = rstamp;
            _cachedReadLen = rlen;
            return n;
        }
        catch { return 0; }
    }

    /// <summary>
    /// A notice is read iff its id is in the read set. Deliberately id-based:
    /// a timestamp watermark is unreliable because Windows' clock granularity
    /// (~15 ms) can put a brand-new notice in the same tick as "mark all read"
    /// and swallow it.
    /// </summary>
    private static bool IsRead(Notice n, ReadState st)
    {
        return st.Ids.Contains(n.Id);
    }

    /// <summary>Marks one notice read (by id).</summary>
    internal static void MarkRead(string id)
    {
        if (String.IsNullOrEmpty(id)) return;
        try
        {
            ReadState st = LoadRead();
            if (!st.Ids.Contains(id)) st.Ids.Add(id);
            SaveRead(st);
        }
        catch { }
    }

    /// <summary>
    /// Marks every notice about <paramref name="refId"/> read - used when an
    /// approval is answered (in the shell or in the web UI), so its notification
    /// and the tray red dot clear themselves.
    /// </summary>
    internal static int MarkReadByRef(string refId)
    {
        if (String.IsNullOrEmpty(refId)) return 0;
        int marked = 0;
        try
        {
            if (!File.Exists(FileName)) return 0;
            ReadState st = LoadRead();
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    if (!String.Equals(Str(d, "ref"), refId, StringComparison.Ordinal)) continue;
                    string id = Str(d, "id");
                    if (id.Length == 0) id = "t" + FromUnixMs(ToL(d, "t")).Ticks;
                    if (!st.Ids.Contains(id)) { st.Ids.Add(id); marked++; }
                }
                catch { }
            }
            if (marked > 0) SaveRead(st);
        }
        catch { }
        return marked;
    }

    /// <summary>
    /// Marks every notice about a session read - used when the user goes back
    /// to that conversation (a new user/message arrives), which means they have
    /// seen whatever we told them about the previous turn.
    /// </summary>
    internal static int MarkReadBySession(string sessionId)
    {
        if (String.IsNullOrEmpty(sessionId)) return 0;
        int marked = 0;
        try
        {
            if (!File.Exists(FileName)) return 0;
            ReadState st = LoadRead();
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    if (!String.Equals(Str(d, "sid"), sessionId, StringComparison.Ordinal)) continue;
                    string id = Str(d, "id");
                    if (id.Length == 0) id = "t" + FromUnixMs(ToL(d, "t")).Ticks;
                    if (!st.Ids.Contains(id)) { st.Ids.Add(id); marked++; }
                }
                catch { }
            }
            if (marked > 0) SaveRead(st);
        }
        catch { }
        return marked;
    }

    /// <summary>Marks every notice currently in the history read (by id).</summary>
    internal static void MarkAllRead()
    {
        try
        {
            ReadState st = LoadRead();
            foreach (string id in AllIds()) if (!st.Ids.Contains(id)) st.Ids.Add(id);
            SaveRead(st);
        }
        catch { }
    }

    /// <summary>Every notice id in the history file, in file order.</summary>
    private static List<string> AllIds()
    {
        var ids = new List<string>();
        try
        {
            if (!File.Exists(FileName)) return ids;
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    string id = Str(d, "id");
                    if (id.Length == 0) id = "t" + FromUnixMs(ToL(d, "t")).Ticks;
                    ids.Add(id);
                }
                catch { }
            }
        }
        catch { }
        return ids;
    }

    /// <summary>
    /// One-off migration: notices written before notices carried a refId cannot
    /// be linked to their approval any more, so they would sit unread forever
    /// (and keep the tray red dot lit). Mark those read once at startup.
    /// </summary>
    internal static int MarkLegacyApprovalRead()
    {
        int marked = 0;
        try
        {
            if (!File.Exists(FileName)) return 0;
            ReadState st = LoadRead();
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    if (Str(d, "ref").Length > 0) continue;                     // already linked
                    if (!Str(d, "title").StartsWith("等待审批", StringComparison.Ordinal)) continue;
                    string id = Str(d, "id");
                    if (id.Length == 0) id = "t" + FromUnixMs(ToL(d, "t")).Ticks;
                    if (!st.Ids.Contains(id)) { st.Ids.Add(id); marked++; }
                }
                catch { }
            }
            if (marked > 0) SaveRead(st);
        }
        catch { }
        return marked;
    }

    internal static void Clear()
    {
        try { if (File.Exists(FileName)) File.Delete(FileName); } catch { }
        MarkAllRead();
    }

    // ---- read-state file ---------------------------------------------------

    private static ReadState LoadRead()
    {
        var st = new ReadState();
        try
        {
            if (File.Exists(ReadFile))
            {
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ReadFile)) as Dictionary<string, object>;
                if (d != null)
                {
                    object ids;
                    if (d.TryGetValue("ids", out ids) && ids != null)
                    {
                        // JavaScriptSerializer hands arrays back either as
                        // ArrayList or as object[] depending on the runtime.
                        var arr = ids as System.Collections.ArrayList;
                        if (arr != null)
                        {
                            foreach (object o in arr) if (o != null) st.Ids.Add(o.ToString());
                        }
                        else
                        {
                            var boxed = ids as object[];
                            if (boxed != null) foreach (object o in boxed) if (o != null) st.Ids.Add(o.ToString());
                        }
                    }
                    // A pre-id build stored a timestamp watermark; fold it into
                    // the id set once so nothing it covered comes back unread.
                    object v;
                    if (d.TryGetValue("all", out v) && v != null)
                    {
                        long ticks = 0;
                        try { ticks = Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { }
                        if (ticks > 0) SeedFromWatermark(st, new DateTime(ticks));
                    }
                    return st;
                }
            }
            // migrate the old single-timestamp marker file
            if (File.Exists(LegacyReadFile))
            {
                long ticks;
                if (long.TryParse(File.ReadAllText(LegacyReadFile).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks > 0)
                {
                    SeedFromWatermark(st, new DateTime(ticks));
                    SaveRead(st);
                }
            }
        }
        catch { }
        return st;
    }

    /// <summary>Adds the ids of every notice at/before a legacy timestamp watermark.</summary>
    private static void SeedFromWatermark(ReadState st, DateTime cut)
    {
        try
        {
            if (!File.Exists(FileName)) return;
            var ser = new JavaScriptSerializer();
            foreach (string raw in File.ReadAllLines(FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (d == null) continue;
                    DateTime t = FromUnixMs(ToL(d, "t"));
                    if (t == DateTime.MinValue || t > cut) continue;
                    string id = Str(d, "id");
                    if (id.Length == 0) id = "t" + t.Ticks;
                    if (!st.Ids.Contains(id)) st.Ids.Add(id);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void SaveRead(ReadState st)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (st.Ids.Count > 3000) st.Ids.RemoveRange(0, st.Ids.Count - 3000);
            var o = new Dictionary<string, object>();
            o["ids"] = st.Ids;
            File.WriteAllText(ReadFile, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
        }
        catch { }
    }

    // ---- helpers -----------------------------------------------------------

    private static long ToUnixMs(DateTime t)
    {
        return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    private static DateTime FromUnixMs(long ms)
    {
        try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }
        catch { return DateTime.MinValue; }
    }

    private static long ToL(Dictionary<string, object> d, string key)
    {
        object v;
        if (!d.TryGetValue(key, out v) || v == null) return 0;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        if (!d.TryGetValue(key, out v) || v == null) return "";
        return v.ToString();
    }
}
