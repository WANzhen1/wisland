using SkiaSharp;

namespace WislandSharp.UI;

internal static class Fonts
{
    public static readonly SKTypeface Ui = SKTypeface.FromFamilyName("Microsoft YaHei UI")
        ?? SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    public static readonly SKTypeface UiSemi = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? Ui;
    public static readonly SKTypeface Num = SKTypeface.FromFamilyName("Segoe UI Variable Display")
        ?? SKTypeface.FromFamilyName("Segoe UI") ?? Ui;
    public static readonly SKTypeface NumSemi = SKTypeface.FromFamilyName("Segoe UI Variable Display", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? Num;
    public static readonly SKTypeface Icon = SKTypeface.FromFamilyName("Segoe Fluent Icons")
        ?? SKTypeface.FromFamilyName("Segoe MDL2 Assets") ?? Ui;
}

internal static class TextUtil
{
    [ThreadStatic] private static SKPaint? _drawPaint;
    [ThreadStatic] private static SKPaint? _measurePaint;

    private static SKPaint DrawPaint => _drawPaint ??= new SKPaint
    {
        IsAntialias = true,
        SubpixelText = true
    };

    private static SKPaint MeasurePaint => _measurePaint ??= new SKPaint
    {
        IsAntialias = true
    };

    public static float Measure(string text, float size, SKTypeface typeface)
    {
        var paint = MeasurePaint;
        paint.Typeface = typeface;
        paint.TextSize = size;
        return paint.MeasureText(text ?? "");
    }

    public static void Draw(SKCanvas canvas, string text, float x, float y, float size, SKColor color,
        SKTypeface? typeface = null, SKTextAlign align = SKTextAlign.Left)
    {
        var paint = DrawPaint;
        paint.Typeface = typeface ?? Fonts.Ui;
        paint.TextSize = size;
        paint.Color = color;
        paint.TextAlign = align;
        canvas.DrawText(text ?? "", x, y, paint);
    }

    public static string Ellipsize(string text, float maxWidth, float size, SKTypeface typeface)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (Measure(text, size, typeface) <= maxWidth) return text;
        const string dots = "…";
        float dotsW = Measure(dots, size, typeface);
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[..mid], size, typeface) + dotsW <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? dots : text[..lo].TrimEnd() + dots;
    }

    public static string Tail(string text, float maxWidth, float size, SKTypeface typeface)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (Measure(text, size, typeface) <= maxWidth) return text;
        const string dots = "…";
        float dotsW = Measure(dots, size, typeface);
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[(text.Length - mid)..], size, typeface) + dotsW <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? dots : dots + text[(text.Length - lo)..].TrimStart();
    }

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        int total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }
}
