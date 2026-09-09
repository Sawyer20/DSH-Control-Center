// ============================================================================
//  PetScene.cs - the DeepSeek whale, ported 1:1 from the HTML animation
//  (assets/deepseek-whale-standalone.html): SVG geometry copied verbatim,
//  motion formulas copied from its requestAnimationFrame script.
//
//  ADR-0010: the pet is a vector-animation scene, so it is the ONE place where
//  absolute positioning (Canvas) is allowed - it is not UI layout.
// ============================================================================
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

internal sealed class PetScene : Grid
{
    // ---- palette (from the HTML: --whale / --water / gradients) -------------
    private static readonly Brush Whale = Solid(0x4D, 0x6B, 0xFE);
    private static readonly Brush Water = Solid(0x40, 0xA9, 0xFF);
    private static readonly Brush Fin = Solid(0x3B, 0x55, 0xD9);
    private static readonly Brush Eye = Solid(0x14, 0x27, 0x55);
    private static readonly Brush EyeHi = Solid(0xFF, 0xFF, 0xFF);
    private static readonly Brush Glint = Solid(0xA5, 0xB9, 0xFF);
    private static readonly Brush Cheek = Solid(0xA7, 0xBB, 0xFF);
    private static readonly Brush Mouth = Solid(0x20, 0x3B, 0x9E);
    private static readonly Brush Blow = Solid(0x31, 0x4E, 0xCA);

    private const double W = 736, H = 460;      // SVG viewBox
    // The fountain is ~50% of the per-frame cost in software rendering
    // (measured: hiding it halves 0.29%/frame to 0.14%), so the counts stay
    // modest: the HTML original uses 7 streams / 108 drops.
    private const int StreamCount = 5;
    private const int DropCount = 24;

    private readonly Canvas _root = new Canvas { Width = W, Height = H };
    private readonly Canvas _backdrop = new Canvas { Width = W, Height = H };
    private readonly Canvas _float = new Canvas { Width = W, Height = H };
    private readonly Canvas _whaleStatic = new Canvas { Width = W, Height = H };
    private readonly Canvas _streams = new Canvas();
    private readonly Canvas _drops = new Canvas();
    private readonly Canvas _eyeGroup = new Canvas();
    private readonly Canvas _ripples = new Canvas();
    private readonly Path[] _streamPaths = new Path[StreamCount];
    private readonly QuadraticBezierSegment[] _streamSegs = new QuadraticBezierSegment[StreamCount];
    private readonly Ellipse[] _dropShapes = new Ellipse[DropCount];
    private readonly TransformGroup[] _dropXf = new TransformGroup[DropCount];
    private readonly TranslateTransform[] _dropMove = new TranslateTransform[DropCount];
    private readonly RotateTransform[] _dropSpin = new RotateTransform[DropCount];
    private readonly ScaleTransform[] _dropScale = new ScaleTransform[DropCount];
    private readonly double[] _dropPhase = new double[DropCount];
    private readonly double[] _dropSeed = new double[DropCount];
    private readonly RotateTransform _tailSpin = new RotateTransform(0, 500, 331);
    private readonly TranslateTransform _floatMove = new TranslateTransform();
    private readonly ScaleTransform _eyeScale = new ScaleTransform(1, 1);
    private readonly Path _tail = new Path();
    private readonly TranslateTransform _fountainMove = new TranslateTransform(317, 270);

    /// <summary>Spout level while a notification / reminder is showing.</summary>
    internal const double BurstAmount = 0.8;

    /// <summary>Easing rates (per second): rising is quick, falling is a slow
    /// subsidence so the water looks like water instead of a cut.</summary>
    private const double RisePerSecond = 6.0;      // ~0.17s time constant
    private const double FallPerSecond = 1.4;      // ~0.7s time constant, ~2s to settle

    private double _amount = 0.35, _rate = 1.0, _time, _drift;
    private double _dt = 1.0 / 60.0;
    private double _accum;                     // unconsumed frame time (see OnFrame)
    private double _targetAmount = 0.35;
    private double _burstAmount = BurstAmount;
    private double _holdLevel = -1;            // >= 0 while an approval is waiting
    private DateTime _burstUntil = DateTime.MinValue;
    private bool _sleepy;                      // eyes closed when the service is stopped
    private bool _running;
    private TimeSpan _lastStamp = TimeSpan.MinValue;
    private long _frames;                      // frames actually drawn (perf probe)

    public PetScene()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        BuildStatic();
        Children.Add(_root);
        Loaded += delegate { Start(); };
        Unloaded += delegate { Stop(); };
        IsVisibleChanged += delegate { if (IsVisible) Start(); else Stop(); };
    }

    // ---- static scene (geometry copied from the SVG) ------------------------
    private void BuildStatic()
    {
        // Static artwork goes through BitmapCache. A layered window is rendered
        // in software, so every frame re-rasterises the whole visual tree; a
        // cache turns "rasterise ~8 complex bezier paths + a radial gradient"
        // into a single bitmap blit. RenderAtScale matches the on-screen size
        // (the Viewbox shows the 736x460 scene at ~240x150): caching at full
        // resolution made WPF downscale a 1.4 MB bitmap every frame, which was
        // SLOWER than no cache at all.
        _backdrop.CacheMode = new BitmapCache(0.33);
        _whaleStatic.CacheMode = new BitmapCache(0.33);

        // halo
        var halo = new Ellipse { Width = 560, Height = 400 };
        halo.Fill = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(31, 0x40, 0xA9, 0xFF), 0),
                new GradientStop(Color.FromArgb(0, 0x40, 0xA9, 0xFF), 1)
            }
        };
        Canvas.SetLeft(halo, 365 - 280); Canvas.SetTop(halo, 245 - 200);
        _backdrop.Children.Add(halo);

        // ambient water dots
        var dots = new Canvas { Opacity = 0.4 };
        AddDot(dots, 152, 214, 3); AddDot(dots, 596, 178, 4); AddDot(dots, 554, 91, 2);
        var spark = new Path { Data = Geometry.Parse("M185 112v12m-6-6h12M571 266v10m-5-5h10"), Stroke = Water, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        dots.Children.Add(spark);
        _backdrop.Children.Add(dots);

        // sea line
        var sea = new Ellipse { Width = 362, Height = 32, Fill = Water, Opacity = 0.08 };
        Canvas.SetLeft(sea, 379 - 181); Canvas.SetTop(sea, 394 - 16);
        _backdrop.Children.Add(sea);
        _root.Children.Add(_backdrop);

        // ripples (opacity animated - must stay outside the cache)
        AddRipple(376, 392, 154, 14);
        AddRipple(376, 392, 206, 25);
        _root.Children.Add(_ripples);

        // floating group
        _float.RenderTransform = _floatMove;
        _root.Children.Add(_float);

        // fountain (streams + drops), anchored at (317,270)
        _streams.RenderTransform = _fountainMove;
        _drops.RenderTransform = _fountainMove;
        BuildStreams();
        BuildDrops();
        _float.Children.Add(_streams);
        _float.Children.Add(_drops);

        // tail (rotates - outside the cache)
        _tail.Data = Geometry.Parse("M474 323C518 330 545 308 550 279C523 279 510 257 514 235C533 242 551 244 566 260C575 235 597 227 620 229C616 260 604 280 578 287C576 331 545 362 498 360Z");
        _tail.Fill = Whale;
        _tail.RenderTransform = _tailSpin;
        _float.Children.Add(_tail);
        _float.Children.Add(_whaleStatic);

        // body
        AddPath(_whaleStatic, "M201 294C218 259 274 249 329 264C361 252 398 267 431 287C459 304 485 320 516 315C528 345 505 370 472 382C430 399 365 403 308 389C256 379 216 354 202 324L174 311Q163 299 178 296Z", Whale);

        // belly (linear gradient #c3dcff -> #e7f3ff)
        var bellyBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0xC3, 0xDC, 0xFF), 0),
                new GradientStop(Color.FromRgb(0xE7, 0xF3, 0xFF), 1)
            }
        };
        AddPath(_whaleStatic, "M221 335C258 368 302 374 348 377C410 383 456 371 496 350C468 387 387 403 315 386C274 378 242 358 221 335Z", bellyBrush);

        AddPath(_whaleStatic, "M350 345C362 359 373 383 358 398C338 398 319 380 316 361Z", Fin);
        AddStroke(_whaleStatic, "M238 282C259 272 279 272 292 274", Glint, 7, 0.7);

        // eye (blinks via scale)
        _eyeGroup.RenderTransform = _eyeScale;
        var eye = new Ellipse { Width = 18, Height = 24, Fill = Eye };
        Canvas.SetLeft(eye, -9); Canvas.SetTop(eye, -12);
        var hi = new Ellipse { Width = 6, Height = 6, Fill = EyeHi };
        Canvas.SetLeft(hi, -5.5); Canvas.SetTop(hi, -7);
        _eyeGroup.Children.Add(eye); _eyeGroup.Children.Add(hi);
        Canvas.SetLeft(_eyeGroup, 253); Canvas.SetTop(_eyeGroup, 314);
        _float.Children.Add(_eyeGroup);

        var cheek = new Ellipse { Width = 24, Height = 12, Fill = Cheek, Opacity = 0.7 };
        Canvas.SetLeft(cheek, 268 - 12); Canvas.SetTop(cheek, 335 - 6);
        _whaleStatic.Children.Add(cheek);

        AddStroke(_whaleStatic, "M213 330Q226 343 239 331", Mouth, 3, 1);

        var blow = new Ellipse { Width = 16, Height = 6, Fill = Blow };
        Canvas.SetLeft(blow, 317 - 8); Canvas.SetTop(blow, 269 - 3);
        _whaleStatic.Children.Add(blow);
        AddStroke(_whaleStatic, "M312 264Q317 258 322 264", Water, 4, 1);
    }

    /// <summary>Test seam: frames drawn since the scene was created.</summary>
    internal long FrameCount { get { return _frames; } }

    private void AddDot(Canvas host, double cx, double cy, double r)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = Water };
        Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r);
        host.Children.Add(e);
    }

    private void AddRipple(double cx, double cy, double rx, double ry)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Stroke = Water, StrokeThickness = 2 };
        Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry);
        _ripples.Children.Add(e);
    }

    private static void AddPath(Canvas host, string data, Brush fill)
    {
        var p = new Path { Data = Geometry.Parse(data), Fill = fill };
        host.Children.Add(p);
    }

    private static void AddStroke(Canvas host, string data, Brush stroke, double width, double opacity)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity
        };
        host.Children.Add(p);
    }

    private void BuildStreams()
    {
        for (int i = 0; i < StreamCount; i++)
        {
            var seg = new QuadraticBezierSegment(new Point(0, 0), new Point(0, 0), true);
            var fig = new PathFigure { StartPoint = new Point(0, 0), IsClosed = false, IsFilled = false };
            fig.Segments.Add(seg);
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            var path = new Path
            {
                Data = geo,
                Stroke = Water,
                StrokeThickness = 2,
                Opacity = 0.28,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 20 })
            };
            _streamSegs[i] = seg;
            _streamPaths[i] = path;
            _streams.Children.Add(path);
        }
    }

    private void BuildDrops()
    {
        for (int i = 0; i < DropCount; i++)
        {
            var move = new TranslateTransform();
            var spin = new RotateTransform();
            var scale = new ScaleTransform(1, 1);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(spin);
            group.Children.Add(move);
            var e = new Ellipse { Width = 2, Height = 2, Fill = Water, RenderTransform = group };
            Canvas.SetLeft(e, -1); Canvas.SetTop(e, -1);
            _dropMove[i] = move; _dropSpin[i] = spin; _dropScale[i] = scale;
            _dropShapes[i] = e; _dropXf[i] = group;
            _dropPhase[i] = (double)i / DropCount;
            _dropSeed[i] = (Math.Sin(i * 127.1 + 9) * 43758.5453) % 1.0;
            _drops.Children.Add(e);
        }
    }

    // ---- animation ---------------------------------------------------------
    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastStamp = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>0..1 spout size, matching the HTML slider default (65%).</summary>
    public double Amount { get { return _amount; } set { _amount = Math.Max(0.1, Math.Min(1.0, value)); } }

    /// <summary>Base speed multiplier, matching the HTML slider default (1.0x).</summary>
    public double Rate { get { return _rate; } set { _rate = Math.Max(0.2, Math.Min(3.0, value)); } }

    /// <summary>Service state: only the sleepy look depends on it now - speed is
    /// entirely user-controlled (the old per-state speed scale felt janky).</summary>
    public void SetState(string state)
    {
        _sleepy = !(state == "running" || state == "starting" || state == "error");
    }

    /// <summary>Steady-state spout size (0..1): small while a task runs, tiny when idle.</summary>
    public void SetTarget(double amount) { _targetAmount = Math.Max(0.05, Math.Min(1.0, amount)); }

    /// <summary>Blast the spout for a few seconds (task finished / notification).</summary>
    public void Burst(int seconds) { Burst(seconds, BurstAmount); }

    /// <summary>Same, with an explicit level (task completion uses 100%).</summary>
    public void Burst(int seconds, double level)
    {
        _burstAmount = Math.Max(0.05, Math.Min(1.0, level));
        _burstUntil = DateTime.Now.AddSeconds(seconds <= 0 ? 8 : seconds);
    }

    /// <summary>
    /// Drops any running burst: a turn that ended abnormally must fall straight
    /// back to the no-task level instead of finishing a blast.
    /// </summary>
    public void CancelBurst() { _burstUntil = DateTime.MinValue; }

    /// <summary>Test seam: a burst is currently running.</summary>
    internal bool Bursting { get { return DateTime.Now < _burstUntil; } }

    /// <summary>
    /// Forces the spout to a level until <see cref="ReleaseHold"/> - used while
    /// an approval is waiting, so the pet keeps signalling "I need you" instead
    /// of fading back to the idle trickle.
    /// </summary>
    public void Hold(double level)
    {
        _holdLevel = Math.Max(0.05, Math.Min(1.0, level));
    }

    public void ReleaseHold() { _holdLevel = -1; }

    public bool Holding { get { return _holdLevel >= 0; } }

    /// <summary>Test seam: the level a burst currently uses.</summary>
    internal double BurstLevel { get { return _burstAmount; } }

    /// <summary>Test seam: the forced level while an approval is waiting (-1 = none).</summary>
    internal double HoldLevel { get { return _holdLevel; } }

    /// <summary>Test seam: current spout level (0..1).</summary>
    internal double SpoutAmount { get { return _amount; } }

    /// <summary>Number of scene layers (used by the smoke probe).</summary>
    public int LayerCount { get { return _root.Children.Count; } }

    /// <summary>Draws one frame immediately (offscreen rendering / tests).</summary>
    public void RenderFrame() { RenderFrame(1.0 / 60.0); }

    /// <summary>Same, with an explicit frame delta (used by the smoke probe).</summary>
    internal void RenderFrame(double dt) { _dt = dt; Draw(); }

    private void OnFrame(object sender, EventArgs e)
    {
        var args = e as RenderingEventArgs;
        TimeSpan now = args != null ? args.RenderingTime : TimeSpan.Zero;
        if (_lastStamp == TimeSpan.MinValue) { _lastStamp = now; return; }
        double dt = (now - _lastStamp).TotalSeconds;
        _lastStamp = now;
        if (dt <= 0) return;
        if (dt > 0.05) dt = 0.05;                  // clamp like the HTML script

        // Adaptive frame budget. A layered (AllowsTransparency) window is
        // rendered in SOFTWARE by WPF, so every frame repaints ~100 vector
        // shapes on the CPU: measured 33-38% of one core at ~60fps.
        //
        // CRITICAL: the skipped time must be ACCUMULATED, not dropped - the old
        // version updated _lastStamp and returned, which made the animation run
        // at (target fps / vsync) speed (barely moving at the 12fps idle cap).
        _accum += dt;
        double step = 1.0 / FpsFor();
        if (_accum < step) return;
        double elapsed = _accum;
        _accum = 0;

        _time += elapsed * _rate;
        _drift += elapsed * _rate;
        _dt = elapsed;
        _frames++;
        try { Draw(); } catch { Stop(); }        // never let a decoration crash the app
    }

    /// <summary>
    /// Frames per second for the current state. 60 while a reminder is showing,
    /// 45 while the agent works, 30 when idle - 30fps is smooth enough for a
    /// floating whale, and the software-rendered layered window costs roughly
    /// 0.13% of a core per frame, so this keeps the pet affordable.
    /// </summary>
    private double FpsFor()
    {
        if (!FrameCapEnabled) return 125;          // A/B switch for the CPU probe
        if (_holdLevel >= 0 || DateTime.Now < _burstUntil) return 60;
        if (_targetAmount > 0.2 || Math.Abs(_targetAmount - _amount) > 0.02) return 45;
        return 30;
    }

    /// <summary>Test seam: the old uncapped behaviour, for measuring the fix.</summary>
    internal static bool FrameCapEnabled = true;

    private void Draw()
    {
        // Priority: hold (an approval is waiting) > burst (task finished /
        // notification) > the steady target the shell keeps pushing.
        double want = _holdLevel >= 0 ? _holdLevel
                    : (DateTime.Now < _burstUntil ? _burstAmount : _targetAmount);

        // Asymmetric, frame-rate independent easing: rising stays snappy (the
        // user just asked for something), falling is a slow subsidence - water
        // should not snap from a burst straight back to a trickle.
        double perSecond = want > _amount ? RisePerSecond : FallPerSecond;
        _amount += (want - _amount) * Math.Min(1.0, perSecond * _dt);

        double height = 35 + _amount * 210;
        double width = 15 + _amount * 150;

        for (int i = 0; i < StreamCount; i++)
        {
            double fan = (i - 3) / 3.0;
            double lift = height * (1 - Math.Abs(fan) * 0.18);
            double spread = fan * width;
            _streamSegs[i].Point1 = new Point(spread * 0.19, -lift * 0.88);
            _streamSegs[i].Point2 = new Point(spread * 0.38, -4 * lift * 0.38 * 0.62 + 45 * 0.38 * 0.38);
            _streamPaths[i].StrokeThickness = 1.2 + _amount * 2.4;
            _streamPaths[i].StrokeDashOffset = -_time * 95;
        }

        for (int i = 0; i < DropCount; i++)
        {
            double t = (_dropPhase[i] + _time * 0.55) % 1.0;
            double fan = ((i % 9) - 4) / 4.0;
            double seed = Math.Abs(_dropSeed[i]);
            double spread = fan * width * (0.84 + seed * 0.23);
            double lift = height * (1 - Math.Abs(fan) * 0.18) * (0.9 + seed * 0.1);
            double x = spread * t;
            double y = -4 * lift * t * (1 - t) + 45 * t * t;
            double dy = -4 * lift * (1 - 2 * t) + 90 * t;
            double angle = Math.Atan2(dy, spread) * 180 / Math.PI - 90;
            double radius = (1.4 + _amount * 2.2) * (0.7 + seed * 0.5);

            _dropMove[i].X = x; _dropMove[i].Y = y;
            _dropSpin[i].Angle = angle;
            _dropScale[i].ScaleX = radius;
            _dropScale[i].ScaleY = radius * (1.2 + Math.Abs(1 - t * 2));
            _dropShapes[i].Opacity = Math.Min(1.0, (1 - t) * 4) * 0.8;
        }

        _floatMove.Y = Math.Sin(_drift * 1.7) * 4;
        _tailSpin.Angle = Math.Sin(_drift * 2.2) * 2;
        _ripples.Opacity = 0.65 + Math.Sin(_drift * 1.7) * 0.2;

        bool blink = _sleepy || (_drift % 5.2 > 4.98);
        _eyeScale.ScaleY = blink ? 0.12 : 1.0;
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}
