using SkiaSharp;
using WislandSharp.Core;

namespace WislandSharp.UI;

/// <summary>原创极简"发光双眼"小形象：会眨眼、看鼠标、被按住时挤歪。</summary>
public sealed class PetFace
{
    private float _lookX, _lookY;
    private float _blink;
    private long _nextBlink;
    private string _mood = "default";
    private long _moodUntil;
    private float _press;
    private float _pressRel;
    private float _surprise;

    private static readonly string[] Moods = { "happy", "wink", "love", "star", "shy", "pout", "squint" };

    public PetFace()
    {
        _nextBlink = Environment.TickCount64 + 2500 + Random.Shared.Next(2200);
    }

    public void React(string mood = "surprise")
    {
        _mood = mood;
        _moodUntil = Environment.TickCount64 + 1400;
        _surprise = 1f;
    }

    public void SetPress(float amount, float relative)
    {
        _press = Math.Clamp(amount, 0f, 1.2f);
        _pressRel = Math.Clamp(relative, -1f, 1f);
    }

    public void Update(float dt, float lookX, float lookY, bool near)
    {
        _lookX += (Math.Clamp(lookX, -1f, 1f) - _lookX) * Math.Min(1f, dt * 12f);
        _lookY += (Math.Clamp(lookY, -1f, 1f) - _lookY) * Math.Min(1f, dt * 12f);
        _surprise = Math.Max(0f, _surprise - dt * 2.2f);

        long now = Environment.TickCount64;
        if (now >= _nextBlink && _blink <= 0)
        {
            _blink = 1f;
            _nextBlink = now + 2400 + Random.Shared.Next(3200);
        }
        if (_blink > 0) _blink = Math.Max(0f, _blink - dt * 7.5f);

        if (now > _moodUntil)
        {
            _mood = near ? "happy" : "default";
            if (now > _moodUntil + 6000 && Random.Shared.NextDouble() < dt * 0.08)
            {
                _mood = Moods[Random.Shared.Next(Moods.Length)];
                _moodUntil = now + 2600;
            }
        }
    }

    public void Draw(SKCanvas canvas, float cx, float cy, float scale, float alpha, ThemeEngine theme)
    {
        float lean = -_pressRel * Math.Min(1f, _press * 1.6f);
        canvas.Save();
        canvas.Translate(cx + lean * 24f * scale, cy);
        canvas.RotateDegrees(lean * 14f);
        canvas.Scale(scale * (1f + _press * 0.03f), scale * (1f - _press * 0.12f));
        canvas.Translate(-cx, -cy);

        var eye = theme.IsDark ? new SKColor(255, 255, 255) : new SKColor(22, 30, 48);
        var blush = new SKColor(255, 118, 154);
        float spread = _press * (1f - Math.Abs(_pressRel)) * 9f;
        float eyeW = 15f, eyeH = 25f;
        float gap = 23f + spread;
        float eyeCy = cy - 2f;
        float look = _lookX * 2.2f;
        float blink = 1f - Math.Max(0f, Math.Min(1f, _blink)) * 0.92f;
        float surprise = 1f + _surprise * 0.18f;

        if (_mood == "love")
        {
            using var heartPaint = new SKPaint { Color = new SKColor(255, 98, 142, (byte)(255 * alpha)), IsAntialias = true };
            using var glow = new SKPaint
            {
                Color = new SKColor(255, 98, 142, (byte)(110 * alpha)),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7f)
            };
            var h1 = Shapes.Heart(cx - gap, eyeCy, 20f);
            var h2 = Shapes.Heart(cx + gap, eyeCy, 20f);
            canvas.DrawPath(h1, glow);
            canvas.DrawPath(h2, glow);
            canvas.DrawPath(h1, heartPaint);
            canvas.DrawPath(h2, heartPaint);
            DrawBlush(canvas, cx, cy, gap, alpha, blush);
            DrawMouth(canvas, cx, cy + 15f, 10f, alpha, eye, true);
            canvas.Restore();
            return;
        }

        using var eyePaint = new SKPaint { Color = PetColor(eye, alpha), IsAntialias = true };
        using var glowPaint = new SKPaint
        {
            Color = PetColor(eye, alpha * 0.34f),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f)
        };

        float leftH = eyeH * blink * surprise;
        float rightH = eyeH * blink * surprise;
        float leftX = cx - gap + look;
        float rightX = cx + gap + look;
        float eyeY = eyeCy + _lookY * 1.8f;
        if (_mood == "wink") rightH *= 0.22f;
        if (_mood == "squint") { leftH *= 0.42f; rightH *= 0.42f; }
        if (_mood == "star")
        {
            using var starPaint = new SKPaint { Color = PetColor(eye, alpha), IsAntialias = true };
            canvas.DrawPath(Shapes.Sparkle(leftX, eyeY, 13f), starPaint);
            canvas.DrawPath(Shapes.Sparkle(rightX, eyeY, 13f), starPaint);
        }
        else
        {
            var l = new SKRect(leftX - eyeW / 2, eyeY - leftH / 2, leftX + eyeW / 2, eyeY + leftH / 2);
            var r = new SKRect(rightX - eyeW / 2, eyeY - rightH / 2, rightX + eyeW / 2, eyeY + rightH / 2);
            canvas.DrawRoundRect(l, eyeW / 2, eyeW / 2, glowPaint);
            canvas.DrawRoundRect(r, eyeW / 2, eyeW / 2, glowPaint);
            canvas.DrawRoundRect(l, eyeW / 2, eyeW / 2, eyePaint);
            canvas.DrawRoundRect(r, eyeW / 2, eyeW / 2, eyePaint);
        }

        if (_mood is "shy" or "happy" or "pout" or "default")
            DrawBlush(canvas, cx, cy, gap, alpha * (_mood == "shy" ? 0.9f : 0.55f), blush);
        DrawMouth(canvas, cx, cy + 14f, _mood == "pout" ? -7f : 9f, alpha, eye, _mood is "happy" or "shy");
        canvas.Restore();
    }

    private static SKColor PetColor(SKColor color, float alpha)
        => new(color.Red, color.Green, color.Blue, (byte)Math.Clamp((int)(color.Alpha * alpha), 0, 255));

    private static void DrawBlush(SKCanvas canvas, float cx, float cy, float gap, float alpha, SKColor blush)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(blush.Red, blush.Green, blush.Blue, (byte)Math.Clamp((int)(70 * alpha), 0, 255)),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
        };
        canvas.DrawOval(new SKRect(cx - gap - 25, cy + 6, cx - gap - 7, cy + 18), paint);
        canvas.DrawOval(new SKRect(cx + gap + 7, cy + 6, cx + gap + 25, cy + 18), paint);
    }

    private static void DrawMouth(SKCanvas canvas, float cx, float cy, float width, float alpha, SKColor color, bool smile)
    {
        using var paint = new SKPaint
        {
            Color = PetColor(color, alpha),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.4f,
            StrokeCap = SKStrokeCap.Round
        };
        var path = new SKPath();
        if (width < 0)
        {
            path.MoveTo(cx - 5, cy + 3);
            path.QuadTo(cx, cy - 1, cx + 5, cy + 3);
        }
        else
        {
            path.MoveTo(cx - width / 2, cy - 1);
            path.QuadTo(cx, cy + (smile ? 8 : 5), cx + width / 2, cy - 1);
        }
        canvas.DrawPath(path, paint);
        path.Dispose();
    }
}
