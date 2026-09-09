// ============================================================================
//  DSH Launcher - tray + status-panel control shell (no console window)
//
//  Behavior (per decisions):
//    * Opening DSH shows a small status panel (service state / URL / PID /
//      uptime / BuildId) with an "Open frontend" button.
//    * The app keeps running in the system tray. Closing (X) the window only
//      minimizes it to the tray; the service keeps running.
//    * "Exit" from the tray menu stops the DSH service (kills the node
//      process tree) and quits the app.
//    * Single instance: a second launch shows the existing window.
//    * The DSH node backend runs hidden; its stdout/stderr go to logs\dsh-web.log.
//
//  Future-facing seams (see 接口约定.md):
//    * ServiceHost  = process lifecycle (Start/Stop/Restart/IsRunning/OpenFrontend)
//    * Program/status snapshot = port/url/BuildId/uptime
//    * Balance (current source): the shell reads ~/.dsh/.credentials.yaml and
//      queries https://api.deepseek.com/user/balance directly. Future: switch
//      to a local DSH backend endpoint without touching the UI (接口约定.md).
//
//  Build:  build-dsh-exe.cmd   (csc /target:winexe, .NET Framework)
// ============================================================================
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using System.Management;

internal static class Program
{
    // Bump on EVERY source change (see 台账.md / README.md)
    internal const string BuildId = "2026-09-02-47";

    internal const string Url = "http://127.0.0.1:3080";
    internal const int Port = 3080;

    internal static readonly string RootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    internal static readonly string BinFile = Path.Combine(RootDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
    internal static readonly string LogDir = Path.Combine(RootDir, "logs");
    internal static readonly string LogFile = Path.Combine(LogDir, "dsh-web.log");
    internal static readonly string WorkDir = ResolveWorkDir();
    internal static readonly string IconFile = ResolveIconFile();

    // app.ico is the primary application icon; DSH.ico is the legacy fallback.
    private static string ResolveIconFile()
    {
        string[] cands = { Path.Combine(RootDir, "app.ico"), Path.Combine(RootDir, "DSH.ico") };
        foreach (string c in cands) if (File.Exists(c)) return c;
        return cands[0];
    }

    // The DSH backend must be launched with the WORKSPACE as its working
    // directory (DSH derives the workspace from cwd). Resolution order:
    //   1) workdir.txt next to the exe, if it names an existing directory;
    //   2) if this app folder is named "DSH-App", the folder above it
    //      (layout A: D:\DSH = workspace root, D:\DSH\DSH-App = software);
    //   3) otherwise the app folder itself (legacy single-folder layout).
    private static string ResolveWorkDir()
    {
        try
        {
            string f = Path.Combine(RootDir, "workdir.txt");
            if (File.Exists(f))
            {
                string t = File.ReadAllText(f).Trim();
                if (t.Length > 0 && Directory.Exists(t)) return t;
            }
        }
        catch { }
        if (Path.GetFileName(RootDir).Equals("DSH-App", StringComparison.OrdinalIgnoreCase))
        {
            DirectoryInfo parent = Directory.GetParent(RootDir);
            if (parent != null) return parent.FullName;
        }
        return RootDir;
    }

    private static Mutex _mutex;
    private static EventWaitHandle _showEvent;

    // ---- UI preference (theme) --------------------------------------------
    // Kept next to the balance history so the portable folder stays clean.
    internal static readonly string UiSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSH", "ui-settings.txt");

    /// <summary>Theme preference; defaults to dark (matching the DSH web frontend).</summary>
    internal static bool LoadDarkTheme()
    {
        return !LoadUiSetting("theme", "dark").Equals("light", StringComparison.OrdinalIgnoreCase);
    }

    internal static void SaveDarkTheme(bool dark)
    {
        SaveUiSetting("theme", dark ? "dark" : "light");
    }

    /// <summary>Generic key=value reader for %LOCALAPPDATA%\DSH\ui-settings.txt.</summary>
    internal static string LoadUiSetting(string key, string fallback)
    {
        try
        {
            if (File.Exists(UiSettingsFile))
            {
                string[] lines = File.ReadAllLines(UiSettingsFile);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(eq + 1).Trim();
                }
            }
        }
        catch { }
        return fallback;
    }

    /// <summary>Writes one key while preserving the others.</summary>
    internal static void SaveUiSetting(string key, string value)
    {
        try
        {
            var kept = new List<string>();
            if (File.Exists(UiSettingsFile))
            {
                string[] lines = File.ReadAllLines(UiSettingsFile);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    kept.Add(line);
                }
            }
            kept.Add(key + "=" + value);
            string dir = Path.GetDirectoryName(UiSettingsFile);
            if (!String.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(UiSettingsFile, kept.ToArray());
        }
        catch { }
    }

    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        _mutex = new Mutex(true, @"Local\DSH Launcher", out firstInstance);
        if (!firstInstance)
        {
            try
            {
                EventWaitHandle.OpenExisting(@"Local\DSH ShowFront").Set();
            }
            catch { }
            System.Windows.MessageBox.Show("DSH 已在运行，已打开它的窗口。", "DSH",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DSH ShowFront");

        try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { } // TLS 1.2

        WpfUI.RunMain();   // WPF entry (defined in WpfUI.cs)

        _mutex.ReleaseMutex();
    }

    // Reserved control/status API (see 接口约定.md)
    internal static bool ServerRunning()
    {
        try
        {
            using (var c = new TcpClient())
            {
                c.Connect("127.0.0.1", Port);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    internal static bool ConsumeShowRequest()
    {
        try { return _showEvent.WaitOne(0); }
        catch { return false; }
    }

    internal static string FindNode()
    {
        string local = Path.Combine(RootDir, ".tools", "node", "node.exe");
        if (File.Exists(local)) return local;
        string pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar != null)
        {
            foreach (string raw in pathVar.Split(';'))
            {
                string dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                string cand = Path.Combine(dir, "node.exe");
                if (File.Exists(cand)) return cand;
            }
        }
        return "node";
    }

    // ========================================================================
    //  Port owner lookup (for stopping a backend owned by another instance)
    //  GetExtendedTcpTable is used instead of shelling out to netstat.
    // ========================================================================
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    /// <summary>PID of the process listening on a local TCP port (0 when unknown).</summary>
    // GetExtendedTcpTable is a two-call API: ask for the size, then fetch. Two
    // traps: (1) the table changes constantly, so the second call can come back
    // with ERROR_INSUFFICIENT_BUFFER and a stale size - retry; (2) closed
    // connections linger as TIME_WAIT rows with OwningPid 0 and our own 3080
    // health probe creates them, so the FIRST row matching the port is not
    // necessarily the listener. Prefer a LISTEN row with a real owner.
    internal static int FindPortOwner(int port)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int size = 0;
            try
            {
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /*AF_INET*/, 5 /*TCP_TABLE_OWNER_PID_ALL*/, 0);
                if (size <= 0) { Thread.Sleep(25); continue; }
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    uint rc = GetExtendedTcpTable(buf, ref size, false, 2, 5, 0);
                    if (rc == 122) { Thread.Sleep(25); continue; }      // ERROR_INSUFFICIENT_BUFFER
                    if (rc != 0) return 0;
                    int count = Marshal.ReadInt32(buf);
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                    long baseAddr = buf.ToInt64() + 4;
                    int fallback = 0;
                    for (int i = 0; i < count; i++)
                    {
                        MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(
                            new IntPtr(baseAddr + (long)i * rowSize), typeof(MIB_TCPROW_OWNER_PID));
                        int localPort = ((int)(row.LocalPort & 0xFF) << 8) | (int)((row.LocalPort >> 8) & 0xFF);
                        if (localPort != port) continue;
                        if (row.OwningPid == 0) continue;               // TIME_WAIT / orphan row
                        if (row.State == 2 /*MIB_TCP_STATE_LISTEN*/) return (int)row.OwningPid;
                        if (fallback == 0) fallback = (int)row.OwningPid;
                    }
                    return fallback;                                    // table read fine, no listener found
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { Thread.Sleep(25); }
        }
        return 0;
    }

    /// <summary>Human-readable description of a PID: name, path, command line and
    /// whether its parent is still alive (a dead parent means the backend was
    /// left behind by a shell that died without stopping it).</summary>
    internal static string DescribeProcess(int pid)
    {
        if (pid <= 0) return "PID 未知（可能是权限不足）";
        string name = "?";
        string path = null;
        try
        {
            Process p = Process.GetProcessById(pid);
            name = p.ProcessName;
            try { if (p.MainModule != null) path = p.MainModule.FileName; } catch { }
        }
        catch { return "PID " + pid + "（进程已退出或无法访问）"; }

        string cmd = null;
        int parent = 0;
        try
        {
            var q = new ManagementObjectSearcher(
                "SELECT CommandLine,ParentProcessId FROM Win32_Process WHERE ProcessId=" + pid);
            foreach (ManagementBaseObject o in q.Get())
            {
                object c = o["CommandLine"];
                if (c != null) cmd = c.ToString();
                object pp = o["ParentProcessId"];
                if (pp != null) parent = Convert.ToInt32(pp);
                break;
            }
        }
        catch { }

        var sb = new StringBuilder();
        sb.Append("PID ").Append(pid).Append(" · ").Append(name);
        if (!String.IsNullOrEmpty(path)) sb.Append("\n").Append(path);
        if (!String.IsNullOrEmpty(cmd)) sb.Append("\n命令行：").Append(cmd);
        if (parent > 0)
        {
            bool alive = false;
            try { alive = Process.GetProcessById(parent) != null; }
            catch { alive = false; }
            sb.Append("\n父进程：PID ").Append(parent)
              .Append(alive ? "（仍在运行）" : "（已退出 → 上次遗留的残留服务）");
        }
        return sb.ToString();
    }

    /// <summary>Kill a process tree; returns true when taskkill reported success.</summary>
    internal static bool KillProcessTree(int pid)
    {
        if (pid <= 0) return false;
        try { if (pid == Process.GetCurrentProcess().Id) return false; }
        catch { }
        try
        {
            var psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process k = Process.Start(psi);
            if (k == null) return false;
            k.WaitForExit(8000);
            return k.HasExited && k.ExitCode == 0;
        }
        catch { return false; }
    }

    internal static void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法打开浏览器：\n" + ex.Message, "DSH",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    internal static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法打开：\n" + ex.Message, "DSH",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    // ============================================================================
    //  Balance (DeepSeek user/balance). API key is read from ~/.dsh/.credentials.yaml
    //  (refs.DEEPSEEK_API_KEY). The key is used in memory only and never logged.
    // ============================================================================
    internal sealed class BalanceInfo
    {
        internal string Currency;
        internal decimal Total;
        internal decimal Granted;
        internal decimal Topped;
    }

    internal static string ReadDeepSeekApiKey()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("DSH_HOME");
            if (String.IsNullOrEmpty(home))
            {
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            }
            string f = Path.Combine(home, ".credentials.yaml");
            if (!File.Exists(f)) return null;
            foreach (string raw in File.ReadAllLines(f))
            {
                string line = raw.Trim();
                if (line.StartsWith("DEEPSEEK_API_KEY:", StringComparison.OrdinalIgnoreCase))
                {
                    string v = line.Substring("DEEPSEEK_API_KEY:".Length).Trim().Trim('"', '\'', ' ', '\t');
                    if (v.Length > 0 && !v.StartsWith("${") && !v.StartsWith("<")) return v;
                }
            }
        }
        catch { }
        return null;
    }

    internal static List<BalanceInfo> FetchDeepSeekBalance(string apiKey)
    {
        var list = new List<BalanceInfo>();
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/user/balance");
        req.Method = "GET";
        req.Headers["Authorization"] = "Bearer " + apiKey;
        req.Accept = "application/json";
        req.Timeout = 20000;
        req.ReadWriteTimeout = 20000;
        string text;
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
        {
            text = sr.ReadToEnd();
        }
        int i = text.IndexOf("balance_infos");
        if (i < 0) return list;
        i = text.IndexOf('[', i);
        if (i < 0) return list;
        int j = text.IndexOf(']', i);
        if (j < 0) j = text.Length;
        string region = text.Substring(i, j - i + 1);
        foreach (Match m in Regex.Matches(region, @"\{[^{}]*\}"))
        {
            string obj = m.Value;
            BalanceInfo b = new BalanceInfo();
            b.Currency = GetJsonString(obj, "currency");
            b.Total = GetJsonDecimal(obj, "total_balance");
            b.Granted = GetJsonDecimal(obj, "granted_balance");
            b.Topped = GetJsonDecimal(obj, "topped_up_balance");
            if (b.Currency != null && b.Currency.Length > 0) list.Add(b);
        }
        return list;
    }

    private static string GetJsonString(string obj, string key)
    {
        Match m = Regex.Match(obj, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static decimal GetJsonDecimal(string obj, string key)
    {
        Match m = Regex.Match(obj, "\"" + key + "\"\\s*:\\s*\"?([0-9]+(?:\\.[0-9]+)?)\"?");
        if (!m.Success) return 0m;
        decimal d;
        return decimal.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0m;
    }

    internal static string FormatAmount(decimal v, string currency)
    {
        string s = v.ToString("0.00", CultureInfo.InvariantCulture);
        if (currency == "CNY") return "¥ " + s;
        if (String.IsNullOrEmpty(currency)) return s;
        return currency + " " + s;
    }

    // ============================================================================
    //  Balance history (local snapshots). Used to estimate spend over rolling
    //  1h / 3h / 6h windows from balance deltas. Stored per-machine under
    //  %LOCALAPPDATA%\DSH\balance-history.jsonl (not shipped with the app).
    // ============================================================================
    internal sealed class BalancePoint
    {
        internal DateTime T;
        internal decimal Total;
    }

    internal static string BalanceHistoryFile()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSH");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "balance-history.jsonl");
    }

    private static readonly object _histLock = new object();

    internal static List<BalancePoint> ReadBalanceHistory()
    {
        var list = new List<BalancePoint>();
        try
        {
            string f = BalanceHistoryFile();
            if (!File.Exists(f)) return list;
            foreach (string line in File.ReadAllLines(f))
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                Match mt = Regex.Match(line, "\"t\":\"([^\"]+)\"");
                Match mv = Regex.Match(line, "\"total\":\"?([0-9.]+)\"?");
                if (mt.Success && mv.Success)
                {
                    DateTime t;
                    decimal v;
                    if (DateTime.TryParse(mt.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)
                        && decimal.TryParse(mv.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        list.Add(new BalancePoint { T = t, Total = v });
                    }
                }
            }
        }
        catch { }
        return list;
    }

    internal static void AppendBalanceSnapshot(DateTime t, decimal total)
    {
        try
        {
            lock (_histLock)
            {
                string f = BalanceHistoryFile();
                var lines = new List<string>();
                if (File.Exists(f)) lines.AddRange(File.ReadAllLines(f));
                lines.Add("{\"t\":\"" + t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
                    + "\",\"total\":\"" + total.ToString("0.00", CultureInfo.InvariantCulture) + "\"}");
                // keep only the last ~25 hours so the file stays tiny
                DateTime cut = t.AddHours(-25);
                var keep = new List<string>();
                foreach (string line in lines)
                {
                    Match mt = Regex.Match(line, "\"t\":\"([^\"]+)\"");
                    DateTime lt;
                    if (mt.Success && DateTime.TryParse(mt.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out lt) && lt >= cut)
                    {
                        keep.Add(line);
                    }
                }
                File.WriteAllLines(f, keep.ToArray());
            }
        }
        catch { }
    }
}

// ============================================================================
//  ServiceHost - owns the DSH node backend process
// ============================================================================
internal class ServiceHost
{
    private Process _proc;
    private readonly object _sync = new object();
    private readonly object _logLock = new object();

    internal event EventHandler ProcessExited;

    internal bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                try { return _proc != null && !_proc.HasExited; }
                catch { return false; }
            }
        }
    }

    internal int Pid
    {
        get
        {
            lock (_sync)
            {
                try { return (_proc != null && !_proc.HasExited) ? _proc.Id : 0; }
                catch { return 0; }
            }
        }
    }

    internal DateTime? StartedAt
    {
        get
        {
            lock (_sync)
            {
                try { return (_proc != null && !_proc.HasExited) ? (DateTime?)_proc.StartTime : null; }
                catch { return null; }
            }
        }
    }

    internal void WriteLog(string line)
    {
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(Program.LogDir);
                File.AppendAllText(Program.LogFile, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    internal void Start()
    {
        lock (_sync)
        {
            try { if (_proc != null && !_proc.HasExited) return; }
            catch { }
            _proc = null;
        }

        if (!File.Exists(Program.BinFile))
        {
            // Do NOT fail silently: give the user an actionable message.
            WriteLog("[DSH] 未找到后端入口: " + Program.BinFile);
            System.Windows.MessageBox.Show(
                "无法启动 DSH 服务：未找到\r\n" + Program.BinFile +
                "\r\n\r\n可能原因：软件文件夹被移动后，本文件夹里的 node_modules 没有就位" +
                "（pnpm 的 node_modules 不能整夹搬动）。\r\n" +
                "解决办法：退出 DSH 后重新运行 migrate-to-dsh-app.cmd（自动在新位置重链依赖），" +
                "或直接重跑 install-dsh.cmd 重新安装。",
                "DSH", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        var psi = new ProcessStartInfo();
        psi.FileName = Program.FindNode();
        psi.Arguments = "\"" + Program.BinFile + "\" web";
        psi.WorkingDirectory = Program.WorkDir;   // cwd = workspace (DSH derives the workspace from cwd)
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        try { psi.StandardOutputEncoding = Encoding.UTF8; } catch { }
        try { psi.StandardErrorEncoding = Encoding.UTF8; } catch { }

        Process p = new Process();
        p.StartInfo = psi;
        p.EnableRaisingEvents = true;
        p.OutputDataReceived += OnOutput;
        p.ErrorDataReceived += OnError;
        p.Exited += delegate
        {
            EventHandler ev = ProcessExited;
            if (ev != null) ev(this, EventArgs.Empty);
        };

        try
        {
            if (!p.Start())
            {
                WriteLog("[DSH] 进程启动失败（Start 返回 false）");
                return;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            JobKill.Assign(p);
            WriteLog("[DSH] 服务启动 PID=" + p.Id + "  node=" + psi.FileName);
        }
        catch (Exception ex)
        {
            WriteLog("[DSH] 启动异常: " + ex.Message);
            System.Windows.MessageBox.Show("启动 DSH 服务失败：\n" + ex.Message, "DSH",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        lock (_sync) { _proc = p; }
    }

    internal void Stop()
    {
        Process p;
        lock (_sync) { p = _proc; }
        if (p == null) return;

        try
        {
            bool alive = false;
            try { alive = !p.HasExited; } catch { }
            if (alive)
            {
                WriteLog("[DSH] 正在停止服务 PID=" + p.Id);
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process k = Process.Start(psi);
                if (k != null) k.WaitForExit(8000);
            }
        }
        catch (Exception ex)
        {
            WriteLog("[DSH] 停止异常: " + ex.Message);
        }
        finally
        {
            lock (_sync)
            {
                try { if (_proc != null) _proc.Dispose(); } catch { }
                _proc = null;
            }
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrEmpty(e.Data)) WriteLog(e.Data);
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrEmpty(e.Data)) WriteLog(e.Data);
    }
}

