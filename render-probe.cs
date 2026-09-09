// ============================================================================
//  render-probe.cs - offscreen screenshot of the real UI (developer tool)
//
//  Builds DshWindow, forces layout, optionally scrolls the page ScrollViewer,
//  and renders the client area to a PNG with RenderTargetBitmap. No window is
//  shown, so this is safe to run while another DSH instance is in use.
//
//  Usage:  render-probe.exe [dark|light] [scrollPixels] [height]
//  Output: logs\preview-<theme>[-scrolled].png
//
//  NOTE: keep this file OUT of build-dsh-exe.cmd - it has its own Main().
// ============================================================================
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class RenderProbe
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0].Equals("pet", StringComparison.OrdinalIgnoreCase))
            {
                RenderPet(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-pet.png"));
                Console.WriteLine("RENDER OK: pet");
                return;
            }
            if (args.Length > 0 && args[0].Equals("petbubble", StringComparison.OrdinalIgnoreCase))
            {
                RenderPetBubble(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-pet-bubble.png"));
                Console.WriteLine("RENDER OK: petbubble");
                return;
            }

            bool settings = args.Length > 0 && args[0].Equals("settings", StringComparison.OrdinalIgnoreCase);
            bool sessions = args.Length > 0 && args[0].Equals("sessions", StringComparison.OrdinalIgnoreCase);
            bool backups = args.Length > 0 && args[0].Equals("backups", StringComparison.OrdinalIgnoreCase);
            bool budget = args.Length > 0 && args[0].Equals("budget", StringComparison.OrdinalIgnoreCase);
            bool notices = args.Length > 0 && args[0].Equals("notifications", StringComparison.OrdinalIgnoreCase);
            bool dark = settings || sessions || backups || budget || notices || args.Length == 0 || !args[0].Equals("light", StringComparison.OrdinalIgnoreCase);
            int scroll = 0;
            if (args.Length > 1) int.TryParse(args[1], out scroll);
            int height = 660;
            if (args.Length > 2) int.TryParse(args[2], out height);
            if (height < 300) height = 300;

            string name = "preview-" + (settings ? "settings" : sessions ? "sessions" : backups ? "backups" : budget ? "budget" : notices ? "notifications" : (dark ? "dark" : "light")) + (scroll > 0 ? "-scrolled" : "") + (height == 660 ? "" : "-h" + height) + ".png";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            WpfTheme.Apply(dark);

            var win = new DshWindow();
            win.PreviewRefreshUsage();            // fill the usage/cost cards for the screenshot
            if (settings) win.PreviewShowPage("settings");
            if (sessions) win.PreviewShowPage("sessions");
            if (backups) win.PreviewShowPage("backups");
            if (budget) win.PreviewShowPage("budget");
            if (notices) { SeedNotices(); win.PreviewShowPage("notifications"); }
            var root = win.Content as UIElement;
            if (root == null) { Console.WriteLine("RENDER FAIL: no content"); return; }
            root.Measure(new Size(940, height));
            root.Arrange(new Rect(0, 0, 940, height));
            root.UpdateLayout();

            if (scroll > 0)
            {
                ScrollViewer sv = FindBiggestScroll(root);
                if (sv == null) { Console.WriteLine("RENDER WARN: no ScrollViewer found"); }
                else
                {
                    sv.ScrollToVerticalOffset(scroll);
                    root.UpdateLayout();
                    Console.WriteLine("RENDER INFO: scrolled to " + sv.VerticalOffset + " (extent " + sv.ExtentHeight + ", viewport " + sv.ViewportHeight + ")");
                }
            }

            var bmp = new RenderTargetBitmap(940, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
            Console.WriteLine("RENDER OK: " + path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("RENDER FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>Seeds three throwaway notices so the history layout is visible.</summary>
    private static void SeedNotices()
    {
        string f = Path.Combine(Path.GetTempPath(), "dsh-preview-notices.jsonl");
        Notifications.OverrideFile = f;
        Notifications.Clear();
        Notifications.Add("每日预算接近上限", "今日已用 92%（¥18.40 / 20 元）", "warn");
        Notifications.Add("任务清单更新", "待办 3 项", "info");
        Notifications.Add("会话已删除", "已移入回收站并清理缓存记录", "info");
    }

    /// <summary>Renders the pet window with a notification bubble on screen.</summary>
    private static void RenderPetBubble(string path)
    {
        var win = new PetWindow();
        win.ShowBubble("等待批准：pwsh\n需要提权执行一次 pwsh 命令", 60);
        var root = win.Content as FrameworkElement;
        if (root == null) return;
        root.Measure(new Size(240, 204));
        root.Arrange(new Rect(0, 0, 240, 204));
        root.UpdateLayout();
        win.Scene.RenderFrame();
        var bmp = new RenderTargetBitmap(240, 204, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
    }

    private static void RenderPet(string path)    {
        var scene = new PetScene();
        scene.SetState("running");
        scene.Measure(new Size(736, 460));
        scene.Arrange(new Rect(0, 0, 736, 460));
        scene.RenderFrame();
        var bmp = new RenderTargetBitmap(736, 460, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(scene);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
    }

    private static ScrollViewer FindBiggestScroll(DependencyObject root)
    {
        ScrollViewer best = null;
        Walk(root, ref best);
        return best;
    }

    private static void Walk(DependencyObject node, ref ScrollViewer best)
    {
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, i);
            var sv = child as ScrollViewer;
            if (sv != null && (best == null || sv.ExtentHeight > best.ExtentHeight)) best = sv;
            Walk(child, ref best);
        }
    }
}
