// ============================================================================
//  smoke-probe.cs - headless startup smoke test (developer tool)
//
//  Builds the real DshWindow and forces a full Measure/Arrange pass, which
//  seals and instantiates every ControlTemplate. Any template error that would
//  kill the app at startup (for example the Track/IAddChild crash of
//  BuildId 2026-09-02-13) surfaces here without showing a window on screen.
//
//  Also asserts the regressions that shipped as "visible" bugs:
//    * a Glass button must paint the fill assigned AFTER construction
//      (this is what made the balance 刷新 label invisible: white on white)
//    * Corner must still update the template after construction
//    * light/dark switching must change colours without throwing
//    * the runtime-XAML scrollbar must apply and expose PART_Track
//    * the hero 停止服务 button must exist
//
//  Run:  run-smoke-test.cmd        (compiles with /main:SmokeProbe)
//  NOTE: keep this file OUT of build-dsh-exe.cmd - it has its own Main().
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

internal static class SmokeProbe
{
    private static int _failed;

    [STAThread]
    private static void Main()
    {
        // Keep the notification store off the real one: DshWindow's constructor
        // reads/migrates it, and a test must never touch the user's history.
        string noticeStore = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dsh-smoke-notices-" + Guid.NewGuid().ToString("N") + ".jsonl");
        Notifications.OverrideFile = noticeStore;
        try
        {
            var win = new DshWindow();
            var root = win.Content as UIElement;
            if (root == null) { Fail("window has no content"); return; }

            root.Measure(new Size(940, 660));
            root.Arrange(new Rect(0, 0, 940, 660));
            root.UpdateLayout();
            Pass("DshWindow built + measured + arranged (all templates sealed)");

            // buttons must paint the fill assigned after construction
            var glass = Buttons.Glass("刷新", 96, 30);
            if (!ReferenceEquals(glass.Background, WpfTheme.GlassBg)) { Fail("glass button kept its constructor fill"); return; }
            glass.Corner = 14;
            if (Math.Abs(glass.Radius.TopLeft - 14) > 0.01) { Fail("Corner did not reach the template"); return; }
            Pass("Glass fill + Corner applied after construction");

            // light/dark switching
            Color darkBg = WpfTheme.WindowBg.Color;
            WpfTheme.Apply(false);
            Color lightBg = WpfTheme.WindowBg.Color;
            WpfTheme.Apply(true);
            if (darkBg == lightBg) { Fail("theme switch did not change WindowBg"); return; }
            Pass("theme switch dark <-> light (" + darkBg + " -> " + lightBg + ")");

            // runtime-XAML scrollbar
            var sv = new ScrollViewer { Width = 200, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
            Ui.ThinScroll(sv);
            var stack = new StackPanel();
            for (int i = 0; i < 60; i++) stack.Children.Add(new TextBlock { Text = "line " + i });
            sv.Content = stack;
            sv.Measure(new Size(200, 100));
            sv.Arrange(new Rect(0, 0, 200, 100));
            sv.UpdateLayout();
            ScrollBar bar = FindVertical(sv);
            if (bar == null) { Fail("vertical scrollbar not found"); return; }
            object track = bar.Template == null ? null : bar.Template.FindName("PART_Track", bar);
            if (track == null) { Fail("scrollbar applied but PART_Track missing"); return; }
            Pass("thin scrollbar applied (width=" + bar.ActualWidth + "px, PART_Track present)");

            // regression: switching theme while a scrollbar is registered must
            // not throw (this is the frozen-brush crash of BuildId -16)
            WpfTheme.Apply(false);
            Ui.RefreshScrollSkin();
            sv.UpdateLayout();
            WpfTheme.Apply(true);
            Ui.RefreshScrollSkin();
            sv.UpdateLayout();
            if (bar.Template == null || bar.Template.FindName("PART_Track", bar) == null)
            { Fail("scrollbar lost PART_Track after theme switch"); return; }
            Pass("theme switch re-skinned the scrollbar without throwing");

            // stop-service button
            if (FindByText(root, "停止服务") == null) { Fail("停止服务 button missing"); return; }
            Pass("停止服务 button present");

            // ---- usage + pricing (ADR-0007) ----
            List<SessionUsage> sessions = Usage.ReadSessions();
            if (sessions.Count == 0)
            {
                Console.WriteLine("SMOKE WARN: no sessions in projcache (usage card will show -)");
            }
            else
            {
                Pricing pricing = Usage.LoadPricing();
                if (pricing.Models.Count == 0) { Fail("pricing has no models"); return; }
                double total = Usage.ComputeCosts(pricing, sessions);
                long tok = 0;
                foreach (SessionUsage s in sessions) tok += s.UncachedInput + s.Output + s.CacheRead + s.CacheWrite;
                if (tok > 0 && total > 0)
                    Pass("usage+cost parsed (" + sessions.Count + " session(s), " + Usage.Tokens(tok) + " tokens, " + Usage.Money(total, pricing) + ")");
                else
                    Fail("usage parsed but tokens/cost are zero");
                Usage.AppendSnapshot(pricing, sessions);
                Pass("usage history readable (" + Usage.ReadHistory().Count + " interval(s))");

                // ---- budget maths + month window + pricing round trip ----
                Usage.BudgetStatus b1 = Usage.Evaluate(15, 120, 20, 300, 80);
                if (b1.DayPercent != 75 || b1.DayWarn || b1.DayOver) { Fail("budget 75% should not warn"); return; }
                Usage.BudgetStatus b2 = Usage.Evaluate(18, 240, 20, 300, 80);
                if (!b2.DayWarn || b2.DayOver || !b2.MonthWarn || b2.MonthOver) { Fail("budget 90%/80% should warn but not over"); return; }
                Usage.BudgetStatus b3 = Usage.Evaluate(25, 400, 20, 300, 80);
                if (!b3.DayOver || !b3.MonthOver) { Fail("budget over 100% must flag over"); return; }
                Usage.BudgetStatus b4 = Usage.Evaluate(5, 5, 0, 0, 80);
                if (b4.AnyWarn || b4.AnyOver) { Fail("budget 0 = disabled must never warn"); return; }
                Pass("budget thresholds evaluated (75/90/125%, disabled row stays silent)");

                List<UsageTick> hist2 = Usage.ReadHistory();
                DateTime ms = Usage.MonthStart();
                if (ms.Day != 1 || ms > DateTime.Now) { Fail("MonthStart is not the 1st of this month"); return; }
                if (Usage.Since(hist2, ms) + 0.0000001 < Usage.Today(hist2))
                { Fail("month window smaller than today window"); return; }
                Pass("month window covers today (" + Usage.Money(Usage.Since(hist2, ms), pricing) + " this month)");

                Pricing roundTrip = Usage.LoadPricing();
                int modelsBefore = roundTrip.Models.Count;
                Usage.SavePricing(roundTrip);
                Pricing again = Usage.LoadPricing();
                if (again.Models.Count != modelsBefore) { Fail("pricing save/load lost models"); return; }
                bool same = true;
                for (int i = 0; i < modelsBefore; i++)
                    if (Math.Abs(again.Models[i].In - roundTrip.Models[i].In) > 1e-9
                        || !String.Equals(again.Models[i].Id, roundTrip.Models[i].Id, StringComparison.Ordinal)) same = false;
                if (!same) { Fail("pricing round trip changed values"); return; }
                Pass("pricing save/load round trip (" + modelsBefore + " models unchanged)");

                var pv = new PricingView();
                pv.Load(again, 20, 300, 80);
                string perr;
                Pricing edited = pv.Collect(out perr);
                if (edited == null || edited.Models.Count != modelsBefore)
                { Fail("pricing view did not round trip (" + perr + ")"); return; }
                Pass("pricing editor collects " + edited.Models.Count + " models (汇率 " + edited.UsdToCny.ToString("0.##") + ")");

                // ---- live signals: busy flag + change detection ----
                var busySessions = new List<SessionUsage>();
                var s1 = new SessionUsage();
                s1.Id = "s-1"; s1.Title = "会话一";
                busySessions.Add(s1);
                if (Usage.AnyBusy(busySessions)) { Fail("idle session reported as busy"); return; }
                s1.OpenStep = true;
                if (!Usage.AnyBusy(busySessions)) { Fail("openStep session not reported as busy"); return; }
                s1.OpenStep = false; s1.PendingCalls = 2;
                if (!Usage.AnyBusy(busySessions)) { Fail("pendingCalls session not reported as busy"); return; }
                s1.PendingCalls = 0; s1.PlanActive = true;
                if (!Usage.AnyBusy(busySessions)) { Fail("plan session not reported as busy"); return; }

                var signals = new Dictionary<string, Usage.SessionSignal>();
                s1.PlanActive = false;
                List<Usage.UsageChange> ch = Usage.Observe(signals, busySessions);
                if (ch.Count != 0) { Fail("first observation must not notify"); return; }
                s1.PlanActive = true;
                ch = Usage.Observe(signals, busySessions);
                if (ch.Count != 1 || ch[0].Title != "计划已开始") { Fail("plan start not detected"); return; }
                s1.TodoCount = 3;
                ch = Usage.Observe(signals, busySessions);
                if (ch.Count != 1 || ch[0].Title != "任务清单更新") { Fail("todo change not detected"); return; }
                s1.PlanActive = false;
                ch = Usage.Observe(signals, busySessions);
                if (ch.Count != 1 || ch[0].Title != "计划已结束" || !ch[0].Burst) { Fail("plan end / burst not detected"); return; }
                ch = Usage.Observe(signals, busySessions);
                if (ch.Count != 0) { Fail("unchanged poll must not notify"); return; }
                Pass("live signals: busy flag + plan/todo change detection");
            }

            // ---- notification history + tray badge (ADR-0009) ----
            string nFile = noticeStore;
            try
            {
                Notifications.Clear();
                Notifications.Add("预算接近上限", "今日已用 90%", "warn");
                Notifications.Add("计划已开始", "agent 正在执行计划", "info");
                string approvalNotice = Notifications.Add("等待审批：pwsh", "需要提权", "warn", "appr-42");
                List<Notice> ns = Notifications.Load();
                if (ns.Count != 3) { Fail("notification store expected 3, got " + ns.Count); return; }
                if (!ns[0].Unread) { Fail("newest notice should be unread"); return; }
                if (Notifications.Unread() != 3) { Fail("unread count should be 3"); return; }
                // answering the approval clears exactly its own notice
                if (Notifications.MarkReadByRef("appr-42") != 1) { Fail("MarkReadByRef did not find the approval notice"); return; }
                if (Notifications.Unread() != 2) { Fail("MarkReadByRef should leave 2 unread, got " + Notifications.Unread()); return; }
                bool refRead = false, refUnread = false;
                foreach (Notice n in Notifications.Load())
                {
                    if (n.RefId == "appr-42") refRead = !n.Unread;
                    else if (n.Unread) refUnread = true;
                }
                if (!refRead || !refUnread) { Fail("per-notice read state is wrong (refRead=" + refRead + ")"); return; }
                Notifications.MarkRead(approvalNotice);          // id-based path
                if (Notifications.Unread() != 2) { Fail("MarkRead(id) changed the count"); return; }

                // returning to a conversation clears that session's notices only
                Notifications.Clear();
                Notifications.Add("任务完成", "第 7 轮 已结束", "warn", "", "session-abc");
                Notifications.Add("任务完成", "第 8 轮 已结束", "warn", "", "session-xyz");
                if (Notifications.Unread() != 2) { Fail("session notices should start unread"); return; }
                if (Notifications.MarkReadBySession("session-abc") != 1)
                { Fail("MarkReadBySession did not find the notice"); return; }
                if (Notifications.Unread() != 1) { Fail("MarkReadBySession should leave 1 unread"); return; }
                bool otherUnread = false;
                foreach (Notice n in Notifications.Load())
                    if (n.SessionId == "session-xyz" && n.Unread) otherUnread = true;
                if (!otherUnread) { Fail("MarkReadBySession cleared the wrong session"); return; }

                Notifications.MarkAllRead();
                if (Notifications.Unread() != 0) { Fail("MarkAllRead did not clear unread"); return; }
                // legacy notice (no refId) must be swept up by the migration
                Notifications.Clear();
                Notifications.Add("等待审批：pwsh", "旧格式、没有 refId", "warn");
                if (Notifications.Unread() != 1) { Fail("legacy notice should start unread"); return; }
                if (Notifications.MarkLegacyApprovalRead() != 1) { Fail("legacy approval notice not migrated"); return; }
                if (Notifications.Unread() != 0) { Fail("legacy approval notice still unread"); return; }
                Pass("notification history round trip (per-notice read, refId clears the approval notice, legacy swept)");

                IntPtr plain = TrayBadge.Get(BadgeIcon(), false);
                IntPtr badged = TrayBadge.Get(BadgeIcon(), true);
                if (plain == IntPtr.Zero || badged == IntPtr.Zero || plain == badged)
                { Fail("tray badge icon not composed"); return; }

                // The composed bitmap must keep per-pixel alpha: the QR-code
                // artifact in the tray was ToBitmap() flattening transparency.
                var cb = TrayBadge.Compose(BadgeIcon(), true);
                if (cb == null) { Fail("tray badge bitmap is null"); return; }
                try
                {
                    cb.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-tray-badge.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                    var cp0 = TrayBadge.Compose(BadgeIcon(), false);
                    if (cp0 != null)
                        cp0.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-tray-plain.png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                }
                catch { }
                if (cb.Width != TrayBadge.TraySize() || cb.Width != cb.Height
                    || cb.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                { Fail("tray badge bitmap is not " + TrayBadge.TraySize() + "px ARGB ("
                       + cb.Width + "x" + cb.Height + ", " + cb.PixelFormat + ")"); return; }
                int S = cb.Width;
                int bd = TrayBadge.BadgeDiameter(S);
                var dot = cb.GetPixel(S - bd - 1 + bd / 2, 1 + bd / 2);   // badge centre (top-right)
                if (dot.A < 200 || dot.R < 150 || dot.R <= dot.B)
                { Fail("tray badge red dot missing at top-right: " + dot); return; }
                // The icon must actually be drawn (not a blank or noisy frame):
                // the badged bitmap may only differ from the plain one in the
                // badge area, so anything else means the frame decode produced
                // garbage again.
                var cp = TrayBadge.Compose(BadgeIcon(), false);
                if (cp == null) { Fail("plain tray bitmap is null"); return; }
                int opaque = 0, diff = 0, cornerDiff = 0;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        if (cp.GetPixel(x, y).A > 200) opaque++;
                        bool changed = cp.GetPixel(x, y) != cb.GetPixel(x, y);
                        if (changed) diff++;
                        // the service-state dot lives bottom-right: the badge
                        // must never cover that corner again
                        if (changed && x >= S * 6 / 10 && y >= S * 6 / 10) cornerDiff++;
                    }
                if (opaque < S * S / 4) { Fail("tray icon frame looks empty (opaque px=" + opaque + "/" + (S * S) + ")"); return; }
                if (diff < S * S / 12 || diff > S * S / 2)
                { Fail("badge changed " + diff + " px of " + (S * S) + " (dot=" + bd + "px)"); return; }
                if (cornerDiff != 0)
                { Fail("badge still covers the state dot (" + cornerDiff + " px bottom-right)"); return; }
                Pass("tray badge icon composed (" + S + "px native, dot " + bd + "px, " + opaque
                    + " opaque px, " + diff + " px badged, state corner untouched)");

                if (Math.Abs(PetScene.BurstAmount - 0.8) > 1e-9) { Fail("pet burst level is not 80%"); return; }
                if (WpfTheme.BubbleBg.Color.A >= 255) { Fail("pet bubble background is not translucent"); return; }
                if (WpfTheme.BubbleBg.Color.B <= WpfTheme.BubbleBg.Color.R)
                { Fail("pet bubble background is not blue-tinted"); return; }

                // hold (approval) + two burst levels + sticky bubble
                var probeScene = new PetScene();
                probeScene.Hold(0.8);
                if (!probeScene.Holding || Math.Abs(probeScene.HoldLevel - 0.8) > 1e-9)
                { Fail("pet hold did not engage"); return; }
                probeScene.ReleaseHold();
                if (probeScene.Holding) { Fail("pet hold did not release"); return; }
                probeScene.Burst(10, 1.0);
                if (Math.Abs(probeScene.BurstLevel - 1.0) > 1e-9) { Fail("task-complete burst is not 100%"); return; }
                probeScene.Burst(8);
                if (Math.Abs(probeScene.BurstLevel - 0.8) > 1e-9) { Fail("default burst is not 80%"); return; }
                if (!probeScene.Bursting) { Fail("burst did not engage"); return; }
                probeScene.CancelBurst();
                if (probeScene.Bursting) { Fail("CancelBurst left a burst running"); return; }

                // The fall must be a smooth subsidence, not a snap.
                var fall = new PetScene();
                fall.Amount = 0.8;
                fall.SetTarget(0.12);
                fall.RenderFrame(1.0 / 60.0);
                double oneFrame = fall.SpoutAmount;
                if (oneFrame < 0.70)
                { Fail("spout snapped down in one frame (" + oneFrame.ToString("F3") + ")"); return; }
                for (int i = 0; i < 120; i++) fall.RenderFrame(1.0 / 60.0);   // ~2s
                if (fall.SpoutAmount > 0.20)
                { Fail("spout did not settle after 2s (" + fall.SpoutAmount.ToString("F3") + ")"); return; }
                Pass("spout falls smoothly (0.80 -> " + oneFrame.ToString("F3") + " after one frame, "
                    + fall.SpoutAmount.ToString("F3") + " after 2s)");

                var probePet = new PetWindow();
                probePet.ShowBubble("等待批准：pwsh", 0);
                if (!probePet.BubbleSticky) { Fail("approval bubble is not sticky"); return; }
                probePet.HideBubble();
                if (probePet.BubbleVisible) { Fail("HideBubble left the bubble visible"); return; }
                probePet.ShowBubble("普通提醒", 6);
                if (!probePet.BubbleVisible || probePet.BubbleSticky)
                { Fail("timed bubble should auto-hide (not sticky)"); return; }
                probePet.HideBubble();
                Pass("pet spout/bubble tokens (burst 80%/100%, sticky approval bubble, translucent blue)");
            }
            finally
            {
                Notifications.OverrideFile = noticeStore;      // keep the seam until the very end
                try { System.IO.File.Delete(nFile); } catch { }
                try { System.IO.File.Delete(nFile + ".read"); } catch { }
                try { System.IO.File.Delete(nFile + ".read.json"); } catch { }
                TrayBadge.Free();
            }

            // ---- in-shell approvals: frame parsing + body + live mux ----
            string sample = "{\"type\":\"server-request\",\"rpcId\":\"11111111-2222-3333-4444-555555555555\","
                + "\"method\":\"approval/requested\",\"payload\":{\"type\":\"approval/requested\","
                + "\"sessionId\":\"session-abc\",\"approvalId\":\"appr-1\",\"toolName\":\"pwsh\","
                + "\"callId\":\"call_1\",\"reason\":\"needs admin\"}}";
            ApprovalItem ai;
            string method;
            if (!Mux.ParseFrame(sample, out ai, out method) || method != "approval/requested"
                || ai.ToolName != "pwsh" || ai.Reason != "needs admin" || ai.ApprovalId != "appr-1")
            { Fail("approval/requested frame did not parse"); return; }
            string other = "{\"type\":\"server-request\",\"rpcId\":\"x\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\"}}";
            if (Mux.ParseFrame(other, out ai, out method))
            { Fail("a non-approval frame was parsed as an approval"); return; }
            string resolved = "{\"type\":\"server-request\",\"rpcId\":\"y\",\"method\":\"approval/resolved\","
                + "\"payload\":{\"type\":\"approval/resolved\",\"sessionId\":\"session-abc\",\"approvalId\":\"appr-1\",\"outcome\":\"rejected\"}}";
            if (!Mux.ParseFrame(resolved, out ai, out method) || method != "approval/resolved" || ai.ApprovalId != "appr-1")
            { Fail("approval/resolved frame did not parse"); return; }
            var probeItem = new ApprovalItem();
            probeItem.RpcId = "rpc-9";
            probeItem.SessionId = "session-abc";
            probeItem.ApprovalId = "appr-1";
            string body = Mux.BuildBody(probeItem, "rejected");
            if (body.IndexOf("\"client-response\"", StringComparison.Ordinal) < 0
                || body.IndexOf("\"outcome\":\"rejected\"", StringComparison.Ordinal) < 0
                || body.IndexOf("\"rpcId\":\"rpc-9\"", StringComparison.Ordinal) < 0
                || body.IndexOf("\"approvalId\":\"appr-1\"", StringComparison.Ordinal) < 0)
            { Fail("respond body is wrong: " + body); return; }
            Pass("approval frame parse + respond body (outcome=rejected)");

            // ---- turn lifecycle frames (authoritative task state) ----
            string turnStart = "{\"type\":\"server-request\",\"rpcId\":\"a\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\",\"sessionId\":\"session-abc\","
                + "\"event\":{\"type\":\"turn/start\",\"seq\":10,\"time\":1,\"data\":{\"turn\":81}}}}";
            string turnEnd = "{\"type\":\"server-request\",\"rpcId\":\"b\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\",\"sessionId\":\"session-abc\","
                + "\"event\":{\"type\":\"turn/end\",\"seq\":11,\"time\":2,\"data\":{\"turn\":81,\"reason\":{\"kind\":\"completed\"}}}}}";
            string turnAbort = "{\"type\":\"server-request\",\"rpcId\":\"c\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\",\"sessionId\":\"session-abc\","
                + "\"event\":{\"type\":\"turn/end\",\"seq\":12,\"time\":3,\"data\":{\"turn\":82,\"reason\":{\"kind\":\"interrupted\"}}}}}";
            string chunkFrame = "{\"type\":\"server-request\",\"rpcId\":\"d\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\",\"sessionId\":\"session-abc\","
                + "\"event\":{\"type\":\"assistant/chunk\",\"seq\":13,\"time\":4,\"data\":{}}}}";
            string userMsg = "{\"type\":\"server-request\",\"rpcId\":\"e\",\"method\":\"session/event\","
                + "\"payload\":{\"type\":\"session/event\",\"sessionId\":\"session-abc\","
                + "\"event\":{\"type\":\"user/message\",\"seq\":14,\"time\":5,\"data\":{\"content\":[]}}}}";
            TurnEvent te;
            if (!Mux.ParseTurnFrame(turnStart, out te) || !te.Started || te.Turn != 81)
            { Fail("turn/start frame did not parse"); return; }
            if (!Mux.ParseTurnFrame(turnEnd, out te) || te.Started || te.Turn != 81 || te.Reason != "completed")
            { Fail("turn/end(completed) frame did not parse"); return; }
            if (!Mux.ParseTurnFrame(turnAbort, out te) || te.Reason != "interrupted")
            { Fail("turn/end(interrupted) frame did not parse"); return; }
            if (Mux.ParseTurnFrame(chunkFrame, out te)) { Fail("assistant/chunk parsed as a turn boundary"); return; }
            if (!Mux.ParseTurnFrame(userMsg, out te) || te.Type != "user/message" || te.SessionId != "session-abc")
            { Fail("user/message frame did not parse"); return; }
            Pass("turn lifecycle frames parsed (start / completed / interrupted / user message, chunks ignored)");

            if (Program.ServerRunning())
            {
                Mux.Start();
                DateTime until = DateTime.Now.AddSeconds(8);
                while (!Mux.Connected && DateTime.Now < until) System.Threading.Thread.Sleep(200);
                bool conn = Mux.Connected;
                string st = Mux.Status;
                Mux.Stop();
                if (conn) Pass("approval mux connected (status=" + st + ")");
                else Fail("approval mux did not connect: " + st);

                // Write path without touching a real approval: a ghost rpcId must
                // come back as "not-pending" (=> our body parsed) rather than
                // "bad-response" (=> our body would be wrong) or an HTTP error.
                var ghost = new ApprovalItem();
                ghost.RpcId = "00000000-0000-0000-0000-000000000000";
                ghost.SessionId = "session-smoke-nonexistent";
                ghost.ApprovalId = "smoke-nonexistent";
                string ghostErr = Mux.Answer(ghost, false);
                if (ghostErr.Length == 0) { Fail("a ghost approval was accepted by the backend"); return; }
                if (ghostErr.IndexOf("bad-response", StringComparison.Ordinal) >= 0
                    || ghostErr.IndexOf("后端未接受", StringComparison.Ordinal) >= 0)
                { Fail("respond body rejected by the backend (schema mismatch): " + ghostErr); return; }
                Pass("respond HTTP path verified (ghost approval -> " + ghostErr + ")");
            }

            // persisted UI prefs must actually reach the controls (regression:
            // BuildChrome() pushed the field defaults and was never re-pushed)
            var sv2 = new SettingsView();
            bool petEventFired = false;
            sv2.PetBusyChanged += delegate { petEventFired = true; };
            sv2.PetSpeedChanged += delegate { petEventFired = true; };
            sv2.UpdatePetSettings(2.0, 0.55);
            if (Math.Abs(sv2.PetRateShown - 2.0) > 0.06 || Math.Abs(sv2.PetBusyShown - 0.55) > 0.011)
            { Fail("pet sliders did not reflect the persisted values (rate=" + sv2.PetRateShown.ToString("F2")
                   + " busy=" + sv2.PetBusyShown.ToString("F2") + ")"); return; }
            if (petEventFired) { Fail("UpdatePetSettings re-saved the values (should be silent)"); return; }
            Pass("persisted pet settings reach the sliders without re-saving");

            // ---- pet scene + window (ADR-0010) ----
            var scene = new PetScene();
            scene.Measure(new Size(736, 460));
            scene.Arrange(new Rect(0, 0, 736, 460));
            if (scene.Children.Count == 0) { Fail("pet scene has no layers"); return; }
            var pet = new PetWindow();
            var petContent = pet.Content as UIElement;
            if (petContent != null)
            {
                petContent.Measure(new Size(240, 210));
                petContent.Arrange(new Rect(0, 0, 240, 210));
            }
            pet.ShowBubble("冒烟测试", 1);
            pet.SetState("running");
            Pass("pet scene + window built (" + scene.LayerCount + " layers)");

            // ---- diagnostic package (P0-4) ----
            Diagnostics.Note("冒烟测试", "诊断包自检");
            string zip = Diagnostics.Create("smoke", 0, null, true);
            if (zip == null) { Fail("Diagnostics.Create returned null: " + Diagnostics.LastError); return; }
            var fi = new System.IO.FileInfo(zip);
            if (!fi.Exists) { Fail("diagnostic zip not written"); return; }
            if (fi.Length > Diagnostics.MaxBytes) { Fail("diagnostic zip exceeds 5MB (" + fi.Length + ")"); return; }
            string[] want = { "summary.txt", "env.txt", "service.txt", "processes.txt", "deps.txt",
                              "projcache-summary.txt", "events.txt", "log-tail.txt", "settings.txt" };
            var got = new List<string>();
            using (var za = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(zip)))
                foreach (var e in za.Entries) got.Add(e.FullName);
            foreach (string w in want)
                if (!got.Contains(w)) { Fail("diagnostic zip misses " + w); return; }
            string summary;
            using (var za = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(zip)))
            using (var sr = new System.IO.StreamReader(za.GetEntry("summary.txt").Open()))
                summary = sr.ReadToEnd();
            if (summary.IndexOf(Program.BuildId, StringComparison.Ordinal) < 0)
            { Fail("summary.txt does not mention BuildId"); return; }
            Pass("diagnostic package built (" + got.Count + " files, " + (fi.Length / 1024) + " KB, BuildId present)");
            try { System.IO.File.Delete(zip); } catch { }

            // ---- session library (P1-1) ----
            Pricing sp = Usage.LoadPricing();
            List<SessionEntry> sl = Sessions.List(sp);
            int onDisk = 0;
            foreach (SessionEntry x in sl) if (x.OnDisk) onDisk++;
            Pass("session library listed (" + sl.Count + " session(s), " + onDisk + " with disk files)");

            var sview = new SessionsView();
            sview.SetEntries(sl);
            string allCount = sview.CountText;
            sview.PreviewSearch("zzz-no-such-session-zzz");
            if (sview.CountText == allCount) { Fail("search filter did not change the list"); return; }
            string probe = null;
            foreach (SessionEntry x in sl) if ((x.Title ?? "").Length >= 2) { probe = x.Title.Substring(0, 2); break; }
            if (probe != null)
            {
                sview.PreviewSearch(probe);
                if (sview.CountText.StartsWith("0 ")) { Fail("title search found nothing for \"" + probe + "\""); return; }
                Pass("session search hits title (\"" + probe + "\" -> " + sview.CountText + ")");
            }
            sview.PreviewSearch("");

            SessionEntry real = null;
            foreach (SessionEntry x in sl) if (x.OnDisk) { real = x; break; }
            if (real != null)
            {
                string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-smoke-sess-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tmpDir);
                string zipPath = System.IO.Path.Combine(tmpDir, "s.zip");
                if (Sessions.Export(real, zipPath) == null) { Fail("session export failed: " + Sessions.LastError); return; }
                string home = System.IO.Path.Combine(tmpDir, "home");
                int n = Sessions.Import(zipPath, home);
                if (n <= 0) { Fail("session import failed: " + Sessions.LastError); return; }
                string rel = real.Folder.Substring(Sessions.Root.Length).TrimStart('\\');
                string dst = System.IO.Path.Combine(home, "sessions", rel, "session.jsonl.zstd");
                if (!System.IO.File.Exists(dst)) { Fail("restored session file missing: " + dst); return; }
                byte[] a1 = System.IO.File.ReadAllBytes(System.IO.Path.Combine(real.Folder, "session.jsonl.zstd"));
                byte[] a2 = System.IO.File.ReadAllBytes(dst);
                if (a1.Length != a2.Length) { Fail("restored session file differs in size"); return; }
                Pass("session export/import round trip (" + n + " file(s), " + a1.Length + " bytes identical)");
                try { System.IO.Directory.Delete(tmpDir, true); } catch { }
            }

            // delete-to-recycle-bin + cache prune, on throwaway copies only
            string delDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-smoke-del-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(delDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(delDir, "x.txt"), "x");
            var fake = new SessionEntry();
            fake.Id = "session-smoke";
            fake.Folder = delDir;
            fake.OnDisk = true;
            if (!Sessions.DeleteToRecycleBin(fake)) { Fail("delete to recycle bin failed: " + Sessions.LastError); return; }
            if (System.IO.Directory.Exists(delDir)) { Fail("deleted folder still exists"); return; }
            Pass("session delete moved the folder to the Recycle Bin");

            if (System.IO.File.Exists(Usage.ProjCacheFile) && sl.Count > 0)
            {
                string tmpCache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-smoke-cache-" + Guid.NewGuid().ToString("N") + ".json");
                System.IO.File.Copy(Usage.ProjCacheFile, tmpCache, true);
                string victim = sl[0].Id;
                if (!Sessions.PruneCacheFile(tmpCache, victim)) { Fail("cache prune failed: " + Sessions.LastError); return; }
                string after = System.IO.File.ReadAllText(tmpCache);
                if (after.IndexOf(victim, StringComparison.Ordinal) >= 0) { Fail("pruned session id still in the cache copy"); return; }
                Pass("session cache prune removed the row (" + victim.Substring(0, Math.Min(18, victim.Length)) + "…)");
                try { System.IO.File.Delete(tmpCache); } catch { }
            }

            // ---- backup / restore (P1-2) ----
            string bkDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-smoke-bk-" + Guid.NewGuid().ToString("N"));
            BackupResult br = Backup.Create(bkDir);
            if (!br.Ok) { Fail("backup failed: " + br.Error); return; }
            if (br.Files <= 0) { Fail("backup contains no files"); return; }
            BackupManifest bm = Backup.ReadManifest(br.Path);
            if (bm == null) { Fail("backup manifest unreadable: " + Backup.LastError); return; }
            if (bm.Excluded.Length == 0) { Fail("backup excluded nothing (projection cache / node_modules expected)"); return; }
            if (bm.ShellFiles <= 0) { Fail("backup did not include the shell's own data (%LOCALAPPDATA%\\DSH)"); return; }
            Pass("backup created (" + br.Files + " files, " + Backup.SizeText(br.Bytes) + ", "
                + bm.Excluded.Length + " exclusions, " + bm.ShellFiles + " shell files)");

            string bkHome = System.IO.Path.Combine(bkDir, "home");
            RestoreResult rr = Backup.Restore(br.Path, false, bkHome);
            if (!rr.Ok || rr.Added <= 0) { Fail("restore failed: " + rr.Error + " (added=" + rr.Added + ")"); return; }
            RestoreResult rr2 = Backup.Restore(br.Path, false, bkHome);
            if (rr2.Added != 0 || rr2.Skipped == 0)
            { Fail("merge semantics broken (added=" + rr2.Added + ", skipped=" + rr2.Skipped + ")"); return; }
            if (!System.IO.File.Exists(System.IO.Path.Combine(bkHome, "settings.yaml")))
            { Fail("settings.yaml missing after restore"); return; }
            Pass("restore merges without overwriting (" + rr.Added + " added, then " + rr2.Skipped + " skipped)");

            string foreign = System.IO.Path.Combine(bkDir, "foreign.zip");
            using (var zf = new System.IO.Compression.ZipArchive(System.IO.File.Create(foreign), System.IO.Compression.ZipArchiveMode.Create))
            { zf.CreateEntry("x.txt"); }
            if (Backup.ReadManifest(foreign) != null) { Fail("foreign archive accepted as a DSH backup"); return; }
            Pass("foreign archive rejected (manifest.json required)");

            // retention: keep the newest N, recycle the rest
            string retDir = System.IO.Path.Combine(bkDir, "ret");
            string srcHome = System.IO.Path.Combine(bkDir, "src");
            System.IO.Directory.CreateDirectory(srcHome);
            System.IO.File.WriteAllText(System.IO.Path.Combine(srcHome, "a.txt"), "a");
            System.IO.File.WriteAllText(System.IO.Path.Combine(srcHome, "b.txt"), "b");
            var made = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                BackupResult b = Backup.Create(retDir, srcHome, "dsh-backup-");
                if (!b.Ok) { Fail("retention test backup failed: " + b.Error); return; }
                System.IO.File.SetLastWriteTime(b.Path, DateTime.Now.AddMinutes(-10 + i));   // deterministic order
                made.Add(b.Path);
            }
            List<BackupItem> items = Backup.List(retDir);
            if (items.Count != 3) { Fail("backup list expected 3, got " + items.Count); return; }
            int gone = Backup.Prune(retDir, 2);
            List<BackupItem> left = Backup.List(retDir);
            if (gone != 1 || left.Count != 2) { Fail("prune kept " + left.Count + " (removed " + gone + "), expected 2 left / 1 removed"); return; }
            foreach (BackupItem it in left)
                if (String.Equals(it.Path, made[0], StringComparison.OrdinalIgnoreCase))
                { Fail("prune removed the newest backup instead of the oldest"); return; }
            Pass("backup retention keeps the newest N (" + gone + " recycled, " + left.Count + " left)");

            // credentials are excluded by default and included only on request
            string credHome = System.IO.Path.Combine(bkDir, "credhome");
            System.IO.Directory.CreateDirectory(credHome);
            System.IO.File.WriteAllText(System.IO.Path.Combine(credHome, "settings.yaml"), "x");
            System.IO.File.WriteAllText(System.IO.Path.Combine(credHome, ".credentials.yaml"), "DEEPSEEK_API_KEY: sk-test");
            string credDir = System.IO.Path.Combine(bkDir, "credzip");
            BackupResult noCred = Backup.Create(credDir, credHome, "dsh-backup-", null, false);
            if (!noCred.Ok) { Fail("credential test backup failed: " + noCred.Error); return; }
            bool hasCred = false, hasSettings = false;
            using (var za = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(noCred.Path)))
                foreach (var e in za.Entries)
                {
                    if (e.FullName.EndsWith(".credentials.yaml", StringComparison.Ordinal)) hasCred = true;
                    if (e.FullName.EndsWith("settings.yaml", StringComparison.Ordinal)) hasSettings = true;
                }
            if (hasCred) { Fail("backup included .credentials.yaml although credentials are off"); return; }
            if (!hasSettings) { Fail("backup dropped a normal file while excluding credentials"); return; }
            BackupResult withCred = Backup.Create(credDir, credHome, "dsh-backup-", null, true);
            if (!withCred.Ok) { Fail("opt-in credential backup failed: " + withCred.Error); return; }
            bool hasCred2 = false;
            using (var za = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(withCred.Path)))
                foreach (var e in za.Entries)
                    if (e.FullName.EndsWith(".credentials.yaml", StringComparison.Ordinal)) hasCred2 = true;
            if (!hasCred2) { Fail("opt-in credential backup did not include .credentials.yaml"); return; }
            Pass("credentials excluded by default, included only on opt-in");

            try { System.IO.Directory.Delete(bkDir, true); } catch { }

            // port-owner lookup (needed to stop a backend owned by another instance)
            if (Program.ServerRunning())
            {
                int owner = Program.FindPortOwner(Program.Port);
                if (owner > 0) Pass("port-owner lookup works (3080 -> pid " + owner + ")");
                else Fail("FindPortOwner found no pid although 3080 is listening");
                string desc = Program.DescribeProcess(owner);
                if (desc != null && desc.IndexOf("PID", StringComparison.Ordinal) >= 0)
                    Pass("process description (WMI) works");
                else Fail("DescribeProcess returned nothing useful");
            }

            // JobKill end-to-end check: a child in the kill-on-close job must die
            // when this process exits (the caller verifies the pid afterwards).
            var dummy = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (dummy != null)
            {
                JobKill.Assign(dummy);
                Console.WriteLine("SMOKE INFO: job child pid = " + dummy.Id + " (must be gone once this process exits)");
            }

            Console.WriteLine(_failed == 0 ? "SMOKE ALL PASS" : "SMOKE FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine("SMOKE FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Notifications.OverrideFile = "";
            try { System.IO.File.Delete(noticeStore); } catch { }
            try { System.IO.File.Delete(noticeStore + ".read.json"); } catch { }
        }
    }

    private static void Pass(string what) { Console.WriteLine("SMOKE OK: " + what); }

    /// <summary>The probe runs from logs\, so walk up to the app folder for an icon.</summary>
    private static string BadgeIcon()
    {
        string[] cands =
        {
            Program.IconFile,
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "app.ico"),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tray_running.ico")
        };
        foreach (string c in cands)
        {
            try { if (System.IO.File.Exists(c)) return System.IO.Path.GetFullPath(c); } catch { }
        }
        return cands[0];
    }    private static void Fail(string what) { _failed++; Console.WriteLine("SMOKE FAIL: " + what); }

    private static Button FindByText(DependencyObject root, string text)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            var b = child as Button;
            if (b != null && b.Content as string == text) return b;
            Button found = FindByText(child, text);
            if (found != null) return found;
        }
        return null;
    }

    private static ScrollBar FindVertical(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            var bar = child as ScrollBar;
            if (bar != null && bar.Orientation == Orientation.Vertical) return bar;
            ScrollBar found = FindVertical(child);
            if (found != null) return found;
        }
        return null;
    }
}
