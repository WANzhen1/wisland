using SkiaSharp;

namespace WislandSharp.Core;

public static class Shapes
{
    private const float K = 0.5522847f;

    /// <summary>
    /// 贴顶边的液态胶囊：顶部通过两侧内凹肩线连接屏幕边缘，底部保持大圆角。
    /// shoulderHeight 控制肩线高度，fillet 控制顶部外扩宽度。
    /// </summary>
    public static SKPath TopAttached(SKRect body, float fillet = 13f, float bottomRadius = 18f,
        float topOverscan = 0f, float shoulderHeight = 12f)
    {
        var path = new SKPath();
        TopAttachedInto(path, body, fillet, bottomRadius, topOverscan, shoulderHeight);
        return path;
    }

    public static void TopAttachedInto(SKPath path, SKRect body, float fillet = 13f,
        float bottomRadius = 18f, float topOverscan = 0f, float shoulderHeight = 12f)
    {
        float l = body.Left, r = body.Right, t = body.Top, b = body.Bottom;
        float rb = Math.Clamp(bottomRadius, 0f, Math.Min(body.Width, body.Height) / 2f);
        float f = Math.Max(0f, Math.Min(fillet, body.Width / 2f));
        float top = t - Math.Max(0f, topOverscan);
        float shoulder = t + Math.Max(0f, shoulderHeight);
        float shoulderH = Math.Max(1f, shoulder - top);

        path.Rewind();
        path.MoveTo(l - f, top);
        path.LineTo(r + f, top);
        path.CubicTo(r + f - f * K, top, r, shoulder - shoulderH * K, r, shoulder);
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
        path.LineTo(l, shoulder);
        path.CubicTo(l, shoulder - shoulderH * K, l - f + f * K, top, l - f, top);
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
