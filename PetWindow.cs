// ============================================================================
//  PetWindow.cs - transparent, always-on-top desktop pet (ADR-0010)
//
//  Shows the whale animation (PetScene) plus a speech bubble used for task /
//  approval / balance notifications (ADR-0009). Dragging moves it, double-click
//  opens the console, right-click shows the same menu as the tray.
//
//  Position is remembered in %LOCALAPPDATA%\DSH\pet-pos.txt.
// ============================================================================
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

internal sealed class PetWindow : Window
{
    private const double SceneW = 240, SceneH = 150;

    private readonly PetScene _scene = new PetScene();
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly DispatcherTimer _bubbleTimer;

    public event Action DoubleClicked;
    public event Action RightClicked;

    public PetWindow()
    {
        Title = "DSH 宠物";
        Width = SceneW;
        Height = SceneH + 54;                 // room for the bubble
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SceneH) });

        _bubbleText = new TextBlock
        {
            FontFamily = WpfTheme.Ui,
            FontSize = 12,
            Foreground = WpfTheme.BubbleText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 210
        };
        _bubble = new Border
        {
            Background = WpfTheme.BubbleBg,
            BorderBrush = WpfTheme.BubbleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(6, 6, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = _bubbleText,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 1,
                Opacity = 0.22,
                Color = Color.FromRgb(0x1E, 0x4B, 0x8C)
            }
        };
        Grid.SetRow(_bubble, 0);
        root.Children.Add(_bubble);

        var host = new Viewbox { Stretch = Stretch.Uniform, Child = _scene, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(host, 1);
        root.Children.Add(host);
        Content = root;

        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _bubbleTimer.Tick += delegate { _bubbleTimer.Stop(); _bubble.Visibility = Visibility.Collapsed; };

        MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                Action a = DoubleClicked;
                if (a != null) a();
                return;
            }
            try { DragMove(); SavePosition(); } catch { }
        };
        MouseRightButtonUp += delegate
        {
            Action a = RightClicked;
            if (a != null) a();
        };

        Loaded += delegate { RestorePosition(); };
    }

    // ---- state -------------------------------------------------------------
    public void SetState(string state) { _scene.SetState(state); }
    public void SetTarget(double amount) { _scene.SetTarget(amount); }
    public void Burst(int seconds) { _scene.Burst(seconds); }
    public void Burst(int seconds, double level) { _scene.Burst(seconds, level); }
    public void CancelBurst() { _scene.CancelBurst(); }
    public void Hold(double level) { _scene.Hold(level); }
    public void ReleaseHold() { _scene.ReleaseHold(); }
    public PetScene Scene { get { return _scene; } }

    /// <summary>
    /// Shows the speech bubble. seconds &lt;= 0 keeps it on screen until
    /// <see cref="HideBubble"/> is called - an approval has to stay visible
    /// until the user actually decides.
    /// </summary>
    public void ShowBubble(string text, int seconds)
    {
        if (String.IsNullOrEmpty(text)) return;
        try
        {
            _bubbleText.Text = text;
            _bubble.Visibility = Visibility.Visible;
            _bubbleTimer.Stop();
            if (seconds > 0)
            {
                _bubbleTimer.Interval = TimeSpan.FromSeconds(seconds);
                _bubbleTimer.Start();
            }
        }
        catch { }
    }

    public void HideBubble()
    {
        try { _bubbleTimer.Stop(); _bubble.Visibility = Visibility.Collapsed; }
        catch { }
    }

    /// <summary>Test seam: bubble visible on screen.</summary>
    internal bool BubbleVisible { get { return _bubble.Visibility == Visibility.Visible; } }

    /// <summary>Test seam: visible and NOT on an auto-hide timer (approval mode).</summary>
    internal bool BubbleSticky { get { return BubbleVisible && !_bubbleTimer.IsEnabled; } }

    // ---- position ----------------------------------------------------------
    private static string PosFile
    {
        get { return Path.Combine(Usage.DataDir, "pet-pos.txt"); }
    }

    private void SavePosition()
    {
        try
        {
            if (!Directory.Exists(Usage.DataDir)) Directory.CreateDirectory(Usage.DataDir);
            File.WriteAllText(PosFile, ((int)Left).ToString(CultureInfo.InvariantCulture) + ","
                + ((int)Top).ToString(CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private void RestorePosition()
    {
        double x = double.NaN, y = double.NaN;
        try
        {
            if (File.Exists(PosFile))
            {
                string[] p = File.ReadAllText(PosFile).Split(',');
                if (p.Length == 2)
                {
                    double.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                    double.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                }
            }
        }
        catch { }
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            Rect wa = SystemParameters.WorkArea;
            x = wa.Right - Width - 24;
            y = wa.Bottom - Height - 8;
        }
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        x = Math.Max(area.Left, Math.Min(x, area.Right - Width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));
        Left = x; Top = y;
    }
}
