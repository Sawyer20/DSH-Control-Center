// ============================================================================
//  Workspace.cs - read the harness's own workspace registry (read-only)
//
//  DSH keeps a registry of workspaces in ~/.dsh/storages/workspace.json:
//
//    { "unit": { "name": "workspace", "version": 2 },
//      "global": { "initialized": true, "workspaceIds": [<id>...],
//                  "archivedSessionIds": [...] },
//      "tables": { "workspaces": { "<id>": { "path", "title",
//                  "sessionIds": [...], "createdAt", "updatedAt" } } } }
//
//  A session's workspace is its `identity.cwd` (see session_projcache.json), and
//  a NEW session inherits the backend process's cwd. That is the whole sync
//  story: the web UI picks/adds a workspace, the backend records it here, and
//  the shell - which launches `dsh web` - should start it in the workspace the
//  UI used last instead of inventing its own. We only READ this file (the domain
//  store is owned by the backend and writes pending-change markers); the shell's
//  own override stays the documented `workdir.txt`.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class WorkspaceEntry
{
    public string Id = "";
    public string Path = "";
    public string Title = "";
    public int SessionCount;
    public DateTime UpdatedAt = DateTime.MinValue;
    public DateTime CreatedAt = DateTime.MinValue;
    /// <summary>The folder still exists on disk (the registry never deletes records).</summary>
    public bool Exists;
}

internal static class Workspace
{
    /// <summary>Test seam (probes only).</summary>
    internal static string OverrideFile = "";

    public static string FilePath
    {
        get
        {
            if (OverrideFile.Length > 0) return OverrideFile;
            return Path.Combine(Usage.DshHome, "storages", "workspace.json");
        }
    }

    /// <summary>Every registered workspace, in the registry's persistent order.</summary>
    public static List<WorkspaceEntry> List()
    {
        var result = new List<WorkspaceEntry>();
        try
        {
            if (!File.Exists(FilePath)) return result;
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) return result;
            object v;
            var tables = Sub(root, "tables");
            var table = Sub(tables, "workspaces");
            if (table == null) return result;
            var byId = new Dictionary<string, WorkspaceEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kv in table)
            {
                var row = kv.Value as Dictionary<string, object>;
                if (row == null) continue;
                var e = new WorkspaceEntry();
                e.Id = kv.Key;
                e.Path = Str(row, "path");
                e.Title = Str(row, "title");
                if (row.TryGetValue("sessionIds", out v) && v != null)
                {
                    var arr = v as object[];
                    e.SessionCount = arr == null ? 0 : arr.Length;
                }
                e.CreatedAt = Time(row, "createdAt");
                e.UpdatedAt = Time(row, "updatedAt");
                e.Exists = e.Path.Length > 0 && Directory.Exists(e.Path);
                byId[e.Id] = e;
            }
            // persistent display order first, anything unknown after it
            var order = new List<string>();
            var glob = Sub(root, "global");
            if (glob != null && glob.TryGetValue("workspaceIds", out v) && v != null)
            {
                var ids = v as object[];
                if (ids != null) foreach (object o in ids) if (o != null) order.Add(o.ToString());
            }
            foreach (string id in order)
            {
                WorkspaceEntry e;
                if (byId.TryGetValue(id, out e)) { result.Add(e); byId.Remove(id); }
            }
            foreach (KeyValuePair<string, WorkspaceEntry> kv in byId) result.Add(kv.Value);
        }
        catch { }
        return result;
    }


    private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v)) return null;
        return v as Dictionary<string, object>;
    }

    private static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
        return v.ToString();
    }

    private static DateTime Time(Dictionary<string, object> d, string key)
    {
        string s = Str(d, key);
        if (s.Length == 0) return DateTime.MinValue;
        DateTime t;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
            return t.ToLocalTime();
        return DateTime.MinValue;
    }
}
