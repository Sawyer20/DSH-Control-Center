// ============================================================================
//  WpfUI.cs - "DSH 控制中心"
//  ARCHITECTURE (strict): native WPF only (Border+Grid+StackPanel+
//  ControlTemplate); NO Canvas absolute positioning, NO images emulating UI,
//  NO fake cards; every card is an independent UserControl; rounded corners
//  only via Border.CornerRadius; buttons use custom ControlTemplate.
//  Each nav page uses its OWN card instances (an element can never have two
//  parents) - state is pushed to every registered view.
// ============================================================================
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

internal static class WpfTheme
{
    // ---------------------------------------------------------------------
    //  DSH design tokens. Dark values come from the DSH web frontend
    //  (@deepseek-ai/dsh-client-ui-theme, `body[data-ds-dark-theme]`); light
    //  values are the matching light aliases of the same system.
    //
    //  The brushes are deliberately NOT frozen: Apply(dark) only rewrites each
    //  brush's Color, so every element that already holds a reference
    //  re-themes live (no window rebuild, no re-binding).
    // ---------------------------------------------------------------------
    public static readonly SolidColorBrush WindowBg   = Brush(0x15, 0x15, 0x17);
    public static readonly SolidColorBrush BaseLayer  = Brush(0x15, 0x15, 0x17, 0xD9);
    public static readonly SolidColorBrush Sidebar    = Brush(0x1B, 0x1B, 0x1C);
    public static readonly SolidColorBrush CardSolid  = Brush(0x23, 0x23, 0x24);
    public static readonly SolidColorBrush HeroBg     = Brush(0x28, 0x31, 0x42);
    public static readonly SolidColorBrush Border     = Brush(0xFF, 0xFF, 0xFF, 0x1F);
    public static readonly SolidColorBrush CardBorder = Brush(0xFF, 0xFF, 0xFF, 0x1F);
    public static readonly SolidColorBrush Separator  = Brush(0xFF, 0xFF, 0xFF, 0x1F);

    public static readonly SolidColorBrush TitleText   = Brush(0xED, 0xEF, 0xF3);
    public static readonly SolidColorBrush TextPrimary = Brush(0xED, 0xEF, 0xF3);
    public static readonly SolidColorBrush TextSecond  = Brush(0xCF, 0xD3, 0xD6);
    public static readonly SolidColorBrush TextMuted   = Brush(0xAD, 0xB2, 0xB8);
    public static readonly SolidColorBrush TextLight   = Brush(0x81, 0x85, 0x8C);

    public static readonly SolidColorBrush Accent        = Brush(0x67, 0x9E, 0xFE);
    public static readonly SolidColorBrush AccentHover   = Brush(0x56, 0x86, 0xFE);
    public static readonly SolidColorBrush AccentPressed = Brush(0x41, 0x76, 0xE6);
    public static readonly SolidColorBrush AccentSoft    = Brush(0xFF, 0xFF, 0xFF, 0x14);

    public static readonly SolidColorBrush PrimaryFill    = Brush(0xF9, 0xFA, 0xFB);
    public static readonly SolidColorBrush PrimaryHover   = Brush(0xEB, 0xEE, 0xF2);
    public static readonly SolidColorBrush PrimaryPressed = Brush(0xCF, 0xD3, 0xD6);
    public static readonly SolidColorBrush PrimaryText    = Brush(0x0F, 0x11, 0x15);

    public static readonly SolidColorBrush GlassBg      = Brush(0x2C, 0x2C, 0x2E);
    public static readonly SolidColorBrush GlassHover   = Brush(0x35, 0x36, 0x38);
    public static readonly SolidColorBrush GlassPressed = Brush(0x43, 0x45, 0x4A);
    public static readonly SolidColorBrush BtnBorder    = Brush(0xFF, 0xFF, 0xFF, 0x1F);
    public static readonly SolidColorBrush BtnText      = Brush(0xF9, 0xFA, 0xFB);
    public static readonly SolidColorBrush NavHover     = Brush(0x2C, 0x2C, 0x2E);
    public static readonly SolidColorBrush NavActive    = Brush(0x43, 0x45, 0x4A);

    public static readonly SolidColorBrush Success = Brush(0x22, 0xC5, 0x5E);
    public static readonly SolidColorBrush Warning = Brush(0xF5, 0x9E, 0x0B);
    public static readonly SolidColorBrush Danger  = Brush(0xF2, 0x5A, 0x5A);
    public static readonly SolidColorBrush Offline = Brush(0x81, 0x85, 0x8C);
    public static readonly SolidColorBrush ChipGreenBg   = Brush(0x23, 0x3C, 0x2C);
    public static readonly SolidColorBrush ChipGreenText = Brush(0x62, 0xDE, 0x92);
    public static readonly SolidColorBrush SwitchOff     = Brush(0x35, 0x36, 0x38);

    // Pet speech bubble: a translucent light-blue card from the same family as
    // the light theme (#E8F2FF / #9CC0F0) so it reads as part of the product
    // even when it floats over the desktop. Deliberately theme-invariant.
    public static readonly SolidColorBrush BubbleBg     = Brush(0xE8, 0xF2, 0xFF, 0xE6);
    public static readonly SolidColorBrush BubbleBorder = Brush(0x9C, 0xC0, 0xF0, 0xFF);
    public static readonly SolidColorBrush BubbleText   = Brush(0x12, 0x2F, 0x5E);

    // NOTE: the scrollbar pill colours are NOT brushes here. A brush placed in
    // an element's ResourceDictionary gets frozen by WPF, and mutating a frozen
    // brush is what crashed the app on the first light/dark switch; those
    // colours are baked into the parsed scrollbar template instead (see Ui).

    public static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text");
    // Display optical size reads better (and softer) for large headings
    public static readonly FontFamily UiDisplay = new FontFamily("Segoe UI Variable Display");
    public static readonly FontFamily Mono = new FontFamily("Cascadia Mono");

    public static bool IsDark { get; private set; }
    /// <summary>Raised after a theme switch so window-level chrome can follow.</summary>
    public static event Action Changed;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            Rgb(WindowBg, 0x15, 0x15, 0x17);      Argb(BaseLayer, 0x15, 0x15, 0x17, 0xD9);
            Argb(Sidebar, 0x1B, 0x1B, 0x1C, 0xFF); Rgb(CardSolid, 0x23, 0x23, 0x24);
            Rgb(HeroBg, 0x28, 0x31, 0x42);
            Argb(Border, 0xFF, 0xFF, 0xFF, 0x1F);  Argb(CardBorder, 0xFF, 0xFF, 0xFF, 0x1F);
            Argb(Separator, 0xFF, 0xFF, 0xFF, 0x1F);
            Rgb(TitleText, 0xED, 0xEF, 0xF3);      Rgb(TextPrimary, 0xED, 0xEF, 0xF3);
            Rgb(TextSecond, 0xCF, 0xD3, 0xD6);     Rgb(TextMuted, 0xAD, 0xB2, 0xB8);
            Rgb(TextLight, 0x81, 0x85, 0x8C);
            Rgb(Accent, 0x67, 0x9E, 0xFE);         Rgb(AccentHover, 0x56, 0x86, 0xFE);
            Rgb(AccentPressed, 0x41, 0x76, 0xE6);  Argb(AccentSoft, 0xFF, 0xFF, 0xFF, 0x14);
            Rgb(PrimaryFill, 0xF9, 0xFA, 0xFB);    Rgb(PrimaryHover, 0xEB, 0xEE, 0xF2);
            Rgb(PrimaryPressed, 0xCF, 0xD3, 0xD6); Rgb(PrimaryText, 0x0F, 0x11, 0x15);
            Rgb(GlassBg, 0x2C, 0x2C, 0x2E);        Rgb(GlassHover, 0x35, 0x36, 0x38);
            Rgb(GlassPressed, 0x43, 0x45, 0x4A);   Argb(BtnBorder, 0xFF, 0xFF, 0xFF, 0x1F);
            Rgb(BtnText, 0xF9, 0xFA, 0xFB);        Rgb(NavHover, 0x2C, 0x2C, 0x2E);
            Rgb(NavActive, 0x43, 0x45, 0x4A);
            Rgb(Success, 0x22, 0xC5, 0x5E);        Rgb(Warning, 0xF5, 0x9E, 0x0B);
            Rgb(Danger, 0xF2, 0x5A, 0x5A);         Rgb(Offline, 0x81, 0x85, 0x8C);
            Rgb(ChipGreenBg, 0x23, 0x3C, 0x2C);    Rgb(ChipGreenText, 0x62, 0xDE, 0x92);
            Rgb(SwitchOff, 0x35, 0x36, 0x38);
        }
        else
        {
            Rgb(WindowBg, 0xEF, 0xF6, 0xFF);      Rgb(BaseLayer, 0xEF, 0xF6, 0xFF);
            Rgb(Sidebar, 0xE9, 0xF2, 0xFF);        Rgb(CardSolid, 0xFF, 0xFF, 0xFF);
            Rgb(HeroBg, 0xE1, 0xED, 0xFF);
            Rgb(Border, 0xDC, 0xE7, 0xF7);         Rgb(CardBorder, 0xDC, 0xE7, 0xF7);
            Rgb(Separator, 0xE4, 0xEC, 0xF8);
            Rgb(TitleText, 0x1A, 0x24, 0x33);      Rgb(TextPrimary, 0x1F, 0x29, 0x33);
            Rgb(TextSecond, 0x5B, 0x6B, 0x82);     Rgb(TextMuted, 0x7D, 0x8C, 0xA6);
            Rgb(TextLight, 0xA5, 0xB2, 0xC6);
            Rgb(Accent, 0x2E, 0x7B, 0xF6);         Rgb(AccentHover, 0x4A, 0x90, 0xFF);
            Rgb(AccentPressed, 0x1E, 0x63, 0xD6);  Rgb(AccentSoft, 0xE3, 0xEE, 0xFF);
            Rgb(PrimaryFill, 0x2E, 0x7B, 0xF6);    Rgb(PrimaryHover, 0x4A, 0x90, 0xFF);
            Rgb(PrimaryPressed, 0x1E, 0x63, 0xD6); Rgb(PrimaryText, 0xFF, 0xFF, 0xFF);
            Rgb(GlassBg, 0xF4, 0xF9, 0xFF);        Rgb(GlassHover, 0xE8, 0xF1, 0xFE);
            Rgb(GlassPressed, 0xDC, 0xE9, 0xFB);   Rgb(BtnBorder, 0xD9, 0xE5, 0xF6);
            Rgb(BtnText, 0x2F, 0x3F, 0x58);        Rgb(NavHover, 0xE8, 0xF2, 0xFF);
            Rgb(NavActive, 0xD9, 0xE8, 0xFF);
            Rgb(Success, 0x2F, 0xA6, 0x6E);        Rgb(Warning, 0xF0, 0xA9, 0x4B);
            Rgb(Danger, 0xE5, 0x48, 0x4D);         Rgb(Offline, 0x9A, 0xA7, 0xBA);
            Rgb(ChipGreenBg, 0xE6, 0xF7, 0xEF);    Rgb(ChipGreenText, 0x1F, 0x8F, 0x5A);
            Rgb(SwitchOff, 0xDC, 0xE7, 0xF7);
        }
        Action h = Changed;
        if (h != null) h();
    }

    private static void Rgb(SolidColorBrush b, byte r, byte g, byte bl) { b.Color = Color.FromRgb(r, g, bl); }
    private static void Argb(SolidColorBrush b, byte r, byte g, byte bl, byte a) { b.Color = Color.FromArgb(a, r, g, bl); }
    private static SolidColorBrush Brush(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) { return new SolidColorBrush(Color.FromArgb(a, r, g, b)); }
}

// RoundedButton - custom ControlTemplate (no OnRender owner-drawing)
internal class RoundedButton : Button
{
    // Brushes are applied through setters: assigning FillNormal/FillHover/
    // FillPressed/TextBrush AFTER construction (as Buttons.Glass does) must
    // repaint immediately - otherwise the button keeps the constructor's
    // default fill while its label already uses the new colour, which is
    // exactly how the balance "刷新" label became invisible (white on white)
    // until the first mouse-over.
    private SolidColorBrush _fillNormal, _fillHover, _fillPressed, _textBrush;
    public SolidColorBrush FillNormal { get { return _fillNormal; } set { _fillNormal = value; Refresh(); } }
    public SolidColorBrush FillHover { get { return _fillHover; } set { _fillHover = value; Refresh(); } }
    public SolidColorBrush FillPressed { get { return _fillPressed; } set { _fillPressed = value; Refresh(); } }
    public SolidColorBrush TextBrush { get { return _textBrush; } set { _textBrush = value; Refresh(); } }

    // Real DP so the template's Border tracks Corner changes made after
    // construction (CaptionButton / Refresh both set Corner afterwards).
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        "Radius", typeof(CornerRadius), typeof(RoundedButton), new PropertyMetadata(new CornerRadius(8)));
    public CornerRadius Radius { get { return (CornerRadius)GetValue(RadiusProperty); } set { SetValue(RadiusProperty, value); } }
    public double Corner { get { return Radius.TopLeft; } set { Radius = new CornerRadius(value); } }

    private bool _hv;
    private bool _dn;

    public RoundedButton()
    {
        Corner = 8;                          // web button radius
        FillNormal = WpfTheme.PrimaryFill;   // dark-theme contrast button
        FillHover = WpfTheme.PrimaryHover;
        FillPressed = WpfTheme.PrimaryPressed;
        TextBrush = WpfTheme.PrimaryText;
        FontFamily = WpfTheme.Ui;
        FontSize = 13;
        Cursor = Cursors.Hand;
        Height = 36;
        Padding = new Thickness(14, 0, 14, 0);
        Foreground = TextBrush;
        Background = FillNormal;
        BorderBrush = WpfTheme.BtnBorder;
        BorderThickness = new Thickness(0);

        var tpl = new ControlTemplate(typeof(RoundedButton));
        var border = new FrameworkElementFactory(typeof(Border));
        RelativeSource self = new RelativeSource(RelativeSourceMode.TemplatedParent);
        border.SetBinding(Border.CornerRadiusProperty, new Binding("Radius") { RelativeSource = self });
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = self });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = self });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = self });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        tpl.VisualTree = border;
        Template = tpl;

        MouseEnter += (s, e) => { _hv = true; Refresh(); };
        MouseLeave += (s, e) => { _hv = false; _dn = false; Refresh(); };
        MouseLeftButtonDown += (s, e) => { _dn = true; Refresh(); };
        MouseLeftButtonUp += (s, e) => { _dn = false; Refresh(); };
        IsEnabledChanged += (s, e) => Refresh();
    }

    /// <summary>Re-apply fill/text from the state flags (also for callers that swap the brushes).</summary>
    public void Refresh()
    {
        SolidColorBrush fill = _dn ? _fillPressed : (_hv ? _fillHover : _fillNormal);
        if (fill != null) Background = fill;
        if (_textBrush == null) return;
        Foreground = IsEnabled ? _textBrush
            : new SolidColorBrush(Color.FromArgb(150, _textBrush.Color.R, _textBrush.Color.G, _textBrush.Color.B));
    }
}

internal static class Ui
{
    public static TextBlock Tb(string text, double size, SolidColorBrush brush, FontWeight weight = default(FontWeight))
    {
        return Tb(text, size, brush, weight, WpfTheme.Ui);
    }
    public static TextBlock Tb(string text, double size, SolidColorBrush brush, FontWeight weight, FontFamily family)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = weight, FontFamily = family, VerticalAlignment = VerticalAlignment.Center };
        // Small UI text keeps the window policy (Display + ClearType: hinted,
        // pixel-snapped, no distortion). Large headings opt into Ideal +
        // grayscale, which looks softer and avoids colour fringing on big glyphs.
        if (size >= 18)
        {
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Grayscale);
        }
        return tb;
    }
    public static Border Card(double radius = 12)
    {
        return new Border { CornerRadius = new CornerRadius(radius), Background = WpfTheme.CardSolid, BorderBrush = WpfTheme.CardBorder, BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14) };
    }

    // ---- thin scrollbar ---------------------------------------------------
    // Track.Thumb / Track.*RepeatButton are plain CLR properties (no
    // DependencyProperty) and Track does not implement IAddChild, so a
    // FrameworkElementFactory template is impossible - that is exactly what
    // crashed BuildId -13. A ControlTemplate parsed from a XAML string at
    // runtime (no XAML file, no MSBuild involved) can express the property
    // elements; XamlReader.Parse is the supported way to do that.
    // Colours are baked in per theme instead of shared brushes: a brush placed
    // in an element's ResourceDictionary is frozen by WPF, and mutating a
    // frozen brush is what crashed the app on the first light/dark switch.
    // Vertical-only usage: IsDirectionReversed is fixed to True.
    private static readonly List<ScrollViewer> _scrollViews = new List<ScrollViewer>();
    private static ControlTemplate _tplDark, _tplLight;
    private static bool _tplFailed;

    private static string ScrollBarXaml(string thumb, string hover, string drag)
    {
        return @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                   xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                   TargetType='{x:Type ScrollBar}'>
  <Grid Background='Transparent' SnapsToDevicePixels='True'>
    <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False' IsTabStop='False' Opacity='0'/>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='{x:Type Thumb}'>
              <Border x:Name='ThumbFill' Width='6' HorizontalAlignment='Center' Background='@THUMB@' CornerRadius='3'/>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='ThumbFill' Property='Background' Value='@HOVER@'/>
                </Trigger>
                <Trigger Property='IsDragging' Value='True'>
                  <Setter TargetName='ThumbFill' Property='Background' Value='@DRAG@'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False' IsTabStop='False' Opacity='0'/>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>".Replace("@THUMB@", thumb).Replace("@HOVER@", hover).Replace("@DRAG@", drag);
    }

    // ---- top fade (softens the scroll cut-off) ----------------------------
    // An overlay gradient instead of an OpacityMask: masking the scroll region
    // would force its text back to grayscale rendering.
    private static readonly List<Border> _fades = new List<Border>();
    private static readonly List<SolidColorBrush> _fadeSurfaces = new List<SolidColorBrush>();

    public static Border TopFade(double px, SolidColorBrush surface)
    {
        var b = new Border { Height = px, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        b.Background = FadeBrush(surface);
        _fades.Add(b);
        _fadeSurfaces.Add(surface);
        return b;
    }

    private static LinearGradientBrush FadeBrush(SolidColorBrush surface)
    {
        Color c = surface == null ? Colors.Transparent : surface.Color;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(c, 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        return g;
    }

    /// <summary>Re-colour every registered top fade after a theme switch.</summary>
    public static void RefreshTopFades()
    {
        for (int i = 0; i < _fades.Count; i++)
        {
            var g = _fades[i].Background as LinearGradientBrush;
            if (g == null || g.GradientStops.Count < 2) continue;
            Color c = _fadeSurfaces[i] == null ? Colors.Transparent : _fadeSurfaces[i].Color;
            g.GradientStops[0].Color = c;
            g.GradientStops[1].Color = Color.FromArgb(0, c.R, c.G, c.B);
        }
    }

    /// <summary>Apply the thin scrollbar (10px track / 6px pill) to a ScrollViewer.</summary>
    public static void ThinScroll(ScrollViewer sv)
    {
        if (sv == null) return;
        if (!_scrollViews.Contains(sv)) _scrollViews.Add(sv);
        ApplyScrollStyle(sv);
    }

    /// <summary>Re-skin every registered ScrollViewer after a theme switch.</summary>
    public static void RefreshScrollSkin()
    {
        for (int i = 0; i < _scrollViews.Count; i++)
        {
            try { ApplyScrollStyle(_scrollViews[i]); } catch { }
        }
    }

    private static void ApplyScrollStyle(ScrollViewer sv)
    {
        ControlTemplate tpl = ScrollBarTemplate();
        if (tpl == null) return;                 // parse failed: keep system default
        var style = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        sv.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        // The ScrollViewer template sizes the ScrollBar from this system
        // resource key, which outranks a Style setter; override it here so the
        // reserved strip (and the pill inside it) is thin.
        try { sv.Resources[SystemParameters.VerticalScrollBarWidthKey] = 10.0; }
        catch { }
    }

    private static ControlTemplate ScrollBarTemplate()
    {
        if (WpfTheme.IsDark)
        {
            if (_tplDark == null && !_tplFailed) _tplDark = ParseScrollTemplate("#29FFFFFF", "#3DFFFFFF", "#4DFFFFFF");
            return _tplDark;
        }
        if (_tplLight == null && !_tplFailed) _tplLight = ParseScrollTemplate("#29000000", "#3D000000", "#4D000000");
        return _tplLight;
    }

    private static ControlTemplate ParseScrollTemplate(string thumb, string hover, string drag)
    {
        try
        {
            return (ControlTemplate)XamlReader.Parse(ScrollBarXaml(thumb, hover, drag));
        }
        catch (Exception ex)
        {
            // cosmetic only - never take the app down for a scrollbar
            _tplFailed = true;
            try { File.AppendAllText(Program.LogFile, "[UI 滚动条样式跳过] " + ex.Message + Environment.NewLine); } catch { }
            return null;
        }
    }
}

internal static class Buttons
{
    public static RoundedButton Primary(string text, int w)
    {
        return new RoundedButton { Content = text, Width = w };
    }
    public static RoundedButton Glass(string text, int w, double h)
    {
        var b = new RoundedButton { Content = text, Width = w, Height = h, FontSize = 13 };
        b.FillNormal = WpfTheme.GlassBg;
        b.FillHover = WpfTheme.GlassHover;
        b.FillPressed = WpfTheme.GlassPressed;
        b.TextBrush = WpfTheme.BtnText;
        b.BorderBrush = WpfTheme.BtnBorder;
        b.BorderThickness = new Thickness(1);
        return b;
    }

    // icon (rotated 90 deg) + label, centered content for RoundedButton
    public static System.Windows.FrameworkElement ArrowLabel(string icon, string label, SolidColorBrush fore)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ic = new TextBlock { Text = icon, FontFamily = WpfTheme.Ui, FontSize = 12, Foreground = fore, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
        ic.RenderTransformOrigin = new Point(0.5, 0.5);
        ic.RenderTransform = new RotateTransform(90);
        sp.Children.Add(ic);
        var lb = new TextBlock { Text = label, FontFamily = WpfTheme.Ui, FontSize = 12, Foreground = fore, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(lb);
        return sp;
    }
}

// ============================================================================
//  Tray: fully native Shell_NotifyIcon + WPF self-drawn menu -> TrayNative.cs
// ============================================================================

// ============================================================================
//  Cards (each an independent UserControl)
// ============================================================================
internal class HeroView : UserControl
{
    private TextBlock _title, _sub;
    private RoundedButton _btnOpen, _btnRestart, _btnStop;
    private Border _dot;
    public RoundedButton OpenButton { get { return _btnOpen; } }
    public RoundedButton RestartButton { get { return _btnRestart; } }
    public RoundedButton StopButton { get { return _btnStop; } }

    public HeroView()
    {
        var card = Ui.Card(12);
        card.Background = WpfTheme.HeroBg;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _dot = Dot(WpfTheme.Success);
        row.Children.Add(_dot);
        _title = Ui.Tb("服务运行中", 20, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay);
        row.Children.Add(_title);
        left.Children.Add(row);
        _sub = Ui.Tb("前端已就绪 · 已运行 0 分钟", 12, WpfTheme.TextSecond);
        left.Children.Add(_sub);
        left.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        _btnOpen = Buttons.Primary("打开 DSH ↗", 132);
        btns.Children.Add(_btnOpen);
        _btnRestart = Buttons.Glass("", 138, 36);
        _btnRestart.Content = Buttons.ArrowLabel("↻", "重新启动", WpfTheme.BtnText);
        _btnRestart.Margin = new Thickness(10, 0, 0, 0);
        btns.Children.Add(_btnRestart);
        _btnStop = Buttons.Glass("停止服务", 104, 36);
        _btnStop.Margin = new Thickness(10, 0, 0, 0);
        btns.Children.Add(_btnStop);
        left.Children.Add(btns);
        grid.Children.Add(left);

        var badge = new Border { CornerRadius = new CornerRadius(46), Background = Brushes.White, BorderBrush = WpfTheme.Border, BorderThickness = new Thickness(1), Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        // brand whale logo in the top-right roundel (image used as branding
        // only, never as fake UI); falls back to text when the PNG is missing
        string whalePng = Path.Combine(Program.RootDir, "whale_base.png");
        FrameworkElement mark = null;
        try
        {
            if (File.Exists(whalePng))
            {
                var img = new Image { Width = 60, Height = 60, Stretch = Stretch.Uniform };
                img.Source = new BitmapImage(new Uri(whalePng));
                mark = img;
            }
        }
        catch { }
        if (mark == null)
        {
            mark = new TextBlock { Text = "DSH", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = WpfTheme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        badge.Child = mark;
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        card.Child = grid;
        Content = card;
    }

    private static Border Dot(SolidColorBrush color)
    {
        return new Border { CornerRadius = new CornerRadius(6), Background = color, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    }

    public void SetStatus(string state, string sub)
    {
        string title = "服务已停止";
        SolidColorBrush color = WpfTheme.Offline;
        if (state == "running") { title = "服务运行中"; color = WpfTheme.Success; }
        else if (state == "starting") { title = "服务启动中"; color = WpfTheme.Warning; }
        else if (state == "error") { title = "服务异常"; color = WpfTheme.Danger; }
        _title.Text = title;
        _sub.Text = sub;
        _dot.Background = color;
    }
}

internal class ServiceInfoView : UserControl
{
    private TextBlock _pid, _build, _start;
    public ServiceInfoView()
    {
        var card = Ui.Card();
        var st = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badgeHost = new Border { CornerRadius = new CornerRadius(10), Background = WpfTheme.ChipGreenBg, Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        badgeHost.Child = Ui.Tb("正常运行", 12, WpfTheme.ChipGreenText, FontWeights.Medium);
        Grid.SetColumn(badgeHost, 1);
        head.Children.Add(Ui.Tb("服务信息", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        head.Children.Add(badgeHost);
        st.Children.Add(head);
        st.Children.Add(new Border { Height = 8, Background = Brushes.Transparent });
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddRow(grid, 0, "服务地址", Program.Url, true);
        _pid = AddRow(grid, 1, "进程 ID (PID)", "—", false);
        _build = AddRow(grid, 2, "构建版本 (Build)", "—", false);
        _start = AddRow(grid, 3, "启动时间", "—", false);
        st.Children.Add(grid);
        card.Child = st;
        Content = card;
    }
    private TextBlock AddRow(Grid grid, int row, string key, string val, bool mono)
    {
        var k = Ui.Tb(key, 12, WpfTheme.TextSecond);
        Grid.SetRow(k, row); Grid.SetColumn(k, 0);
        grid.Children.Add(k);
        var v = Ui.Tb(val, 12, WpfTheme.TextPrimary, FontWeights.SemiBold);
        if (mono) v.FontFamily = WpfTheme.Mono;
        Grid.SetRow(v, row); Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return v;
    }
    public void SetInfo(string pid, string build, string start)
    {
        _pid.Text = pid;
        _build.Text = build;
        _start.Text = start;
    }
}

internal class BalanceView : UserControl
{
    private TextBlock _val, _detail, _meta, _today, _rate, _eta, _life, _lifeLabel;
    public RoundedButton Refresh;
    public BalanceView()
    {
        var card = Ui.Card();
        var grid = new Grid();
        // header row is Auto so the 30px refresh button is never clipped
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Tb("DeepSeek 余额", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        Refresh = Buttons.Glass("", 96, 30);
        Refresh.Corner = 14;
        Refresh.FontSize = 12;
        Refresh.Padding = new Thickness(0);
        Refresh.Content = Buttons.ArrowLabel("↻", "刷新", WpfTheme.BtnText);
        Grid.SetColumn(Refresh, 1);
        head.Children.Add(Refresh);
        grid.Children.Add(head);
        _val = Ui.Tb("—", 30, WpfTheme.TitleText, FontWeights.SemiBold);
        Grid.SetRow(_val, 1); grid.Children.Add(_val);
        _detail = Ui.Tb("", 11, WpfTheme.TextSecond);
        Grid.SetRow(_detail, 2); grid.Children.Add(_detail);
        _meta = Ui.Tb("", 11, WpfTheme.TextLight);
        Grid.SetRow(_meta, 3); grid.Children.Add(_meta);

        // real usage / cost row (projcache + tiered pricing, ADR-0007)
        var costRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 4; i++) costRow.ColumnDefinitions.Add(new ColumnDefinition());
        _today = CostCell(costRow, 0, "今日消费");
        _rate = CostCell(costRow, 1, "近 1 小时");
        _life = CostCell(costRow, 2, "累计", out _lifeLabel);
        _eta = CostCell(costRow, 3, "预计可用");
        Grid.SetRow(costRow, 4); grid.Children.Add(costRow);

        card.Child = grid;
        Content = card;
    }

    private TextBlock CostCell(Grid host, int col, string label)
    {
        TextBlock ignore;
        return CostCell(host, col, label, out ignore);
    }

    private TextBlock CostCell(Grid host, int col, string label, out TextBlock labelRef)
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        labelRef = Ui.Tb(label, 10.5, WpfTheme.TextLight);
        box.Children.Add(labelRef);
        var v = Ui.Tb("—", 13, WpfTheme.TextPrimary, FontWeights.SemiBold);
        box.Children.Add(v);
        Grid.SetColumn(box, col);
        host.Children.Add(box);
        return v;
    }

    public void Set(string value, string detail, string meta)
    {
        _val.Text = value; _detail.Text = detail; _meta.Text = meta;
    }
    public void SetCost(string today, string hour, string lifetime, int days, string eta)
    {
        if (_today == null) return;
        _today.Text = today; _rate.Text = hour; _eta.Text = eta;
        if (_life != null) _life.Text = lifetime;
        if (_lifeLabel != null) _lifeLabel.Text = "累计 · " + days + " 天";
    }
    public void SetMeta(string meta, SolidColorBrush color) { _meta.Text = meta; _meta.Foreground = color; }
}

internal class UsageView : UserControl
{
    private TextBlock _u1, _u3, _u6, _tin, _tout, _tcache;
    public UsageView()
    {
        var card = Ui.Card();
        var st = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Tb("用量与成本", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        var cap = Ui.Tb("projcache 真实计量", 11, WpfTheme.TextLight);
        Grid.SetColumn(cap, 1);
        head.Children.Add(cap);
        st.Children.Add(head);
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        _u1 = Cell(grid, 0, 0, "近 1 小时", true);
        _u3 = Cell(grid, 1, 0, "近 3 小时", true);
        _u6 = Cell(grid, 2, 0, "近 6 小时", true);
        _tin = Cell(grid, 0, 1, "输入 tokens", false);
        _tout = Cell(grid, 1, 1, "输出 tokens", false);
        _tcache = Cell(grid, 2, 1, "缓存读 tokens", false);
        st.Children.Add(grid);
        card.Child = st;
        Content = card;
    }
    private TextBlock Cell(Grid grid, int col, int row, string label, bool big)
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(Ui.Tb(label, 10.5, WpfTheme.TextLight));
        var v = Ui.Tb("—", big ? 13 : 11.5, big ? WpfTheme.TextPrimary : WpfTheme.TextSecond, big ? FontWeights.SemiBold : FontWeights.Normal);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        box.Children.Add(v);
        Grid.SetRow(box, row); Grid.SetColumn(box, col);
        grid.Children.Add(box);
        return v;
    }
    public void Set(string h1, string h3, string h6) { _u1.Text = h1; _u3.Text = h3; _u6.Text = h6; }
    public void SetTokens(string inTok, string outTok, string cacheTok)
    {
        if (_tin == null) return;
        _tin.Text = inTok; _tout.Text = outTok; _tcache.Text = cacheTok;
    }
}

internal class EventsView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    public EventsView()
    {
        var card = Ui.Card();
        var st = new StackPanel();
        st.Children.Add(Ui.Tb("最近事件", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        st.Children.Add(new Border { Height = 8, Background = Brushes.Transparent });
        st.Children.Add(_list);
        card.Child = st;
        Content = card;
    }
    public void Add(string title, string sub, SolidColorBrush dot)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var d = new Border { CornerRadius = new CornerRadius(4), Background = dot, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(d);
        var txt = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        txt.Children.Add(Ui.Tb(title, 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        txt.Children.Add(Ui.Tb(sub, 11, WpfTheme.TextMuted));
        Grid.SetColumn(txt, 1);
        row.Children.Add(txt);
        var t = Ui.Tb(DateTime.Now.ToString("HH:mm:ss"), 11, WpfTheme.TextLight);
        Grid.SetColumn(t, 2);
        row.Children.Add(t);
        _list.Children.Insert(0, row);
        while (_list.Children.Count > 12) _list.Children.RemoveAt(12);
    }
}

// Minimal themed slider (a Slider template cannot be built with
// FrameworkElementFactory either - Track.Thumb is not a DP - so this is a
// small self-contained control: track + fill + draggable thumb).
internal sealed class MiniSlider : Grid
{
    private readonly Border _track;
    private readonly Border _fill;
    private readonly Border _thumb;
    private double _value = 0.5;
    private bool _drag;

    public event EventHandler ValueChanged;

    public MiniSlider()
    {
        Width = 130;
        Height = 22;
        Background = Brushes.Transparent;
        _track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = WpfTheme.SwitchOff, VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = WpfTheme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        _thumb = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = WpfTheme.PrimaryFill, BorderBrush = WpfTheme.BtnBorder, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_thumb);
        SizeChanged += delegate { Layout(); };
        MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
        {
            _drag = true;
            CaptureMouse();
            SetFromX(e.GetPosition(this).X);
        };
        MouseMove += delegate(object s, MouseEventArgs e)
        {
            if (_drag) SetFromX(e.GetPosition(this).X);
        };
        MouseLeftButtonUp += delegate
        {
            _drag = false;
            ReleaseMouseCapture();
        };
    }

    /// <summary>0..1 position (setting it does not raise ValueChanged).</summary>
    public double Value
    {
        get { return _value; }
        set { _value = Math.Max(0, Math.Min(1, value)); Layout(); }
    }

    private void SetFromX(double x)
    {
        double w = ActualWidth - 16;
        if (w <= 0) return;
        _value = Math.Max(0, Math.Min(1, (x - 8) / w));
        Layout();
        EventHandler h = ValueChanged;
        if (h != null) h(this, EventArgs.Empty);
    }

    private void Layout()
    {
        double w = ActualWidth - 16;
        if (w < 0) w = 0;
        _fill.Width = w * _value;
        _thumb.Margin = new Thickness(w * _value, 0, 0, 0);
    }
}

internal class SettingsView : UserControl
{
    private Border _switch;
    private Border _thumb;
    private Border _segLight, _segDark;
    private TextBlock _segLightText, _segDarkText;
    private Border _petSwitch, _petThumb;
    private MiniSlider _petSpeed, _petBusy;
    private TextBlock _petSpeedText, _petBusyText;
    private bool _suppressPet;
    public event EventHandler Toggled;
    /// <summary>Raised with true = dark theme.</summary>
    public event EventHandler<bool> AppearanceChanged;
    /// <summary>Raised with true = show the desktop pet.</summary>
    public event EventHandler<bool> PetToggled;
    /// <summary>Raised with the pet animation speed (0.2x .. 3.0x).</summary>
    public event EventHandler<double> PetSpeedChanged;
    /// <summary>Raised with the busy spout size (0 .. 0.6).</summary>
    public event EventHandler<double> PetBusyChanged;
    /// <summary>Raised when the user asks for a diagnostic package (P0-4).</summary>
    public event EventHandler DiagnoseRequested;
    /// <summary>Raised when the user asks for a ~/.dsh backup (P1-2).</summary>
    public event EventHandler BackupRequested;
    /// <summary>Raised when the user asks to restore a backup (P1-2).</summary>
    public event EventHandler RestoreRequested;
    /// <summary>Raised with the backup retention count (1–20).</summary>
    public event EventHandler<int> BackupKeepChanged;

    private MiniSlider _backupKeep;
    private TextBlock _backupKeepText;
    private bool _suppressKeep;

    /// <summary>Maps the slider (0..1) to a retention count (1..20).</summary>
    public int BackupKeepValue
    {
        get { return _backupKeep == null ? 5 : 1 + (int)Math.Round(_backupKeep.Value * 19); }
    }

    /// <summary>Applies the persisted retention count without firing the change event.</summary>
    public void UpdateBackupKeep(int keep)
    {
        if (keep < 1) keep = 1;
        if (keep > 20) keep = 20;
        _suppressKeep = true;
        try
        {
            if (_backupKeep != null) _backupKeep.Value = (keep - 1) / 19.0;
            if (_backupKeepText != null) _backupKeepText.Text = keep + " 份";
        }
        finally { _suppressKeep = false; }
    }
    public SettingsView()
    {
        var card = Ui.Card();
        var st = new StackPanel();
        st.Children.Add(Ui.Tb("通用", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        st.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb("开机自动启动", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb.Children.Add(Ui.Tb("登录 Windows 后自动启动 DSH 控制中心", 11, WpfTheme.TextMuted));
        row.Children.Add(lb);
        _switch = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = WpfTheme.BtnBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        _thumb = new Border { CornerRadius = new CornerRadius(10), Background = WpfTheme.PrimaryFill, Width = 20, Height = 20 };
        _switch.Child = _thumb;
        _switch.MouseLeftButtonUp += delegate { if (Toggled != null) Toggled(this, EventArgs.Empty); };
        Grid.SetColumn(_switch, 1);
        row.Children.Add(_switch);
        st.Children.Add(row);

        // ---- appearance (light / dark) ----
        st.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
        var row2 = new Grid();
        row2.ColumnDefinitions.Add(new ColumnDefinition());
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb2 = new StackPanel();
        lb2.Children.Add(Ui.Tb("外观", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb2.Children.Add(Ui.Tb("浅色 / 深色主题（与 DSH 网页前端同源配色）", 11, WpfTheme.TextMuted));
        row2.Children.Add(lb2);
        var seg = new Border { Background = WpfTheme.SwitchOff, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        var segGrid = new Grid();
        segGrid.ColumnDefinitions.Add(new ColumnDefinition());
        segGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _segLight = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(16, 5, 16, 5), Cursor = Cursors.Hand };
        _segLightText = Ui.Tb("浅色", 12, WpfTheme.TextSecond, FontWeights.SemiBold);
        _segLight.Child = _segLightText;
        segGrid.Children.Add(_segLight);
        _segDark = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(16, 5, 16, 5), Cursor = Cursors.Hand };
        _segDarkText = Ui.Tb("深色", 12, WpfTheme.TextSecond, FontWeights.SemiBold);
        _segDark.Child = _segDarkText;
        Grid.SetColumn(_segDark, 1);
        segGrid.Children.Add(_segDark);
        seg.Child = segGrid;
        Grid.SetColumn(seg, 1);
        row2.Children.Add(seg);
        _segLight.MouseLeftButtonUp += delegate { if (AppearanceChanged != null) AppearanceChanged(this, false); };
        _segDark.MouseLeftButtonUp += delegate { if (AppearanceChanged != null) AppearanceChanged(this, true); };
        st.Children.Add(row2);

        // ---- desktop pet ----
        st.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
        var row3 = new Grid();
        row3.ColumnDefinitions.Add(new ColumnDefinition());
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb3 = new StackPanel();
        lb3.Children.Add(Ui.Tb("桌面宠物", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb3.Children.Add(Ui.Tb("漂浮的鲸鱼：显示状态、弹出任务与审批提醒", 11, WpfTheme.TextMuted));
        row3.Children.Add(lb3);
        _petSwitch = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = WpfTheme.BtnBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        _petThumb = new Border { CornerRadius = new CornerRadius(10), Background = WpfTheme.PrimaryFill, Width = 20, Height = 20 };
        _petSwitch.Child = _petThumb;
        _petSwitch.MouseLeftButtonUp += delegate { if (PetToggled != null) PetToggled(this, !_petOn); };
        Grid.SetColumn(_petSwitch, 1);
        row3.Children.Add(_petSwitch);
        st.Children.Add(row3);

        // ---- pet speed ----
        st.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        st.Children.Add(PetSliderRow("宠物速度", "喷水与浮动的快慢（0.2×–3.0×）", out _petSpeed, out _petSpeedText, true,
            delegate(double v) { if (PetSpeedChanged != null) PetSpeedChanged(this, v); }, "1.0×"));

        // ---- busy spout size ----
        st.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        st.Children.Add(PetSliderRow("工作时水花", "任务运行时的水花大小；完成/提醒时会自动喷到最大", out _petBusy, out _petBusyText, false,
            delegate(double v) { if (PetBusyChanged != null) PetBusyChanged(this, v); }, "35%"));

        // ---- backup / migration (P1-2) ----
        st.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
        var row5 = new Grid();
        row5.ColumnDefinitions.Add(new ColumnDefinition());
        row5.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb5 = new StackPanel();
        lb5.Children.Add(Ui.Tb("备份与迁移", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb5.Children.Add(Ui.Tb("备份 ~/.dsh + 壳数据（排除可重建缓存与 API 凭据）；恢复时先自动备份现状、只补缺失文件", 11, WpfTheme.TextMuted));
        row5.Children.Add(lb5);
        var bkBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var bBackup = Buttons.Glass("立即备份", 84, 30);
        bBackup.Margin = new Thickness(0, 0, 8, 0);
        bBackup.Click += delegate { if (BackupRequested != null) BackupRequested(this, EventArgs.Empty); };
        var bRestore = Buttons.Glass("从备份恢复", 96, 30);
        bRestore.Click += delegate { if (RestoreRequested != null) RestoreRequested(this, EventArgs.Empty); };
        bkBtns.Children.Add(bBackup);
        bkBtns.Children.Add(bRestore);
        Grid.SetColumn(bkBtns, 1);
        row5.Children.Add(bkBtns);
        st.Children.Add(row5);

        // ---- backup retention ----
        st.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        var row6 = new Grid();
        row6.ColumnDefinitions.Add(new ColumnDefinition());
        row6.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb6 = new StackPanel();
        lb6.Children.Add(Ui.Tb("备份保留", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb6.Children.Add(Ui.Tb("超出后把最旧的备份移入回收站（1–20 份）", 11, WpfTheme.TextMuted));
        row6.Children.Add(lb6);
        var keepBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _backupKeep = new MiniSlider();
        _backupKeepText = Ui.Tb("5 份", 12, WpfTheme.TextPrimary, FontWeights.SemiBold);
        _backupKeepText.Width = 44;
        _backupKeepText.TextAlignment = TextAlignment.Right;
        _backupKeepText.Margin = new Thickness(8, 0, 0, 0);
        _backupKeep.ValueChanged += delegate
        {
            if (_suppressKeep) return;
            int n = BackupKeepValue;
            _backupKeepText.Text = n + " 份";
            if (BackupKeepChanged != null) BackupKeepChanged(this, n);
        };
        keepBox.Children.Add(_backupKeep);
        keepBox.Children.Add(_backupKeepText);
        Grid.SetColumn(keepBox, 1);
        row6.Children.Add(keepBox);
        st.Children.Add(row6);

        // (the "include API credentials" switch lives on the 备份 page now)

        // ---- diagnostics (P0-4) ----
        st.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
        var row4 = new Grid();
        row4.ColumnDefinitions.Add(new ColumnDefinition());
        row4.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb4 = new StackPanel();
        lb4.Children.Add(Ui.Tb("诊断", 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb4.Children.Add(Ui.Tb("环境 / 进程 / 日志尾部 / 依赖 / 事件 → 单个 zip（已脱敏）", 11, WpfTheme.TextMuted));
        row4.Children.Add(lb4);
        var diagBtn = Buttons.Glass("生成诊断包", 96, 30);
        diagBtn.Click += delegate { if (DiagnoseRequested != null) DiagnoseRequested(this, EventArgs.Empty); };
        Grid.SetColumn(diagBtn, 1);
        row4.Children.Add(diagBtn);
        st.Children.Add(row4);

        card.Child = st;
        Content = card;
        Update(false);
        UpdateAppearance(WpfTheme.IsDark);
        UpdatePet(true);
    }

    private Grid PetSliderRow(string title, string sub, out MiniSlider slider, out TextBlock valueText, bool speed, Action<double> onChange, string initial)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb(title, 13, WpfTheme.TextPrimary, FontWeights.SemiBold));
        lb.Children.Add(Ui.Tb(sub, 11, WpfTheme.TextMuted));
        row.Children.Add(lb);

        var box = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        slider = new MiniSlider();
        valueText = Ui.Tb(initial, 12, WpfTheme.TextPrimary, FontWeights.SemiBold);
        valueText.Width = 44;
        valueText.TextAlignment = TextAlignment.Right;
        valueText.Margin = new Thickness(8, 0, 0, 0);
        MiniSlider s = slider;
        TextBlock vt = valueText;
        slider.ValueChanged += delegate
        {
            if (_suppressPet) return;          // programmatic load must not re-save
            double v = s.Value;                // 0..1 slider position
            // Report the REAL value, not the slider position: the old code saved
            // the position as if it were the rate (1.0x became 0.29).
            double mapped = speed ? 0.2 + v * 2.8 : v * 0.6;
            onChange(mapped);
            vt.Text = speed ? mapped.ToString("F1") + "×" : ((int)Math.Round(mapped * 100)) + "%";
        };
        box.Children.Add(slider);
        box.Children.Add(valueText);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        return row;
    }

    /// <summary>Test seam: the spout level the busy slider currently represents.</summary>
    internal double PetBusyShown { get { return _petBusy == null ? 0.35 : _petBusy.Value * 0.6; } }

    /// <summary>Test seam: the rate the speed slider currently represents.</summary>
    internal double PetRateShown { get { return _petSpeed == null ? 1.0 : 0.2 + _petSpeed.Value * 2.8; } }

    /// <summary>Applies the persisted pet values to the two sliders.</summary>
    public void UpdatePetSettings(double rate, double busy)
    {
        _suppressPet = true;
        try
        {
            if (_petSpeed != null)
            {
                _petSpeed.Value = Math.Max(0, Math.Min(1, (rate - 0.2) / 2.8));
                _petSpeedText.Text = rate.ToString("F1") + "×";
            }
            if (_petBusy != null)
            {
                _petBusy.Value = Math.Max(0, Math.Min(1, busy / 0.6));
                _petBusyText.Text = ((int)Math.Round(busy * 100)) + "%";
            }
        }
        finally { _suppressPet = false; }
    }
    private bool _petOn = true;
    public void UpdatePet(bool on)
    {
        _petOn = on;
        if (_petSwitch == null) return;
        _petSwitch.Background = on ? WpfTheme.Accent : WpfTheme.SwitchOff;
        _petThumb.Background = on ? WpfTheme.PrimaryFill : WpfTheme.TextLight;
        _petThumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _petThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }
    public void Update(bool on)
    {
        if (_switch == null) return;
        _switch.Background = on ? WpfTheme.Accent : WpfTheme.SwitchOff;
        _thumb.Background = on ? WpfTheme.PrimaryFill : WpfTheme.TextLight;
        _thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _thumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }
    public void UpdateAppearance(bool dark)
    {
        if (_segLight == null) return;
        _segLight.Background = dark ? Brushes.Transparent : WpfTheme.CardSolid;
        _segLightText.Foreground = dark ? WpfTheme.TextSecond : WpfTheme.TextPrimary;
        _segDark.Background = dark ? WpfTheme.CardSolid : Brushes.Transparent;
        _segDarkText.Foreground = dark ? WpfTheme.TextPrimary : WpfTheme.TextSecond;
    }
}

internal class LogsView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    public LogsView()
    {
        var card = Ui.Card();
        var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420, Content = _list };
        Ui.ThinScroll(sc);
        var scHost = new Grid();
        scHost.Children.Add(sc);
        scHost.Children.Add(Ui.TopFade(18, WpfTheme.CardSolid));
        var st = new StackPanel();
        st.Children.Add(Ui.Tb("日志", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        st.Children.Add(new Border { Height = 8, Background = Brushes.Transparent });
        st.Children.Add(scHost);
        card.Child = st;
        Content = card;
    }
    public void Load()
    {
        try
        {
            if (!File.Exists(Program.LogFile)) return;
            var lines = File.ReadAllLines(Program.LogFile);
            int take = Math.Min(40, lines.Length);
            for (int i = lines.Length - take; i < lines.Length; i++)
            {
                if (String.IsNullOrEmpty(lines[i])) continue;
                var t = Ui.Tb(lines[i], 11, WpfTheme.TextMuted);
                t.FontFamily = WpfTheme.Mono;
                t.TextWrapping = TextWrapping.Wrap;
                _list.Children.Add(t);
            }
        }
        catch { }
    }
}

// ============================================================================
//  P1-1 session library: list / search / export / import / delete to recycle bin
// ============================================================================
internal class SessionsView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    private readonly List<SessionEntry> _all = new List<SessionEntry>();
    private TextBox _search;
    private TextBlock _hint;
    private TextBlock _count;

    /// <summary>Raised with the session id.</summary>
    public event EventHandler<string> ExportRequested;
    public event EventHandler<string> DeleteRequested;
    public event EventHandler ImportRequested;
    public event EventHandler RefreshRequested;

    public SessionsView()
    {
        var card = Ui.Card();
        var st = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb("会话库", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        _count = Ui.Tb("—", 11, WpfTheme.TextMuted);
        lb.Children.Add(_count);
        head.Children.Add(lb);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var imp = Buttons.Glass("导入 zip", 84, 30);
        imp.Margin = new Thickness(0, 0, 8, 0);
        imp.Click += delegate { if (ImportRequested != null) ImportRequested(this, EventArgs.Empty); };
        var rf = Buttons.Glass("刷新", 68, 30);
        rf.Click += delegate { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); };
        btns.Children.Add(imp);
        btns.Children.Add(rf);
        Grid.SetColumn(btns, 1);
        head.Children.Add(btns);
        st.Children.Add(head);

        st.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });

        // search box with a placeholder overlay
        var searchHost = new Grid();
        _search = new TextBox
        {
            Height = 32,
            FontSize = 13,
            FontFamily = WpfTheme.Ui,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = WpfTheme.SwitchOff,
            Foreground = WpfTheme.TextPrimary,
            CaretBrush = WpfTheme.TextPrimary,
            SelectionBrush = WpfTheme.Accent,
            BorderBrush = WpfTheme.BtnBorder,
            BorderThickness = new Thickness(1)
        };
        _search.TextChanged += delegate { Rebuild(); };
        searchHost.Children.Add(_search);
        _hint = Ui.Tb("搜索标题或会话 id…", 13, WpfTheme.TextLight);
        _hint.Margin = new Thickness(12, 0, 0, 0);
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.IsHitTestVisible = false;
        searchHost.Children.Add(_hint);
        st.Children.Add(searchHost);

        st.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });

        var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420, Content = _list };
        Ui.ThinScroll(sc);
        var scHost = new Grid();
        scHost.Children.Add(sc);
        scHost.Children.Add(Ui.TopFade(18, WpfTheme.CardSolid));
        st.Children.Add(scHost);

        card.Child = st;
        Content = card;
    }

    public void SetEntries(List<SessionEntry> entries)
    {
        _all.Clear();
        if (entries != null) _all.AddRange(entries);
        Rebuild();
    }

    /// <summary>Test/preview hook: types a search term (used by the smoke probe).</summary>
    public void PreviewSearch(string q)
    {
        if (_search != null) _search.Text = q ?? "";
    }

    /// <summary>Test/preview hook: the "shown / total" line.</summary>
    public string CountText { get { return _count == null ? "" : _count.Text; } }

    private void Rebuild()
    {
        _list.Children.Clear();
        string q = _search == null ? "" : _search.Text.Trim();
        if (_hint != null) _hint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        int shown = 0, onDisk = 0;
        foreach (SessionEntry e in _all)
        {
            if (q.Length > 0)
            {
                bool hit = (e.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (e.Id ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
            }
            if (e.OnDisk) onDisk++;
            _list.Children.Add(Row(e));
            _list.Children.Add(new Border { Height = 1, Background = WpfTheme.Border, Margin = new Thickness(0, 4, 0, 4) });
            shown++;
        }
        if (shown == 0)
        {
            var empty = Ui.Tb(q.Length > 0 ? "没有匹配的会话" : "还没有会话记录", 12, WpfTheme.TextLight);
            empty.Margin = new Thickness(0, 8, 0, 8);
            _list.Children.Add(empty);
        }
        if (_count != null)
            _count.Text = shown + " / " + _all.Count + " 个会话 · " + onDisk + " 个有磁盘文件";
    }

    private FrameworkElement Row(SessionEntry e)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Margin = new Thickness(0, 2, 8, 2) };
        string title = (e.Title ?? "").Length > 0 ? e.Title : "(无标题会话)";
        var t = Ui.Tb(title, 13, WpfTheme.TextPrimary, FontWeights.SemiBold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.ToolTip = title + "\n" + e.Id;
        info.Children.Add(t);
        info.Children.Add(Ui.Tb(Meta(e), 11, WpfTheme.TextMuted));
        g.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ex = Buttons.Glass("导出", 58, 28);
        ex.FontSize = 12;
        ex.IsEnabled = e.OnDisk;
        string id = e.Id;
        ex.Click += delegate { if (ExportRequested != null) ExportRequested(this, id); };
        var del = Buttons.Glass("删除", 58, 28);
        del.FontSize = 12;
        del.TextBrush = WpfTheme.Danger;
        del.FillHover = WpfTheme.AccentSoft;
        del.Click += delegate { if (DeleteRequested != null) DeleteRequested(this, id); };
        actions.Children.Add(ex);
        actions.Children.Add(del);
        Grid.SetColumn(actions, 1);
        g.Children.Add(actions);
        return g;
    }

    private static string Meta(SessionEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.When == DateTime.MinValue ? "—" : e.When.ToString("yyyy-MM-dd HH:mm"));
        sb.Append(" · ").Append(e.Turns).Append(" 轮 / ").Append(e.Steps).Append(" 步");
        sb.Append(" · ").Append(Usage.Tokens(e.Tokens)).Append(" tokens");
        if ((e.CostText ?? "").Length > 0) sb.Append(" · ").Append(e.CostText);
        sb.Append(" · ").Append(e.SizeText);
        if (!e.OnDisk) sb.Append(" · 仅缓存");
        if (e.PlanActive) sb.Append(" · 计划进行中");
        return sb.ToString();
    }
}

// ============================================================================
//  P1-2 backup history: list / restore / delete / retention cleanup
// ============================================================================
internal class BackupView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    private TextBlock _count;
    private TextBlock _hint;

    public event EventHandler BackupRequested;
    public event EventHandler RestoreRequested;            // pick a file…
    public event EventHandler<string> RestoreItemRequested;
    public event EventHandler<string> DeleteItemRequested;
    public event EventHandler CleanupRequested;
    public event EventHandler OpenFolderRequested;
    /// <summary>Raised with true when the user wants the API key inside backups.</summary>
    public event EventHandler<bool> CredentialsChanged;

    private Border _credSwitch, _credThumb;
    private bool _credOn;

    public BackupView()
    {
        var card = Ui.Card();
        var st = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb("备份历史", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        _count = Ui.Tb("—", 11, WpfTheme.TextMuted);
        lb.Children.Add(_count);
        head.Children.Add(lb);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var bNew = Buttons.Glass("立即备份", 84, 30);
        bNew.Margin = new Thickness(0, 0, 8, 0);
        bNew.Click += delegate { if (BackupRequested != null) BackupRequested(this, EventArgs.Empty); };
        var bRestore = Buttons.Glass("从文件恢复…", 100, 30);
        bRestore.Margin = new Thickness(0, 0, 8, 0);
        bRestore.Click += delegate { if (RestoreRequested != null) RestoreRequested(this, EventArgs.Empty); };
        var bClean = Buttons.Glass("清理旧备份", 90, 30);
        bClean.Click += delegate { if (CleanupRequested != null) CleanupRequested(this, EventArgs.Empty); };
        btns.Children.Add(bNew);
        btns.Children.Add(bRestore);
        btns.Children.Add(bClean);
        Grid.SetColumn(btns, 1);
        head.Children.Add(btns);
        st.Children.Add(head);

        // ---- include credentials (default OFF: the API key is sensitive) ----
        st.Children.Add(new Border { Height = 10, Background = Brushes.Transparent });
        var credRow = new Grid();
        credRow.ColumnDefinitions.Add(new ColumnDefinition());
        credRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var credLb = new StackPanel();
        credLb.Children.Add(Ui.Tb("备份包含 API 凭据", 12, WpfTheme.TextPrimary, FontWeights.SemiBold));
        credLb.Children.Add(Ui.Tb("默认关闭：包内不含 .credentials.yaml，避免 Key 随压缩包外流", 11, WpfTheme.TextMuted));
        credRow.Children.Add(credLb);
        _credSwitch = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = WpfTheme.BtnBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        _credThumb = new Border { CornerRadius = new CornerRadius(10), Background = WpfTheme.PrimaryFill, Width = 20, Height = 20 };
        _credSwitch.Child = _credThumb;
        _credSwitch.MouseLeftButtonUp += delegate
        {
            if (CredentialsChanged != null) CredentialsChanged(this, !_credOn);
        };
        Grid.SetColumn(_credSwitch, 1);
        credRow.Children.Add(_credSwitch);
        st.Children.Add(credRow);
        UpdateCredentials(false);

        st.Children.Add(new Border { Height = 8, Background = Brushes.Transparent });
        _hint = Ui.Tb("—", 11, WpfTheme.TextLight);
        _hint.Cursor = Cursors.Hand;
        _hint.MouseLeftButtonUp += delegate { if (OpenFolderRequested != null) OpenFolderRequested(this, EventArgs.Empty); };
        st.Children.Add(_hint);
        st.Children.Add(new Border { Height = 10, Background = Brushes.Transparent });

        var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 400, Content = _list };
        Ui.ThinScroll(sc);
        var scHost = new Grid();
        scHost.Children.Add(sc);
        scHost.Children.Add(Ui.TopFade(18, WpfTheme.CardSolid));
        st.Children.Add(scHost);

        card.Child = st;
        Content = card;
    }

    /// <summary>Applies the persisted "include credentials" preference.</summary>
    public void UpdateCredentials(bool on)
    {
        _credOn = on;
        if (_credSwitch == null) return;
        _credSwitch.Background = on ? WpfTheme.Accent : WpfTheme.SwitchOff;
        _credThumb.Background = on ? WpfTheme.PrimaryFill : WpfTheme.TextLight;
        _credThumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _credThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    public void SetItems(List<BackupItem> items, int keep)    {
        _list.Children.Clear();
        if (items == null) items = new List<BackupItem>();
        long total = 0;
        foreach (BackupItem it in items)
        {
            total += it.Bytes;
            _list.Children.Add(Row(it));
            _list.Children.Add(new Border { Height = 1, Background = WpfTheme.Border, Margin = new Thickness(0, 4, 0, 4) });
        }
        if (items.Count == 0)
        {
            var empty = Ui.Tb("还没有备份。点「立即备份」生成第一份。", 12, WpfTheme.TextLight);
            empty.Margin = new Thickness(0, 8, 0, 8);
            _list.Children.Add(empty);
        }
        _count.Text = items.Count + " 份备份 · 共 " + Backup.SizeText(total) + " · 保留最近 " + keep + " 份";
        _hint.Text = "存放位置：" + Backup.DefaultTargetDir + "（点击打开）";
    }

    private FrameworkElement Row(BackupItem it)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Margin = new Thickness(0, 2, 8, 2) };
        var t = Ui.Tb(it.Name, 13, WpfTheme.TextPrimary, FontWeights.SemiBold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.ToolTip = it.Path;
        info.Children.Add(t);
        string meta = it.Kind + " · " + (it.CreatedAt == DateTime.MinValue ? "—" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"))
            + " · " + (it.Files > 0 ? it.Files + " 文件 · " : "") + it.SizeText;
        if ((it.BuildId ?? "").Length > 0) meta += " · BuildId " + it.BuildId;
        info.Children.Add(Ui.Tb(meta, 11, WpfTheme.TextMuted));
        g.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var restore = Buttons.Glass("恢复", 58, 28);
        restore.FontSize = 12;
        string path = it.Path;
        restore.Click += delegate { if (RestoreItemRequested != null) RestoreItemRequested(this, path); };
        var del = Buttons.Glass("删除", 58, 28);
        del.FontSize = 12;
        del.TextBrush = WpfTheme.Danger;
        del.FillHover = WpfTheme.AccentSoft;
        del.Click += delegate { if (DeleteItemRequested != null) DeleteItemRequested(this, path); };
        actions.Children.Add(restore);
        actions.Children.Add(del);
        Grid.SetColumn(actions, 1);
        g.Children.Add(actions);
        return g;
    }
}

// ============================================================================
//  Budget + pricing editor (pure shell side: budget lives in ui-settings.txt,
//  prices in %LOCALAPPDATA%\DSH\pricing.json)
// ============================================================================
internal class PricingView : UserControl
{
    private TextBox _dayBudget, _monthBudget, _warnPct, _rate, _offStart, _offEnd;
    private Border _weekendSwitch, _weekendThumb;
    private bool _weekend = true;
    private bool _suppress;
    private TextBlock _dayLine, _monthLine;
    private Bar _dayBar, _monthBar;
    private readonly StackPanel _models = new StackPanel();
    private readonly List<TextBox[]> _modelBoxes = new List<TextBox[]>();
    private readonly List<ModelPrice> _order = new List<ModelPrice>();

    public event EventHandler SettingsChanged;
    public event EventHandler<Pricing> PricingSaved;
    public event EventHandler DefaultsRequested;

    private sealed class Bar
    {
        public Grid Root;
        public ColumnDefinition Fill;
        public ColumnDefinition Rest;
        public Border FillBorder;
    }

    public PricingView()
    {
        var st = new StackPanel();

        // ---- budget ----
        var cardA = Ui.Card();
        var a = new StackPanel();
        a.Children.Add(Ui.Tb("预算", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        a.Children.Add(Ui.Tb("0 = 不启用；到达告警阈值后弹宠物气泡并计入事件", 11, WpfTheme.TextMuted));
        a.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _dayBudget = NumBox(72);
        _monthBudget = NumBox(72);
        _warnPct = NumBox(52);
        row.Children.Add(Ui.Tb("每日预算", 12, WpfTheme.TextPrimary));
        row.Children.Add(_dayBudget);
        row.Children.Add(Lbl("元 / 天", 11, WpfTheme.TextMuted, 10));
        row.Children.Add(Lbl("每月预算", 12, WpfTheme.TextPrimary, 18));
        row.Children.Add(_monthBudget);
        row.Children.Add(Lbl("元 / 月", 11, WpfTheme.TextMuted, 10));
        row.Children.Add(Lbl("告警阈值", 12, WpfTheme.TextPrimary, 18));
        row.Children.Add(_warnPct);
        row.Children.Add(Lbl("%", 11, WpfTheme.TextMuted, 10));
        a.Children.Add(row);
        WireChange(_dayBudget); WireChange(_monthBudget); WireChange(_warnPct);

        a.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        _dayLine = Ui.Tb("今日 —", 12, WpfTheme.TextSecond);
        a.Children.Add(_dayLine);
        a.Children.Add(new Border { Height = 6, Background = Brushes.Transparent });
        _dayBar = MakeBar();
        a.Children.Add(_dayBar.Root);
        a.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        _monthLine = Ui.Tb("本月 —", 12, WpfTheme.TextSecond);
        a.Children.Add(_monthLine);
        a.Children.Add(new Border { Height = 6, Background = Brushes.Transparent });
        _monthBar = MakeBar();
        a.Children.Add(_monthBar.Root);
        cardA.Child = a;
        st.Children.Add(cardA);
        st.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });

        // ---- pricing ----
        var cardB = Ui.Card();
        var b = new StackPanel();
        b.Children.Add(Ui.Tb("定价（峰谷）", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        b.Children.Add(Ui.Tb("USD / 1M tokens 的低谷价；高峰 = 低谷价 × 峰值倍数", 11, WpfTheme.TextMuted));
        b.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _rate = NumBox(56);
        _offStart = TextNumBox(56);
        _offEnd = TextNumBox(56);
        row2.Children.Add(Ui.Tb("汇率 USD→CNY", 12, WpfTheme.TextPrimary));
        row2.Children.Add(_rate);
        row2.Children.Add(Lbl("低谷时段", 12, WpfTheme.TextPrimary, 18));
        row2.Children.Add(_offStart);
        row2.Children.Add(Lbl("→", 11, WpfTheme.TextMuted, 6));
        row2.Children.Add(_offEnd);
        row2.Children.Add(Lbl("周末全低谷", 12, WpfTheme.TextPrimary, 18));
        _weekendSwitch = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = WpfTheme.BtnBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        _weekendThumb = new Border { CornerRadius = new CornerRadius(10), Background = WpfTheme.PrimaryFill, Width = 20, Height = 20 };
        _weekendSwitch.Child = _weekendThumb;
        _weekendSwitch.MouseLeftButtonUp += delegate { SetWeekend(!_weekend, true); };
        row2.Children.Add(_weekendSwitch);
        b.Children.Add(row2);
        WireChange(_rate); WireChange(_offStart); WireChange(_offEnd);

        b.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        for (int i = 0; i < 5; i++) head.ColumnDefinitions.Add(new ColumnDefinition());
        string[] titles = { "模型", "输入", "输出", "缓存读", "缓存写", "峰值 ×" };
        for (int i = 0; i < titles.Length; i++)
        {
            var t = Ui.Tb(titles[i], 10.5, WpfTheme.TextLight);
            Grid.SetColumn(t, i);
            head.Children.Add(t);
        }
        b.Children.Add(head);
        b.Children.Add(new Border { Height = 6, Background = Brushes.Transparent });
        b.Children.Add(_models);

        b.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        var save = Buttons.Primary("保存定价", 96);
        save.Click += delegate { Save(); };
        var def = Buttons.Glass("恢复默认", 90, 30);
        def.Margin = new Thickness(8, 0, 0, 0);
        def.Click += delegate { if (DefaultsRequested != null) DefaultsRequested(this, EventArgs.Empty); };
        btnRow.Children.Add(save);
        btnRow.Children.Add(def);
        b.Children.Add(btnRow);
        cardB.Child = b;
        st.Children.Add(cardB);

        Content = st;
    }

    private static TextBlock Lbl(string text, double size, SolidColorBrush brush, double leftMargin)
    {
        var t = Ui.Tb(text, size, brush);
        t.Margin = new Thickness(leftMargin, 0, 0, 0);
        return t;
    }

    private static TextBox NumBox(double width)
    {
        var tb = TextNumBox(width);
        tb.Margin = new Thickness(8, 0, 0, 0);
        return tb;
    }

    private static TextBox TextNumBox(double width)
    {
        var tb = new TextBox
        {
            Width = width,
            Height = 28,
            FontSize = 12,
            FontFamily = WpfTheme.Ui,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = WpfTheme.SwitchOff,
            Foreground = WpfTheme.TextPrimary,
            CaretBrush = WpfTheme.TextPrimary,
            SelectionBrush = WpfTheme.Accent,
            BorderBrush = WpfTheme.BtnBorder,
            BorderThickness = new Thickness(1)
        };
        return tb;
    }

    private static Bar MakeBar()
    {
        var bar = new Bar();
        var g = new Grid { Height = 6 };
        bar.Fill = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        bar.Rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        g.ColumnDefinitions.Add(bar.Fill);
        g.ColumnDefinitions.Add(bar.Rest);
        var bg = new Border { Background = WpfTheme.SwitchOff, CornerRadius = new CornerRadius(3) };
        Grid.SetColumnSpan(bg, 2);
        g.Children.Add(bg);
        bar.FillBorder = new Border { Background = WpfTheme.Accent, CornerRadius = new CornerRadius(3) };
        g.Children.Add(bar.FillBorder);
        bar.Root = g;
        return bar;
    }

    // ---- values -----------------------------------------------------------

    public double DayBudget { get { return Read(_dayBudget); } }
    public double MonthBudget { get { return Read(_monthBudget); } }
    public double WarnPct
    {
        get
        {
            double v = Read(_warnPct);
            return v <= 0 ? 80 : v;
        }
    }

    private static double Read(TextBox tb)
    {
        double v;
        if (tb != null && double.TryParse(tb.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
        return 0;
    }

    private void WireChange(TextBox tb)
    {
        if (tb == null) return;
        tb.TextChanged += delegate
        {
            if (!_suppress && SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        };
    }

    /// <summary>Fills the form from the persisted pricing + budget values.</summary>
    public void Load(Pricing p, double dayBudget, double monthBudget, double warnPct)
    {
        _suppress = true;
        try
        {
            _dayBudget.Text = dayBudget > 0 ? dayBudget.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "0";
            _monthBudget.Text = monthBudget > 0 ? monthBudget.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "0";
            _warnPct.Text = ((int)Math.Round(warnPct)).ToString();
            if (p != null)
            {
                _rate.Text = p.UsdToCny.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                _offStart.Text = p.OffPeakStart;
                _offEnd.Text = p.OffPeakEnd;
                SetWeekend(p.WeekendAllOffPeak, false);
                BuildModels(p);
            }
        }
        finally { _suppress = false; }
    }

    private void BuildModels(Pricing p)
    {
        _models.Children.Clear();
        _modelBoxes.Clear();
        _order.Clear();
        foreach (ModelPrice m in p.Models)
        {
            _order.Add(m);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            for (int i = 0; i < 5; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
            var name = Ui.Tb(m.Id, 11.5, WpfTheme.TextPrimary);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = m.Id;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var boxes = new TextBox[5];
            string[] vals =
            {
                Fmt(m.In), Fmt(m.Out), Fmt(m.CacheRead), Fmt(m.CacheWrite), Fmt(m.PeakMultiplier)
            };
            for (int i = 0; i < 5; i++)
            {
                var tb = TextNumBox(74);
                tb.Text = vals[i];
                tb.FontSize = 11.5;
                tb.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(tb, i + 1);
                row.Children.Add(tb);
                boxes[i] = tb;
            }
            _modelBoxes.Add(boxes);
            _models.Children.Add(row);
        }
    }

    private static string Fmt(double v)
    {
        return v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void SetWeekend(bool on, bool notify)
    {
        _weekend = on;
        _weekendSwitch.Background = on ? WpfTheme.Accent : WpfTheme.SwitchOff;
        _weekendThumb.Background = on ? WpfTheme.PrimaryFill : WpfTheme.TextLight;
        _weekendThumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _weekendThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        if (notify && !_suppress && SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
    }

    /// <summary>Pushes spend vs budget into the two progress rows.</summary>
    public void SetUsage(Usage.BudgetStatus st, Pricing p)
    {
        if (st == null) return;
        _dayLine.Text = st.DayBudget > 0
            ? ("今日 " + Usage.Money(st.DayCost, p) + " / " + st.DayBudget.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
               + " 元（" + st.DayPercent + "%）")
            : ("今日 " + Usage.Money(st.DayCost, p) + "（未设预算）");
        _monthLine.Text = st.MonthBudget > 0
            ? ("本月 " + Usage.Money(st.MonthCost, p) + " / " + st.MonthBudget.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
               + " 元（" + st.MonthPercent + "%）")
            : ("本月 " + Usage.Money(st.MonthCost, p) + "（未设预算）");
        Paint(_dayBar, st.DayBudget > 0 ? st.DayPercent : 0, st.DayOver, st.DayWarn);
        Paint(_monthBar, st.MonthBudget > 0 ? st.MonthPercent : 0, st.MonthOver, st.MonthWarn);
    }

    private static void Paint(Bar bar, int percent, bool over, bool warn)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        bar.Fill.Width = new GridLength(percent, GridUnitType.Star);
        bar.Rest.Width = new GridLength(100 - percent, GridUnitType.Star);
        bar.FillBorder.Background = over ? WpfTheme.Danger : (warn ? WpfTheme.Warning : WpfTheme.Accent);
    }

    /// <summary>Reads the edited form back into a Pricing object (null on bad input).</summary>
    public Pricing Collect(out string error)
    {
        error = "";
        var p = new Pricing();
        p.UsdToCny = Read(_rate);
        if (p.UsdToCny <= 0) { error = "汇率必须大于 0"; return null; }
        p.OffPeakStart = _offStart.Text.Trim();
        p.OffPeakEnd = _offEnd.Text.Trim();
        p.WeekendAllOffPeak = _weekend;
        for (int i = 0; i < _order.Count; i++)
        {
            TextBox[] boxes = _modelBoxes[i];
            ModelPrice m = _order[i];
            var nm = new ModelPrice();
            nm.Id = m.Id;
            nm.In = Read(boxes[0]);
            nm.Out = Read(boxes[1]);
            nm.CacheRead = Read(boxes[2]);
            nm.CacheWrite = Read(boxes[3]);
            nm.PeakMultiplier = Read(boxes[4]);
            if (nm.PeakMultiplier <= 0) nm.PeakMultiplier = 2.0;
            p.Models.Add(nm);
        }
        p.DefaultModel = p.Models.Count > 0 ? p.Models[0].Id : "deepseek-v4-flash";
        return p;
    }

    private void Save()
    {
        string err;
        Pricing p = Collect(out err);
        if (p == null)
        {
            MessageBox.Show(err, "DSH 定价", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (PricingSaved != null) PricingSaved(this, p);
    }
}

// ============================================================================
//  Notification history (ADR-0009): unread dots + tray badge
// ============================================================================
internal class NotificationsView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    private TextBlock _count;

    public event EventHandler MarkReadRequested;
    public event EventHandler ClearRequested;
    /// <summary>Raised when the user clicks a notice (mark it read / open the page).</summary>
    public event Action<Notice> ItemActivated;

    public NotificationsView()
    {
        var card = Ui.Card();
        var st = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb("通知历史", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        _count = Ui.Tb("—", 11, WpfTheme.TextMuted);
        lb.Children.Add(_count);
        head.Children.Add(lb);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var read = Buttons.Glass("全部已读", 84, 30);
        read.Margin = new Thickness(0, 0, 8, 0);
        read.Click += delegate { if (MarkReadRequested != null) MarkReadRequested(this, EventArgs.Empty); };
        var clr = Buttons.Glass("清空", 68, 30);
        clr.Click += delegate { if (ClearRequested != null) ClearRequested(this, EventArgs.Empty); };
        btns.Children.Add(read);
        btns.Children.Add(clr);
        Grid.SetColumn(btns, 1);
        head.Children.Add(btns);
        st.Children.Add(head);

        st.Children.Add(new Border { Height = 10, Background = Brushes.Transparent });
        var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 430, Content = _list };
        Ui.ThinScroll(sc);
        var scHost = new Grid();
        scHost.Children.Add(sc);
        scHost.Children.Add(Ui.TopFade(18, WpfTheme.CardSolid));
        st.Children.Add(scHost);

        card.Child = st;
        Content = card;
    }

    public void SetItems(List<Notice> items)
    {
        _list.Children.Clear();
        if (items == null) items = new List<Notice>();
        int unread = 0;
        foreach (Notice n in items)
        {
            if (n.Unread) unread++;
            _list.Children.Add(Row(n));
            _list.Children.Add(new Border { Height = 1, Background = WpfTheme.Border, Margin = new Thickness(0, 4, 0, 4) });
        }
        if (items.Count == 0)
        {
            var empty = Ui.Tb("还没有通知。任务变化、预算告警与服务事件都会出现在这里。", 12, WpfTheme.TextLight);
            empty.Margin = new Thickness(0, 8, 0, 8);
            _list.Children.Add(empty);
        }
        _count.Text = items.Count + " 条通知 · " + unread + " 条未读";
    }

    private FrameworkElement Row(Notice n)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "点击标记已读" };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            CornerRadius = new CornerRadius(4),
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Background = n.Unread ? WpfTheme.Accent : WpfTheme.TextLight
        };
        row.Children.Add(dot);

        var txt = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        var t = Ui.Tb(n.Title, 13, n.Unread ? WpfTheme.TextPrimary : WpfTheme.TextSecond, n.Unread ? FontWeights.SemiBold : FontWeights.Normal);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        txt.Children.Add(t);
        if ((n.Sub ?? "").Length > 0)
        {
            var s = Ui.Tb(n.Sub, 11, WpfTheme.TextMuted);
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            txt.Children.Add(s);
        }
        Grid.SetColumn(txt, 1);
        row.Children.Add(txt);

        var when = Ui.Tb(n.Time == DateTime.MinValue ? "—" : n.Time.ToString("MM-dd HH:mm"), 11, WpfTheme.TextLight);
        Grid.SetColumn(when, 2);
        row.Children.Add(when);

        Notice clicked = n;
        row.MouseLeftButtonUp += delegate
        {
            Action<Notice> a = ItemActivated;
            if (a != null) a(clicked);
        };
        return row;
    }
}

// ============================================================================
//  In-shell approvals: pending list + 允许/拒绝 (Mux.cs holds the mux)
// ============================================================================
internal class ApprovalsView : UserControl
{
    private readonly StackPanel _list = new StackPanel();
    private TextBlock _status;
    private TextBlock _empty;
    private readonly Dictionary<string, ApprovalItem> _pending = new Dictionary<string, ApprovalItem>();

    /// <summary>Raised with the approvalId.</summary>
    public event EventHandler<string> AllowRequested;
    public event EventHandler<string> RejectRequested;

    public ApprovalsView()
    {
        var card = Ui.Card();
        var st = new StackPanel();

        var lb = new StackPanel();
        lb.Children.Add(Ui.Tb("审批", 13, WpfTheme.TextSecond, FontWeights.SemiBold));
        _status = Ui.Tb("事件通道：未连接", 11, WpfTheme.TextLight);
        lb.Children.Add(_status);
        st.Children.Add(lb);

        st.Children.Add(new Border { Height = 10, Background = Brushes.Transparent });
        _empty = Ui.Tb("当前没有等待审批的请求。", 12, WpfTheme.TextLight);
        st.Children.Add(_empty);
        st.Children.Add(_list);

        card.Child = st;
        Content = card;
    }

    public int PendingCount { get { return _pending.Count; } }

    public ApprovalItem Get(string approvalId)
    {
        ApprovalItem it;
        return (approvalId != null && _pending.TryGetValue(approvalId, out it)) ? it : null;
    }

    public void SetStatus(string s)
    {
        if (_status != null) _status.Text = "事件通道：" + (s ?? "未连接");
    }

    public void Add(ApprovalItem it)
    {
        if (it == null || _pending.ContainsKey(it.ApprovalId)) return;
        _pending[it.ApprovalId] = it;
        Rebuild();
    }

    public void Remove(string approvalId)
    {
        if (approvalId == null || !_pending.Remove(approvalId)) return;
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        foreach (ApprovalItem it in new List<ApprovalItem>(_pending.Values))
            _list.Children.Add(Row(it));
        _empty.Visibility = _pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement Row(ApprovalItem it)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var t = Ui.Tb("等待批准：" + (it.ToolName.Length > 0 ? it.ToolName : "工具调用"), 13, WpfTheme.TextPrimary, FontWeights.SemiBold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(t);
        string sub = (it.Reason ?? "").Length > 0 ? it.Reason : "后端请求一次工具调用授权";
        sub += " · " + it.SeenAt.ToString("HH:mm:ss");
        var s = Ui.Tb(sub, 11, WpfTheme.TextMuted);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(s);
        g.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string id = it.ApprovalId;
        var allow = Buttons.Glass("允许一次", 78, 28);
        allow.FontSize = 12;
        allow.Margin = new Thickness(0, 0, 8, 0);
        allow.Click += delegate { if (AllowRequested != null) AllowRequested(this, id); };
        var deny = Buttons.Glass("拒绝", 64, 28);
        deny.FontSize = 12;
        deny.TextBrush = WpfTheme.Danger;
        deny.FillHover = WpfTheme.AccentSoft;
        deny.Click += delegate { if (RejectRequested != null) RejectRequested(this, id); };
        actions.Children.Add(allow);
        actions.Children.Add(deny);
        Grid.SetColumn(actions, 1);
        g.Children.Add(actions);
        return g;
    }
}

internal class SidebarView : UserControl
{
    public event EventHandler<string> Navigated;
    private readonly Dictionary<string, RoundedButton> _nav = new Dictionary<string, RoundedButton>();
    private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
    private readonly StackPanel _host;
    public SidebarView()
    {
        var side = new Border { Background = WpfTheme.Sidebar, BorderBrush = WpfTheme.Border, BorderThickness = new Thickness(0, 0, 1, 0) };
        _host = new StackPanel { Margin = new Thickness(12, 22, 12, 12) };
        TextBlock tTitle = Ui.Tb("DSH", 19, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay);
        tTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _host.Children.Add(tTitle);
        TextBlock tVer = Ui.Tb("v0.1.1-rc.2", 11, WpfTheme.TextLight);
        tVer.HorizontalAlignment = HorizontalAlignment.Center;
        tVer.Margin = new Thickness(0, 2, 0, 16);
        _host.Children.Add(tVer);
        AddNav("总览", "overview");
        AddNav("服务", "service");
        AddNav("用量", "usage");
        AddNav("预算", "budget");
        AddNav("通知", "notifications");
        AddNav("会话", "sessions");
        AddNav("备份", "backups");
        AddNav("日志", "logs");
        AddNav("设置", "settings");
        side.Child = _host;
        Content = side;
        Select("overview");
    }
    private void AddNav(string text, string key)
    {
        _labels[key] = text;
        var b = new RoundedButton { Content = text, Height = 38, FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Left };
        b.FillNormal = Brushes.Transparent;
        b.FillHover = WpfTheme.NavHover;
        b.FillPressed = WpfTheme.NavActive;
        b.TextBrush = WpfTheme.TextSecond;
        b.BorderBrush = Brushes.Transparent;
        b.BorderThickness = new Thickness(0);
        b.Padding = new Thickness(16, 0, 0, 0);
        b.Corner = 8;
        string tag = key;
        b.Click += delegate { if (Navigated != null) Navigated(this, tag); };
        _nav[key] = b;
        _host.Children.Add(b);
    }

    /// <summary>Shows an unread counter next to a nav label ("通知  3").</summary>
    public void SetBadge(string key, int n)
    {
        RoundedButton b;
        if (!_nav.TryGetValue(key, out b)) return;
        string label;
        if (!_labels.TryGetValue(key, out label)) label = key;
        string text = n > 0 ? label + "  " + n : label;
        if (!String.Equals(b.Content as string, text, StringComparison.Ordinal)) b.Content = text;
    }
    public void Select(string key)
    {        foreach (var kv in _nav)
        {
            bool on = kv.Key == key;
            kv.Value.FillNormal = on ? WpfTheme.NavActive : Brushes.Transparent;
            kv.Value.TextBrush = on ? WpfTheme.TextPrimary : WpfTheme.TextSecond;
            kv.Value.FontWeight = on ? FontWeights.Medium : FontWeights.Regular;
            kv.Value.Refresh();
        }
    }
}

// ============================================================================
//  Main window
// ============================================================================
internal class DshWindow : Window
{
    private readonly ServiceHost _host = new ServiceHost();
    private DispatcherTimer _timer;
    private NativeTray _tray;
    private SidebarView _sidebar;
    private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();

    private HeroView _hero;
    private readonly List<ServiceInfoView> _svcViews = new List<ServiceInfoView>();
    private readonly List<BalanceView> _balViews = new List<BalanceView>();
    private readonly List<UsageView> _useViews = new List<UsageView>();
    private readonly List<EventsView> _evtViews = new List<EventsView>();
    private BalanceView _balPrimary;
    private SettingsView _settings;
    private LogsView _logs;
    private SessionsView _sessions;
    private BackupView _backups;
    private int _backupKeep = 5;
    private bool _backupCreds;                 // default OFF: never back up the API key
    private PricingView _pricingView;
    private NotificationsView _notices;
    private ApprovalsView _approvalsView;
    private readonly Dictionary<string, DispatcherTimer> _approvalGrace = new Dictionary<string, DispatcherTimer>();
    private readonly HashSet<string> _notified = new HashSet<string>();
    private double _budgetDay, _budgetMonth, _budgetWarn = 80;
    private string _budgetWarnedKey = "";

    private bool _exitRequested = false;
    private bool _alreadyRunning = false;
    private bool _readyEver = false;
    private bool _restartPending = false;
    private DateTime _restartRequestedAt;
    private bool _stoppedBalloonShown = false;
    private bool _refreshingBalance = false;
    private bool _balanceEverRefreshed = false;
    private DateTime _lastBalanceOk = DateTime.MinValue;
    private decimal _lastBalanceTotal = 0m;
    private string _lastBalanceCurrency = "CNY";
    private const int BalanceAutoMinutes = 10;
    private string _state = "starting";
    private bool _stoppedByUser = false;
    private int _externalPid = 0;

    // ---- usage / cost / pet (ADR-0007, ADR-0010) --------------------------
    private PetWindow _pet;
    private bool _petEnabled = true;
    private Pricing _pricing;
    private DateTime _usageReadAt = DateTime.MinValue;
    private DateTime _approvalWarnedAt = DateTime.MinValue;
    private int _lastSteps = -1;
    private DateTime _lastStepsAt = DateTime.Now;
    private double _ratePerHour;
    private double _petRate = 1.0;
    private double _petBusy = 0.35;
    private readonly Dictionary<string, Usage.SessionSignal> _signals = new Dictionary<string, Usage.SessionSignal>();
    private bool _turnBusy;                    // authoritative: a turn is running (mux)
    private bool _pollBusy;                    // fallback: projcache says something runs
    private DateTime _lastBusyAt = DateTime.MinValue;
    private bool _idleBurstDone = true;        // one "task finished" spout per busy run
    private DateTime _signalAt = DateTime.MinValue;

    public DshWindow()
    {
        Title = "DSH 控制中心";
        Width = 940;
        Height = 660;
        MinWidth = 880;
        MinHeight = 620;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;              // hardware rendering: crisp text
        Background = WpfTheme.WindowBg;          // opaque fallback when Mica is unavailable
        FontFamily = WpfTheme.Ui;
        // Windows 11 system backdrop (Mica): real composited blur behind our own
        // translucent layers; on Windows 10 / older builds we stay opaque.
        SourceInitialized += delegate
        {
            Win11Backdrop.Apply(this, false);
            _micaOk = Win11Backdrop.Applied;
            ApplyBackdrop();
        };
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ApplyTextRendering();
        // Live re-theme: WpfTheme.Apply() mutates the shared brushes in place,
        // so only window-level chrome has to be refreshed here.
        WpfTheme.Changed += delegate
        {
            ApplyTextRendering();
            Win11Backdrop.SetDarkMode(this, WpfTheme.IsDark);
            Ui.RefreshScrollSkin();
            Ui.RefreshTopFades();
            ApplyBackdrop();
        };
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildChrome();
        SetupTray();

        _host.ProcessExited += delegate { _host.WriteLog("[DSH] 服务进程已退出"); };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();

        _petEnabled = !Program.LoadUiSetting("pet", "1").Equals("0", StringComparison.OrdinalIgnoreCase);
        // One-time repair: builds before -47 saved the SLIDER POSITION as the
        // rate (1.0x -> 0.29) and the busy fraction as a 0..100 percent
        // (0.35 -> 83). Those files cannot be trusted, so reset once.
        if (!Program.LoadUiSetting("petUnits", "").Equals("2", StringComparison.Ordinal))
        {
            _petRate = 1.0;
            _petBusy = 0.35;
            Program.SaveUiSetting("petRate", "1.00");
            Program.SaveUiSetting("petBusy", "35");
            Program.SaveUiSetting("petUnits", "2");
        }
        else
        {
            _petRate = ParseDouble(Program.LoadUiSetting("petRate", "1"), 1.0);
            _petBusy = ParseDouble(Program.LoadUiSetting("petBusy", "35"), 35) / 100.0;
        }
        if (_petRate < 0.2) _petRate = 0.2;
        if (_petRate > 3.0) _petRate = 3.0;
        if (_petBusy < 0) _petBusy = 0;
        if (_petBusy > 0.6) _petBusy = 0.6;
        _backupKeep = (int)ParseDouble(Program.LoadUiSetting("backupKeep", "5"), 5);
        if (_backupKeep < 1) _backupKeep = 1;
        if (_backupKeep > 20) _backupKeep = 20;
        _backupCreds = Program.LoadUiSetting("backupCreds", "0").Equals("1", StringComparison.OrdinalIgnoreCase);
        // BuildChrome() pushed the FIELD DEFAULTS into the settings UI before
        // these values were read; re-push the persisted ones or every launch
        // silently shows (and would then save) the defaults.
        _settings.UpdatePet(_petEnabled);
        _settings.UpdatePetSettings(_petRate, _petBusy);
        _settings.UpdateBackupKeep(_backupKeep);
        if (_backups != null) _backups.UpdateCredentials(_backupCreds);
        _budgetDay = ParseDouble(Program.LoadUiSetting("budgetDay", "0"), 0);
        _budgetMonth = ParseDouble(Program.LoadUiSetting("budgetMonth", "0"), 0);
        _budgetWarn = ParseDouble(Program.LoadUiSetting("budgetWarn", "80"), 80);
        try { _pricing = Usage.LoadPricing(); } catch { _pricing = Usage.DefaultPricing(); }
        ApplyPet();
        Notifications.MarkLegacyApprovalRead();    // one-off: old approval notices had no refId
        LoadNotifications();

        if (Program.ServerRunning())
        {
            _alreadyRunning = true;
            _readyEver = true;
            _hero.RestartButton.IsEnabled = false;
            _externalPid = Program.FindPortOwner(Program.Port);
            PushEvent("检测到已有 DSH 服务",
                _externalPid > 0
                    ? ("3080 由 PID " + _externalPid + " 占用，可能是上次未正常退出遗留（可用「停止服务」清理）")
                    : "3080 已被其他进程占用",
                WpfTheme.Warning);
        }
        else
        {
            _host.Start();
        }
    }

    private bool _micaOk;

    // Mica is dark-mode only on purpose: a translucent light base layer over a
    // blurred desktop turns grey, which is what made the light theme look muddy.
    private void ApplyBackdrop()
    {
        bool dark = WpfTheme.IsDark;
        Win11Backdrop.SetBackdrop(this, dark && _micaOk);
        Background = (dark && _micaOk) ? Brushes.Transparent : WpfTheme.WindowBg;
    }

    // ---- desktop pet -------------------------------------------------------
    private void ApplyPet()
    {
        try
        {
            if (_petEnabled)
            {
                if (_pet == null)
                {
                    _pet = new PetWindow();
                    _pet.DoubleClicked += delegate { ShowConsole(); };
                    _pet.RightClicked += delegate { ShowTrayMenu(); };
                    _pet.Show();
                }
                _pet.SetState(_state);
                _pet.Scene.Rate = _petRate;
                _pet.SetTarget(_petBusy);
                // a re-created pet must not lose a reminder that really waits
                foreach (string id in _notified)
                {
                    ApprovalItem waiting = _approvalsView == null ? null : _approvalsView.Get(id);
                    if (waiting != null) { ShowApprovalBubble(waiting); break; }
                }
            }
            else if (_pet != null)
            {
                _pet.Close();
                _pet = null;
            }
        }
        catch { _pet = null; }
    }

    // ---- real usage / cost (ADR-0007) --------------------------------------
    private void RefreshUsageCost()
    {
        try
        {
            if (_pricing == null) _pricing = Usage.LoadPricing();
            List<SessionUsage> sessions = Usage.ReadSessions();
            Usage.ComputeCosts(_pricing, sessions);
            Lifetime life = Usage.LoadLifetime(_pricing, sessions);
            Usage.AppendSnapshot(_pricing, sessions);

            List<UsageTick> hist = Usage.ReadHistory();
            double h1, h3, h6;
            long t1, t3, t6;
            Usage.Window(hist, 1, out h1, out t1);
            Usage.Window(hist, 3, out h3, out t3);
            Usage.Window(hist, 6, out h6, out t6);
            double today = Usage.Today(hist);
            double month = Usage.Since(hist, Usage.MonthStart());
            _ratePerHour = h1;

            long inTok = 0, outTok = 0, crTok = 0;
            foreach (SessionUsage s in sessions)
            {
                inTok += s.UncachedInput; outTok += s.Output; crTok += s.CacheRead;
            }
            foreach (UsageView v in _useViews)
            {
                v.Set(Usage.Money(h1, _pricing), Usage.Money(h3, _pricing), Usage.Money(h6, _pricing));
                v.SetTokens(Usage.Tokens(inTok), Usage.Tokens(outTok), Usage.Tokens(crTok));
            }

            string eta = "—";
            if (_ratePerHour > 0.0001 && _lastBalanceTotal > 0m)
            {
                double usd = (double)_lastBalanceTotal / (_pricing.UsdToCny <= 0 ? 7.2 : _pricing.UsdToCny);
                double hours = usd / _ratePerHour;
                eta = hours >= 48 ? ((int)(hours / 24)) + " 天" : (hours >= 1 ? ((int)hours) + " 小时" : "< 1 小时");
            }
            foreach (BalanceView v in _balViews)
                v.SetCost(Usage.Money(today, _pricing), Usage.Money(h1, _pricing), Usage.Money(life.Cost, _pricing), life.Days, eta);

            EvaluateBudget(today, month);
        }
        catch { }
    }

    /// <summary>
    /// A pending approval only becomes the user's problem if it is still
    /// unanswered after <see cref="ApprovalGraceSeconds"/>: approvals answered
    /// by the backend / configured answerer within a few seconds are normal
    /// traffic, and shouting about them made the pet look like it was reporting
    /// approvals that do not exist.
    /// </summary>
    private void OnApprovalRequested(ApprovalItem it)
    {
        _approvalsView.Add(it);
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ApprovalGraceSeconds) };
        timer.Tick += delegate
        {
            timer.Stop();
            _approvalGrace.Remove(it.ApprovalId);
            if (_approvalsView.Get(it.ApprovalId) == null) return;   // already answered
            NotifyApproval(it);
        };
        _approvalGrace[it.ApprovalId] = timer;
        timer.Start();
    }

    private const double ApprovalGraceSeconds = 6;

    /// <summary>Shows the sticky bubble / notification for an approval that really waits.</summary>
    private void NotifyApproval(ApprovalItem it)
    {
        string title = "等待审批：" + (it.ToolName.Length > 0 ? it.ToolName : "工具调用");
        string sub = (it.Reason ?? "").Length > 0 ? it.Reason : "可在「总览 → 审批」直接允许或拒绝";
        PushEvent(title, sub, WpfTheme.Warning);
        Notifications.Add(title, sub, "warn", it.ApprovalId, it.SessionId);   // refId ties the notice to the approval
        _notified.Add(it.ApprovalId);
        LoadNotifications();
        ShowApprovalBubble(it);
    }

    private void OnApprovalResolved(string approvalId)
    {
        DispatcherTimer timer;
        if (_approvalGrace.TryGetValue(approvalId, out timer))
        {
            timer.Stop();
            _approvalGrace.Remove(approvalId);
        }
        _approvalsView.Remove(approvalId);
        _notified.Remove(approvalId);
        // Answering it (in the shell OR in the web UI) must also clear its own
        // notification - that is what makes the tray red dot disappear without
        // wiping unrelated unread alerts.
        if (Notifications.MarkReadByRef(approvalId) > 0) LoadNotifications();

        ApprovalItem next = null;
        foreach (string id in _notified)
        {
            next = _approvalsView.Get(id);
            if (next != null) break;
        }
        if (next != null)
        {
            ShowApprovalBubble(next);          // another notified one is still waiting
            return;
        }
        if (_pet != null)
        {
            _pet.HideBubble();
            _pet.ReleaseHold();
        }
    }

    private void ShowApprovalBubble(ApprovalItem it)
    {
        if (_pet == null || it == null) return;
        _pet.Hold(PetScene.BurstAmount);                       // stay at 80%
        _pet.ShowBubble("等待批准：" + (it.ToolName.Length > 0 ? it.ToolName : "工具调用")
            + "\n" + Short((it.Reason ?? "").Length > 0 ? it.Reason : "请在「总览 → 审批」允许或拒绝", 110), 0);
    }

    private static string Short(string s, int max)
    {
        if (String.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    /// <summary>Pushes spend vs budget to the 预算 page and alerts on crossings.</summary>
    private void EvaluateBudget(double today, double month)
    {
        Usage.BudgetStatus st = Usage.Evaluate(today, month, _budgetDay, _budgetMonth, _budgetWarn);
        if (_pricingView != null) _pricingView.SetUsage(st, _pricing);

        string key = "";
        if (st.DayOver) key = "day-over";
        else if (st.MonthOver) key = "month-over";
        else if (st.DayWarn) key = "day-warn";
        else if (st.MonthWarn) key = "month-warn";
        if (key.Length == 0) { _budgetWarnedKey = ""; return; }

        string stamp = DateTime.Now.ToString("yyyy-MM-dd") + "|" + key;
        if (_budgetWarnedKey == stamp) return;      // alert once per day per level
        _budgetWarnedKey = stamp;

        if (st.DayOver)
            Notify("每日预算已用尽", "今日 " + Usage.Money(today, _pricing) + " / 预算 " + _budgetDay.ToString("0.##") + " 元");
        else if (st.MonthOver)
            Notify("本月预算已用尽", "本月 " + Usage.Money(month, _pricing) + " / 预算 " + _budgetMonth.ToString("0.##") + " 元");
        else if (st.DayWarn)
            Notify("每日预算接近上限", "今日已用 " + st.DayPercent + "%（" + Usage.Money(today, _pricing) + "）");
        else
            Notify("本月预算接近上限", "本月已用 " + st.MonthPercent + "%（" + Usage.Money(month, _pricing) + "）");
    }

    /// <summary>Forces a usage/cost refresh (used by the offscreen render probe).</summary>
    public void PreviewRefreshUsage()
    {
        try { RefreshUsageCost(); } catch { }
    }

    /// <summary>Switches the visible page (used by the offscreen render probe).</summary>
    public void PreviewShowPage(string key)
    {
        try { ShowPage(key); } catch { }
    }

    /// <summary>
    /// Fallback signal poll (every 3s) used only while the mux is down: it keeps
    /// the pet's "working" state roughly right and reports plan/todo changes.
    /// The authoritative turn lifecycle comes from <see cref="Mux.TurnChanged"/>.
    /// </summary>
    private void PollSignals()
    {
        try
        {
            _signalAt = DateTime.Now;
            List<SessionUsage> sessions = Usage.ReadSessions();
            _pollBusy = Usage.AnyBusy(sessions);

            List<Usage.UsageChange> changes = Usage.Observe(_signals, sessions);
            foreach (Usage.UsageChange c in changes)
            {
                if (c.Burst && _pet != null) _pet.Burst(10);
                Notify(c.Title, c.Sub, true, c.SessionId);
            }

            // No mux => we cannot know when a turn really ended. Only report a
            // *guessed* completion after a long silence, and say so.
            if (!Mux.Connected)
            {
                if (_pollBusy)
                {
                    _lastBusyAt = DateTime.Now;
                    _idleBurstDone = false;
                }
                else if (!_idleBurstDone && _lastBusyAt != DateTime.MinValue
                         && (DateTime.Now - _lastBusyAt).TotalSeconds >= 45)
                {
                    _idleBurstDone = true;
                    Notify("任务可能已完成", "（推测）agent 已静默 45 秒，事件通道未连接");
                    if (_pet != null) _pet.Burst(10, 1.0);
                }
            }

            DetectApproval(sessions);
        }
        catch { }
    }

    /// <summary>
    /// Authoritative turn lifecycle from the event downlink. A turn/start makes
    /// the pet work immediately; a turn/end reports the real outcome - only
    /// "completed" is a completion, an interruption is not.
    /// </summary>
    private void OnTurnChanged(TurnEvent ev)
    {
        if (ev == null) return;
        if (ev.Type == "user/message")
        {
            // the user is back in that conversation: whatever we told them about
            // the previous turn has been seen
            if (Notifications.MarkReadBySession(ev.SessionId) > 0) LoadNotifications();
            return;
        }
        if (ev.Started)
        {
            _turnBusy = true;
            _lastBusyAt = DateTime.Now;
            _idleBurstDone = false;
            return;
        }

        _turnBusy = false;
        string who = "第 " + ev.Turn + " 轮";
        if (ev.Reason == "completed" || ev.Reason.Length == 0)
        {
            Notify("任务完成", who + " 已结束", true, ev.SessionId);
            if (_pet != null) _pet.Burst(10, 1.0);          // full spout for 10s
            return;
        }

        // Abnormal ending: still remind (bubble + notification), but no blast -
        // the spout must drop straight to the no-task level.
        if (_pet != null) _pet.CancelBurst();
        if (ev.Reason == "interrupted" || ev.Reason == "aborted")
            Notify("任务已中断", who + "被中止（" + ev.Reason + "）", false, ev.SessionId);
        else
            Notify("任务异常结束", who + "：" + ev.Reason, false, ev.SessionId);
    }

    /// <summary>Fallback heuristic for a pending decision when the mux is unavailable.</summary>
    private void DetectApproval(List<SessionUsage> sessions)
    {
        SessionUsage newest = null;
        foreach (SessionUsage s in sessions)
            if (newest == null || s.LastPromptAt > newest.LastPromptAt) newest = s;
        if (newest == null) return;

        if (newest.Steps != _lastSteps) { _lastSteps = newest.Steps; _lastStepsAt = DateTime.Now; }
        if (newest.PendingCalls > 0 && (DateTime.Now - _lastStepsAt).TotalSeconds > 60
            && (DateTime.Now - _approvalWarnedAt).TotalMinutes > 5)
        {
            _approvalWarnedAt = DateTime.Now;
            Notify("可能正在等待审批", "请回到网页确认（" + (newest.Title.Length > 0 ? newest.Title : "当前会话") + "）");
        }
    }

    private void Notify(string title, string sub)
    {
        Notify(title, sub, true, "");
    }

    private void Notify(string title, string sub, bool spout)
    {
        Notify(title, sub, spout, "");
    }

    /// <summary>
    /// Records a notification (event line + history + bubble). <paramref name="spout"/>
    /// = false keeps the pet quiet: no spout blast, so an abnormal ending falls
    /// straight back to the no-task trickle. <paramref name="sessionId"/> lets the
    /// notice be marked read when the user returns to that conversation.
    /// </summary>
    private void Notify(string title, string sub, bool spout, string sessionId)
    {
        PushEvent(title, sub, WpfTheme.Warning);
        Notifications.Add(title, sub, "warn", "", sessionId);
        LoadNotifications();
        if (_pet != null)
        {
            _pet.ShowBubble(title + "\n" + sub, 8);
            if (spout) _pet.Burst(8);         // notification => spout to full for a while
        }
    }

    /// <summary>Refreshes the notification page, the sidebar counter and the tray badge.</summary>
    private void LoadNotifications()
    {
        try
        {
            List<Notice> items = Notifications.Load();
            if (_notices != null) _notices.SetItems(items);
            int unread = 0;
            foreach (Notice n in items) if (n.Unread) unread++;
            if (_sidebar != null) _sidebar.SetBadge("notifications", unread);
            RefreshTrayTooltip();
        }
        catch { }
    }

    private static double ParseDouble(string s, double fallback)
    {
        double d;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
        return fallback;
    }

    // Text rendering policy. Small UI text uses Display formatting (hinted,
    // pixel-snapped: this is what stops 11-13px glyphs from looking ragged or
    // distorted) and ClearType. ClearType needs an OPAQUE surface, which is why
    // the cards and the sidebar are fully opaque now - over the translucent
    // Mica base layer WPF falls back to grayscale by itself, and headings opt
    // into Ideal + grayscale in Ui.Tb.
    private void ApplyTextRendering()
    {
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
    }

    private void BuildChrome()
    {
        var grid = new Grid { Background = WpfTheme.BaseLayer };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        _sidebar = new SidebarView();
        _sidebar.Navigated += delegate(object s, string key) { ShowPage(key); };
        grid.Children.Add(_sidebar);

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        main.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid();
        bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        var caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var minB = CaptionButton("—");
        minB.Click += delegate { WindowState = WindowState.Minimized; };
        var clsB = CaptionButton("✕");
        clsB.Click += delegate { Hide(); };
        caps.Children.Add(minB);
        caps.Children.Add(clsB);
        bar.Children.Add(caps);
        Grid.SetRow(bar, 0);
        main.Children.Add(bar);

        _hero = new HeroView();
        _settings = new SettingsView();
        _settings.Update(IsAutoStart());
        _settings.Toggled += delegate
        {
            bool on = !IsAutoStart();
            SetAutoStart(on);
            _settings.Update(on);
        };
        _settings.UpdateAppearance(WpfTheme.IsDark);
        _settings.UpdatePet(_petEnabled);
        _settings.PetToggled += delegate(object s, bool on)
        {
            _petEnabled = on;
            Program.SaveUiSetting("pet", on ? "1" : "0");
            _settings.UpdatePet(on);
            ApplyPet();
        };
        _settings.UpdatePetSettings(_petRate, _petBusy);
        _settings.PetSpeedChanged += delegate(object s, double v)
        {
            _petRate = v;
            Program.SaveUiSetting("petRate", v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            if (_pet != null) _pet.Scene.Rate = v;
        };
        _settings.PetBusyChanged += delegate(object s, double v)
        {
            _petBusy = v;
            Program.SaveUiSetting("petBusy", ((int)Math.Round(v * 100)).ToString());
            if (_pet != null) _pet.SetTarget(v);
        };
        _settings.AppearanceChanged += delegate(object s, bool dark)
        {
            WpfTheme.Apply(dark);
            Program.SaveDarkTheme(dark);
            _settings.UpdateAppearance(dark);
        };
        _settings.DiagnoseRequested += delegate { CreateDiagnostics(); };
        _settings.BackupRequested += delegate { CreateBackup(); };
        _settings.RestoreRequested += delegate { RestoreBackup(null); };
        _settings.UpdateBackupKeep(_backupKeep);
        _settings.BackupKeepChanged += delegate(object s, int keep)
        {
            _backupKeep = keep;
            Program.SaveUiSetting("backupKeep", keep.ToString());
            LoadBackups();
        };

        _logs = new LogsView();
        _logs.Load();

        // ---- in-shell approvals (mux watcher) ----
        _approvalsView = new ApprovalsView();
        _approvalsView.AllowRequested += delegate(object s, string id) { AnswerApproval(id, true); };
        _approvalsView.RejectRequested += delegate(object s, string id) { AnswerApproval(id, false); };
        Mux.Requested += delegate(ApprovalItem it)
        {
            Dispatcher.BeginInvoke(new Action(delegate { OnApprovalRequested(it); }));
        };
        Mux.Resolved += delegate(string approvalId)
        {
            Dispatcher.BeginInvoke(new Action(delegate { OnApprovalResolved(approvalId); }));
        };
        Mux.StatusChanged += delegate
        {
            Dispatcher.BeginInvoke(new Action(delegate { _approvalsView.SetStatus(Mux.Status); }));
        };
        Mux.TurnChanged += delegate(TurnEvent ev)
        {
            Dispatcher.BeginInvoke(new Action(delegate { OnTurnChanged(ev); }));
        };
        Mux.Start();

        // ---- notifications (ADR-0009) ----
        _notices = new NotificationsView();
        _notices.MarkReadRequested += delegate
        {
            Notifications.MarkAllRead();
            LoadNotifications();
        };
        _notices.ClearRequested += delegate
        {
            var ans = MessageBox.Show("清空全部通知历史？", "DSH 通知",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ans != MessageBoxResult.Yes) return;
            Notifications.Clear();
            LoadNotifications();
        };
        _notices.ItemActivated += delegate(Notice n)
        {
            if (n == null) return;
            Notifications.MarkRead(n.Id);
            LoadNotifications();
            if (n.SessionId.Length > 0) Program.OpenBrowser();   // jump to that conversation
        };

        // ---- budget / pricing (④) ----
        _pricingView = new PricingView();
        if (_pricing == null) { try { _pricing = Usage.LoadPricing(); } catch { _pricing = Usage.DefaultPricing(); } }
        _pricingView.Load(_pricing, _budgetDay, _budgetMonth, _budgetWarn);
        _pricingView.SettingsChanged += delegate
        {
            _budgetDay = _pricingView.DayBudget;
            _budgetMonth = _pricingView.MonthBudget;
            _budgetWarn = _pricingView.WarnPct;
            Program.SaveUiSetting("budgetDay", _budgetDay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Program.SaveUiSetting("budgetMonth", _budgetMonth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Program.SaveUiSetting("budgetWarn", ((int)Math.Round(_budgetWarn)).ToString());
            RefreshUsageCost();
        };
        _pricingView.PricingSaved += delegate(object s, Pricing p)
        {
            Usage.SavePricing(p);
            _pricing = p;
            _pricingView.Load(_pricing, _budgetDay, _budgetMonth, _budgetWarn);
            RefreshUsageCost();
            PushEvent("定价已保存", p.Models.Count + " 个模型 · 汇率 " + p.UsdToCny.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), WpfTheme.Success);
        };
        _pricingView.DefaultsRequested += delegate
        {
            var ans = MessageBox.Show("恢复默认峰谷定价？（当前定价会被覆盖）", "DSH 定价",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ans != MessageBoxResult.Yes) return;
            Pricing def = Usage.DefaultPricing();
            def.UsdToCny = _pricing != null && _pricing.UsdToCny > 0 ? _pricing.UsdToCny : def.UsdToCny;
            Usage.SavePricing(def);
            _pricing = def;
            _pricingView.Load(_pricing, _budgetDay, _budgetMonth, _budgetWarn);
            RefreshUsageCost();
            PushEvent("定价已恢复默认", "已写回 pricing.json", WpfTheme.Success);
        };

        // ---- backup history (P1-2) ----
        _backups = new BackupView();
        _backups.BackupRequested += delegate { CreateBackup(); };
        _backups.RestoreRequested += delegate { RestoreBackup(null); };
        _backups.RestoreItemRequested += delegate(object s, string path) { RestoreBackup(path); };
        _backups.DeleteItemRequested += delegate(object s, string path) { DeleteBackup(path); };
        _backups.CleanupRequested += delegate { CleanupBackups(); };
        _backups.OpenFolderRequested += delegate { Program.OpenFile(Backup.DefaultTargetDir); };
        _backups.UpdateCredentials(_backupCreds);
        _backups.CredentialsChanged += delegate(object s, bool on)
        {
            _backupCreds = on;
            Program.SaveUiSetting("backupCreds", on ? "1" : "0");
            _backups.UpdateCredentials(on);
        };

        // ---- session library (P1-1) ----
        _sessions = new SessionsView();
        _sessions.RefreshRequested += delegate { LoadSessions(); };
        _sessions.ExportRequested += delegate(object s, string id) { ExportSession(id); };
        _sessions.DeleteRequested += delegate(object s, string id) { DeleteSession(id); };
        _sessions.ImportRequested += delegate { ImportSessions(); };

        // ---- overview page ----
        var pageOverview = new UserControl();
        var ov = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        ov.Children.Add(Ui.Tb("DSH 控制中心", 24, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        ov.Children.Add(Ui.Tb("DeepSeek Harness Service", 13, WpfTheme.TextMuted));
        ov.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        ov.Children.Add(_hero);
        ov.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        ServiceInfoView svc1 = NewSvc();
        ov.Children.Add(svc1);
        ov.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        BalanceView bal1 = NewBal();
        UsageView use1 = NewUse();
        _balPrimary = bal1;
        var duo = new Grid();
        duo.ColumnDefinitions.Add(new ColumnDefinition());
        duo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        duo.ColumnDefinitions.Add(new ColumnDefinition());
        duo.Children.Add(bal1);
        Grid.SetColumn(use1, 2);
        duo.Children.Add(use1);
        ov.Children.Add(duo);
        ov.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        ov.Children.Add(_approvalsView);
        ov.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        EventsView evt1 = NewEvt();
        ov.Children.Add(evt1);
        pageOverview.Content = ov;
        _pages["overview"] = pageOverview;

        // ---- service page ----
        var pageService = new UserControl();
        var psv = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        psv.Children.Add(Ui.Tb("服务", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        psv.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        psv.Children.Add(NewSvc());
        pageService.Content = psv;
        _pages["service"] = pageService;

        // ---- usage page ----
        var pageUsage = new UserControl();
        var pug = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pug.Children.Add(Ui.Tb("用量", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pug.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        var duo2 = new Grid();
        duo2.ColumnDefinitions.Add(new ColumnDefinition());
        duo2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        duo2.ColumnDefinitions.Add(new ColumnDefinition());
        duo2.Children.Add(NewBal());
        UsageView use2 = NewUse();
        Grid.SetColumn(use2, 2);
        duo2.Children.Add(use2);
        pug.Children.Add(duo2);
        pug.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
        pug.Children.Add(NewEvt());
        pageUsage.Content = pug;
        _pages["usage"] = pageUsage;

        // ---- logs page ----
        var pageLogs = new UserControl();
        var pLg = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pLg.Children.Add(Ui.Tb("日志", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pLg.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pLg.Children.Add(_logs);
        pageLogs.Content = pLg;
        _pages["logs"] = pageLogs;

        // ---- sessions page ----
        var pageSessions = new UserControl();
        var pSs = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pSs.Children.Add(Ui.Tb("会话", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pSs.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pSs.Children.Add(_sessions);
        pageSessions.Content = pSs;
        _pages["sessions"] = pageSessions;

        // ---- backups page ----
        var pageBackups = new UserControl();
        var pBk = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pBk.Children.Add(Ui.Tb("备份", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pBk.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pBk.Children.Add(_backups);
        pageBackups.Content = pBk;
        _pages["backups"] = pageBackups;

        // ---- notifications page ----
        var pageNotices = new UserControl();
        var pNo = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pNo.Children.Add(Ui.Tb("通知", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pNo.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pNo.Children.Add(_notices);
        pageNotices.Content = pNo;
        _pages["notifications"] = pageNotices;

        // ---- budget page ----
        var pageBudget = new UserControl();
        var pBu = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pBu.Children.Add(Ui.Tb("预算与定价", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pBu.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pBu.Children.Add(_pricingView);
        pageBudget.Content = pBu;
        _pages["budget"] = pageBudget;

        // ---- settings page ----
        var pageSettings = new UserControl();
        var pSt = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pSt.Children.Add(Ui.Tb("设置", 22, WpfTheme.TitleText, FontWeights.SemiBold, WpfTheme.UiDisplay));
        pSt.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        pSt.Children.Add(_settings);
        pageSettings.Content = pSt;
        _pages["settings"] = pageSettings;

        var pageHost = new Grid();
        foreach (var kv in _pages) pageHost.Children.Add(kv.Value);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pageHost };
        Ui.ThinScroll(scroll);
        var body = new Grid { Margin = new Thickness(20, 4, 20, 18) };
        body.Children.Add(scroll);
        body.Children.Add(Ui.TopFade(26, WpfTheme.BaseLayer));
        Grid.SetRow(body, 1);
        main.Children.Add(body);
        Grid.SetColumn(main, 1);
        grid.Children.Add(main);
        Content = grid;

        _balPrimary.Refresh.Click += delegate { RefreshBalance(); };
        _hero.OpenButton.Click += delegate { Program.OpenBrowser(); };
        _hero.RestartButton.Click += delegate { RequestRestart(); };
        _hero.StopButton.Click += delegate { ToggleService(); };
        ShowPage("overview");
    }

    private ServiceInfoView NewSvc() { var v = new ServiceInfoView(); _svcViews.Add(v); return v; }
    private BalanceView NewBal() { var v = new BalanceView(); _balViews.Add(v); return v; }
    private UsageView NewUse() { var v = new UsageView(); _useViews.Add(v); return v; }
    private EventsView NewEvt() { var v = new EventsView(); _evtViews.Add(v); return v; }

    private RoundedButton CaptionButton(string text)
    {
        var b = Buttons.Glass(text, 30, 26);
        b.Corner = 6;
        b.FontSize = 11;
        b.FillHover = WpfTheme.AccentSoft;
        b.Margin = new Thickness(2, 0, 0, 0);
        return b;
    }

    private void ShowPage(string key)
    {
        foreach (var kv in _pages)
            kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
        _sidebar.Select(key);
        if (key == "sessions") LoadSessions();
        if (key == "backups") LoadBackups();
        if (key == "budget") RefreshUsageCost();
        if (key == "notifications") LoadNotifications();
    }

    // ---- business ----------------------------------------------------------
    private void PushEvent(string title, string sub, SolidColorBrush dot)
    {
        Diagnostics.Note(title, sub);          // ring buffer for the diagnostic pack
        foreach (var v in _evtViews) v.Add(title, sub, dot);
    }

    /// <summary>P0-4: builds logs\diagnostics-&lt;stamp&gt;.zip and offers to open the folder.</summary>
    private void CreateDiagnostics()
    {
        int pid = _host.IsRunning ? _host.Pid : _externalPid;
        string path = Diagnostics.Create(_state, pid, _host.StartedAt, WpfTheme.IsDark);
        if (path == null)
        {
            PushEvent("诊断包生成失败", Diagnostics.LastError, WpfTheme.Warning);
            MessageBox.Show("诊断包生成失败：\n" + Diagnostics.LastError, "DSH 诊断",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PushEvent("诊断包已生成", Path.GetFileName(path), WpfTheme.Success);
        MessageBoxResult r = MessageBox.Show(
            "诊断包已生成：\n" + path + "\n\n是否打开所在文件夹？", "DSH 诊断",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (r == MessageBoxResult.Yes) Program.OpenFile(Program.LogDir);
    }

    /// <summary>Answers one approval from the shell (allow needs an extra confirm).</summary>
    private void AnswerApproval(string approvalId, bool allow)
    {
        ApprovalItem it = _approvalsView.Get(approvalId);
        if (it == null) return;
        string err = Mux.Answer(it, allow);
        if (err.Length == 0)
        {
            PushEvent(allow ? "已在壳内允许一次" : "已在壳内拒绝", it.ToolName, WpfTheme.Success);
        }
        else
        {
            PushEvent("审批提交失败", err, WpfTheme.Danger);
            MessageBox.Show("提交失败：\n" + err + "\n\n请回网页处理这次审批。", "DSH 审批",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- P1-2 backup / restore --------------------------------------------
    private void LoadBackups()
    {
        try { _backups.SetItems(Backup.List(Backup.DefaultTargetDir), _backupKeep); }
        catch (Exception ex) { PushEvent("备份列表读取失败", ex.Message, WpfTheme.Warning); }
    }

    private void CreateBackup()
    {
        BackupResult r = Backup.Create(Backup.DefaultTargetDir, Backup.DshHome, "dsh-backup-", Backup.ShellDataDir, _backupCreds);
        if (!r.Ok)
        {
            PushEvent("备份失败", r.Error, WpfTheme.Danger);
            MessageBox.Show("备份失败：\n" + r.Error, "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int pruned = Backup.Prune(Backup.DefaultTargetDir, _backupKeep);
        LoadBackups();
        PushEvent("已创建备份", Path.GetFileName(r.Path) + " · " + r.Files + " 文件 · " + Backup.SizeText(r.Bytes), WpfTheme.Success);
        var ans = MessageBox.Show(
            "备份完成\n\n" + r.Path + "\n\n" + r.Files + " 个文件 · " + Backup.SizeText(r.Bytes)
            + "\n内容：会话记录（含全部对话）· 配置 · 附件 · 模型设置 · 壳自身的用量/成本/通知数据"
            + (_backupCreds ? "\n⚠ 含 API 凭据（.credentials.yaml）—— 请妥善保管这个压缩包" : "\n不含 API 凭据（.credentials.yaml）")
            + "\n（已排除可重建的缓存与插件依赖）"
            + (pruned > 0 ? "\n已自动清理 " + pruned + " 份最旧的备份（保留 " + _backupKeep + " 份）" : "")
            + "\n\n是否打开备份文件夹？",
            "DSH 备份", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (ans == MessageBoxResult.Yes) Program.OpenFile(Backup.DefaultTargetDir);
    }

    private void DeleteBackup(string path)
    {
        string name = Path.GetFileName(path);
        var ans = MessageBox.Show("把这份备份移入回收站？\n\n" + name, "DSH 备份",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;
        if (Sessions.DeletePathToRecycleBin(path))
        {
            LoadBackups();
            PushEvent("备份已删除", name, WpfTheme.Success);
        }
        else
        {
            MessageBox.Show("删除失败：\n" + Sessions.LastError, "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CleanupBackups()
    {
        List<BackupItem> all = Backup.List(Backup.DefaultTargetDir);
        int over = all.Count - _backupKeep;
        if (over <= 0)
        {
            MessageBox.Show("当前 " + all.Count + " 份备份，未超过保留上限（" + _backupKeep + " 份），无需清理。",
                "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        long bytes = 0;
        for (int i = _backupKeep; i < all.Count; i++) bytes += all[i].Bytes;
        var ans = MessageBox.Show(
            "将把最旧的 " + over + " 份备份移入回收站，释放约 " + Backup.SizeText(bytes) + "。\n\n保留最近 " + _backupKeep + " 份。\n\n继续？",
            "DSH · 清理旧备份", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;
        int n = Backup.Prune(Backup.DefaultTargetDir, _backupKeep);
        LoadBackups();
        PushEvent("已清理旧备份", n + " 份移入回收站", WpfTheme.Success);
    }

    /// <summary>Restores a specific archive, or asks for one when path is null.</summary>
    private void RestoreBackup(string path)
    {
        if (String.IsNullOrEmpty(path))
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "选择 DSH 备份包";
            dlg.Filter = "DSH 备份包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
            dlg.InitialDirectory = Directory.Exists(Backup.DefaultTargetDir) ? Backup.DefaultTargetDir : "";
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }

        BackupManifest mf = Backup.ReadManifest(path);
        if (mf == null)
        {
            MessageBox.Show("这不是 DSH 备份包：\n" + Backup.LastError, "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        bool running = _host.IsRunning || _alreadyRunning;
        var ans = MessageBox.Show(
            "即将从备份恢复\n\n"
            + "· 文件：" + Path.GetFileName(path) + "\n"
            + "· 创建于：" + mf.CreatedAt + "\n"
            + "· BuildId：" + mf.BuildId + "\n"
            + "· 内容：" + mf.Files + " 个文件 · " + Backup.SizeText(mf.Bytes)
            + (mf.ShellFiles > 0 ? "（含 " + mf.ShellFiles + " 个壳数据文件）" : "")
            + (mf.Credentials ? " · ⚠ 含 API 凭据" : " · 不含 API 凭据") + "\n\n"
            + "恢复规则：**只补缺失文件**（不覆盖现有），并先把当前数据自动备份一份。\n"
            + (running && !_alreadyRunning ? "· 服务会先停止，恢复完成后自动重启（网页需刷新一次）。\n" : "")
            + (_alreadyRunning ? "· 注意：3080 由其他实例运行，恢复前建议先停止它，否则部分文件可能无法写入。\n" : "")
            + "\n继续？",
            "DSH · 从备份恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;

        if (running && !_alreadyRunning)
        {
            _host.Stop();
            WaitPortFree(6);
        }
        BackupResult pre = Backup.Create(Backup.DefaultTargetDir, Backup.DshHome, "pre-restore-", Backup.ShellDataDir, _backupCreds);
        RestoreResult r = Backup.Restore(path, false);
        Backup.Prune(Backup.DefaultTargetDir, _backupKeep);
        if (running && !_alreadyRunning)
        {
            _stoppedByUser = false;
            _stoppedBalloonShown = false;
            _host.Start();
        }
        LoadSessions();
        LoadBackups();
        RefreshUsageCost();

        if (!r.Ok)
        {
            PushEvent("恢复失败", r.Error, WpfTheme.Danger);
            MessageBox.Show("恢复失败：\n" + r.Error, "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string msg = "恢复完成\n\n新增 " + r.Added + " 个文件 · 跳过（已存在）" + r.Skipped + " 个"
            + (r.Failed > 0 ? " · 失败 " + r.Failed + " 个" : "")
            + (pre.Ok ? "\n\n恢复前的现状已备份：\n" + pre.Path : "\n\n（恢复前的自动备份未成功：" + pre.Error + "）");
        PushEvent("已从备份恢复", r.Added + " 新增 / " + r.Skipped + " 跳过", WpfTheme.Success);
        MessageBox.Show(msg, "DSH 备份", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateServiceInfo(string pid, string build, string start)
    {
        foreach (var v in _svcViews) v.SetInfo(pid, build, start);
    }

    // ---- P1-1 session library ---------------------------------------------
    private void LoadSessions()
    {
        try
        {
            if (_pricing == null) _pricing = Usage.LoadPricing();
            _sessions.SetEntries(Sessions.List(_pricing));
        }
        catch (Exception ex)
        {
            PushEvent("会话列表读取失败", ex.Message, WpfTheme.Warning);
        }
    }

    private SessionEntry FindSession(string id)
    {
        foreach (SessionEntry e in Sessions.List(_pricing))
            if (String.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    private void ExportSession(string id)
    {
        SessionEntry e = FindSession(id);
        if (e == null || !e.OnDisk)
        {
            MessageBox.Show("该会话在磁盘上没有文件，无法导出。", "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog();
        dlg.Title = "导出会话";
        dlg.Filter = "会话压缩包 (*.zip)|*.zip";
        dlg.FileName = SafeName((e.Title ?? "").Length > 0 ? e.Title : e.Id) + ".zip";
        if (dlg.ShowDialog() != true) return;
        string path = Sessions.Export(e, dlg.FileName);
        if (path == null)
        {
            PushEvent("导出失败", Sessions.LastError, WpfTheme.Danger);
            MessageBox.Show("导出失败：\n" + Sessions.LastError, "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PushEvent("会话已导出", Path.GetFileName(path), WpfTheme.Success);
        MessageBox.Show("已导出：\n" + path + "\n\n用「导入 zip」可以原样恢复（保持 ~/.dsh/sessions 下的目录结构）。",
            "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportSessions()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog();
        dlg.Title = "导入会话";
        dlg.Filter = "会话压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
        if (dlg.ShowDialog() != true) return;
        int n = Sessions.Import(dlg.FileName);
        if (n < 0)
        {
            PushEvent("导入失败", Sessions.LastError, WpfTheme.Danger);
            MessageBox.Show("导入失败：\n" + Sessions.LastError, "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoadSessions();
        PushEvent("会话已导入", n + " 个文件", WpfTheme.Success);
        MessageBox.Show("已导入 " + n + " 个文件。\n\n网页端列表需要刷新一次（必要时重启服务）才能看到。",
            "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteSession(string id)
    {
        SessionEntry e = FindSession(id);
        if (e == null) return;
        bool running = _host.IsRunning || _alreadyRunning;

        string body = "删除会话「" + ((e.Title ?? "").Length > 0 ? e.Title : e.Id) + "」？\n\n"
            + "· 会话文件会移入回收站（可还原）\n"
            + "· 该会话的缓存记录会一并清除\n";
        if (running)
            body += "· 服务会先停止、删除后自动重启，网页端列表随之同步（当前页面需要刷新一次）\n";
        if (InUse(e))
            body += "\n注意：该会话的文件当前正被服务占用，很可能是**你正在进行的对话**，删除后无法继续。\n";

        var answer = MessageBox.Show(body, "DSH · 删除会话",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (running && !_alreadyRunning)
        {
            _host.Stop();
            WaitPortFree(6);
        }
        bool ok = Sessions.DeleteToRecycleBin(e);
        bool pruned = false;
        if (ok && !_alreadyRunning) pruned = Sessions.PruneCache(e.Id);

        if (running && !_alreadyRunning)
        {
            _stoppedByUser = false;
            _stoppedBalloonShown = false;
            _host.Start();
        }
        LoadSessions();
        RefreshUsageCost();

        if (ok)
        {
            string sub = pruned ? "已移入回收站并清理缓存记录" : "已移入回收站";
            PushEvent("会话已删除", sub, WpfTheme.Success);
        }
        else
        {
            PushEvent("删除失败", Sessions.LastError, WpfTheme.Danger);
            MessageBox.Show("删除失败：\n" + Sessions.LastError, "DSH 会话", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>True when the backend currently holds the session log open.</summary>
    private static bool InUse(SessionEntry e)
    {
        try
        {
            if (!e.OnDisk) return false;
            string f = Path.Combine(e.Folder, "session.jsonl.zstd");
            if (!File.Exists(f)) return false;
            using (File.Open(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return false;
        }
        catch { return true; }
    }

    private static void WaitPortFree(int seconds)
    {
        DateTime until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until)
        {
            if (!Program.ServerRunning()) return;
            Thread.Sleep(200);
        }
    }

    private static string SafeName(string s)
    {
        var sb = new StringBuilder();
        char[] bad = Path.GetInvalidFileNameChars();
        foreach (char c in s)
        {
            bool ok = true;
            foreach (char b in bad) if (c == b) { ok = false; break; }
            sb.Append(ok ? c : '_');
        }
        string r = sb.ToString().Trim();
        if (r.Length > 40) r = r.Substring(0, 40);
        return r.Length == 0 ? "session" : r;
    }

    private void UpdateBalance(string value, string detail, string meta)
    {
        foreach (var v in _balViews) v.Set(value, detail, meta);
    }

    private void UpdateBalanceMeta(string meta, SolidColorBrush color)
    {
        foreach (var v in _balViews) v.SetMeta(meta, color);
    }

    private void UpdateUsage(string h1, string h3, string h6)
    {
        foreach (var v in _useViews) v.Set(h1, h3, h6);
    }

    // ---- start / stop ------------------------------------------------------
    /// <summary>Stop our own backend, or start it again when stopped. A backend
    /// owned by another instance cannot be stopped from here (we have no PID
    /// for it), so that case explains itself instead of killing something.</summary>
    private void ToggleService()
    {
        bool running = _host.IsRunning || _alreadyRunning;
        if (!running)
        {
            _stoppedByUser = false;
            _stoppedBalloonShown = false;
            _host.Start();
            PushEvent("正在启动服务", "已请求启动 DSH 后端服务", WpfTheme.Accent);
            return;
        }
        if (_alreadyRunning)
        {
            // The backend belongs to another instance (no PID of our own), so
            // resolve the port owner and confirm before ending it: this may be
            // the DSH web instance the user is currently talking to.
            int pid = _externalPid > 0 ? _externalPid : Program.FindPortOwner(Program.Port);
            string who = Program.DescribeProcess(pid);
            var answer = System.Windows.MessageBox.Show(this,
                "3080 端口上的 DSH 服务由另一个实例运行：\n\n" + who +
                "\n\n停止它会中断该实例的后端服务（含它正在进行的会话与网页界面）。\n确定要停止吗？",
                "DSH · 停止其他实例的服务",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            bool ok = Program.KillProcessTree(pid);
            if (ok)
            {
                _alreadyRunning = false;
                _readyEver = false;
                _stoppedByUser = true;
                _externalPid = 0;
                PushEvent("已停止外部实例", "已结束占用 3080 的进程（" + who.Replace("\n", " ") + "）", WpfTheme.Warning);
            }
            else
            {
                PushEvent("停止外部实例失败", "需要管理员权限，或该进程已退出", WpfTheme.Danger);
            }
            return;
        }
        _stoppedByUser = true;
        _host.Stop();
        PushEvent("服务已停止", "已手动停止 DSH 后端服务（可点「启动服务」重新启动）", WpfTheme.Warning);
    }

    private void RequestRestart()
    {
        if (_restartPending) return;
        _restartPending = true;
        _restartRequestedAt = DateTime.Now;
        _hero.RestartButton.IsEnabled = false;
        _hero.RestartButton.Content = "正在重启…";
        _host.Stop();
        _hero.SetStatus("starting", "正在重启…");
    }

    private void RefreshBalance()
    {
        if (_refreshingBalance) return;
        string key = Program.ReadDeepSeekApiKey();
        if (key == null)
        {
            UpdateBalanceMeta("未配置 API Key（~/.dsh/.credentials.yaml）", WpfTheme.TextLight);
            return;
        }
        _refreshingBalance = true;
        _balPrimary.Refresh.IsEnabled = false;
        UpdateBalanceMeta("刷新中…", WpfTheme.Accent);
        ThreadPool.QueueUserWorkItem(delegate
        {
            string err = null;
            List<Program.BalanceInfo> list = null;
            try { list = Program.FetchDeepSeekBalance(key); }
            catch (Exception ex) { err = ex.Message; }
            Dispatcher.BeginInvoke(new Action(delegate { ApplyBalance(list, err); }));
        });
    }

    private void ApplyBalance(List<Program.BalanceInfo> list, string err)
    {
        _refreshingBalance = false;
        _balPrimary.Refresh.IsEnabled = true;
        if (err == null && list != null && list.Count > 0)
        {
            Program.BalanceInfo main = list[0];
            foreach (Program.BalanceInfo b in list) if (b.Currency == "CNY") { main = b; break; }
            _lastBalanceTotal = main.Total;
            _lastBalanceCurrency = main.Currency;
            Program.AppendBalanceSnapshot(DateTime.Now, main.Total);
            _lastBalanceOk = DateTime.Now;
            UpdateBalance(Program.FormatAmount(main.Total, main.Currency),
                "可用 " + Program.FormatAmount(main.Total, main.Currency)
                + " · 赠送 " + Program.FormatAmount(main.Granted, main.Currency)
                + " · 充值 " + Program.FormatAmount(main.Topped, main.Currency),
                DateTime.Now.ToString("HH:mm:ss") + " 更新 · 每 " + BalanceAutoMinutes + " 分钟自动刷新");
            PushEvent("余额已更新", "DeepSeek 余额已更新为 " + Program.FormatAmount(main.Total, main.Currency), WpfTheme.Accent);
            PushEvent("快照已写入", "用量快照已成功写入本地数据库", WpfTheme.Success);
            RefreshUsageCost();
            RefreshTrayTooltip();
            return;
        }
        if (err != null && err.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0)
            UpdateBalanceMeta("更新失败 · API Key 无效（401）", WpfTheme.Warning);
        else if (err != null && err.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0)
            UpdateBalanceMeta("更新失败 · 请求频繁（429）", WpfTheme.Warning);
        else if (err != null)
            UpdateBalanceMeta("更新失败 · 无法连接余额接口", WpfTheme.Warning);
        else
            UpdateBalanceMeta("查询成功但无余额数据", WpfTheme.TextLight);
    }

    // ---- tray --------------------------------------------------------------
    private void SetupTray()
    {
        _tray = new NativeTray(Program.IconFile, "DSH · 正在启动");
        _tray.LeftClick += delegate { ShowConsole(); };
        _tray.DoubleClick += delegate { ShowConsole(); };
        _tray.RightClick += delegate { ShowTrayMenu(); };
    }

    private static TrayMenuItem Item(string text, Action act)
    {
        var m = new TrayMenuItem(text);
        m.Act = act;
        return m;
    }

    private void ShowTrayMenu()
    {
        try
        {
            bool running = _host.IsRunning || _alreadyRunning;
            var items = new List<TrayMenuItem>();
            items.Add(Item("打开 DSH ↗", delegate { Program.OpenBrowser(); }));
            items.Add(Item("打开控制台", delegate { ShowConsole(); }));
            items.Add(TrayMenuItem.Sep());
            items.Add(Item("刷新余额", delegate { RefreshBalance(); }));
            items.Add(Item("重新启动服务", delegate { RequestRestart(); }));
            // start/stop mirrors the hero button; auto-start lives in Settings only
            items.Add(Item(running ? "停止服务" : "启动服务", delegate { ToggleService(); }));
            items.Add(TrayMenuItem.Sep());
            items.Add(Item("日志", delegate { ShowPage("logs"); ShowConsole(); }));
            items.Add(Item("通知", delegate { ShowPage("notifications"); ShowConsole(); }));
            items.Add(Item("预算与定价", delegate { ShowPage("budget"); ShowConsole(); }));
            items.Add(Item("会话库", delegate { ShowPage("sessions"); ShowConsole(); }));
            items.Add(Item("备份历史", delegate { ShowPage("backups"); ShowConsole(); }));
            items.Add(Item("生成诊断包", delegate { CreateDiagnostics(); }));
            items.Add(Item("设置", delegate { ShowPage("settings"); ShowConsole(); }));
            items.Add(TrayMenuItem.Sep());
            items.Add(Item("退出", delegate { ExitApp(); }));

            // Fresh instance per open: a WPF Window cannot be re-Shown once closed.
            var pop = new TrayMenuPopup();
            pop.BuildAndShow(TrayWin32.CursorScreen(), items);
        }
        catch { }
    }

    private void ShowConsole()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private const string RunKeyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private bool IsAutoStart()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyName))
                return k != null && k.GetValue("DSH") != null;
        }
        catch { return false; }
    }
    private void SetAutoStart(bool on)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyName))
            {
                if (k == null) return;
                if (on) k.SetValue("DSH", "\"" + Application.ResourceAssembly.Location + "\"");
                else k.DeleteValue("DSH", false);
            }
        }
        catch { }
    }

    private void ExitApp()
    {
        _exitRequested = true;
        if (_pet != null) { try { _pet.Close(); } catch { } _pet = null; }
        if (_tray != null) { _tray.Dispose(); _tray = null; }
        _host.Stop();
        WpfUI.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // State -> tray icon file. Falls back to the app icon when a state icon is
    // missing, and keeps tooltip + icon in sync with the current _state.
    private void RefreshTrayTooltip()
    {
        if (_tray == null) return;
        string tip = "DSH · 已停止";
        string state = "offline";
        if (_state == "running")
        {
            state = "running";
            tip = "DSH · 正在运行";
            if (_lastBalanceOk != DateTime.MinValue && _lastBalanceTotal > 0m)
                tip += " · " + Program.FormatAmount(_lastBalanceTotal, _lastBalanceCurrency);
        }
        else if (_state == "starting") { state = "starting"; tip = "DSH · 正在启动"; }
        else if (_state == "error") { state = "error"; tip = "DSH · 服务异常"; }
        string path = Path.Combine(Program.RootDir, "tray_" + state + ".ico");
        if (!File.Exists(path)) path = Program.IconFile;
        int unread = 0;
        try { unread = Notifications.Unread(); } catch { }
        try
        {
            if (unread > 0)
            {
                IntPtr h = TrayBadge.Get(path, true);
                if (h != IntPtr.Zero) _tray.UpdateIconHandle(h);
                else _tray.UpdateIconFromFile(path);
                tip += " · " + unread + " 条未读";
            }
            else
            {
                _tray.UpdateIconFromFile(path);
            }
        }
        catch { }
        try { _tray.UpdateTip(tip); } catch { }
    }

    private static string FormatUptime(TimeSpan t)
    {
        if (t.TotalDays >= 1) return ((int)t.TotalDays) + " 天 " + t.Hours + " 小时";
        if (t.TotalHours >= 1) return ((int)t.TotalHours) + " 小时 " + t.Minutes + " 分";
        if (t.TotalMinutes >= 1) return ((int)t.TotalMinutes) + " 分 " + t.Seconds + " 秒";
        return t.Seconds + " 秒";
    }

    // ---- tick --------------------------------------------------------------
    private void OnTick(object sender, EventArgs e)
    {
        if (Program.ConsumeShowRequest()) ShowConsole();
        if (!_balanceEverRefreshed)
        {
            _balanceEverRefreshed = true;
            RefreshBalance();
        }
        else if (!_refreshingBalance && (DateTime.Now - _lastBalanceOk).TotalMinutes >= BalanceAutoMinutes)
        {
            RefreshBalance();
        }

        // real usage/cost every minute; live signals every 3s
        if ((DateTime.Now - _usageReadAt).TotalSeconds >= 60) { _usageReadAt = DateTime.Now; RefreshUsageCost(); }
        if ((DateTime.Now - _signalAt).TotalSeconds >= 3) PollSignals();
        if (_pet != null)
        {
            _pet.SetState(_state);
            // small spout while a task runs (user-set size), tiny when idle.
            // With the mux connected the turn lifecycle decides; otherwise we
            // fall back to the projcache guess.
            bool busy = Mux.Connected ? _turnBusy : _pollBusy;
            _pet.SetTarget(busy ? _petBusy : Math.Min(0.12, _petBusy));
        }

        bool running = _host.IsRunning || _alreadyRunning;
        bool ready = Program.ServerRunning();

        if (_restartPending && !running && (DateTime.Now - _restartRequestedAt).TotalSeconds >= 1.5)
        {
            _restartPending = false;
            _hero.RestartButton.Content = "↻ 重新启动";
            _hero.RestartButton.IsEnabled = true;
            _host.Start();
        }

        if (running && ready && !_readyEver)
        {
            _readyEver = true;
            PushEvent(_alreadyRunning ? "检测到运行实例" : "服务启动成功",
                _alreadyRunning ? "已连接现有 DSH 实例（端口 3080）" : "DSH 后端服务已正常启动并监听端口 3080", WpfTheme.Success);
        }
        if (!running && _readyEver && !_restartPending && !_stoppedBalloonShown)
        {
            _stoppedBalloonShown = true;
            PushEvent("服务已停止", "DSH 后端服务未正常响应", WpfTheme.Danger);
            try { _tray.ShowBalloon("DSH", "DSH 服务已停止。\n可通过托盘菜单重新启动。", true); } catch { }
        }
        if (running) { _stoppedBalloonShown = false; _stoppedByUser = false; }

        // hero start/stop button mirrors the service state; an external instance
        // can also be stopped now (with an explicit confirmation)
        _hero.StopButton.Content = running ? "停止服务" : "启动服务";
        _hero.StopButton.IsEnabled = true;
        _hero.StopButton.ToolTip = (running && _alreadyRunning) ? "停止其他实例的服务（会先确认）" : null;

        if (running)
        {
            UpdateServiceInfo(_alreadyRunning ? "外部实例" : _host.Pid.ToString(), Program.BuildId,
                _alreadyRunning ? "由其他实例运行" : (_host.StartedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"));
            if (ready)
            {
                _hero.SetStatus("running", _alreadyRunning
                    ? ("前端已就绪 · 由其他实例运行" + (_externalPid > 0 ? "（PID " + _externalPid + "）" : ""))
                    : ("前端已就绪 · 已运行 " + FormatUptime(DateTime.Now - (_host.StartedAt ?? DateTime.Now))));
            }
            else
            {
                _hero.SetStatus("starting", "正在等待 3080 就绪…");
            }
            _state = "running";
        }
        else
        {
            UpdateServiceInfo("-", Program.BuildId, "-");
            if (_stoppedByUser)
            {
                _hero.SetStatus("offline", "服务已手动停止");
                _state = "offline";
            }
            else if (_readyEver && !_restartPending)
            {
                _hero.SetStatus("error", "服务未正常响应");
                _state = "error";
            }
            else if (_restartPending)
            {
                _hero.SetStatus("starting", "正在重启…");
                _state = "starting";
            }
            else
            {
                _hero.SetStatus("offline", "服务未启动");
                _state = "offline";
            }
        }
        RefreshTrayTooltip();
    }
}

internal static class WpfUI
{
    private static System.Windows.Application _app;
    public static void RunMain()
    {
        _app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try { File.AppendAllText(Program.LogFile, "[UI 异常] " + e.Exception + Environment.NewLine); } catch { }
            e.Handled = false;
        };
        try
        {
            WpfTheme.Apply(Program.LoadDarkTheme());   // saved preference (default dark)
            var win = new DshWindow();
            win.Show();
            _app.Run();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Program.LogFile, "[UI 启动失败] " + ex + Environment.NewLine); } catch { }
        }
    }
    public static void Shutdown()
    {
        try { if (_app != null) _app.Shutdown(); } catch { }
    }
}
