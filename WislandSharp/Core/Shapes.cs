using SkiaSharp;

namespace WislandSharp.Core;

public static class Shapes
{
    private const float K = 0.5522847f;

    /// <summary>
    /// 贴顶边的液态胶囊：顶部与屏幕边缘通过两侧内凹"表面张力"肩线相连，
    /// 底部保持大圆角。body.Top 是胶囊本体的顶面，通常留 2~4px 给肩线。
    /// </summary>
    public static SKPath TopAttached(SKRect body, float fillet = 13f, float bottomRadius = 18f)
    {
        var path = new SKPath();
        TopAttachedInto(path, body, fillet, bottomRadius);
        return path;
    }

    public static void TopAttachedInto(SKPath path, SKRect body, float fillet = 13f,
        float bottomRadius = 18f)
    {
        float l = body.Left, r = body.Right, t = body.Top, b = body.Bottom;
        float rb = Math.Clamp(bottomRadius, 0f, Math.Min(body.Width, body.Height) / 2f);
        float f = Math.Max(0f, Math.Min(fillet, body.Width / 2f));

        path.Rewind();
        path.MoveTo(l - f, 0f);
        path.LineTo(r + f, 0f);
        path.CubicTo(r + f - f * K, 0f, r, t - f * K, r, t);
        path.LineTo(r, b - rb);
        if (rb > 0.01f)
        {
            path.CubicTo(r, b - rb + rb * K, r - rb + rb * K, b, r - rb, b);
        }
        path.LineTo(l + rb, b);
        if (rb > 0.01f)
        {
            path.CubicTo(l + rb - rb * K, b, l, b - rb + rb * K, l, b - rb);
        }
        path.LineTo(l, t);
        path.CubicTo(l, t - f * K, l - f + f * K, 0f, l - f, 0f);
        path.Close();
    }

    public static SKPath TopAttachedCompressed(SKRect body, float fillet, float squeeze)
    {
        squeeze = Math.Clamp(squeeze, 0f, 1f);
        float f = fillet * (1f + squeeze * 0.8f);
        return TopAttached(body, f, Math.Min(30f, body.Height / 2f));
    }

    public static SKRoundRect RoundRect(SKRect rect, float radius)
        => new(rect, Math.Clamp(radius, 0f, Math.Min(rect.Width, rect.Height) / 2f));

    public static SKPath RoundedRectPath(SKRect rect, float radius)
    {
        var path = new SKPath();
        path.AddRoundRect(RoundRect(rect, radius));
        return path;
    }

    public static SKPath Heart(float cx, float cy, float size)
    {
        var path = new SKPath();
        float s = size / 2f;
        path.MoveTo(cx, cy + s * 0.65f);
        path.CubicTo(cx - s * 1.35f, cy - s * 0.2f, cx - s * 0.75f, cy - s * 1.15f, cx, cy - s * 0.45f);
        path.CubicTo(cx + s * 0.75f, cy - s * 1.15f, cx + s * 1.35f, cy - s * 0.2f, cx, cy + s * 0.65f);
        path.Close();
        return path;
    }

    public static SKPath Sparkle(float cx, float cy, float r)
    {
        var path = new SKPath();
        path.MoveTo(cx, cy - r);
        path.CubicTo(cx + r * 0.12f, cy - r * 0.18f, cx + r * 0.18f, cy - r * 0.12f, cx + r, cy);
        path.CubicTo(cx + r * 0.18f, cy + r * 0.12f, cx + r * 0.12f, cy + r * 0.18f, cx, cy + r);
        path.CubicTo(cx - r * 0.12f, cy + r * 0.18f, cx - r * 0.18f, cy + r * 0.12f, cx - r, cy);
        path.CubicTo(cx - r * 0.18f, cy - r * 0.12f, cx - r * 0.12f, cy - r * 0.18f, cx, cy - r);
        path.Close();
        return path;
    }
}
