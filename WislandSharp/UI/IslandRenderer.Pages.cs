using SkiaSharp;
using WislandSharp.Core;

namespace WislandSharp.UI;

public sealed partial class IslandRenderer
{
    private void BuildHomeHits(float contentL, float contentR, float y)
    {
        // 主页的命中矩形由 DrawHome 在绘制时写入，和视觉使用同一组坐标。
    }

    private void DrawMessages(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        TextUtil.Draw(canvas, "消息", contentL + 2, y + 16, 14f, _theme.Text(alpha), Fonts.UiSemi);
        string status = _notificationState.Available ? "Windows 通知" : "通知不可用";
        TextUtil.Draw(canvas, status, contentR - 56, y + 16, 10.5f,
            _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Right);

        var clear = new SKRect(contentR - 48, y + 1, contentR, y + 27);
        using (var clearBg = new SKPaint
               {
                   Color = clear.Contains(_mouseX, _mouseY) ? _theme.Hover(alpha) : _theme.Card(alpha * 0.75f),
                   IsAntialias = true
               })
            canvas.DrawRoundRect(clear, 8f, 8f, clearBg);
        TextUtil.Draw(canvas, "清空", clear.MidX, clear.MidY + 4, 10.5f,
            _theme.Text2(alpha), Fonts.Ui, SKTextAlign.Center);

        string[] kinds = { "qq", "wechat", "mail" };
        string[] names = { "QQ", "微信", "邮件" };
        SKColor[] colors = { new(18, 183, 245), new(7, 193, 96), new(255, 159, 10) };
        for (int i = 0; i < 3; i++)
        {
            var tile = MessageTileRect(i, contentL, contentR, y);
            var last = _notificationState.History.FirstOrDefault(x => x.Kind == kinds[i]);
            bool hover = tile.Contains(_mouseX, _mouseY);
            using var bg = new SKPaint
            {
                Color = hover ? _theme.Hover(alpha) : _theme.Card(alpha * 0.82f),
                IsAntialias = true
            };
            canvas.DrawRoundRect(tile, 14f, 14f, bg);
            using var dot = new SKPaint { Color = colors[i].WithAlpha((byte)(255 * alpha)), IsAntialias = true };
            canvas.DrawCircle(tile.Left + 22, tile.Top + 24, 9f, dot);
            TextUtil.Draw(canvas, i == 0 ? "\uE8BD" : i == 1 ? "\uE8F2" : "\uE715",
                tile.Left + 22, tile.Top + 29, 12f,
                _theme.IsDark ? SKColors.White.WithAlpha((byte)(245 * alpha)) : SKColors.Black.WithAlpha((byte)(220 * alpha)),
                Fonts.Icon, SKTextAlign.Center);
            TextUtil.Draw(canvas, names[i], tile.Left + 40, tile.Top + 28, 13f,
                _theme.Text(alpha), Fonts.UiSemi);

            int count = GetCount(kinds[i]);
            if (count > 0)
            {
                string badge = count > 9 ? "9+" : count.ToString();
                float bw = Math.Max(16f, TextUtil.Measure(badge, 9.5f, Fonts.NumSemi) + 8f);
                var br = new SKRect(tile.Right - bw - 8, tile.Top + 9, tile.Right - 8, tile.Top + 25);
                using var badgePaint = new SKPaint { Color = new SKColor(255, 69, 58, (byte)(255 * alpha)), IsAntialias = true };
                canvas.DrawRoundRect(br, 8f, 8f, badgePaint);
                TextUtil.Draw(canvas, badge, br.MidX, br.MidY + 3.5f, 9.5f,
                    SKColors.White.WithAlpha((byte)(255 * alpha)), Fonts.NumSemi, SKTextAlign.Center);
            }

            string sub = last == null ? "暂无消息" :
                (string.IsNullOrWhiteSpace(last.Sender) ? last.Preview : last.Sender);
            TextUtil.Draw(canvas, TextUtil.Ellipsize(sub, tile.Width - 26, 10.5f, Fonts.Ui),
                tile.Left + 14, tile.Bottom - 14, 10.5f,
                last == null ? _theme.Text3(alpha) : _theme.Text2(alpha), Fonts.Ui);
        }

        var list = new SKRect(contentL, y + 136, contentR, y + 292);
        DrawCard(canvas, list, 16f, alpha * 0.70f);
        if (!_notificationState.Available)
        {
            TextUtil.Draw(canvas, "通知读取不可用", list.MidX, list.Top + 46, 13f,
                _theme.Text2(alpha), Fonts.UiSemi, SKTextAlign.Center);
            TextUtil.Draw(canvas, TextUtil.Ellipsize(_notificationState.Error, list.Width - 40, 10.5f, Fonts.Ui),
                list.MidX, list.Top + 70, 10.5f, _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Center);
            TextUtil.Draw(canvas, "收到一条系统通知后会自动刷新", list.MidX, list.Top + 92, 10.5f,
                _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Center);
            return;
        }
        if (_notificationState.History.Count == 0)
        {
            TextUtil.Draw(canvas, "暂无消息", list.MidX, list.MidY, 12f,
                _theme.Text2(alpha), Fonts.Ui, SKTextAlign.Center);
            return;
        }

        for (int i = 0; i < _notificationState.History.Count && i < 4; i++)
        {
            var item = _notificationState.History[i];
            var row = NotificationRowRect(i, contentL, contentR, y);
            if (row.Contains(_mouseX, _mouseY))
            {
                using var hov = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
                canvas.DrawRoundRect(row, 8f, 8f, hov);
            }
            SKColor color = item.Kind == "qq" ? new SKColor(18, 183, 245) :
                item.Kind == "wechat" ? new SKColor(7, 193, 96) : new SKColor(255, 159, 10);
            using var dot = new SKPaint { Color = color.WithAlpha((byte)(255 * alpha)), IsAntialias = true };
            canvas.DrawCircle(row.Left + 12, row.MidY, 3.4f, dot);
            TextUtil.Draw(canvas, item.AppName, row.Left + 22, row.MidY + 4, 10.5f,
                _theme.Text2(alpha), Fonts.UiSemi);
            string line = string.IsNullOrWhiteSpace(item.Sender) ? item.Preview :
                string.IsNullOrWhiteSpace(item.Preview) ? item.Sender : $"{item.Sender} · {item.Preview}";
            TextUtil.Draw(canvas, TextUtil.Ellipsize(line, row.Width - 150, 11f, Fonts.Ui),
                row.Left + 72, row.MidY + 4, 11f, _theme.Text(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, item.Time.ToString("HH:mm"), row.Right - 10, row.MidY + 4, 9.5f,
                _theme.Text3(alpha), Fonts.Num, SKTextAlign.Right);
        }
    }

    private void BuildMessageHits(float contentL, float contentR, float y)
    {
        for (int i = 0; i < 3; i++)
        {
            var hit = i == 0 ? IslandHit.MessageQq : i == 1 ? IslandHit.MessageWechat : IslandHit.MessageMail;
            _pageHits.Add((hit, MessageTileRect(i, contentL, contentR, y), i));
        }
        _pageHits.Add((IslandHit.MessageClear, new SKRect(contentR - 48, y + 1, contentR, y + 27), 0));
        for (int i = 0; i < _notificationState.History.Count && i < 4; i++)
            _pageHits.Add((IslandHit.NotificationRow, NotificationRowRect(i, contentL, contentR, y), i));
    }

    private static SKRect MessageTileRect(int index, float contentL, float contentR, float y)
    {
        float gap = 8f;
        float width = (contentR - contentL - gap * 2) / 3f;
        float left = contentL + index * (width + gap);
        return new SKRect(left, y + 30, left + width, y + 124);
    }

    private static SKRect NotificationRowRect(int index, float contentL, float contentR, float y)
    {
        float top = y + 136 + 8 + index * 34;
        return new SKRect(contentL + 6, top, contentR - 6, top + 30);
    }

    private void DrawStocks(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        float w = contentR - contentL;
        string status = MarketStatus();
        TextUtil.Draw(canvas, "自选行情", contentL + 2, y + 16, 14f, _theme.Text(alpha), Fonts.UiSemi);
        TextUtil.Draw(canvas, $"腾讯财经 · {status}", contentR, y + 16, 11f,
            _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Right);

        var card = new SKRect(contentL, y + 26, contentR, y + 288);
        DrawCard(canvas, card, 16f, alpha * 0.75f);
        if (_stockState.Count == 0)
        {
            TextUtil.Draw(canvas, "正在获取行情…", card.MidX, card.MidY, 13f,
                _theme.Text2(alpha), Fonts.Ui, SKTextAlign.Center);
            return;
        }

        float rowY = card.Top + 8;
        for (int i = 0; i < _stockState.Count && rowY + 38 < card.Bottom; i++)
        {
            var q = _stockState[i];
            var row = new SKRect(card.Left + 6, rowY, card.Right - 6, rowY + 38);
            bool hover = row.Contains(_mouseX, _mouseY);
            if (hover)
            {
                using var hov = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
                canvas.DrawRoundRect(row, 9f, 9f, hov);
            }

            TextUtil.Draw(canvas, TextUtil.Ellipsize(q.Name, 180, 13f, Fonts.UiSemi),
                row.Left + 12, rowY + 17, 13f, _theme.Text(alpha), Fonts.UiSemi);
            TextUtil.Draw(canvas, q.Code.ToUpperInvariant(), row.Left + 12, rowY + 31, 10f,
                _theme.Text3(alpha), Fonts.Num);

            string price = q.Price > 0 ? q.Price.ToString("0.00") : "--";
            TextUtil.Draw(canvas, price, row.Right - 108, rowY + 18, 14f,
                _theme.Text(alpha), Fonts.NumSemi, SKTextAlign.Right);
            string pct = q.Price > 0 ? (q.Percent >= 0 ? "+" : "") + q.Percent.ToString("0.00") + "%" : "--";
            var color = q.Percent >= 0 ? _theme.Up(alpha) : _theme.Down(alpha);
            TextUtil.Draw(canvas, pct, row.Right - 12, rowY + 18, 12f, color, Fonts.Num, SKTextAlign.Right);
            TextUtil.Draw(canvas, q.Time, row.Right - 12, rowY + 31, 9.5f,
                _theme.Text3(alpha), Fonts.Num, SKTextAlign.Right);
            rowY += 40;
        }
    }

    private void DrawWeather(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        float w = contentR - contentL;
        var current = new SKRect(contentL, y, contentL + 212, y + 158);
        DrawCard(canvas, current, 16f, alpha);
        if (!_weatherState.Ok)
        {
            TextUtil.Draw(canvas, "天气", current.Left + 16, current.Top + 24, 13f,
                _theme.Text2(alpha), Fonts.UiSemi);
            TextUtil.Draw(canvas, "正在获取…", current.Left + 16, current.Top + 58, 16f,
                _theme.Text(alpha), Fonts.UiSemi);
            TextUtil.Draw(canvas, TextUtil.Ellipsize(_weatherState.Error, 180, 10.5f, Fonts.Ui),
                current.Left + 16, current.Top + 82, 10.5f, _theme.Text3(alpha), Fonts.Ui);
        }
        else
        {
            var ws = _weatherState;
            TextUtil.Draw(canvas, $"{ws.City}  ·  {ws.Description}", current.Left + 16, current.Top + 24,
                12f, _theme.Text2(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, $"{Math.Round(ws.Temperature):0}°", current.Left + 14, current.Top + 76,
                42f, _theme.Text(alpha), Fonts.NumSemi);
            DrawWeatherIcon(canvas, ws.Code, ws.IsDay, current.Right - 38, current.Top + 58, 34f, _theme.Text(alpha * 0.92f));
            TextUtil.Draw(canvas, $"最高 {Math.Round(ws.TempMax):0}°  最低 {Math.Round(ws.TempMin):0}°",
                current.Left + 16, current.Top + 100, 11f, _theme.Text2(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, $"体感 {Math.Round(ws.FeelsLike):0}°   湿度 {Math.Round(ws.Humidity):0}%",
                current.Left + 16, current.Top + 119, 10.5f, _theme.Text3(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, $"风速 {ws.Wind:0.0} km/h", current.Left + 16, current.Top + 137,
                10.5f, _theme.Text3(alpha), Fonts.Ui);
        }

        var hourly = new SKRect(current.Right + 10, y, contentR, y + 158);
        DrawCard(canvas, hourly, 16f, alpha * 0.78f);
        TextUtil.Draw(canvas, "未来 24 小时", hourly.Left + 14, hourly.Top + 22, 11.5f,
            _theme.Text2(alpha), Fonts.UiSemi);
        if (_weatherState.Ok && _weatherState.Hourly.Count > 0)
        {
            float cw = (hourly.Width - 24) / Math.Min(8, _weatherState.Hourly.Count);
            for (int i = 0; i < _weatherState.Hourly.Count && i < 8; i++)
            {
                var h = _weatherState.Hourly[i];
                float cx = hourly.Left + 12 + cw * (i + 0.5f);
                TextUtil.Draw(canvas, h.Time.ToString("HH时"), cx, hourly.Top + 52, 9.5f,
                    _theme.Text3(alpha), Fonts.Num, SKTextAlign.Center);
                DrawWeatherIcon(canvas, h.Code, h.IsDay, cx, hourly.Top + 79, 20f, _theme.Text(alpha * 0.9f));
                TextUtil.Draw(canvas, $"{Math.Round(h.Temperature):0}°", cx, hourly.Top + 108, 12f,
                    _theme.Text(alpha), Fonts.Num, SKTextAlign.Center);
                if (h.Precipitation > 0.01)
                    TextUtil.Draw(canvas, $"{Math.Round(h.Precipitation * 100):0}%", cx, hourly.Top + 126, 9f,
                        _theme.Accent(alpha), Fonts.Num, SKTextAlign.Center);
            }
        }

        var daily = new SKRect(contentL, y + 168, contentR, y + 330);
        DrawCard(canvas, daily, 16f, alpha * 0.72f);
        TextUtil.Draw(canvas, "5 天预报", daily.Left + 14, daily.Top + 22, 11.5f,
            _theme.Text2(alpha), Fonts.UiSemi);
        if (_weatherState.Ok)
        {
            float rowY = daily.Top + 34;
            for (int i = 0; i < _weatherState.Daily.Count && i < 5; i++)
            {
                var d = _weatherState.Daily[i];
                string day = i == 0 ? "今天" : d.Date.ToString("ddd");
                TextUtil.Draw(canvas, day, daily.Left + 18, rowY + 17, 12f,
                    _theme.Text(alpha), Fonts.Ui);
                DrawWeatherIcon(canvas, d.Code, true, daily.Left + 92, rowY + 12, 18f, _theme.Text2(alpha));
                TextUtil.Draw(canvas, $"{Math.Round(d.Min):0}° / {Math.Round(d.Max):0}°",
                    daily.Right - 20, rowY + 17, 11.5f, _theme.Text2(alpha), Fonts.Num, SKTextAlign.Right);
                rowY += 26;
            }
        }

        var add = new SKRect(contentR - 34, y + 4, contentR, y + 34);
        using var addBg = new SKPaint { Color = _theme.Card(alpha * 1.4f), IsAntialias = true };
        canvas.DrawRoundRect(add, 10f, 10f, addBg);
        TextUtil.Draw(canvas, "\uE710", add.MidX, add.MidY + 5, 14f, _theme.Text2(alpha), Fonts.Icon, SKTextAlign.Center);
    }

    private void DrawControls(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        float w = contentR - contentL;
        var volume = new SKRect(contentL, y, contentR, y + 92);
        DrawCard(canvas, volume, 16f, alpha);
        TextUtil.Draw(canvas, "\uE767", volume.Left + 24, volume.Top + 34, 18f,
            _theme.Text(alpha), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, _systemState.Muted ? "已静音" : "音量", volume.Left + 48, volume.Top + 31,
            13f, _theme.Text(alpha), Fonts.UiSemi);
        TextUtil.Draw(canvas, $"{(int)Math.Round(_systemState.Volume * 100)}%", volume.Right - 22, volume.Top + 31,
            12f, _theme.Text2(alpha), Fonts.Num, SKTextAlign.Right);
        _volumeRect = new SKRect(volume.Left + 48, volume.Top + 56, volume.Right - 22, volume.Top + 61);
        DrawSlider(canvas, _volumeRect, _systemState.Volume, alpha);
        _pageHits.Add((IslandHit.VolumeTrack, new SKRect(_volumeRect.Left - 8, _volumeRect.Top - 8,
            _volumeRect.Right + 8, _volumeRect.Bottom + 8), 0));

        var muteTile = new SKRect(volume.Right - 104, volume.Top + 48, volume.Right - 22, volume.Top + 82);
        _pageHits.Add((IslandHit.MuteTile, muteTile, 0));
        using (var mt = new SKPaint { Color = _theme.Card(alpha * 1.2f), IsAntialias = true })
            canvas.DrawRoundRect(muteTile, 12f, 12f, mt);
        TextUtil.Draw(canvas, _systemState.Muted ? "\uE74F" : "\uE767", muteTile.MidX, muteTile.MidY + 5, 14f,
            _theme.Text(alpha), Fonts.Icon, SKTextAlign.Center);

        var bright = new SKRect(contentL, y + 102, contentR, y + 194);
        DrawCard(canvas, bright, 16f, alpha * 0.82f);
        TextUtil.Draw(canvas, "亮度", bright.Left + 48, bright.Top + 30, 13f,
            _theme.Text(alpha), Fonts.UiSemi);
        if (_systemState.Brightness >= 0)
        {
            TextUtil.Draw(canvas, $"{_systemState.Brightness}%", bright.Right - 22, bright.Top + 30,
                12f, _theme.Text2(alpha), Fonts.Num, SKTextAlign.Right);
            _brightnessRect = new SKRect(bright.Left + 48, bright.Top + 56, bright.Right - 22, bright.Top + 61);
            DrawSlider(canvas, _brightnessRect, _systemState.Brightness / 100f, alpha);
            _pageHits.Add((IslandHit.BrightnessTrack, new SKRect(_brightnessRect.Left - 8,
                _brightnessRect.Top - 8, _brightnessRect.Right + 8, _brightnessRect.Bottom + 8), 0));
        }
        else
        {
            TextUtil.Draw(canvas, "当前显示器不支持系统亮度控制", bright.Left + 48, bright.Top + 56,
                11f, _theme.Text3(alpha), Fonts.Ui);
        }
        TextUtil.Draw(canvas, "\uE706", bright.Left + 24, bright.Top + 33, 18f,
            _theme.Text2(alpha), Fonts.Icon, SKTextAlign.Center);

        float tw = (w - 12) / 2f;
        DrawRadioTile(canvas, new SKRect(contentL, y + 204, contentL + tw, y + 284), "\uE701", "WLAN", "打开网络设置", alpha, IslandHit.WifiTile);
        DrawRadioTile(canvas, new SKRect(contentR - tw, y + 204, contentR, y + 284), "\uE702", "蓝牙", "打开蓝牙设置", alpha, IslandHit.BluetoothTile);
    }

    private void DrawRadioTile(SKCanvas canvas, SKRect rect, string glyph, string title, string sub,
        float alpha, IslandHit hit)
    {
        bool hover = rect.Contains(_mouseX, _mouseY);
        using var bg = new SKPaint
        {
            Color = hover ? _theme.Hover(alpha) : _theme.Card(alpha * 0.8f),
            IsAntialias = true
        };
        canvas.DrawRoundRect(rect, 16f, 16f, bg);
        TextUtil.Draw(canvas, glyph, rect.Left + 30, rect.MidY + 5, 19f,
            _theme.Text(alpha), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, title, rect.Left + 52, rect.MidY - 4, 13f,
            _theme.Text(alpha), Fonts.UiSemi);
        TextUtil.Draw(canvas, sub, rect.Left + 52, rect.MidY + 15, 10f,
            _theme.Text3(alpha), Fonts.Ui);
        _pageHits.Add((hit, rect, 0));
    }

    private void DrawSlider(SKCanvas canvas, SKRect rect, float ratio, float alpha)
    {
        using var track = new SKPaint { Color = _theme.Track(alpha), IsAntialias = true };
        canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, track);
        float x = rect.Left + rect.Width * Math.Clamp(ratio, 0f, 1f);
        if (x > rect.Left + 0.5f)
        {
            using var fill = new SKPaint { Color = _theme.Fill(alpha), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(rect.Left, rect.Top, x, rect.Bottom),
                rect.Height / 2, rect.Height / 2, fill);
        }
        using var knob = new SKPaint { Color = _theme.Fill(alpha), IsAntialias = true };
        canvas.DrawCircle(x, rect.MidY, 6.5f, knob);
    }

    private void DrawClipboard(SKCanvas canvas, float contentL, float contentR, float y, float alpha)
    {
        TextUtil.Draw(canvas, "最近复制", contentL + 2, y + 16, 14f, _theme.Text(alpha), Fonts.UiSemi);
        TextUtil.Draw(canvas, $"{_clipState.Count} 条", contentR, y + 16, 11f,
            _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Right);
        var card = new SKRect(contentL, y + 26, contentR, y + 292);
        DrawCard(canvas, card, 16f, alpha * 0.72f);

        if (_clipState.Count == 0)
        {
            TextUtil.Draw(canvas, "复制一段文字后会出现在这里", card.MidX, card.MidY, 12f,
                _theme.Text2(alpha), Fonts.Ui, SKTextAlign.Center);
            return;
        }

        for (int i = 0; i < _clipState.Count && i < 9; i++)
        {
            var entry = _clipState[i];
            var row = ClipboardRowRect(i);
            bool hover = i == _hoverClipboardRow;
            if (hover)
            {
                using var hov = new SKPaint { Color = _theme.Hover(alpha), IsAntialias = true };
                canvas.DrawRoundRect(row, 8f, 8f, hov);
            }
            TextUtil.Draw(canvas, $"{i + 1}", row.Left + 12, row.MidY + 4, 10.5f,
                _theme.Text3(alpha), Fonts.Num);
            TextUtil.Draw(canvas, entry.Time.ToString("HH:mm"), row.Left + 32, row.MidY + 4, 9.5f,
                _theme.Text3(alpha), Fonts.Num);
            string oneLine = entry.Text.Replace('\r', ' ').Replace('\n', ' ');
            TextUtil.Draw(canvas, TextUtil.Ellipsize(oneLine, row.Width - 94, 11.5f, Fonts.Ui),
                row.Left + 76, row.MidY + 4, 11.5f, _theme.Text(alpha), Fonts.Ui);
        }
    }

    private SKRect ClipboardRowRect(int index)
    {
        float contentL = _body.Left + Pad;
        float contentR = _body.Right - Pad;
        float top = _body.Top + Pad + NavH + PageGap + 32 + index * 28;
        return new SKRect(contentL + 8, top, contentR - 8, top + 26);
    }

    private void BuildControlHits(float contentL, float contentR, float y)
    {
        // 音量 / 亮度轨道和磁贴由 DrawControls 写入，保证绘制与命中完全一致。
    }

    private void BuildClipboardHits(float contentL, float contentR, float y)
    {
        for (int i = 0; i < _clipState.Count && i < 9; i++)
            _pageHits.Add((IslandHit.ClipboardRow, ClipboardRowRect(i), i));
    }

    private void DrawWeatherIcon(SKCanvas canvas, int code, bool isDay, float cx, float cy, float size, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, size * 0.075f),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

        bool cloudy = code is 2 or 3 or 45 or 48 or 51 or 53 or 55 or 61 or 63 or 65 or 66 or 67
            or 71 or 73 or 75 or 77 or 80 or 81 or 82 or 85 or 86 or 95 or 96 or 99;
        bool rainy = code is 51 or 53 or 55 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82;
        bool snowy = code is 71 or 73 or 75 or 77 or 85 or 86;
        bool storm = code is 95 or 96 or 99;
        bool fog = code is 45 or 48;

        if (!cloudy && !storm)
        {
            if (isDay)
            {
                canvas.DrawCircle(cx, cy, size * 0.24f, fill);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4f;
                    float x1 = cx + MathF.Cos(a) * size * 0.34f;
                    float y1 = cy + MathF.Sin(a) * size * 0.34f;
                    float x2 = cx + MathF.Cos(a) * size * 0.46f;
                    float y2 = cy + MathF.Sin(a) * size * 0.46f;
                    canvas.DrawLine(x1, y1, x2, y2, paint);
                }
            }
            else
            {
                using var moon = new SKPath { FillType = SKPathFillType.EvenOdd };
                moon.AddCircle(cx, cy, size * 0.34f);
                moon.AddCircle(cx + size * 0.16f, cy - size * 0.12f, size * 0.29f);
                canvas.DrawPath(moon, fill);
            }
            return;
        }

        // 云
        using var cloud = new SKPath();
        cloud.AddCircle(cx - size * 0.2f, cy + size * 0.04f, size * 0.22f);
        cloud.AddCircle(cx, cy - size * 0.13f, size * 0.27f);
        cloud.AddCircle(cx + size * 0.24f, cy + size * 0.05f, size * 0.2f);
        cloud.AddRoundRect(new SKRect(cx - size * 0.36f, cy + size * 0.02f,
            cx + size * 0.38f, cy + size * 0.25f), size * 0.12f, size * 0.12f);
        canvas.DrawPath(cloud, fill);

        if (rainy)
        {
            for (int i = -1; i <= 1; i++)
            {
                float x = cx + i * size * 0.2f;
                canvas.DrawLine(x, cy + size * 0.34f, x - size * 0.08f, cy + size * 0.52f, paint);
            }
        }
        else if (snowy)
        {
            for (int i = -1; i <= 1; i++)
            {
                float x = cx + i * size * 0.2f;
                float yy = cy + size * 0.42f;
                canvas.DrawLine(x - size * 0.07f, yy, x + size * 0.07f, yy, paint);
                canvas.DrawLine(x, yy - size * 0.07f, x, yy + size * 0.07f, paint);
            }
        }
        else if (storm)
        {
            using var bolt = new SKPath();
            bolt.MoveTo(cx + size * 0.03f, cy + size * 0.3f);
            bolt.LineTo(cx - size * 0.12f, cy + size * 0.58f);
            bolt.LineTo(cx + size * 0.02f, cy + size * 0.55f);
            bolt.LineTo(cx - size * 0.05f, cy + size * 0.76f);
            bolt.LineTo(cx + size * 0.2f, cy + size * 0.46f);
            bolt.LineTo(cx + size * 0.06f, cy + size * 0.47f);
            bolt.Close();
            canvas.DrawPath(bolt, fill);
        }
        else if (fog)
        {
            for (int i = 0; i < 3; i++)
                canvas.DrawLine(cx - size * 0.3f, cy + size * 0.34f + i * size * 0.12f,
                    cx + size * 0.3f, cy + size * 0.34f + i * size * 0.12f, paint);
        }
    }

    private static string MarketStatus()
    {
        DateTime now = DateTime.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return "休市";
        int hm = now.Hour * 100 + now.Minute;
        if (hm >= 930 && hm < 1130 || hm >= 1300 && hm < 1500) return "A股交易中";
        if (hm >= 1130 && hm < 1300) return "午间休市";
        return "已收盘";
    }
}
