using System.Diagnostics;
using SkiaSharp;
using WislandSharp.Core;
using WislandSharp.Services;

namespace WislandSharp.UI;

public sealed partial class IslandRenderer
{
    public void Draw(SKCanvas canvas)
    {
        lock (_lock)
        {
            canvas.Clear(SKColors.Transparent);
            if (_body.Width < 1f || _body.Height < 1f) return;

            DrawShadow(canvas);
            DrawGlass(canvas);
            DrawRim(canvas);

            float e = Ease.InOut(Math.Clamp(_expand.Value, 0f, 1f));
            if (e < 0.995f)
            {
                canvas.Save();
                canvas.ClipPath(_path, SKClipOperation.Intersect, true);
                DrawCompact(canvas, 1f - e);
                canvas.Restore();
            }
            if (e > 0.005f)
            {
                canvas.Save();
                canvas.ClipPath(_path, SKClipOperation.Intersect, true);
                if (e < 0.995f)
                {
                    using var layerPaint = new SKPaint
                    {
                        Color = new SKColor(255, 255, 255, (byte)Math.Clamp((int)(255 * e), 0, 255))
                    };
                    canvas.SaveLayer(layerPaint);
                    DrawExpanded(canvas);
                    canvas.Restore();
                }
                else
                {
                    DrawExpanded(canvas);
                }
                canvas.Restore();
            }
        }
    }

    private void DrawShadow(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Translate(0, 1.5f);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round
        };
        (float Width, byte Alpha)[] layers = { (10f, 4), (5f, 7), (2f, 12) };
        foreach (var layer in layers)
        {
            paint.StrokeWidth = layer.Width;
            paint.Color = new SKColor(0, 0, 0, layer.Alpha);
            canvas.DrawPath(_path, paint);
        }
        canvas.Restore();
    }

    private void DrawGlass(SKCanvas canvas)
    {
        canvas.Save();
        canvas.ClipPath(_path, SKClipOperation.Intersect, true);
        var full = new SKRect(0, 0, WinW, WinH);
        bool hasBackdrop = _backdrop?.TryDraw(canvas, full) == true;
        if (!hasBackdrop)
        {
            (SKColor Top, SKColor Bottom) baseColors = _theme.ResolvedName switch
            {
                "light" => (new SKColor(248, 250, 255), new SKColor(218, 225, 238)),
                "rouge" => (new SKColor(58, 30, 46), new SKColor(18, 14, 20)),
                "sky" => (new SKColor(78, 174, 250), new SKColor(247, 239, 225)),
                _ => (new SKColor(58, 55, 84), new SKColor(24, 26, 38))
            };
            using var fallback = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(_body.Left, _body.Top),
                    new SKPoint(_body.Right * 0.35f + _body.Left * 0.65f, _body.Bottom),
                    new[] { baseColors.Top, baseColors.Bottom },
                    null, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(full, fallback);
        }

        using var tint = new SKPaint { IsAntialias = true };
        if (_theme.Palette.Tint2.HasValue)
        {
            tint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(_body.Left, _body.Top),
                new SKPoint(_body.Right * 0.25f + _body.Left * 0.75f, _body.Bottom),
                new[] { _theme.Palette.Tint, _theme.Palette.Tint2.Value },
                null,
                SKShaderTileMode.Clamp);
        }
        else
        {
            tint.Color = _theme.Palette.Tint;
        }
        if (!hasBackdrop)
            tint.Color = ThemeEngine.WithAlpha(tint.Color, 0.42f);
        canvas.DrawRect(full, tint);

        using var gloss = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, _body.Top), new SKPoint(0, _body.Bottom),
                new[]
                {
                    new SKColor(255, 255, 255, _theme.IsDark ? (byte)16 : (byte)30),
                    new SKColor(255, 255, 255, 0),
                    new SKColor(255, 255, 255, _theme.IsDark ? (byte)10 : (byte)18)
                },
                new[] { 0f, 0.52f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(full, gloss);
        canvas.Restore();
    }

    private void DrawRim(SKCanvas canvas)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, _body.Top + 0.75f, WinW, WinH));
        using var outline = new SKPaint
        {
            Color = _theme.Outline(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f
        };
        canvas.DrawPath(_path, outline);

        using var rim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, _body.Top), new SKPoint(0, _body.Bottom),
                new[] { _theme.RimTop(), _theme.RimBottom() },
                null, SKShaderTileMode.Clamp)
        };
        canvas.DrawPath(_path, rim);
        canvas.Restore();
    }

    private void DrawCompact(SKCanvas canvas, float alpha)
    {
        if (alpha <= 0.01f) return;
        long now = Environment.TickCount64;
        string mode = CompactMode(now);
        var r = _body;
        if (mode == "activity")
        {
            DrawActivity(canvas, r, alpha);
            return;
        }
        if (mode == "subtitle")
        {
            DrawCompactSubtitle(canvas, r, alpha);
            return;
        }
        if (mode == "media")
        {
            DrawCompactMedia(canvas, r, alpha);
            return;
        }
        if (mode == "pet")
        {
            _pet.Draw(canvas, r.MidX, r.MidY - 1, 1f, alpha, _theme);
            DrawNotificationBadge(canvas, r, alpha);
            return;
        }

        float cy = r.MidY;
        string time = DateTime.Now.ToString("HH:mm");
        TextUtil.Draw(canvas, time, r.Left + 18, cy + 5.5f, 15f, _theme.Text(alpha),
            Fonts.NumSemi);

        if (_weatherState.Ok)
        {
            string temp = $"{Math.Round(_weatherState.Temperature):0}°";
            float tw = TextUtil.Measure(temp, 13f, Fonts.Num);
            float tx = r.Right - 16 - tw;
            TextUtil.Draw(canvas, temp, tx, cy + 4.5f, 13f, _theme.Text2(alpha), Fonts.Num);
            DrawWeatherIcon(canvas, _weatherState.Code, _weatherState.IsDay,
                tx - 22, cy, 14f, _theme.Text2(alpha));
        }
        DrawNotificationBadge(canvas, r, alpha);
    }

    private void DrawCompactMedia(SKCanvas canvas, SKRect r, float alpha)
    {
        float cy = r.MidY;
        var coverRect = new SKRect(r.Left + 8, cy - 13, r.Left + 34, cy + 13);
        DrawCover(canvas, coverRect, 7f, alpha);

        float right = r.Right - 10;
        _compactPlayRect = new SKRect(right - 28, cy - 14, right, cy + 14);
        if (_hover && _compactPlayRect.Contains(_mouseX, _mouseY))
        {
            using var hover = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
            canvas.DrawCircle(_compactPlayRect.MidX, _compactPlayRect.MidY, 13f, hover);
        }
        TextUtil.Draw(canvas, _mediaState.Playing ? "\uE769" : "\uE768",
            _compactPlayRect.MidX, _compactPlayRect.MidY + 5, 14f, _theme.Text(alpha),
            Fonts.Icon, SKTextAlign.Center);

        float barsW = 26;
        float bx = _compactPlayRect.Left - 7 - barsW;
        using var barPaint = new SKPaint { Color = _theme.Text(alpha * 0.86f), IsAntialias = true };
        for (int i = 0; i < 4; i++)
        {
            float phase = (float)((nowPhase() * (7.3 + i * 2.1) + i * 1.7) % (Math.PI * 2));
            float target = 0.18f + _peak * (0.55f + 0.45f * MathF.Sin(phase) * MathF.Sin(phase));
            float h = 4 + 14 * target;
            canvas.DrawRoundRect(new SKRect(bx + i * 7, cy - h / 2, bx + i * 7 + 3.6f, cy + h / 2), 1.8f, 1.8f, barPaint);
        }
        DrawNotificationBadge(canvas, r, alpha);

        float tx = coverRect.Right + 10;
        float maxW = bx - 10 - tx;
        LyricLine? lyric = _lyrics?.At(_mediaState.EstimatedPosition());
        if (lyric != null && !string.IsNullOrWhiteSpace(lyric.Text))
        {
            string shownLyric = TextUtil.Ellipsize(lyric.Text, Math.Max(20, maxW), 13f, Fonts.UiSemi);
            TextUtil.Draw(canvas, shownLyric, tx + maxW / 2f, cy + 4.5f, 13f,
                _theme.Text(alpha), Fonts.UiSemi, SKTextAlign.Center);
        }
        else
        {
            string title = string.IsNullOrWhiteSpace(_mediaState.Title) ? "未在播放" : _mediaState.Title;
            string artist = _mediaState.Artist ?? "";
            string text = string.IsNullOrWhiteSpace(artist) ? title : $"{title}  ·  {artist}";
            string shown = TextUtil.Ellipsize(text, Math.Max(20, maxW), 13f, Fonts.UiSemi);
            TextUtil.Draw(canvas, shown, tx, cy + 4.5f, 13f, _theme.Text(alpha), Fonts.UiSemi);
        }
    }

    private static float nowPhase() => (Environment.TickCount64 % 100000) / 1000f;

    private void DrawActivity(SKCanvas canvas, SKRect r, float alpha)
    {
        var act = (_activityTitle, _activitySub, _activityColor, _activityGlyph);
        float cy = r.MidY;
        var iconRect = new SKRect(r.Left + 9, cy - 13, r.Left + 35, cy + 13);
        using var iconBg = new SKPaint
        {
            Color = act._activityColor == SKColors.Transparent
                ? _theme.Accent(alpha)
                : new SKColor(act._activityColor.Red, act._activityColor.Green, act._activityColor.Blue,
                    (byte)Math.Clamp((int)(act._activityColor.Alpha * alpha), 0, 255)),
            IsAntialias = true
        };
        canvas.DrawCircle(iconRect.MidX, iconRect.MidY, 13f, iconBg);
        TextUtil.Draw(canvas, _activityGlyph, iconRect.MidX, iconRect.MidY + 5, 13f,
            _theme.IsDark ? SKColors.White.WithAlpha((byte)(255 * alpha)) : SKColors.Black.WithAlpha((byte)(220 * alpha)),
            Fonts.Icon, SKTextAlign.Center);

        TextUtil.Draw(canvas, TextUtil.Ellipsize(_activityTitle, r.Width - 60, 13f, Fonts.UiSemi),
            iconRect.Right + 9, cy + (string.IsNullOrEmpty(_activitySub) ? 5 : -1), 13f,
            _theme.Text(alpha), Fonts.UiSemi);
        if (!string.IsNullOrEmpty(_activitySub))
        {
            TextUtil.Draw(canvas, TextUtil.Ellipsize(_activitySub, r.Width - 60, 11f, Fonts.Ui),
                iconRect.Right + 9, cy + 13, 11f, _theme.Text2(alpha), Fonts.Ui);
        }
    }

    private void DrawExpanded(SKCanvas canvas)
    {
        float e = Ease.InOut(Math.Clamp(_expand.Value, 0f, 1f));
        if (e < 0.3f) return;

        float contentL = _body.Left + Pad;
        float contentR = _body.Right - Pad;
        float navY = _body.Top + Pad;
        DrawNav(canvas, contentL, contentR, navY, e);

        float pageY = navY + NavH + PageGap;
        canvas.Save();
        canvas.ClipRect(new SKRect(contentL - 2, pageY - 2, contentR + 2, _body.Bottom - 4), SKClipOperation.Intersect, true);
        switch (_navIndex)
        {
            case 0: DrawHome(canvas, contentL, contentR, pageY, e); break;
            case 1: DrawMessages(canvas, contentL, contentR, pageY, e); break;
            case 2: DrawStocks(canvas, contentL, contentR, pageY, e); break;
            case 3: DrawWeather(canvas, contentL, contentR, pageY, e); break;
            case 4: DrawControls(canvas, contentL, contentR, pageY, e); break;
            case 5: DrawClipboard(canvas, contentL, contentR, pageY, e); break;
            case 6: DrawTranslationPage(canvas, contentL, contentR, pageY, e); break;
            case 7: DrawSubtitlesPage(canvas, contentL, contentR, pageY, e); break;
        }
        canvas.Restore();
    }

    private void DrawNav(SKCanvas canvas, float contentL, float contentR, float navY, float alpha)
    {
        for (int i = 0; i < _navHits.Length; i++)
        {
            var r = _navHits[i].Rect;
            if (r.Width < 1) continue;
            bool active = i == _navIndex;
            if (active)
            {
                using var pill = new SKPaint { Color = _theme.Pill(alpha), IsAntialias = true };
                canvas.DrawRoundRect(r, r.Height / 2, r.Height / 2, pill);
            }
            else if (i == _hoverNav)
            {
                using var hover = new SKPaint { Color = _theme.Hover(alpha * 0.8f), IsAntialias = true };
                canvas.DrawRoundRect(r, r.Height / 2, r.Height / 2, hover);
            }
            var color = active ? _theme.PillText(alpha) : _theme.Text(alpha * (i == _hoverNav ? 0.92f : 0.58f));
            TextUtil.Draw(canvas, _navIcons[i], r.Left + 17, r.MidY + 5.2f, 15f, color, Fonts.Icon, SKTextAlign.Center);
            if (active)
            {
                TextUtil.Draw(canvas, _navLabels[i], r.Left + 34 - 4, r.MidY + 4.4f, 12.5f, color, Fonts.UiSemi);
            }
            if (i == 1 && _notificationState.TotalCount > 0)
            {
                using var badge = new SKPaint { Color = new SKColor(255, 69, 58, (byte)(255 * alpha)), IsAntialias = true };
                canvas.DrawCircle(r.Right - 7, r.Top + 7, 3.2f, badge);
            }
        }

        var gear = new SKRect(contentR - 34, navY + 2, contentR, navY + NavH - 2);
        bool gearHover = gear.Contains(_mouseX, _mouseY);
        if (gearHover)
        {
            using var hover = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
            canvas.DrawRoundRect(gear, gear.Height / 2, gear.Height / 2, hover);
        }
        TextUtil.Draw(canvas, "\uE713", gear.MidX, gear.MidY + 5, 14f,
            _theme.Text(alpha * 0.8f), Fonts.Icon, SKTextAlign.Center);
    }

    private void DrawHome(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        float mediaW = 220f;
        var mediaRect = new SKRect(contentL, y, contentL + mediaW, y + 170f);
        DrawCard(canvas, mediaRect, 16f, alpha);

        var coverRect = new SKRect(mediaRect.Left + 14, mediaRect.Top + 14, mediaRect.Left + 78, mediaRect.Top + 78);
        DrawCover(canvas, coverRect, 16f, alpha);

        string title = string.IsNullOrWhiteSpace(_mediaState.Title) ? "未在播放" : _mediaState.Title;
        string artist = string.IsNullOrWhiteSpace(_mediaState.Artist) ? "选择一首歌开始" : _mediaState.Artist;
        TextUtil.Draw(canvas, TextUtil.Ellipsize(title, mediaW - 100, 14f, Fonts.UiSemi),
            coverRect.Right + 12, mediaRect.Top + 32, 14f, _theme.Text(alpha), Fonts.UiSemi);
        TextUtil.Draw(canvas, TextUtil.Ellipsize(artist, mediaW - 100, 11.5f, Fonts.Ui),
            coverRect.Right + 12, mediaRect.Top + 51, 11.5f, _theme.Text2(alpha), Fonts.Ui);

        var progress = new SKRect(mediaRect.Left + 16, mediaRect.Top + 96, mediaRect.Right - 16, mediaRect.Top + 99);
        _mediaProgressRect = progress;
        _pageHits.Add((IslandHit.MediaProgress, progress, 0));
        double pos = _mediaState.EstimatedPosition();
        double dur = _mediaState.DurationSeconds;
        float ratio = dur > 0 ? (float)Math.Clamp(pos / dur, 0, 1) : 0;
        DrawProgress(canvas, progress, ratio, alpha);
        TextUtil.Draw(canvas, TextUtil.FormatTime(pos), progress.Left, progress.Bottom + 16, 10.5f, _theme.Text3(alpha), Fonts.Num);
        TextUtil.Draw(canvas, TextUtil.FormatTime(dur), progress.Right, progress.Bottom + 16, 10.5f,
            _theme.Text3(alpha), Fonts.Num, SKTextAlign.Right);

        float btnY = mediaRect.Top + 140;
        var prev = new SKRect(mediaRect.Left + 36, btnY - 15, mediaRect.Left + 66, btnY + 15);
        var play = new SKRect(mediaRect.MidX - 17, btnY - 17, mediaRect.MidX + 17, btnY + 17);
        var next = new SKRect(mediaRect.Right - 66, btnY - 15, mediaRect.Right - 36, btnY + 15);
        _pageHits.Add((IslandHit.MediaPrevious, prev, 0));
        _pageHits.Add((IslandHit.MediaPlay, play, 0));
        _pageHits.Add((IslandHit.MediaNext, next, 0));
        using (var playBg = new SKPaint { Color = _theme.Card(alpha * 1.7f), IsAntialias = true })
            canvas.DrawCircle(play.MidX, play.MidY, 17f, playBg);
        TextUtil.Draw(canvas, "\uE892", prev.MidX, prev.MidY + 5, 18f, _theme.Text(alpha * 0.86f), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, _mediaState.Playing ? "\uE769" : "\uE768", play.MidX, play.MidY + 6, 20f,
            _theme.Text(alpha), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, "\uE893", next.MidX, next.MidY + 5, 18f, _theme.Text(alpha * 0.86f), Fonts.Icon, SKTextAlign.Center);

        float petCx = contentL + (contentR - contentL) * 0.52f;
        _pet.Draw(canvas, petCx, y + 80f, 2.3f, alpha, _theme);

        float actionsR = contentR - 4;
        float actionsT = y + 2;
        DrawAction(canvas, actionsR - 150, actionsT, 70, 70, _pinned ? "\uE77A" : "\uE718",
            _pinned ? "保持展开" : "悬停展开", _pinned, alpha, IslandHit.ActionPin);
        DrawAction(canvas, actionsR - 70, actionsT, 70, 70, "\uE8B0",
            _passthrough ? "已穿透" : "可穿透", _passthrough, alpha, IslandHit.ActionPassthrough);
        DrawAction(canvas, actionsR - 150, actionsT + 82, 70, 70, _systemState.Muted ? "\uE74F" : "\uE767",
            _systemState.Muted ? "已静音" : "静音", _systemState.Muted, alpha, IslandHit.ActionMute);
        DrawAction(canvas, actionsR - 70, actionsT + 82, 70, 70, "\uE720", "实时字幕",
            _subtitleState.Running || _subtitleState.Starting, alpha, IslandHit.ActionLive);

        float lx = contentL + 2;
        float ly = y + 192;
        for (int i = 0; i < _config.Launcher.Count && i < 5; i++)
        {
            var item = _config.Launcher[i];
            var rect = new SKRect(lx, ly, lx + 44, ly + 44);
            DrawLauncher(canvas, rect, item, alpha);
            _pageHits.Add((IslandHit.Launcher, rect, i));
            lx += 62;
        }
    }

    private void DrawAction(SKCanvas canvas, float x, float y, float w, float h, string glyph,
        string label, bool active, float alpha, IslandHit hit)
    {
        var rect = new SKRect(x, y, x + w, y + h);
        bool hover = rect.Contains(_mouseX, _mouseY);
        if (hover || active)
        {
            using var bg = new SKPaint { Color = active ? _theme.Pill(alpha * 0.85f) : _theme.Card(alpha), IsAntialias = true };
            canvas.DrawRoundRect(rect, 14f, 14f, bg);
        }
        TextUtil.Draw(canvas, glyph, rect.MidX, y + 29, 23f,
            active ? _theme.PillText(alpha) : _theme.Text(alpha * 0.9f), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, label, rect.MidX, y + 52, 11f,
            active ? _theme.PillText(alpha) : _theme.Text2(alpha), Fonts.Ui, SKTextAlign.Center);
        _pageHits.Add((hit, rect, 0));
    }

    private void DrawLauncher(SKCanvas canvas, SKRect rect, LauncherItem item, float alpha)
    {
        using var bg = new SKPaint
        {
            Color = _theme.Card(alpha * 0.65f),
            IsAntialias = true
        };
        canvas.DrawRoundRect(rect, 10f, 10f, bg);
        var icon = GetFileIcon(item.Path);
        if (icon != null)
        {
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(icon, new SKRect(rect.Left + 9, rect.Top + 9, rect.Right - 9, rect.Bottom - 9), paint);
        }
        else
        {
            string ch = string.IsNullOrWhiteSpace(item.Name) ? "?" : item.Name[..1];
            TextUtil.Draw(canvas, ch, rect.MidX, rect.MidY + 7, 20f, _theme.Text(alpha), Fonts.UiSemi, SKTextAlign.Center);
        }
        bool hover = rect.Contains(_mouseX, _mouseY);
        if (hover)
        {
            using var hoverPaint = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
            canvas.DrawRoundRect(rect, 10f, 10f, hoverPaint);
        }
    }

    private SKBitmap? GetFileIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;
        SKBitmap? result = null;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded) && expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = Icon.ExtractAssociatedIcon(expanded);
                if (icon != null)
                {
                    using var bmp = icon.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    result = SKBitmap.Decode(ms.ToArray());
                }
            }
        }
        catch { }
        _iconCache[path] = result;
        return result;
    }

    private void DrawCover(SKCanvas canvas, SKRect rect, float radius, float alpha)
    {
        if (_mediaCover != null)
        {
            using var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.High,
                Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)(255 * alpha), 0, 255))
            };
            canvas.Save();
            using var clip = Shapes.RoundedRectPath(rect, radius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            canvas.DrawBitmap(_mediaCover, rect, paint);
            canvas.Restore();
            return;
        }
        using var bg = new SKPaint { Color = _theme.Card(alpha * 1.2f), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, bg);
        TextUtil.Draw(canvas, "\uE8D6", rect.MidX, rect.MidY + 6, Math.Min(22f, rect.Height * 0.5f),
            _theme.Text(alpha * 0.8f), Fonts.Icon, SKTextAlign.Center);
    }

    private void DrawCard(SKCanvas canvas, SKRect rect, float radius, float alpha)
    {
        using var bg = new SKPaint { Color = _theme.Card(alpha), IsAntialias = true };
        canvas.DrawRoundRect(rect, radius, radius, bg);
    }

    private void DrawProgress(SKCanvas canvas, SKRect rect, float ratio, float alpha)
    {
        using var track = new SKPaint { Color = _theme.Track(alpha), IsAntialias = true };
        canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, track);
        float w = rect.Width * Math.Clamp(ratio, 0f, 1f);
        if (w > 0.5f)
        {
            using var fill = new SKPaint { Color = _theme.Fill(alpha), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(rect.Left, rect.Top, rect.Left + w, rect.Bottom),
                rect.Height / 2, rect.Height / 2, fill);
        }
    }

    private void DrawNotificationBadge(SKCanvas canvas, SKRect r, float alpha)
    {
        int qq = GetCount("qq");
        int wx = GetCount("wechat");
        int mail = GetCount("mail");
        if (qq + wx + mail <= 0) return;
        SKColor color = qq >= wx && qq >= mail ? new SKColor(18, 183, 245) :
            wx >= mail ? new SKColor(7, 193, 96) : new SKColor(255, 159, 10);
        using var paint = new SKPaint { Color = color.WithAlpha((byte)(255 * alpha)), IsAntialias = true };
        canvas.DrawCircle(r.Right - 8, r.Top + 7, 3.4f, paint);
    }

    private int GetCount(string kind)
        => _notificationState.Counts.TryGetValue(kind, out int count) ? count : 0;
}
