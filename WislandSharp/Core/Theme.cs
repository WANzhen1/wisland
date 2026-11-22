using SkiaSharp;
using Microsoft.Win32;

namespace WislandSharp.Core;

public sealed class Palette
{
    public bool Dark { get; init; }
    public SKColor Text { get; init; }
    public float Text2 { get; init; }
    public float Text3 { get; init; }
    public SKColor Tint { get; init; }
    public SKColor? Tint2 { get; init; }
    public SKColor Card { get; init; }
    public SKColor Hover { get; init; }
    public SKColor Pill { get; init; }
    public SKColor? PillFg { get; init; }
    public SKColor Track { get; init; }
    public SKColor Fill { get; init; }
    public SKColor Accent { get; init; }
    public SKColor Up { get; init; }
    public SKColor Down { get; init; }
    public SKColor RimTop { get; init; }
    public SKColor RimBottom { get; init; }
    public SKColor Outline { get; init; }
}

public sealed class ThemeEngine
{
    public string Mode { get; private set; } = "auto";
    public Palette Palette { get; private set; }
    public bool IsDark => Palette.Dark;
    public string ResolvedName { get; private set; } = "dark";

    private float _luminance = 0.5f;
    private long _flipSince;

    public ThemeEngine(string mode)
    {
        Palette = DarkPalette;
        SetMode(mode);
        FallbackToSystemTheme();
    }

    /// <summary>没有实时抓屏时，auto 主题退化为跟随 Windows 应用浅色 / 深色设置。</summary>
    public void FallbackToSystemTheme()
    {
        if (Mode != "auto") return;
        bool light = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch { }
        Apply(light ? LightPalette : DarkPalette, light ? "light" : "dark");
    }

    public static readonly Palette DarkPalette = new()
    {
        Dark = true,
        Text = new SKColor(255, 255, 255),
        Text2 = 0.66f,
        Text3 = 0.45f,
        Tint = new SKColor(28, 28, 34, 120),
        Card = new SKColor(255, 255, 255, 26),
        Hover = new SKColor(255, 255, 255, 30),
        Pill = new SKColor(255, 255, 255, 52),
        Track = new SKColor(255, 255, 255, 34),
        Fill = new SKColor(255, 255, 255, 235),
        Accent = new SKColor(59, 130, 246),
        Up = new SKColor(255, 90, 82),
        Down = new SKColor(53, 201, 142),
        RimTop = new SKColor(255, 255, 255, 60),
        RimBottom = new SKColor(255, 255, 255, 14),
        Outline = new SKColor(0, 0, 0, 70)
    };

    public static readonly Palette LightPalette = new()
    {
        Dark = false,
        Text = new SKColor(16, 18, 24),
        Text2 = 0.74f,
        Text3 = 0.56f,
        Tint = new SKColor(238, 240, 245, 205),
        Card = new SKColor(255, 255, 255, 150),
        Hover = new SKColor(0, 0, 0, 14),
        Pill = new SKColor(255, 255, 255, 250),
        Track = new SKColor(0, 0, 0, 30),
        Fill = new SKColor(255, 255, 255, 255),
        Accent = new SKColor(47, 111, 224),
        Up = new SKColor(212, 44, 37),
        Down = new SKColor(8, 135, 90),
        RimTop = new SKColor(255, 255, 255, 200),
        RimBottom = new SKColor(255, 255, 255, 60),
        Outline = new SKColor(0, 0, 0, 40)
    };

    public static readonly Palette RougePalette = new()
    {
        Dark = true,
        Text = new SKColor(255, 255, 255),
        Text2 = 0.70f,
        Text3 = 0.50f,
        Tint = new SKColor(0, 0, 0, 205),
        Card = new SKColor(255, 255, 255, 24),
        Hover = new SKColor(255, 255, 255, 34),
        Pill = new SKColor(255, 124, 162, 235),
        PillFg = new SKColor(0, 0, 0),
        Track = new SKColor(255, 255, 255, 36),
        Fill = new SKColor(255, 124, 162),
        Accent = new SKColor(255, 124, 162),
        Up = new SKColor(255, 124, 162),
        Down = new SKColor(95, 224, 176),
        RimTop = new SKColor(255, 124, 162, 170),
        RimBottom = new SKColor(255, 124, 162, 40),
        Outline = new SKColor(0, 0, 0, 140)
    };

    public static readonly Palette SkyPalette = new()
    {
        Dark = false,
        Text = new SKColor(12, 32, 56),
        Text2 = 0.78f,
        Text3 = 0.60f,
        Tint = new SKColor(50, 171, 253, 215),
        Tint2 = new SKColor(247, 239, 225, 230),
        Card = new SKColor(255, 255, 255, 105),
        Hover = new SKColor(255, 255, 255, 120),
        Pill = new SKColor(255, 255, 255, 190),
        Track = new SKColor(12, 32, 56, 34),
        Fill = new SKColor(255, 255, 255, 250),
        Accent = new SKColor(22, 103, 201),
        Up = new SKColor(212, 44, 37),
        Down = new SKColor(6, 128, 90),
        RimTop = new SKColor(255, 255, 255, 210),
        RimBottom = new SKColor(255, 255, 255, 70),
        Outline = new SKColor(12, 32, 56, 60)
    };

    public void SetMode(string mode)
    {
        Mode = string.IsNullOrWhiteSpace(mode) ? "auto" : mode.ToLowerInvariant();
        Apply(Mode switch
        {
            "light" => LightPalette,
            "rouge" => RougePalette,
            "sky" => SkyPalette,
            "dark" => DarkPalette,
            _ => IsDark ? DarkPalette : LightPalette
        }, Mode is "light" or "rouge" or "sky" or "dark" ? Mode : (IsDark ? "dark" : "light"));
    }

    public void FeedLuminance(float luminance)
    {
        _luminance = _luminance * 0.8f + Math.Clamp(luminance, 0f, 1f) * 0.2f;
        if (Mode != "auto") return;

        bool wantDark = IsDark;
        if (IsDark && _luminance > 0.64f) wantDark = false;
        else if (!IsDark && _luminance < 0.42f) wantDark = true;
        if (wantDark == IsDark)
        {
            _flipSince = 0;
            return;
        }

        long now = Environment.TickCount64;
        if (_flipSince == 0)
        {
            _flipSince = now;
            return;
        }
        if (now - _flipSince < 3000) return;
        _flipSince = 0;
        Apply(wantDark ? DarkPalette : LightPalette, wantDark ? "dark" : "light");
    }

    private void Apply(Palette palette, string name)
    {
        Palette = palette;
        ResolvedName = name;
    }

    public SKColor Text(float alpha = 1f) => WithAlpha(Palette.Text, alpha);
    public SKColor Text2(float alpha = 1f) => WithAlpha(Palette.Text, Palette.Text2 * alpha);
    public SKColor Text3(float alpha = 1f) => WithAlpha(Palette.Text, Palette.Text3 * alpha);
    public SKColor Card(float alpha = 1f) => WithAlpha(Palette.Card, alpha);
    public SKColor Hover(float alpha = 1f) => WithAlpha(Palette.Hover, alpha);
    public SKColor Track(float alpha = 1f) => WithAlpha(Palette.Track, alpha);
    public SKColor Fill(float alpha = 1f) => WithAlpha(Palette.Fill, alpha);
    public SKColor Accent(float alpha = 1f) => WithAlpha(Palette.Accent, alpha);
    public SKColor Up(float alpha = 1f) => WithAlpha(Palette.Up, alpha);
    public SKColor Down(float alpha = 1f) => WithAlpha(Palette.Down, alpha);
    public SKColor RimTop(float alpha = 1f) => WithAlpha(Palette.RimTop, alpha);
    public SKColor RimBottom(float alpha = 1f) => WithAlpha(Palette.RimBottom, alpha);
    public SKColor Outline(float alpha = 1f) => WithAlpha(Palette.Outline, alpha);
    public SKColor PillText(float alpha = 1f) => WithAlpha(Palette.PillFg ?? Palette.Text, alpha);

    public SKColor Pill(float alpha = 1f) => WithAlpha(Palette.Pill, alpha);

    public static SKColor WithAlpha(SKColor color, float alpha)
        => new(color.Red, color.Green, color.Blue, (byte)Math.Clamp((int)Math.Round(color.Alpha * alpha), 0, 255));
}
