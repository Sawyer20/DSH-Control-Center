using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

/// <summary>
/// The shell's client for the DSH event downlink (verified against the installed
/// backend, not guesswork). It carries two things the shell needs:
///
/// 1. TURN LIFECYCLE - the authoritative "the agent is working / finished"
///    signal. Frames are `session/event` envelopes whose payload.event is a
///    SessionEvent: `turn/start {turn}` and `turn/end {turn, reason:{kind}}`
///    where kind is completed | interrupted | aborted | error | blocked.
///    Guessing from projcache (`openStep`) produced false "task complete" while
///    the model was still thinking - the turn events do not.
///
/// 2. APPROVALS - `approval/requested` (payload: sessionId, approvalId,
///    toolName, callId?, reason?) and `approval/resolved` (sessionId,
///    approvalId, outcome); pending ones are replayed when the mux opens.
///    Answering is an HTTP POST (the socket is downlink-only: sending anything
///    closes it with 1008): POST /api/respond, Content-Type application/json,
///    body {type:"client-response", rpcId, result:{ok:true, value:{sessionId,
///    approvalId, outcome}}} with outcome "allowed-once" | "rejected".
///
/// Both endpoints are UNDOCUMENTED internals guarded only by "loopback Host and
/// no cross-site Origin", so every failure path degrades to polling + telling
/// the user to answer in the web UI.
/// </summary>
internal sealed class ApprovalItem
{
    public string RpcId = "";
    public string SessionId = "";
    public string ApprovalId = "";
    public string ToolName = "";
    public string CallId = "";
    public string Reason = "";
    public DateTime SeenAt = DateTime.Now;
}

/// <summary>
/// One "the model is asking you to choose" request. Frames come from the same
/// downlink as approvals and are equally authoritative:
///   question/requested { sessionId, questions:[ { id, question, detail?,
///                        header?, options?:[{label,description?}],
///                        multiSelect?, intent?:{kind,approve} } ] }
///   question/resolved  { sessionId, questionRpcId, outcome: answered|cancelled }
/// The envelope rpcId IS the questionRpcId. Unlike an approval, a question is
/// never auto-answered - it waits for a human (in the web UI or in the shell),
/// which is exactly why it deserves a reminder.
/// </summary>
internal sealed class QuestionItem
{
    public string RpcId = "";
    public string SessionId = "";
    public string Header = "";        // short label, when the asker supplied one
    public string Text = "";          // the question itself
    public string Detail = "";        // helper text / the plan being reviewed
    public int OptionCount;
    public bool MultiSelect;
    public bool PlanReview;           // intent.kind == "plan-review"
    public DateTime SeenAt = DateTime.Now;

    /// <summary>Short label for the bubble / notification title.</summary>
    public string Label
    {
        get
        {
            if (PlanReview) return "计划待审";
            if (Header.Length > 0) return Header;
            return "模型在等你选";
        }
    }

    /// <summary>One-line gist: the question, or the plan-review hint.</summary>
    public string Gist
    {
        get
        {
            string s = Text.Length > 0 ? Text : (Detail.Length > 0 ? Detail : Header);
            if (OptionCount > 0) s += "（" + OptionCount + (MultiSelect ? " 个选项，可多选）" : " 选 1）");
            return s;
        }
    }
}

/// <summary>One turn boundary, or the user speaking in a session.</summary>
internal sealed class TurnEvent
{
    public string SessionId = "";
    public int Turn;
    public bool Started;
    public string Reason = "";      // completed / interrupted / aborted / error / blocked
    /// <summary>"turn/start" | "turn/end" | "user/message"</summary>
    public string Type = "";
}

internal static class Mux
{
    /// <summary>Raised (background thread) when the backend asks for a decision.</summary>
    internal static event Action<ApprovalItem> Requested;
    /// <summary>Raised (background thread) with the approvalId when one is settled.</summary>
    internal static event Action<string> Resolved;
    /// <summary>Raised (background thread) when the model asks the user to choose.</summary>
    internal static event Action<QuestionItem> QuestionRequested;
    /// <summary>Raised (background thread) with the questionRpcId once it is settled.</summary>
    internal static event Action<string> QuestionResolved;
    /// <summary>Raised (background thread) on every turn/start and turn/end.</summary>
    internal static event Action<TurnEvent> TurnChanged;
    /// <summary>Raised (background thread) when the mux connection state changes.</summary>
    internal static event Action StatusChanged;

    private static Thread _thread;
    private static volatile bool _stop = true;
    private static volatile bool _connected;
    private static volatile string _status = "未连接";

    internal static bool Connected { get { return _connected; } }
    internal static string Status { get { return _status; } }

    /// <summary>Starts the mux watcher (idempotent).</summary>
    internal static void Start()
    {
        if (_thread != null) return;
        _stop = false;
        _thread = new Thread(Loop);
        _thread.IsBackground = true;
        _thread.Name = "dsh-event-mux";
        _thread.Start();
    }

    internal static void Stop()
    {
        _stop = true;
        _thread = null;
    }

    private static void Loop()
    {
        while (!_stop)
        {
            ClientWebSocket ws = null;
            try
            {
                ws = new ClientWebSocket();
                string url = Program.Url.Replace("https://", "wss://").Replace("http://", "ws://") + "/api/events.mux";
                Task ct = ws.ConnectAsync(new Uri(url), CancellationToken.None);
                if (!ct.Wait(6000)) throw new Exception("连接超时");
                if (ws.State != WebSocketState.Open) throw new Exception("握手失败：" + ws.State);
                _connected = true;
                SetStatus("已连接");
                try { ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20); } catch { }

                var buf = new byte[64 * 1024];
                var sb = new StringBuilder();
                while (!_stop && ws.State == WebSocketState.Open)
                {
                    Task<WebSocketReceiveResult> rt = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    while (!rt.IsCompleted && !_stop) Thread.Sleep(40);
                    if (_stop) break;
                    WebSocketReceiveResult r = rt.Result;      // throws on a broken socket
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    string msg = sb.ToString();
                    sb.Length = 0;
                    Dispatch(msg);
                }
            }
            catch (Exception ex)
            {
                _connected = false;
                SetStatus("未连接（" + Short(ex) + "）");
            }
            finally
            {
                if (_connected) { _connected = false; SetStatus("未连接"); }
                try { if (ws != null) ws.Dispose(); } catch { }
            }
            for (int i = 0; i < 30 && !_stop; i++) Thread.Sleep(100);   // 3s back-off
        }
        _connected = false;
        SetStatus("已停止");
    }

    private static void Dispatch(string json)
    {
        string method;
        ApprovalItem item;
        if (ParseFrame(json, out item, out method))
        {
            if (method == "approval/requested")
            {
                Action<ApprovalItem> h = Requested;
                if (h != null) h(item);
            }
            else if (method == "approval/resolved")
            {
                Action<string> h = Resolved;
                if (h != null) h(item.ApprovalId);
            }
            return;
        }
        QuestionItem q;
        if (ParseQuestionFrame(json, out q, out method))
        {
            if (method == "question/requested")
            {
                Action<QuestionItem> h = QuestionRequested;
                if (h != null) h(q);
            }
            else if (method == "question/resolved")
            {
                Action<string> h = QuestionResolved;
                if (h != null) h(q.RpcId);
            }
            return;
        }
        TurnEvent te;
        if (ParseTurnFrame(json, out te))
        {
            Action<TurnEvent> h = TurnChanged;
            if (h != null) h(te);
        }
    }

    /// <summary>
    /// Parses a question frame. Returns true for "question/requested" (with the
    /// first question's text/options filled in) and "question/resolved" (with
    /// <see cref="QuestionItem.RpcId"/> = the settled questionRpcId). Pure.
    /// </summary>
    internal static bool ParseQuestionFrame(string json, out QuestionItem item, out string method)
    {
        item = null;
        method = "";
        try
        {
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return false;
            if (!"server-request".Equals(Str(d, "type"), StringComparison.Ordinal)) return false;
            method = Str(d, "method");
            if (method != "question/requested" && method != "question/resolved") return false;
            var p = Get(d, "payload");
            if (p == null) return false;

            var it = new QuestionItem();
            it.SessionId = Str(p, "sessionId");
            if (method == "question/resolved")
            {
                it.RpcId = Str(p, "questionRpcId");
                if (it.RpcId.Length == 0) it.RpcId = Str(d, "rpcId");
                if (it.RpcId.Length == 0) return false;
                item = it;
                return true;
            }

            it.RpcId = Str(d, "rpcId");
            if (it.RpcId.Length == 0) return false;
            object raw;
            if (!p.TryGetValue("questions", out raw)) return false;
            var arr = raw as object[];
            if (arr == null || arr.Length == 0) return false;
            var q0 = arr[0] as Dictionary<string, object>;
            if (q0 == null) return false;
            it.Text = Str(q0, "question");
            it.Detail = Str(q0, "detail");
            it.Header = Str(q0, "header");
            if (it.Text.Length == 0 && it.Header.Length == 0 && it.Detail.Length == 0) return false;
            object mv;
            if (q0.TryGetValue("multiSelect", out mv) && mv != null)
            {
                try { it.MultiSelect = Convert.ToBoolean(mv); } catch { }
            }
            object opts;
            if (q0.TryGetValue("options", out opts))
            {
                var oa = opts as object[];
                if (oa != null) it.OptionCount = oa.Length;
            }
            object intent;
            if (q0.TryGetValue("intent", out intent))
            {
                var idict = intent as Dictionary<string, object>;
                if (idict != null && Str(idict, "kind") == "plan-review") it.PlanReview = true;
            }
            item = it;
            return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Parses one mux frame. Returns true for approval frames only, with the
    /// envelope method in <paramref name="method"/> ("approval/requested" /
    /// "approval/resolved"). Pure and side-effect free so it can be tested.
    /// </summary>
    internal static bool ParseFrame(string json, out ApprovalItem item, out string method)
    {
        item = null;
        method = "";
        try
        {
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return false;
            if (!"server-request".Equals(Str(d, "type"), StringComparison.Ordinal)) return false;
            method = Str(d, "method");
            if (method.Length == 0) return false;
            var p = Get(d, "payload");
            if (p == null) return false;

            if (method == "approval/requested")
            {
                var it = new ApprovalItem();
                it.RpcId = Str(d, "rpcId");
                it.SessionId = Str(p, "sessionId");
                it.ApprovalId = Str(p, "approvalId");
                it.ToolName = Str(p, "toolName");
                it.CallId = Str(p, "callId");
                it.Reason = Str(p, "reason");
                if (it.RpcId.Length == 0 || it.ApprovalId.Length == 0) return false;
                item = it;
                return true;
            }
            if (method == "approval/resolved")
            {
                var it = new ApprovalItem();
                it.RpcId = Str(d, "rpcId");
                it.SessionId = Str(p, "sessionId");
                it.ApprovalId = Str(p, "approvalId");
                if (it.ApprovalId.Length == 0) return false;
                item = it;
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Parses a `session/event` frame into a turn boundary or a user message.
    /// Returns false for every other event type (assistant chunks, tool calls,
    /// ...). Pure.
    /// </summary>
    internal static bool ParseTurnFrame(string json, out TurnEvent ev)
    {
        ev = null;
        try
        {
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return false;
            if (!"server-request".Equals(Str(d, "type"), StringComparison.Ordinal)) return false;
            if (!"session/event".Equals(Str(d, "method"), StringComparison.Ordinal)) return false;
            var p = Get(d, "payload");
            if (p == null) return false;
            var e = Get(p, "event");
            if (e == null) return false;
            string type = Str(e, "type");
            if (type != "turn/start" && type != "turn/end" && type != "user/message") return false;
            var data = Get(e, "data");

            var t = new TurnEvent();
            t.Type = type;
            t.SessionId = Str(p, "sessionId");
            t.Turn = (int)Num(data, "turn");
            t.Started = type == "turn/start";
            if (type == "turn/end")
            {
                var reason = Get(data, "reason");
                t.Reason = reason == null ? "" : Str(reason, "kind");
            }
            ev = t;
            return true;
        }
        catch { }
        return false;
    }

    /// <summary>Builds the exact client-response body /api/respond expects.</summary>
    internal static string BuildBody(ApprovalItem it, string outcome)
    {
        var o = new Dictionary<string, object>();
        o["type"] = "client-response";
        o["rpcId"] = it.RpcId;
        var result = new Dictionary<string, object>();
        result["ok"] = true;
        var value = new Dictionary<string, object>();
        value["sessionId"] = it.SessionId;
        value["approvalId"] = it.ApprovalId;
        value["outcome"] = outcome;
        result["value"] = value;
        o["result"] = result;
        return new JavaScriptSerializer().Serialize(o);
    }

    /// <summary>Answers one approval. Returns "" on success, else a readable reason.</summary>
    internal static string Answer(ApprovalItem it, bool allow)
    {
        if (it == null) return "没有待处理的审批";
        try
        {
            string body = BuildBody(it, allow ? "allowed-once" : "rejected");
            var req = (HttpWebRequest)WebRequest.Create(Program.Url + "/api/respond");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            req.ContentLength = bytes.Length;
            using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string text = sr.ReadToEnd();
                if (text.IndexOf("\"accepted\":true", StringComparison.Ordinal) >= 0) return "";
                if (text.IndexOf("not-pending", StringComparison.Ordinal) >= 0)
                    return "该审批已失效（后端已不再等待它）";
                return "后端未接受：" + Trim(text);
            }
        }
        catch (Exception ex) { return Short(ex); }
    }

    private static void SetStatus(string s)
    {
        _status = s;
        Action h = StatusChanged;
        if (h != null) { try { h(); } catch { } }
    }

    private static string Short(Exception ex)
    {
        string m = ex.Message;
        if (ex is AggregateException && ex.InnerException != null) m = ex.InnerException.Message;
        if (ex is WebException) { try { m = ((WebException)ex).Status.ToString(); } catch { } }
        return m.Length > 60 ? m.Substring(0, 60) : m;
    }

    private static string Trim(string s)
    {
        if (s == null) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 80 ? s.Substring(0, 80) : s;
    }

    private static Dictionary<string, object> Get(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v)) return null;
        return v as Dictionary<string, object>;
    }

    private static long Num(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
        try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
        return v.ToString();
    }
}
