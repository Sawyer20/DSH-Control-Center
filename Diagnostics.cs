using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

/// <summary>
/// P0-4 one-click diagnostic package (see docs/roadmap.md).
///
/// Collects environment / service / ports / processes / log tail / projcache
/// summary / dependency integrity / recent events into ONE zip under logs\,
/// plus a human-readable summary.txt so the person reading the report does not
/// have to parse raw files.
///
/// Privacy: the account name and the profile path are redacted, and credentials
/// are never read (no .credentials.yaml, no API key, no session content).
/// </summary>
internal static class Diagnostics
{
    /// <summary>Hard cap for the produced archive (roadmap acceptance: default &lt;= 5MB).</summary>
    internal const long MaxBytes = 5 * 1024 * 1024;

    private const int LogTailLines = 300;
    private const int RingMax = 60;
    private const long LogTailBytes = 96 * 1024;

    private static readonly List<string> _ring = new List<string>();

    /// <summary>Remembers a shell event so the next diagnostic package can list it.</summary>
    internal static void Note(string title, string sub)
    {
        try
        {
            lock (_ring)
            {
                _ring.Insert(0, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + title
                    + (String.IsNullOrEmpty(sub) ? "" : " — " + sub));
                while (_ring.Count > RingMax) _ring.RemoveAt(_ring.Count - 1);
            }
        }
        catch { }
    }

    /// <summary>
    /// Builds the zip and returns its full path. Never throws: on failure the
    /// caller gets null and <see cref="LastError"/> explains why.
    /// </summary>
    internal static string Create(string state, int pid, DateTime? startedAt, bool dark)
    {
        LastError = "";
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string tmp = Path.Combine(Path.GetTempPath(), "dsh-diag-" + stamp);
        string zip = Path.Combine(Program.LogDir, "diagnostics-" + stamp + ".zip");
        try
        {
            Directory.CreateDirectory(tmp);
            Directory.CreateDirectory(Program.LogDir);

            List<SessionUsage> sessions = new List<SessionUsage>();
            Pricing pricing = null;
            Lifetime life = null;
            try
            {
                pricing = Usage.LoadPricing();
                sessions = Usage.ReadSessions();
                Usage.ComputeCosts(pricing, sessions);
                life = Usage.LoadLifetime(pricing, sessions);
            }
            catch { }

            Write(tmp, "summary.txt", BuildSummary(state, pid, startedAt, dark, sessions, pricing, life));
            Write(tmp, "env.txt", BuildEnv());
            Write(tmp, "service.txt", BuildService(state, pid, startedAt));
            Write(tmp, "processes.txt", BuildProcesses());
            Write(tmp, "deps.txt", BuildDeps());
            Write(tmp, "projcache-summary.txt", BuildSessions(sessions, pricing, life));
            Write(tmp, "events.txt", BuildEvents());
            Write(tmp, "log-tail.txt", BuildLogTail());
            Write(tmp, "settings.txt", BuildSettings());

            ZipDirectory(tmp, zip);

            long len = new FileInfo(zip).Length;
            if (len > MaxBytes)
            {
                // Should not happen (everything here is text), but keep the
                // acceptance guarantee: rebuild without the log tail.
                File.Delete(zip);
                File.WriteAllText(Path.Combine(tmp, "log-tail.txt"),
                    "(日志过长，已省略；完整日志见 " + Program.LogFile + ")", Encoding.UTF8);
                ZipDirectory(tmp, zip);
                len = new FileInfo(zip).Length;
            }
            Note("已生成诊断包", Path.GetFileName(zip) + " · " + (len / 1024) + " KB");
            return zip;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    internal static string LastError = "";

    // ---- sections ----------------------------------------------------------

    private static string BuildSummary(string state, int pid, DateTime? startedAt, bool dark,
        List<SessionUsage> sessions, Pricing pricing, Lifetime life)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DSH 控制中心 · 诊断摘要");
        sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("BuildId：" + Program.BuildId);
        sb.AppendLine();
        sb.AppendLine("【服务】");
        sb.AppendLine("  状态：" + state);
        sb.AppendLine("  本实例 PID：" + (pid > 0 ? pid.ToString() : "—"));
        if (startedAt.HasValue) sb.AppendLine("  启动时间：" + startedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("  端口：" + Program.Port + "（" + Program.Url + "）");
        int owner = 0;
        try { owner = Program.FindPortOwner(Program.Port); } catch { }
        sb.AppendLine("  端口占用者 PID：" + (owner > 0 ? owner.ToString() : "（无）"));
        if (owner > 0)
        {
            string desc = "";
            try { desc = Program.DescribeProcess(owner); } catch { }
            sb.AppendLine("  占用者描述：" + OneLine(desc));
        }
        sb.AppendLine();
        sb.AppendLine("【环境】");
        sb.AppendLine("  系统：" + Environment.OSVersion.VersionString + " · " + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
        sb.AppendLine("  .NET：" + Environment.Version);
        sb.AppendLine("  Node：" + NodeVersion());
        sb.AppendLine("  工作区：" + Redact(Program.WorkDir));
        sb.AppendLine("  程序目录：" + Redact(Program.RootDir));
        sb.AppendLine("  主题：" + (dark ? "深色" : "浅色"));
        sb.AppendLine();
        sb.AppendLine("【用量】");
        if (life != null)
        {
            sb.AppendLine("  统计起始：" + life.FirstSeen.ToString("yyyy-MM-dd") + "（第 " + life.Days + " 天）");
            sb.AppendLine("  累计：" + Usage.Money(life.Cost, pricing) + " · "
                + Usage.Tokens(life.In + life.Out + life.CacheRead + life.CacheWrite) + " tokens");
        }
        sb.AppendLine("  会话数：" + sessions.Count);
        long tin = 0, tout = 0, tcr = 0;
        foreach (SessionUsage s in sessions) { tin += s.UncachedInput; tout += s.Output; tcr += s.CacheRead; }
        sb.AppendLine("  tokens：输入 " + Usage.Tokens(tin) + " / 输出 " + Usage.Tokens(tout) + " / 缓存读 " + Usage.Tokens(tcr));
        sb.AppendLine();
        sb.AppendLine("【附件】");
        sb.AppendLine("  env.txt               运行环境与路径（已脱敏）");
        sb.AppendLine("  service.txt           服务状态、端口属主与进程描述");
        sb.AppendLine("  processes.txt         node / DSH 进程快照");
        sb.AppendLine("  deps.txt              依赖完整性检查");
        sb.AppendLine("  projcache-summary.txt 每会话用量与状态摘要（不含对话内容）");
        sb.AppendLine("  events.txt            壳内最近事件");
        sb.AppendLine("  log-tail.txt          dsh-web.log 尾部 " + LogTailLines + " 行");
        sb.AppendLine("  settings.txt          ui-settings.txt（已脱敏）");
        sb.AppendLine();
        sb.AppendLine("隐私说明：不含 API Key、不含 ~/.dsh/.credentials.yaml、不含会话正文；");
        sb.AppendLine("用户名与用户目录路径已替换为 <user> / %USERPROFILE%。");
        return sb.ToString();
    }

    private static string BuildEnv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[系统]");
        sb.AppendLine("OS              : " + Environment.OSVersion.VersionString);
        sb.AppendLine("64 位系统        : " + Environment.Is64BitOperatingSystem);
        sb.AppendLine("64 位进程        : " + Environment.Is64BitProcess);
        sb.AppendLine("处理器数         : " + Environment.ProcessorCount);
        sb.AppendLine("区域/语言        : " + CultureInfo.CurrentCulture.Name + " / " + CultureInfo.CurrentUICulture.Name);
        sb.AppendLine("时区             : " + TimeZone.CurrentTimeZone.StandardName);
        sb.AppendLine();
        sb.AppendLine("[运行时]");
        sb.AppendLine(".NET / CLR      : " + Environment.Version);
        sb.AppendLine("exe             : " + Redact(SafeExePath()));
        sb.AppendLine("Node            : " + NodeVersion() + "  (" + Redact(SafeNodePath()) + ")");
        sb.AppendLine("DSH BuildId     : " + Program.BuildId);
        sb.AppendLine();
        sb.AppendLine("[路径]");
        sb.AppendLine("程序目录         : " + Redact(Program.RootDir));
        sb.AppendLine("工作区           : " + Redact(Program.WorkDir));
        sb.AppendLine("后端入口         : " + Redact(Program.BinFile));
        sb.AppendLine("日志             : " + Redact(Program.LogFile));
        sb.AppendLine("UI 偏好          : " + Redact(Program.UiSettingsFile));
        sb.AppendLine();
        sb.AppendLine("[屏幕]");
        try
        {
            sb.AppendLine("虚拟桌面         : " + SystemParameters.VirtualScreenWidth + "x" + SystemParameters.VirtualScreenHeight
                + " @ " + SystemParameters.VirtualScreenLeft + "," + SystemParameters.VirtualScreenTop);
            sb.AppendLine("主屏             : " + SystemParameters.PrimaryScreenWidth + "x" + SystemParameters.PrimaryScreenHeight);
        }
        catch { }
        return sb.ToString();
    }

    private static string BuildService(string state, int pid, DateTime? startedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("状态            : " + state);
        sb.AppendLine("本实例 PID      : " + (pid > 0 ? pid.ToString() : "—"));
        sb.AppendLine("启动时间         : " + (startedAt.HasValue ? startedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—"));
        sb.AppendLine("端口             : " + Program.Port);
        sb.AppendLine("地址             : " + Program.Url);
        int owner = 0;
        try { owner = Program.FindPortOwner(Program.Port); } catch { }
        sb.AppendLine("3080 属主 PID   : " + (owner > 0 ? owner.ToString() : "（无）"));
        if (owner > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[属主进程描述]");
            string d = "";
            try { d = Program.DescribeProcess(owner); } catch (Exception ex) { d = "读取失败: " + ex.Message; }
            sb.AppendLine(Redact(d));
        }
        return sb.ToString();
    }

    private static string BuildProcesses()
    {
        var sb = new StringBuilder();
        AppendProcesses(sb, "node");
        AppendProcesses(sb, "DSH");
        AppendProcesses(sb, "dsh");
        return sb.ToString();
    }

    private static void AppendProcesses(StringBuilder sb, string name)
    {
        sb.AppendLine("[进程] " + name);
        try
        {
            Process[] ps = Process.GetProcessesByName(name);
            if (ps.Length == 0) sb.AppendLine("  （无）");
            foreach (Process p in ps)
            {
                string line = "  PID " + p.Id;
                try { line += " · 启动 " + p.StartTime.ToString("MM-dd HH:mm:ss"); } catch { }
                try { line += " · 内存 " + (p.WorkingSet64 / 1024 / 1024) + "MB"; } catch { }
                try { line += " · " + Redact(p.MainModule.FileName); } catch { line += " · （路径不可读）"; }
                sb.AppendLine(line);
            }
        }
        catch (Exception ex) { sb.AppendLine("  （读取失败: " + ex.Message + "）"); }
        sb.AppendLine();
    }

    private static string BuildDeps()
    {
        var sb = new StringBuilder();
        string nm = Path.Combine(Program.RootDir, "node_modules");
        string pnpm = Path.Combine(nm, ".pnpm");
        sb.AppendLine("node_modules          : " + (Directory.Exists(nm) ? "存在" : "缺失"));
        sb.AppendLine("node_modules/.pnpm    : " + (Directory.Exists(pnpm) ? Directory.GetDirectories(pnpm).Length + " 个包" : "缺失"));
        sb.AppendLine("后端入口 bin.js       : " + (File.Exists(Program.BinFile) ? "存在" : "缺失"));
        sb.AppendLine("dsh 版本              : " + DshVersion());
        sb.AppendLine("pnpm-lock.yaml        : " + (File.Exists(Path.Combine(Program.RootDir, "pnpm-lock.yaml")) ? "存在" : "缺失"));
        sb.AppendLine("node.exe（.tools）    : " + (File.Exists(Path.Combine(Program.RootDir, ".tools", "node", "node.exe")) ? "便携版" : "使用 PATH"));
        sb.AppendLine("Node 版本             : " + NodeVersion());
        sb.AppendLine();
        sb.AppendLine("[原生模块 prebuild 检查]");
        Check(sb, pnpm, "node-pty", Path.Combine("node_modules", ".pnpm"));
        Check(sb, pnpm, "koffi", Path.Combine("node_modules", ".pnpm"));
        Check(sb, pnpm, "sharp", Path.Combine("node_modules", ".pnpm"));
        sb.AppendLine();
        sb.AppendLine("说明：本项目原生模块均为预编译二进制，无需 VS Build Tools / Python（KI-10）。");
        return sb.ToString();
    }

    private static void Check(StringBuilder sb, string pnpm, string pkg, string rel)
    {
        string state = "未安装";
        try
        {
            if (Directory.Exists(pnpm))
            {
                string[] hits = Directory.GetDirectories(pnpm, pkg + "@*");
                if (hits.Length > 0) state = "已安装（" + Path.GetFileName(hits[0]) + "）";
            }
        }
        catch (Exception ex) { state = "读取失败: " + ex.Message; }
        sb.AppendLine("  " + pkg.PadRight(10) + " : " + state);
    }

    private static string BuildSessions(List<SessionUsage> sessions, Pricing pricing, Lifetime life)
    {
        var sb = new StringBuilder();
        sb.AppendLine("会话数：" + sessions.Count + "（按最近提问排序；不含对话正文）");
        if (life != null)
            sb.AppendLine("累计：" + Usage.Money(life.Cost, pricing) + " · 起始 " + life.FirstSeen.ToString("yyyy-MM-dd") + " · 第 " + life.Days + " 天");
        sb.AppendLine();
        var sorted = new List<SessionUsage>(sessions);
        sorted.Sort(delegate(SessionUsage a, SessionUsage b) { return b.LastPromptAt.CompareTo(a.LastPromptAt); });
        foreach (SessionUsage s in sorted)
        {
            sb.AppendLine("- " + (s.Title.Length > 0 ? s.Title : "(无标题)"));
            sb.AppendLine("    id=" + ShortId(s.Id) + " · 轮次 " + s.Turns + " · 步数 " + s.Steps
                + " · 最近 " + (s.LastPromptAt == DateTime.MinValue ? "—" : s.LastPromptAt.ToString("MM-dd HH:mm")));
            sb.AppendLine("    tokens 输入 " + s.UncachedInput + " / 输出 " + s.Output + " / 缓存读 " + s.CacheRead + " / 缓存写 " + s.CacheWrite
                + " · 成本 " + Usage.Money(s.Cost, pricing));
            sb.AppendLine("    上下文 " + Usage.Tokens(s.SurfaceTokens) + " / " + Usage.Tokens(s.ContextWindow)
                + " · 权限 " + (s.PermissionPreset.Length > 0 ? s.PermissionPreset : "—")
                + (s.Approval.Length > 0 ? " · 审批 " + s.Approval : "")
                + " · 计划 " + (s.PlanActive ? "进行中" : "无") + " · 待办 " + s.TodoCount
                + " · 进行中调用 " + s.PendingCalls);
        }
        return sb.ToString();
    }

    private static string BuildEvents()
    {
        var sb = new StringBuilder();
        sb.AppendLine("壳内最近事件（最多 " + RingMax + " 条，最新在上）");
        sb.AppendLine();
        lock (_ring)
        {
            if (_ring.Count == 0) sb.AppendLine("（本次运行尚未产生事件）");
            foreach (string l in _ring) sb.AppendLine(l);
        }
        return sb.ToString();
    }

    private static string BuildLogTail()
    {
        try
        {
            if (!File.Exists(Program.LogFile)) return "（无日志文件：" + Redact(Program.LogFile) + "）";
            string[] lines = File.ReadAllLines(Program.LogFile);
            int take = Math.Min(LogTailLines, lines.Length);
            var sb = new StringBuilder();
            sb.AppendLine("文件：" + Redact(Program.LogFile) + " · 共 " + lines.Length + " 行，以下为最后 " + take + " 行");
            sb.AppendLine();
            for (int i = lines.Length - take; i < lines.Length; i++) sb.AppendLine(lines[i]);
            string text = sb.ToString();
            if (text.Length > LogTailBytes)
                text = "(仅保留尾部 " + (LogTailBytes / 1024) + "KB)\r\n" + text.Substring(text.Length - (int)LogTailBytes);
            return Redact(text);
        }
        catch (Exception ex) { return "（读取日志失败: " + ex.Message + "）"; }
    }

    private static string BuildSettings()
    {
        try
        {
            if (!File.Exists(Program.UiSettingsFile)) return "（无 " + Redact(Program.UiSettingsFile) + "）";
            return Redact(File.ReadAllText(Program.UiSettingsFile));
        }
        catch (Exception ex) { return "（读取失败: " + ex.Message + "）"; }
    }

    // ---- helpers -----------------------------------------------------------

    private static void Write(string dir, string name, string text)
    {
        File.WriteAllText(Path.Combine(dir, name), Redact(text), new UTF8Encoding(false));
    }

    private static void ZipDirectory(string dir, string zipPath)
    {
        using (FileStream fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            string[] files = Directory.GetFiles(dir);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                ZipArchiveEntry e = za.CreateEntry(Path.GetFileName(f), CompressionLevel.Optimal);
                using (Stream es = e.Open())
                using (FileStream input = File.OpenRead(f))
                {
                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, n);
                }
            }
        }
    }

    private static string Redact(string s)
    {
        if (String.IsNullOrEmpty(s)) return s;
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrEmpty(profile)) s = s.Replace(profile, "%USERPROFILE%");
            string user = Environment.UserName;
            if (!String.IsNullOrEmpty(user) && user.Length > 2) s = s.Replace(user, "<user>");
        }
        catch { }
        return s;
    }

    private static string OneLine(string s)
    {
        if (String.IsNullOrEmpty(s)) return "—";
        return s.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string ShortId(string id)
    {
        if (String.IsNullOrEmpty(id)) return "—";
        return id.Length <= 10 ? id : id.Substring(0, 10);
    }

    private static string SafeExePath()
    {
        try { return Process.GetCurrentProcess().MainModule.FileName; }
        catch { return Program.RootDir + "\\DSH.exe"; }
    }

    private static string SafeNodePath()
    {
        try { return Program.FindNode(); } catch { return "（未知）"; }
    }

    private static string NodeVersion()
    {
        return RunVersion(SafeNodePath());
    }

    private static string RunVersion(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, "--version");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return outp.Length > 0 ? outp : "（无输出）";
            }
        }
        catch (Exception ex) { return "（不可用: " + ex.Message + "）"; }
    }

    private static string DshVersion()
    {
        try
        {
            string f = Path.Combine(Program.RootDir, "node_modules", "@deepseek-ai", "dsh", "package.json");
            if (!File.Exists(f)) return "（未安装）";
            Match m = Regex.Match(File.ReadAllText(f), "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : "（未知）";
        }
        catch (Exception ex) { return "（读取失败: " + ex.Message + "）"; }
    }
}
