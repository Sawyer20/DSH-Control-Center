// ============================================================================
//  Settings.cs - read/patch the harness settings document (~/.dsh/settings.yaml)
//
//  The shell touches exactly TWO leaves of that document:
//    * `agent-default-model.model`  - the model every NEW agent/session uses;
//    * the matching `llm-deepseek.models[]` entry's `inputModalities`, which is
//      what decides whether a model may receive IMAGES (KI-22: an id that is not
//      declared - or declared without `image` - makes the adapter throw
//      "does not accept image input" before the request is even sent).
//  Both are written in ONE document pass with ONE atomic write, so the default
//  model and its capability can never disagree on disk.
//
//  Why a plain file write is enough (verified against the installed backend):
//    * @deepseek-ai/dsh-settings-file watches the document (chokidar, 100 ms
//      debounce) and hot-publishes external edits into the settings seam, and
//      AgentDefaultModelConfig reads that seam live - no service restart.
//    * Its own writer re-reads the document under a cross-process lock and
//      patches ONE namespace, so our edit survives later theme/UI writes.
//    * The model change only affects agents created AFTER it: sessions that
//      already resolved a model keep it (their KV-cache prefix must stay valid).
//
//  Editing is a TEXT patch on purpose: comments, key order and every other
//  section must survive byte for byte (the provider's writer works the same
//  way), and the write goes through a temp file + File.Replace so the watcher
//  can never observe a half-written document.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class HarnessSettings
{
    /// <summary>Test seam (probes only): point at another document.</summary>
    internal static string OverrideFile = "";

    public const string Namespace = "agent-default-model";
    public const string ProviderSection = "llm-deepseek";
    public const string DefaultContextWindow = "1000000";

    public static string FilePath
    {
        get
        {
            if (OverrideFile.Length > 0) return OverrideFile;
            return Path.Combine(Usage.DshHome, "settings.yaml");
        }
    }

    private static string[] ReadAll(out string text)
    {
        text = null;
        try
        {
            if (!File.Exists(FilePath)) return null;
            text = File.ReadAllText(FilePath, Encoding.UTF8);
            return text.Replace("\r\n", "\n").Split('\n');
        }
        catch { return null; }
    }

    // ---- reading -----------------------------------------------------------
    /// <summary>Current `agent-default-model.model`, or "" when absent.</summary>
    public static string ModelId { get { return Leaf(Namespace, "model"); } }

    /// <summary>Current `agent-default-model.provider`, or "" when absent.</summary>
    public static string Provider { get { return Leaf(Namespace, "provider"); } }

    /// <summary>Current `agent-default-model.reasoningEffort`, or "" when absent.</summary>
    public static string ReasoningEffort { get { return Leaf(Namespace, "reasoningEffort"); } }

    /// <summary>Value of one leaf key inside a top-level section ("" when missing).</summary>
    public static string Leaf(string section, string key)
    {
        string text;
        string[] lines = ReadAll(out text);
        if (lines == null) return "";
        int start, end;
        if (!Section(lines, section, out start, out end)) return "";
        for (int i = start + 1; i < end; i++)
            if (IsLeaf(lines[i], key)) return Scalar(lines[i]);
        return "";
    }

    /// <summary>
    /// Model ids the provider declares (`llm-deepseek.models[].id`). Informational:
    /// the router itself does not check catalogue membership, so the editor
    /// accepts an undeclared id - it just says what the document holds.
    /// </summary>
    public static List<string> DeclaredModels()
    {
        var list = new List<string>();
        string text;
        string[] lines = ReadAll(out text);
        if (lines == null) return list;
        int start, end;
        if (!Section(lines, ProviderSection, out start, out end)) return list;
        for (int i = start + 1; i < end; i++)
        {
            string s = lines[i].TrimStart();
            if (s.StartsWith("- ")) s = s.Substring(2).TrimStart();
            if (IsLeaf(s, "id"))
            {
                string id = Scalar(s);
                if (id.Length > 0 && !list.Contains(id)) list.Add(id);
            }
        }
        return list;
    }

    /// <summary>
    /// True when the catalogue entry for <paramref name="id"/> allows image
    /// input. An undeclared id is text-only (the adapter's fallback), so false.
    /// </summary>
    public static bool DeclaresImage(string id)
    {
        string text;
        string[] lines = ReadAll(out text);
        if (lines == null || String.IsNullOrEmpty(id)) return false;
        int is_, ie;
        if (!FindCatalogItem(lines, id, out is_, out ie)) return false;
        for (int i = is_; i < ie; i++)
        {
            if (!IsLeaf(lines[i], "inputModalities")) continue;
            string val = Scalar(lines[i]);
            if (val.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // block form: the value sits on the following deeper-indented lines
            int baseIndent = IndentOf(lines[i]).Length;
            for (int j = i + 1; j < ie; j++)
            {
                string s = lines[j].TrimStart();
                if (IndentOf(lines[j]).Length <= baseIndent) break;
                if (s.StartsWith("- ")) s = s.Substring(2);
                if (s.Trim().Equals("image", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        return false;
    }

    /// <summary>Both provider and model, for the "配置里现在是…" hint.</summary>
    public static string Describe()
    {
        string p = Provider, m = ModelId;
        if (m.Length == 0) return "未配置";
        return p.Length > 0 ? (p + " / " + m) : m;
    }

    // ---- writing -----------------------------------------------------------
    /// <summary>Sets the default model id, leaving the catalogue alone.</summary>
    public static bool SetModelId(string id, out string error)
    {
        return ApplyModel(id, null, out error);
    }

    /// <summary>
    /// Sets `agent-default-model.model` and, when <paramref name="image"/> is not
    /// null, makes the catalogue entry for that id declare image input (true) or
    /// text only (false) - creating the entry when the id was undeclared and
    /// image support is being turned ON. One pass, one atomic write.
    /// </summary>
    public static bool ApplyModel(string id, bool? image, out string error)
    {
        error = "";
        string want = (id == null ? "" : id.Trim());
        if (want.Length == 0) { error = "模型 ID 不能为空。"; return false; }
        for (int i = 0; i < want.Length; i++)
        {
            char c = want[i];
            if (c == ' ' || c == '\t' || c == ':' || c == '#' || c == '"' || c == '\''
                || c == '\r' || c == '\n')
            {
                error = "模型 ID 不能包含空格、引号、冒号或 #（示例：deepseek-flash）。";
                return false;
            }
        }

        string path = FilePath;
        bool bom;
        try
        {
            if (!File.Exists(path)) { error = "找不到配置文件：" + path; return false; }
            byte[] raw = File.ReadAllBytes(path);
            bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = Encoding.UTF8.GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            string nl = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

            int start, end;
            if (!Section(lines, Namespace, out start, out end))
            {
                error = "配置文件里没有 " + Namespace + " 节，未改动（请先让后端生成它）。";
                return false;
            }

            int at = -1;
            for (int i = start + 1; i < end; i++) if (IsLeaf(lines[i], "model")) { at = i; break; }
            if (at >= 0)
            {
                lines[at] = ReplaceScalar(lines[at], want);
            }
            else
            {
                string indent = "  ";
                for (int i = start + 1; i < end; i++)
                {
                    string ind = IndentOf(lines[i]);
                    if (ind.Length > 0) { indent = ind; break; }
                }
                lines.Insert(start + 1, indent + "model: " + want);
            }

            if (image.HasValue)
            {
                bool changed;
                if (!PatchCatalog(lines, want, image.Value, out changed, out error)) return false;
                if (changed && image.Value == false && !IsDeclared(lines, want))
                {
                    // nothing to remove and nothing was there: fine, text is default
                }
            }

            string outp = String.Join(nl, lines.ToArray());
            string tmp = path + ".dsh-new";
            File.WriteAllText(tmp, outp, new UTF8Encoding(bom));
            try { File.Replace(tmp, path, null); }
            catch
            {
                try { File.Delete(path); } catch { }
                File.Move(tmp, path);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "写入失败：" + ex.Message;
            return false;
        }
    }

    private static bool IsDeclared(IList<string> lines, string id)
    {
        int is_, ie;
        return FindCatalogItem(lines, id, out is_, out ie);
    }

    // ---- YAML helpers (deliberately minimal: this document is a map of maps) --
    /// <summary>Line range of a top-level `section:` block, children in [start+1,end).</summary>
    private static bool Section(IList<string> lines, string section, out int start, out int end)
    {
        start = -1; end = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            if (IsTopLevelKey(lines[i], section)) { start = i; break; }
        }
        if (start < 0) return false;
        for (int i = start + 1; i < lines.Count; i++)
        {
            string s = lines[i];
            if (s.Length == 0) continue;
            char c = s[0];
            if (c != ' ' && c != '\t' && c != '#') { end = i; break; }
        }
        return true;
    }

    private static bool IsTopLevelKey(string line, string key)
    {
        if (line == null || line.Length == 0) return false;
        if (line[0] == ' ' || line[0] == '\t' || line[0] == '#') return false;
        return StartsWithKey(line, key);
    }

    private static bool IsLeaf(string line, string key)
    {
        if (line == null) return false;
        string s = line.TrimStart();
        if (s.Length == 0 || s[0] == '#') return false;
        if (s[0] == '-')                                       // list item: "- id: x"
        {
            s = s.Substring(1).TrimStart();
            if (!StartsWithKey(s, key)) return false;
        }
        else if (!StartsWithKey(s, key)) return false;
        return true;
    }

    /// <summary>`key:` — the char after the key must be the colon (`model:` yes, `models:` no).</summary>
    private static bool StartsWithKey(string s, string key)
    {
        if (s.Length < key.Length + 1) return false;
        if (String.CompareOrdinal(s, 0, key, 0, key.Length) != 0) return false;
        int i = key.Length;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return i < s.Length && s[i] == ':';
    }

    /// <summary>Scalar value of a leaf line: quotes stripped, trailing comment dropped.</summary>
    private static string Scalar(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return "";
        string rest = line.Substring(colon + 1);
        int hash = CommentAt(rest);
        if (hash >= 0) rest = rest.Substring(0, hash);
        rest = rest.Trim();
        if (rest.Length >= 2
            && ((rest[0] == '"' && rest[rest.Length - 1] == '"')
                || (rest[0] == '\'' && rest[rest.Length - 1] == '\'')))
            rest = rest.Substring(1, rest.Length - 2);
        return rest;
    }

    /// <summary>Index of a trailing comment in the text after a colon, or -1.</summary>
    private static int CommentAt(string rest)
    {
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] != '#') continue;
            if (i == 0 || rest[i - 1] == ' ' || rest[i - 1] == '\t') return i;
        }
        return -1;
    }

    /// <summary>
    /// Same leaf, new value. Indentation, the gap before a trailing comment and
    /// the comment itself are all preserved, so the diff stays one token wide.
    /// </summary>
    private static string ReplaceScalar(string line, string value)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return line;
        string head = line.Substring(0, colon + 1);
        string rest = line.Substring(colon + 1);
        int hash = CommentAt(rest);
        if (hash < 0) return head + " " + value;

        int k = hash - 1;
        while (k >= 0 && (rest[k] == ' ' || rest[k] == '\t')) k--;
        string gap = rest.Substring(k + 1, hash - k - 1);
        return head + " " + value + gap + rest.Substring(hash);
    }

    private static string IndentOf(string line)
    {
        if (line == null) return "";
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i == 0 ? "" : line.Substring(0, i);
    }

    // ---- catalogue (llm-deepseek.models[]) ---------------------------------
    /// <summary>
    /// Line range of the `- id: <id>` entry inside `llm-deepseek.models`, header
    /// line included. False when the section, the list or the entry is absent.
    /// </summary>
    private static bool FindCatalogItem(IList<string> lines, string id, out int is_, out int ie)
    {
        is_ = -1; ie = -1;
        int ls, le;
        if (!Section(lines, ProviderSection, out ls, out le)) return false;
        int ms = -1, modelsIndent = -1;
        for (int i = ls + 1; i < le; i++)
        {
            if (IsLeaf(lines[i], "models")) { ms = i; modelsIndent = IndentOf(lines[i]).Length; break; }
        }
        if (ms < 0) return false;
        // The item indent is the indent of the FIRST "-" line of the sequence.
        // Nested list items (the modality bullets) are deeper, and mistaking them
        // for entries shrinks every entry's range to two lines.
        int itemIndent = -1;
        for (int i = ms + 1; i < le; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            if (IndentOf(lines[i]).Length <= modelsIndent) break;
            if (lines[i].TrimStart().StartsWith("-")) { itemIndent = IndentOf(lines[i]).Length; break; }
        }
        int seqEnd = le;
        for (int i = ms + 1; i < le; i++)
        {
            string s = lines[i];
            if (s.Trim().Length == 0) continue;
            if (IndentOf(s).Length <= modelsIndent) { seqEnd = i; break; }
        }
        int cur = -1;
        for (int i = ms + 1; i <= seqEnd; i++)
        {
            bool itemStart = i < seqEnd && IsItemStart(lines[i], itemIndent);
            if (itemStart || i == seqEnd)
            {
                if (cur >= 0)
                {
                    // entry spans [cur, i)
                    for (int k = cur; k < i; k++)
                    {
                        if (!IsLeaf(lines[k], "id")) continue;
                        if (String.Equals(Scalar(lines[k]), id, StringComparison.OrdinalIgnoreCase))
                        {
                            is_ = cur; ie = i;
                            return true;
                        }
                        break;
                    }
                }
                cur = itemStart ? i : -1;
            }
        }
        return false;
    }

    /// <summary>True for a top-level list item of the catalogue sequence.</summary>
    private static bool IsItemStart(string line, int itemIndent)
    {
        if (line == null || itemIndent < 0) return false;
        string s = line.TrimStart();
        if (s.Length == 0 || s[0] != '-') return false;
        return IndentOf(line).Length == itemIndent;
    }

    /// <summary>Indent of the first top-level item of `models:`, or -1.</summary>
    private static int CatalogItemIndent(IList<string> lines, int ms, int le)
    {
        int modelsIndent = IndentOf(lines[ms]).Length;
        for (int i = ms + 1; i < le; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            if (IndentOf(lines[i]).Length <= modelsIndent) return -1;
            if (lines[i].TrimStart().StartsWith("-")) return IndentOf(lines[i]).Length;
        }
        return -1;
    }

    /// <summary>
    /// Ensures the catalogue declares <paramref name="id"/> with or without image
    /// input. Turning image ON for an undeclared id appends a new entry (name =
    /// id, contextWindow = default) because an undeclared id can never accept
    /// images; turning it OFF on an undeclared id changes nothing (text is the
    /// fallback anyway).
    /// </summary>
    private static bool PatchCatalog(List<string> lines, string id, bool image,
        out bool changed, out string error)
    {
        changed = false;
        error = "";
        int is_, ie;
        if (FindCatalogItem(lines, id, out is_, out ie))
        {
            changed = PatchModalities(lines, is_, ie, image);
            return true;
        }
        if (!image) return true;                        // nothing to declare

        int ls, le;
        if (!Section(lines, ProviderSection, out ls, out le))
        {
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.Add(ProviderSection + ":");
            lines.Add("  models:");
            AppendEntry(lines, "    ", id);
            changed = true;
            return true;
        }
        int ms = -1;
        for (int i = ls + 1; i < le; i++) if (IsLeaf(lines[i], "models")) { ms = i; break; }
        if (ms < 0)
        {
            var add = new List<string>();
            add.Add("  models:");
            AppendEntry(add, "    ", id);
            lines.InsertRange(ls + 1, add);
            changed = true;
            return true;
        }
        // append to the end of the existing sequence, reusing its item indent
        int modelsIndent = IndentOf(lines[ms]).Length;
        int seqEnd = le;
        for (int i = ms + 1; i < le; i++)
        {
            string s = lines[i];
            if (s.Trim().Length == 0) continue;
            if (IndentOf(s).Length <= modelsIndent) { seqEnd = i; break; }
        }
        int itemIndent = CatalogItemIndent(lines, ms, le);
        string indent = itemIndent >= 0 ? new string(' ', itemIndent) : new string(' ', modelsIndent + 2);
        var entry = new List<string>();
        AppendEntry(entry, indent, id);
        lines.InsertRange(seqEnd, entry);
        changed = true;
        return true;
    }

    /// <summary>
    /// Rewrites one entry's `inputModalities` (inserting it after `id:` when the
    /// entry has none). Always block form, so the diff is readable and stable.
    /// </summary>
    private static bool PatchModalities(List<string> lines, int is_, int ie, bool image)
    {
        int key = -1, idLine = -1;
        string keyIndent = null;
        for (int i = is_; i < ie; i++)
        {
            if (idLine < 0 && IsLeaf(lines[i], "id")) idLine = i;
            if (IsLeaf(lines[i], "inputModalities")) { key = i; keyIndent = IndentOf(lines[i]); break; }
        }
        if (keyIndent == null)
        {
            if (idLine < 0) return false;
            keyIndent = IndentOf(lines[idLine]) + "  ";
        }

        var block = new List<string>();
        block.Add(keyIndent + "inputModalities:");
        block.Add(keyIndent + "  - text");
        if (image) block.Add(keyIndent + "  - image");

        if (key < 0)
        {
            // append at the end of the entry (after name/contextWindow), matching
            // the catalogue's own key order
            int at = ie;
            while (at - 1 > idLine && lines[at - 1].Trim().Length == 0) at--;
            lines.InsertRange(at, block);
            return true;
        }

        // replace the key line plus its block-form items (flow form has none)
        int stop = key + 1;
        int valIndent = keyIndent.Length;
        while (stop < ie)
        {
            string s = lines[stop];
            if (s.Trim().Length == 0) break;
            if (IndentOf(s).Length <= valIndent) break;
            if (!s.TrimStart().StartsWith("-")) break;
            stop++;
        }
        bool same = (stop - key - 1) == (image ? 2 : 1);
        if (same)
        {
            same = lines[key].Trim() == "inputModalities:"
                && lines[key + 1].Trim() == "- text"
                && (!image || lines[key + 2].Trim() == "- image");
        }
        if (same) return false;
        lines.RemoveRange(key, stop - key);
        lines.InsertRange(key, block);
        return true;
    }

    private static void AppendEntry(List<string> lines, string itemIndent, string id)
    {
        lines.Add(itemIndent + "- id: " + id);
        lines.Add(itemIndent + "  name: " + id);
        lines.Add(itemIndent + "  contextWindow: " + DefaultContextWindow);
        lines.Add(itemIndent + "  inputModalities:");
        lines.Add(itemIndent + "    - text");
        lines.Add(itemIndent + "    - image");
    }
}
