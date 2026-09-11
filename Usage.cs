// ============================================================================
//  Usage.cs - real token usage + cost (ADR-0007)
//
//  Data source: ~/.dsh/storages/session_projcache.json  (plain JSON, written by
//  the backend; no zstd, no backend changes).
//  Pricing:     %LOCALAPPDATA%\DSH\pricing.json  (seeded on first run; peak /
//               off-peak per model - DeepSeek moved to tiered pricing on
//               2026-08-17: weekday peak ~2x, weekends always off-peak).
//  Time series: %LOCALAPPDATA%\DSH\usage-history.jsonl  (aggregated deltas per
//  snapshot interval, so counters that reset per session never corrupt the sum).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class SessionUsage
{
    public string Id = "";
    public string Title = "";
    public long UncachedInput;
    public long Output;
    public long CacheRead;
    public long CacheWrite;
    public long SurfaceTokens;
    public long ContextWindow;
    public int Turns;
    public int Steps;
    public DateTime LastPromptAt = DateTime.MinValue;
    public string PermissionPreset = "";
    public string Approval = "";
    public bool PlanActive;                  // plan.active
    public bool OpenStep;                    // sessionStats.openStep != null => a step is running NOW
    public int TodoCount;                    // todos[] length
    public int PendingCalls;                 // in-flight tool calls (approval heuristic)
    public DateTime CreatedAt = DateTime.MinValue;   // identity.createdAt
    public string Cwd = "";                          // identity.cwd = the session's workspace (project)
    public double Cost;          // filled by Usage.ComputeCosts
}

/// <summary>All-time totals plus when tracking started (usage-state.json).</summary>
internal sealed class Lifetime
{
    public DateTime FirstSeen = DateTime.Now;
    public long In, Out, CacheRead, CacheWrite;
    public double Cost;

    /// <summary>Calendar days since tracking started (>= 1).</summary>
    public int Days
    {
        get
        {
            int d = (int)Math.Floor((DateTime.Now - FirstSeen).TotalDays) + 1;
            return d < 1 ? 1 : d;
        }
    }
}

internal sealed class ModelPrice
{
    public string Id = "";
    public double In;            // 元 / 1M tokens, cache MISS input (off-peak)
    public double CacheRead;     // 元 / 1M tokens, cache HIT input (off-peak)
    public double Out;           // 元 / 1M tokens output (off-peak)
    public double CacheWrite;    // 元 / 1M tokens cache write (off-peak)
    public double PeakMultiplier = 2.0;
}

/// <summary>One peak (busy-hours) range, "HH:mm" Beijing time, [start, end).</summary>
internal sealed class PeakWindow
{
    public string Start = "09:00";
    public string End = "12:00";

    public PeakWindow() { }
    public PeakWindow(string start, string end) { Start = start; End = end; }
}

internal sealed class Pricing
{
    /// <summary>Bumped when the on-disk shape changes (2 = 元 + no USD rate, 3 = peak windows).</summary>
    public const int CurrentVersion = 3;

    public int Version = CurrentVersion;
    public string DefaultModel = "deepseek-flash";
    /// <summary>
    /// Busy hours in BEIJING time. Everything outside them is off-peak, so the
    /// lunch break (12:00-14:00), the evening and the night are off-peak too -
    /// this is a PEAK list, not a single off-peak window. The user's rule:
    /// 周一至周五 09:00-12:00、14:00-18:00.
    /// </summary>
    public readonly List<PeakWindow> PeakWindows = new List<PeakWindow>();
    /// <summary>Weekends never count as peak (the rule only mentions Mon-Fri).</summary>
    public bool WeekendsOffPeak = true;
    public readonly List<ModelPrice> Models = new List<ModelPrice>();

    public ModelPrice Find(string id)
    {
        for (int i = 0; i < Models.Count; i++)
            if (String.Equals(Models[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Models[i];
        for (int i = 0; i < Models.Count; i++)
            if (id != null && id.IndexOf(Models[i].Id, StringComparison.OrdinalIgnoreCase) >= 0) return Models[i];
        return Models.Count > 0 ? Models[0] : new ModelPrice();
    }

    /// <summary>True when the given local (Beijing) time is inside a peak window.</summary>
    public bool IsPeak(DateTime t)
    {
        if (WeekendsOffPeak && (t.DayOfWeek == DayOfWeek.Saturday || t.DayOfWeek == DayOfWeek.Sunday)) return false;
        TimeSpan now = t.TimeOfDay, start, end;
        for (int i = 0; i < PeakWindows.Count; i++)
        {
            if (!TryParseHm(PeakWindows[i].Start, out start) || !TryParseHm(PeakWindows[i].End, out end)) continue;
            if (start <= end)
            {
                if (now >= start && now < end) return true;
            }
            else if (now >= start || now < end) return true;      // window crosses midnight
        }
        return false;
    }

    /// <summary>True when the given local (Beijing) time is off-peak (cheap).</summary>
    public bool IsOffPeak(DateTime t) { return !IsPeak(t); }

    private static bool TryParseHm(string s, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        if (String.IsNullOrEmpty(s)) return false;
        string[] p = s.Split(':');
        int h, m;
        if (p.Length != 2 || !Int32.TryParse(p[0], out h) || !Int32.TryParse(p[1], out m)) return false;
        if (h < 0 || h > 24 || m < 0 || m > 59) return false;
        ts = new TimeSpan(h, m, 0);
        return true;
    }
}

internal sealed class UsageTick          // one aggregated snapshot interval
{
    public DateTime Time;
    public long In, Out, CacheRead, CacheWrite;
    public double Cost;
    public bool OffPeak;
}

internal static class Usage
{
    public static string DshHome
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"); }
    }
    public static string ProjCacheFile { get { return Path.Combine(DshHome, "storages", "session_projcache.json"); } }
    public static string DataDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSH"); }
    }
    /// <summary>
    /// Test seam (probes only): point the price list at another file. Without it
    /// a smoke run would read - and migrate - the user's real pricing.json.
    /// </summary>
    internal static string OverridePricingFile = "";
    public static string PricingFile
    {
        get
        {
            return OverridePricingFile.Length > 0
                ? OverridePricingFile
                : Path.Combine(DataDir, "pricing.json");
        }
    }
    public static string HistoryFile { get { return Path.Combine(DataDir, "usage-history.jsonl"); } }
    public static string StateFile { get { return Path.Combine(DataDir, "usage-state.json"); } }

    // ---- projcache ---------------------------------------------------------
    /// <summary>Reads every session projection row. Returns an empty list on any failure.</summary>
    public static List<SessionUsage> ReadSessions()
    {
        var list = new List<SessionUsage>();
        try
        {
            if (!File.Exists(ProjCacheFile)) return list;
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(File.ReadAllText(ProjCacheFile, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) return list;
            var tables = Get(root, "tables");
            var sessions = Get(tables, "sessions");
            if (sessions == null) return list;

            foreach (KeyValuePair<string, object> kv in sessions)
            {
                var s = kv.Value as Dictionary<string, object>;
                var rows = Get(s, "rows");
                if (rows == null) continue;

                var u = new SessionUsage();
                u.Id = kv.Key;
                var ident = Get(s, "identity");
                if (ident != null)
                {
                    long cms = Num(ident, "createdAt");
                    if (cms > 0) u.CreatedAt = FromUnixMs(cms);
                    // identity.cwd is the session's workspace (its project) - the same
                    // folder the web UI groups sessions by. NOTE: Val() only returns
                    // SUB-DICTIONARIES, so a scalar must be read with TryGetValue.
                    object cw;
                    if (ident.TryGetValue("cwd", out cw) && cw != null) u.Cwd = cw.ToString();
                }
                u.Title = Str(Val(rows, "title"));

                var usage = Get(Val(rows, "tokenUsage"), "totals");
                if (usage != null)
                {
                    u.UncachedInput = Num(usage, "uncachedInputTokens");
                    u.Output = Num(usage, "outputTokens");
                    u.CacheRead = Num(usage, "cacheReadTokens");
                    u.CacheWrite = Num(usage, "cacheWriteTokens");
                }
                var pressure = Val(rows, "contextPressure");
                if (pressure != null)
                {
                    u.SurfaceTokens = Num(pressure, "surfaceTokens");
                    u.ContextWindow = Num(pressure, "contextWindow");
                }
                var stats = Val(rows, "sessionStats");
                if (stats != null)
                {
                    u.Turns = (int)Num(stats, "turns");
                    u.Steps = (int)Num(stats, "steps");
                    var pending = Get(stats, "pendingCalls");
                    if (pending != null) u.PendingCalls = pending.Count;
                    // openStep is an object while a step runs, null when idle -
                    // this is the only accurate "the agent is working right now"
                    // signal in projcache (the step counter alone lags a lot).
                    if (Get(stats, "openStep") != null) u.OpenStep = true;
                }
                var plan = Val(rows, "plan");
                if (plan != null)
                {
                    object pa;
                    if (plan is Dictionary<string, object> && ((Dictionary<string, object>)plan).TryGetValue("active", out pa) && pa != null)
                    {
                        try { u.PlanActive = Convert.ToBoolean(pa); } catch { }
                    }
                }
                var todos = Val(rows, "todos");
                if (todos is System.Collections.ArrayList) u.TodoCount = ((System.Collections.ArrayList)todos).Count;
                else if (todos is object[]) u.TodoCount = ((object[])todos).Length;
                var meta = Val(rows, "sessionListMetadata");
                if (meta != null)
                {
                    long ms = Num(meta, "lastPromptAt");
                    if (ms > 0) u.LastPromptAt = FromUnixMs(ms);
                }
                var perm = Val(rows, "permissions");
                if (perm != null)
                {
                    u.PermissionPreset = Str(Get(perm, "preset"));
                    u.Approval = Str(Get(perm, "approval"));
                }
                if (u.UncachedInput + u.Output + u.CacheRead + u.CacheWrite > 0 || u.Title.Length > 0) list.Add(u);
            }
        }
        catch { }
        return list;
    }

    private static object Val(Dictionary<string, object> rows, string row)
    {
        if (rows == null) return null;
        object o;
        if (!rows.TryGetValue(row, out o)) return null;
        var d = o as Dictionary<string, object>;
        if (d == null) return null;
        object v;
        return d.TryGetValue("val", out v) ? v : null;
    }
    private static Dictionary<string, object> Get(object o, string key)
    {
        var d = o as Dictionary<string, object>;
        if (d == null) return null;
        object v;
        if (!d.TryGetValue(key, out v)) return null;
        return v as Dictionary<string, object>;
    }
    private static long Num(object o, string key)
    {
        var d = o as Dictionary<string, object>;
        if (d == null) return 0;
        object v;
        if (!d.TryGetValue(key, out v) || v == null) return 0;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }
    private static string Str(object o) { return o == null ? "" : o.ToString(); }
    private static DateTime FromUnixMs(long ms)
    {
        try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }
        catch { return DateTime.MinValue; }
    }

    // ---- pricing -----------------------------------------------------------
    public static Pricing LoadPricing()
    {
        try
        {
            if (!File.Exists(PricingFile))
            {
                Pricing seed = DefaultPricing();
                SavePricing(seed);
                return seed;
            }
            var ser = new JavaScriptSerializer();
            Dictionary<string, object> root = null;
            try { root = ser.DeserializeObject(File.ReadAllText(PricingFile, Encoding.UTF8)) as Dictionary<string, object>; }
            catch { root = null; }
            if (root == null) return RecoverPricing();
            object v;
            // v1 files priced everything in USD with a usdToCny multiplier; that
            // made every historical amount break whenever the rate changed, so
            // v2 is 元-denominated and old files are replaced by the defaults.
            // v3 replaced the single off-peak window with a peak-window list and
            // dropped the retired model rows - the money is carried over.
            int version = 0;
            if (root.TryGetValue("version", out v) && v != null) { try { version = (int)ToL(v); } catch { } }
            if (version < 2)
            {
                Pricing replaced = DefaultPricing();
                SavePricing(replaced);
                return replaced;
            }
            if (version < Pricing.CurrentVersion)
            {
                Pricing migrated = Migrate(root);
                SavePricing(migrated);
                return migrated;
            }

            var p = DefaultPricing();
            if (root.TryGetValue("defaultModel", out v) && v != null) p.DefaultModel = v.ToString();
            if (root.TryGetValue("weekendsOffPeak", out v) && v != null) { try { p.WeekendsOffPeak = Convert.ToBoolean(v); } catch { } }
            if (root.TryGetValue("peakWindows", out v) && v != null)
            {
                // JSON arrays come back as object[] of Dictionary<string,object>
                object[] arr = v as object[];
                if (arr != null)
                {
                    p.PeakWindows.Clear();
                    foreach (object o in arr)
                    {
                        var w = o as Dictionary<string, object>;
                        if (w == null) continue;
                        var pw = new PeakWindow();
                        if (w.TryGetValue("start", out v) && v != null) pw.Start = v.ToString();
                        if (w.TryGetValue("end", out v) && v != null) pw.End = v.ToString();
                        p.PeakWindows.Add(pw);
                    }
                }
            }
            var models = Get(root, "models");
            if (models != null && models.Count > 0)
            {
                p.Models.Clear();
                foreach (KeyValuePair<string, object> kv in models)
                {
                    var m = kv.Value as Dictionary<string, object>;
                    if (m == null) continue;
                    var mp = new ModelPrice();
                    mp.Id = kv.Key;
                    if (m.TryGetValue("in", out v) && v != null) mp.In = ToD(v);
                    if (m.TryGetValue("out", out v) && v != null) mp.Out = ToD(v);
                    if (m.TryGetValue("cacheRead", out v) && v != null) mp.CacheRead = ToD(v);
                    if (m.TryGetValue("cacheWrite", out v) && v != null) mp.CacheWrite = ToD(v);
                    if (m.TryGetValue("peakMultiplier", out v) && v != null) mp.PeakMultiplier = ToD(v);
                    p.Models.Add(mp);
                }
            }
            return p;
        }
        catch { return DefaultPricing(); }
    }

    /// <summary>
    /// The file exists but is not readable JSON. The writer used to emit a
    /// `_comment` line WITHOUT a trailing comma, which made every load throw and
    /// silently fall back to the defaults - the user's price edits were dropped
    /// on each restart (and the v1/v2 migrations never ran). Keep the broken file
    /// for inspection and start a clean, valid one.
    /// </summary>
    private static Pricing RecoverPricing()
    {
        Pricing fresh = DefaultPricing();
        try { if (File.Exists(PricingFile)) File.Copy(PricingFile, PricingFile + ".broken", true); }
        catch { }
        SavePricing(fresh);
        return fresh;
    }

    /// <summary>
    /// v2 -> v3: keep the money the user had configured (the retired model rows
    /// only ever carried the same numbers), adopt the new peak windows and the
    /// single current model id.
    /// </summary>
    private static Pricing Migrate(Dictionary<string, object> old)
    {
        Pricing p = DefaultPricing();
        try
        {
            object v;
            string oldDefault = null;
            if (old.TryGetValue("defaultModel", out v) && v != null) oldDefault = v.ToString();
            var models = Get(old, "models");
            Dictionary<string, object> pick = null;
            if (models != null && models.Count > 0)
            {
                if (oldDefault != null && models.ContainsKey(oldDefault)) pick = models[oldDefault] as Dictionary<string, object>;
                if (pick == null)
                    foreach (KeyValuePair<string, object> kv in models) { pick = kv.Value as Dictionary<string, object>; if (pick != null) break; }
            }
            if (pick != null && p.Models.Count > 0)
            {
                ModelPrice m = p.Models[0];
                if (pick.TryGetValue("in", out v) && v != null) m.In = ToD(v);
                if (pick.TryGetValue("out", out v) && v != null) m.Out = ToD(v);
                if (pick.TryGetValue("cacheRead", out v) && v != null) m.CacheRead = ToD(v);
                if (pick.TryGetValue("cacheWrite", out v) && v != null) m.CacheWrite = ToD(v);
                if (pick.TryGetValue("peakMultiplier", out v) && v != null) m.PeakMultiplier = ToD(v);
            }
        }
        catch { }
        return p;
    }

    private static double ToD(object o) { try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; } }

    /// <summary>
    /// Defaults in 元 / 1M tokens (off-peak; peak = ×2). User-supplied table:
    /// cache-hit input 0.02 (peak 0.04), cache-miss input 1 (peak 2),
    /// output 4 (peak 8). Busy hours: 周一至周五 09:00-12:00、14:00-18:00
    /// (Beijing time), everything else off-peak.
    /// </summary>
    public static Pricing DefaultPricing()
    {
        var p = new Pricing();
        p.PeakWindows.Add(new PeakWindow("09:00", "12:00"));
        p.PeakWindows.Add(new PeakWindow("14:00", "18:00"));
        p.WeekendsOffPeak = true;
        // One model only: the backend now reports a single "deepseek-flash"
        // family, and pricing is looked up through this row.
        p.Models.Add(Make("deepseek-flash", 1.0, 4.0, 0.02, 0));
        p.DefaultModel = "deepseek-flash";
        return p;
    }
    private static ModelPrice Make(string id, double i, double o, double cr, double cw)
    {
        var m = new ModelPrice();
        m.Id = id; m.In = i; m.Out = o; m.CacheRead = cr; m.CacheWrite = cw;
        return m;
    }

    public static void SavePricing(Pricing p)
    {
        try
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"_comment\": \"元 / 百万 tokens 的空闲价；高峰价 = 空闲价 × peakMultiplier。peakWindows 是北京时间的高峰时段（其余为空闲），可直接编辑。\",");
            sb.AppendLine("  \"version\": " + Pricing.CurrentVersion + ",");
            sb.AppendLine("  \"currency\": \"CNY\",");
            sb.AppendLine("  \"defaultModel\": \"" + p.DefaultModel + "\",");
            sb.AppendLine("  \"weekendsOffPeak\": " + (p.WeekendsOffPeak ? "true" : "false") + ",");
            sb.AppendLine("  \"peakWindows\": [");
            for (int i = 0; i < p.PeakWindows.Count; i++)
            {
                PeakWindow w = p.PeakWindows[i];
                sb.Append("    { \"start\": \"").Append(w.Start).Append("\", \"end\": \"").Append(w.End)
                  .Append("\" }").Append(i == p.PeakWindows.Count - 1 ? "" : ",").AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"models\": {");
            for (int i = 0; i < p.Models.Count; i++)
            {
                ModelPrice m = p.Models[i];
                sb.Append("    \"").Append(m.Id).Append("\": { \"in\": ").Append(m.In.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"out\": ").Append(m.Out.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"cacheRead\": ").Append(m.CacheRead.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"cacheWrite\": ").Append(m.CacheWrite.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"peakMultiplier\": ").Append(m.PeakMultiplier.ToString(CultureInfo.InvariantCulture))
                  .Append(" }").Append(i == p.Models.Count - 1 ? "" : ",").AppendLine();
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(PricingFile, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    // ---- cost --------------------------------------------------------------
    public static double CostOf(Pricing p, ModelPrice m, long inTok, long outTok, long cacheRead, long cacheWrite, bool offPeak)
    {
        double k = offPeak ? 1.0 : m.PeakMultiplier;
        return (inTok / 1e6) * m.In * k
             + (outTok / 1e6) * m.Out * k
             + (cacheRead / 1e6) * m.CacheRead * k
             + (cacheWrite / 1e6) * m.CacheWrite * k;
    }

    /// <summary>Fills Cost for every session using the default model + its prompt time tier.</summary>
    public static double ComputeCosts(Pricing p, List<SessionUsage> sessions)
    {
        ModelPrice m = p.Find(p.DefaultModel);
        double total = 0;
        foreach (SessionUsage s in sessions)
        {
            bool off = p.IsOffPeak(s.LastPromptAt == DateTime.MinValue ? DateTime.Now : s.LastPromptAt);
            s.Cost = CostOf(p, m, s.UncachedInput, s.Output, s.CacheRead, s.CacheWrite, off);
            total += s.Cost;
        }
        return total;
    }

    // ---- history (aggregated deltas) --------------------------------------

    /// <summary>
    /// Cost of one stored interval, recomputed with the CURRENT price list.
    /// Every history line keeps its tokens, which is what makes "追溯" possible:
    /// changing the pricing never has to destroy or invalidate old numbers.
    /// </summary>
    public static double CostOfTick(Pricing p, UsageTick t)
    {
        ModelPrice m = p.Find(p.DefaultModel);
        // Recompute the tier from the stored timestamp: the "off" flag inside old
        // lines was recorded with the PREVIOUS window definition (a single
        // 00:30-08:30 off-peak window), which billed the lunch break and the
        // evening at peak rates.
        return CostOf(p, m, t.In, t.Out, t.CacheRead, t.CacheWrite, p.IsOffPeak(t.Time));
    }

    private static readonly Dictionary<string, long[]> _last = new Dictionary<string, long[]>();
    private static bool _seeded;

    /// <summary>Restores the per-session baseline from the last history line so
    /// deltas stay correct across restarts. Returns false when there is none.</summary>
    private static bool SeedFromHistory()
    {
        if (_seeded) return true;
        _seeded = true;
        try
        {
            if (!File.Exists(HistoryFile)) return false;
            string[] lines = File.ReadAllLines(HistoryFile);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                var d = new JavaScriptSerializer().DeserializeObject(s) as Dictionary<string, object>;
                if (d == null) continue;
                var map = Get(d, "s");
                if (map == null) continue;
                foreach (KeyValuePair<string, object> kv in map)
                {
                    var arr = kv.Value as object[];
                    if (arr == null || arr.Length < 4) continue;
                    _last[kv.Key] = new long[] { ToL(arr[0]), ToL(arr[1]), ToL(arr[2]), ToL(arr[3]) };
                }
                return _last.Count > 0;
            }
        }
        catch { }
        return false;
    }

    private static long ToL(object o) { try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return 0; } }

    public static void AppendSnapshot(Pricing p, List<SessionUsage> sessions)
    {
        try
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            bool haveBaseline = SeedFromHistory();

            long dIn = 0, dOut = 0, dCr = 0, dCw = 0;
            var map = new StringBuilder();
            bool firstItem = true;
            foreach (SessionUsage s in sessions)
            {
                long[] prev;
                if (!_last.TryGetValue(s.Id, out prev)) prev = new long[4];
                long a = s.UncachedInput - prev[0]; if (a > 0) dIn += a;
                long b = s.Output - prev[1]; if (b > 0) dOut += b;
                long c = s.CacheRead - prev[2]; if (c > 0) dCr += c;
                long d = s.CacheWrite - prev[3]; if (d > 0) dCw += d;
                _last[s.Id] = new long[] { s.UncachedInput, s.Output, s.CacheRead, s.CacheWrite };
                if (!firstItem) map.Append(",");
                firstItem = false;
                map.Append("\"").Append(s.Id.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\":[")
                   .Append(s.UncachedInput).Append(",").Append(s.Output).Append(",")
                   .Append(s.CacheRead).Append(",").Append(s.CacheWrite).Append("]");
            }

            // First ever run (no baseline): record the baseline with zero deltas so
            // the whole existing session is not attributed to the last hour.
            if (!haveBaseline) { dIn = dOut = dCr = dCw = 0; }
            if (haveBaseline && dIn + dOut + dCr + dCw == 0) return;

            bool off = p.IsOffPeak(DateTime.Now);
            ModelPrice m = p.Find(p.DefaultModel);
            double cost = CostOf(p, m, dIn, dOut, dCr, dCw, off);
            AccrueLifetime(p, dIn, dOut, dCr, dCw, off);
            string line = "{\"t\":" + ToUnixMs(DateTime.Now)
                + ",\"off\":" + (off ? "true" : "false")
                + ",\"in\":" + dIn + ",\"out\":" + dOut + ",\"cr\":" + dCr + ",\"cw\":" + dCw
                + ",\"cost\":" + cost.ToString("F6", CultureInfo.InvariantCulture)
                + ",\"s\":{" + map.ToString() + "}}";
            File.AppendAllText(HistoryFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private static long ToUnixMs(DateTime t)
    {
        try { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        catch { return 0; }
    }

    // ---- lifetime totals (usage-state.json) -------------------------------
    /// <summary>Loaded all-time totals (set by LoadLifetime).</summary>
    public static Lifetime Current;

    public static Lifetime LoadLifetime(Pricing p, List<SessionUsage> sessions)
    {
        var lt = new Lifetime();
        try
        {
            if (File.Exists(StateFile))
            {
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(StateFile, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null)
                {
                    lt.FirstSeen = FromUnixMs(Num(d, "firstSeen"));
                    if (lt.FirstSeen == DateTime.MinValue) lt.FirstSeen = DateTime.Now;
                    lt.In = Num(d, "in"); lt.Out = Num(d, "out");
                    lt.CacheRead = Num(d, "cr"); lt.CacheWrite = Num(d, "cw");
                    object cv;
                    if (d.TryGetValue("cost", out cv) && cv != null) lt.Cost = ToD(cv);
                    Current = lt;
                    return lt;
                }
            }
            // first run: seed from the sessions we can see, and start counting days
            DateTime earliest = DateTime.Now;
            foreach (SessionUsage s in sessions)
            {
                lt.In += s.UncachedInput; lt.Out += s.Output;
                lt.CacheRead += s.CacheRead; lt.CacheWrite += s.CacheWrite;
                if (s.CreatedAt != DateTime.MinValue && s.CreatedAt < earliest) earliest = s.CreatedAt;
            }
            lt.FirstSeen = earliest;
            lt.Cost = CostOf(p, p.Find(p.DefaultModel), lt.In, lt.Out, lt.CacheRead, lt.CacheWrite, p.IsOffPeak(earliest));
            SaveLifetime(lt);
        }
        catch { }
        Current = lt;
        return lt;
    }

    public static void SaveLifetime(Lifetime lt)
    {
        try
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            string json = "{\"firstSeen\":" + ToUnixMs(lt.FirstSeen)
                + ",\"in\":" + lt.In + ",\"out\":" + lt.Out + ",\"cr\":" + lt.CacheRead + ",\"cw\":" + lt.CacheWrite
                + ",\"cost\":" + lt.Cost.ToString("F6", CultureInfo.InvariantCulture) + "}";
            File.WriteAllText(StateFile, json, Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>Adds one interval's deltas to the all-time totals.</summary>
    public static void AccrueLifetime(Pricing p, long dIn, long dOut, long dCr, long dCw, bool offPeak)
    {
        Lifetime lt = Current;
        if (lt == null) return;
        lt.In += dIn; lt.Out += dOut; lt.CacheRead += dCr; lt.CacheWrite += dCw;
        lt.Cost += CostOf(p, p.Find(p.DefaultModel), dIn, dOut, dCr, dCw, offPeak);
        SaveLifetime(lt);
    }

    public static List<UsageTick> ReadHistory()
    {
        var list = new List<UsageTick>();
        try
        {
            if (!File.Exists(HistoryFile)) return list;
            string[] lines = File.ReadAllLines(HistoryFile);
            var ser = new JavaScriptSerializer();
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                try
                {
                    var d = ser.DeserializeObject(s) as Dictionary<string, object>;
                    if (d == null) continue;
                    var t = new UsageTick();
                    t.Time = FromUnixMs(Num(d, "t"));
                    t.OffPeak = Str(Get(d, "off")) == "True";
                    t.In = Num(d, "in"); t.Out = Num(d, "out");
                    t.CacheRead = Num(d, "cr"); t.CacheWrite = Num(d, "cw");
                    object cv;
                    if (d.TryGetValue("cost", out cv) && cv != null) t.Cost = ToD(cv);
                    list.Add(t);
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    /// <summary>Cost + tokens consumed in the last <paramref name="hours"/> hours.</summary>
    public static void Window(List<UsageTick> hist, double hours, out double cost, out long tokens)
    {
        Window(hist, hours, null, false, out cost, out tokens);
    }

    /// <summary>
    /// Same, but when <paramref name="retroactive"/> the amount is recomputed
    /// from the stored tokens with the CURRENT pricing instead of the ledger
    /// value written at the time (so editing prices never loses history).
    /// </summary>
    public static void Window(List<UsageTick> hist, double hours, Pricing p, bool retroactive, out double cost, out long tokens)
    {
        cost = 0; tokens = 0;
        DateTime from = DateTime.Now.AddHours(-hours);
        foreach (UsageTick t in hist)
        {
            if (t.Time < from) continue;
            cost += (retroactive && p != null) ? CostOfTick(p, t) : t.Cost;
            tokens += t.In + t.Out + t.CacheRead + t.CacheWrite;
        }
    }

    /// <summary>Cost consumed since local midnight.</summary>
    public static double Today(List<UsageTick> hist)
    {
        return Today(hist, null, false);
    }

    public static double Today(List<UsageTick> hist, Pricing p, bool retroactive)
    {
        double c = 0;
        DateTime today = DateTime.Today;
        foreach (UsageTick t in hist)
        {
            if (t.Time < today) continue;
            c += (retroactive && p != null) ? CostOfTick(p, t) : t.Cost;
        }
        return c;
    }

    /// <summary>Cost consumed since a local timestamp (used for the calendar month).</summary>
    public static double Since(List<UsageTick> hist, DateTime from)
    {
        return Since(hist, from, null, false);
    }

    public static double Since(List<UsageTick> hist, DateTime from, Pricing p, bool retroactive)
    {
        double c = 0;
        foreach (UsageTick t in hist)
        {
            if (t.Time < from) continue;
            c += (retroactive && p != null) ? CostOfTick(p, t) : t.Cost;
        }
        return c;
    }

    /// <summary>
    /// All-time cost of everything the history covers, recomputed with the
    /// current pricing (the 累计 figure in retroactive mode).
    /// </summary>
    public static double HistoryCost(List<UsageTick> hist, Pricing p, out long inTok, out long outTok, out long cacheRead, out long cacheWrite)
    {
        double c = 0;
        inTok = outTok = cacheRead = cacheWrite = 0;
        foreach (UsageTick t in hist)
        {
            c += CostOfTick(p, t);
            inTok += t.In; outTok += t.Out; cacheRead += t.CacheRead; cacheWrite += t.CacheWrite;
        }
        return c;
    }

    /// <summary>First instant of the current calendar month (local time).</summary>
    public static DateTime MonthStart()
    {
        DateTime n = DateTime.Now;
        return new DateTime(n.Year, n.Month, 1);
    }

    /// <summary>Budget evaluation result (pure maths, so it can be smoke-tested).</summary>
    internal sealed class BudgetStatus
    {
        public double DayCost, MonthCost, DayBudget, MonthBudget, WarnPct = 80;
        public int DayPercent, MonthPercent;
        public bool DayWarn, MonthWarn, DayOver, MonthOver;
        public string Text = "";

        public bool AnyWarn { get { return DayWarn || MonthWarn; } }
        public bool AnyOver { get { return DayOver || MonthOver; } }
    }

    /// <summary>
    /// Compares spend against the configured budgets. A budget of 0 disables
    /// that row. Percentages are capped for display but the flags stay honest.
    /// </summary>
    public static BudgetStatus Evaluate(double dayCost, double monthCost, double dayBudget, double monthBudget, double warnPct)
    {
        var s = new BudgetStatus();
        s.DayCost = dayCost; s.MonthCost = monthCost;
        s.DayBudget = dayBudget; s.MonthBudget = monthBudget;
        if (warnPct <= 0) warnPct = 80;
        s.WarnPct = warnPct;

        if (dayBudget > 0)
        {
            s.DayPercent = (int)Math.Round(dayCost / dayBudget * 100.0);
            s.DayWarn = s.DayPercent >= warnPct;
            s.DayOver = s.DayPercent >= 100;
        }
        if (monthBudget > 0)
        {
            s.MonthPercent = (int)Math.Round(monthCost / monthBudget * 100.0);
            s.MonthWarn = s.MonthPercent >= warnPct;
            s.MonthOver = s.MonthPercent >= 100;
        }
        return s;
    }

    /// <summary>Formats a 元 amount (prices are CNY-denominated since v2).</summary>
    public static string Money(double cny, Pricing p)
    {
        return "¥" + cny.ToString("F2", CultureInfo.InvariantCulture);
    }

    public static string Tokens(long n)
    {
        if (n >= 1000000) return (n / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + "M";
        if (n >= 1000) return (n / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "k";
        return n.ToString(CultureInfo.InvariantCulture);
    }

    // ---- live signals: pet spout + notifications ---------------------------

    /// <summary>Compact per-session state used to diff two polls.</summary>
    internal sealed class SessionSignal
    {
        public int Plan, Todos, Steps, Pending, Open;
    }

    /// <summary>One human-facing change detected between two polls.</summary>
    internal sealed class UsageChange
    {
        public string SessionId = "";
        public string Title = "";
        public string Sub = "";
        public bool Burst;              // pet should spout to max
    }

    /// <summary>
    /// True when any session is running right now: a step is open, a tool call
    /// is in flight, or a plan is executing. openStep is the accurate one - the
    /// step counter only moves between polls.
    /// </summary>
    public static bool AnyBusy(List<SessionUsage> sessions)
    {
        if (sessions == null) return false;
        foreach (SessionUsage s in sessions)
            if (s.OpenStep || s.PendingCalls > 0 || s.PlanActive) return true;
        return false;
    }

    /// <summary>
    /// Diffs every session against the previous poll and returns what the user
    /// should hear about. Pure: no UI, no clock, so the smoke probe can drive it
    /// with synthetic sessions.
    /// </summary>
    public static List<UsageChange> Observe(Dictionary<string, SessionSignal> prev, List<SessionUsage> sessions)
    {
        var list = new List<UsageChange>();
        if (sessions == null) return list;
        var seen = new List<string>();
        foreach (SessionUsage s in sessions)
        {
            var cur = new SessionSignal();
            cur.Plan = s.PlanActive ? 1 : 0;
            cur.Todos = s.TodoCount;
            cur.Steps = s.Steps;
            cur.Pending = s.PendingCalls;
            cur.Open = s.OpenStep ? 1 : 0;
            seen.Add(s.Id);

            SessionSignal p;
            if (prev.TryGetValue(s.Id, out p))
            {
                string who = s.Title.Length > 0 ? s.Title : "当前会话";
                if (p.Plan == 0 && cur.Plan == 1) Push(list, "计划已开始", who + " · agent 正在执行计划", false, s.Id);
                else if (p.Plan == 1 && cur.Plan == 0) Push(list, "计划已结束", who + " · 计划执行完成或已停止", true, s.Id);
                if (p.Todos != cur.Todos && (cur.Todos > 0 || p.Todos > 0))
                    Push(list, "任务清单更新", who + " · 待办 " + cur.Todos + " 项", false, s.Id);
            }
            prev[s.Id] = cur;
        }
        // forget sessions that disappeared so the map cannot grow forever
        if (prev.Count > seen.Count)
        {
            var gone = new List<string>();
            foreach (string k in prev.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (string k in gone) prev.Remove(k);
        }
        return list;
    }

    private static void Push(List<UsageChange> list, string title, string sub) { Push(list, title, sub, false, ""); }

    private static void Push(List<UsageChange> list, string title, string sub, bool burst, string sessionId)
    {
        var c = new UsageChange();
        c.SessionId = sessionId;
        c.Title = title;
        c.Sub = sub;
        c.Burst = burst;
        list.Add(c);
    }
}
