using SkiaSharp;
using WislandSharp.Core;

namespace WislandSharp.UI;

public sealed partial class IslandRenderer
{
    private void DrawCompactSubtitle(SKCanvas canvas, SKRect r, float alpha)
    {
        string source = _subtitleState.Source;
        string translation = string.IsNullOrWhiteSpace(_subtitleState.Translation)
            ? "…"
            : _subtitleState.Translation;
        float maxW = Math.Max(20f, r.Width - 54f);
        string top = TextUtil.Tail(source, maxW, 11.5f, Fonts.Ui);
        string bottom = TextUtil.Ellipsize(translation, maxW, 14f, Fonts.UiSemi);

        TextUtil.Draw(canvas, "\uE720", r.Left + 24, r.MidY + 5, 14f,
            _theme.Text2(alpha * 0.8f), Fonts.Icon, SKTextAlign.Center);
        TextUtil.Draw(canvas, top, r.MidX + 12, r.Top + 20, 11.5f,
            _theme.Text2(alpha * 0.78f), Fonts.Ui, SKTextAlign.Center);
        TextUtil.Draw(canvas, bottom, r.MidX + 12, r.Top + 39, 14f,
            _theme.Text(alpha), Fonts.UiSemi, SKTextAlign.Center);
    }

    private void DrawTranslationPage(SKCanvas canvas, float contentL, float contentR,
        float y, float alpha)
    {
        TextUtil.Draw(canvas, "翻译", contentL + 2, y + 16, 14f,
            _theme.Text(alpha), Fonts.UiSemi);
        string status = _translationState.Busy ? "翻译中…" :
            !string.IsNullOrWhiteSpace(_translationState.Error) ? _translationState.Error :
            !string.IsNullOrWhiteSpace(_translationState.Result)
                ? (_translationState.Provider.Length > 0 ? _translationState.Provider : "翻译完成")
                : "自动识别语言 → 简体中文";
        SKColor statusColor = !string.IsNullOrWhiteSpace(_translationState.Error)
            ? new SKColor(255, 100, 100, (byte)(255 * alpha))
            : _theme.Text3(alpha);
        TextUtil.Draw(canvas, TextUtil.Ellipsize(status, 330f, 10.5f, Fonts.Ui),
            contentR, y + 16, 10.5f, statusColor, Fonts.Ui, SKTextAlign.Right);

        var clipBtn = new SKRect(contentL, y + 28, contentL + 128, y + 58);
        var openBtn = new SKRect(contentL + 138, y + 28, contentL + 278, y + 58);
        DrawPill(canvas, clipBtn, "翻译剪贴板", alpha, IslandHit.TranslateClipboard);
        DrawPill(canvas, openBtn, "打开翻译窗口", alpha, IslandHit.TranslateOpen);

        string source = _translationState.Source;
        string result = _translationState.Result;
        var srcCard = new SKRect(contentL, y + 66, contentR, y + 158);
        DrawCard(canvas, srcCard, 16f, alpha * 0.78f);
        TextUtil.Draw(canvas, "原文", srcCard.Left + 16, srcCard.Top + 21, 10.5f,
            _theme.Text3(alpha), Fonts.UiSemi);
        if (string.IsNullOrWhiteSpace(source))
        {
            TextUtil.Draw(canvas, "用 Ctrl+Alt+J 或点击「翻译剪贴板」开始",
                srcCard.Left + 16, srcCard.Top + 55, 12f,
                _theme.Text2(alpha), Fonts.Ui);
        }
        else
        {
            DrawWrappedText(canvas, source,
                new SKRect(srcCard.Left + 16, srcCard.Top + 31,
                    srcCard.Right - 16, srcCard.Bottom - 8),
                12f, _theme.Text(alpha), Fonts.Ui, 3, 19f);
        }

        var outCard = new SKRect(contentL, y + 166, contentR, y + 294);
        DrawCard(canvas, outCard, 16f, alpha * 0.86f);
        TextUtil.Draw(canvas, "译文", outCard.Left + 16, outCard.Top + 21, 10.5f,
            _theme.Text3(alpha), Fonts.UiSemi);
        if (!string.IsNullOrWhiteSpace(_translationState.Error))
        {
            TextUtil.Draw(canvas, TextUtil.Ellipsize(_translationState.Error, outCard.Width - 32,
                    12f, Fonts.Ui),
                outCard.Left + 16, outCard.Top + 53, 12f,
                new SKColor(255, 105, 105, (byte)(255 * alpha)), Fonts.Ui);
        }
        else if (_translationState.Busy)
        {
            TextUtil.Draw(canvas, "翻译中…", outCard.Left + 16, outCard.Top + 53, 12f,
                _theme.Text2(alpha), Fonts.Ui);
        }
        else if (string.IsNullOrWhiteSpace(result))
        {
            TextUtil.Draw(canvas, "译文会显示在这里", outCard.Left + 16, outCard.Top + 53, 12f,
                _theme.Text3(alpha), Fonts.Ui);
        }
        else
        {
            DrawWrappedText(canvas, result,
                new SKRect(outCard.Left + 16, outCard.Top + 31,
                    outCard.Right - 16, outCard.Bottom - 8),
                14f, _theme.Text(alpha), Fonts.UiSemi, 4, 22f);
        }

        bool hasResult = !string.IsNullOrWhiteSpace(result);
        DrawPill(canvas, new SKRect(contentL, y + 300, contentL + 104, y + 328),
            "复制译文", alpha, IslandHit.TranslateCopy, hasResult);
        DrawPill(canvas, new SKRect(contentL + 114, y + 300, contentL + 218, y + 328),
            "朗读译文", alpha, IslandHit.TranslateSpeak, hasResult);
    }

    private void DrawSubtitlesPage(SKCanvas canvas, float contentL, float contentR,
        float y, float alpha)
    {
        TextUtil.Draw(canvas, "实时字幕", contentL + 2, y + 16, 14f,
            _theme.Text(alpha), Fonts.UiSemi);
        string status = _subtitleState.Starting ? "准备中…" :
            !string.IsNullOrWhiteSpace(_subtitleState.Error) ? _subtitleState.Error :
            _subtitleState.Status;
        if (string.IsNullOrWhiteSpace(status))
            status = _subtitles.HasKey ? "中/英识别 · 自动翻译" : "未配置 MiMo API Key";
        SKColor statusColor = !string.IsNullOrWhiteSpace(_subtitleState.Error)
            ? new SKColor(255, 100, 100, (byte)(255 * alpha))
            : _theme.Text3(alpha);
        TextUtil.Draw(canvas, TextUtil.Ellipsize(status, 360f, 10.5f, Fonts.Ui),
            contentR, y + 16, 10.5f, statusColor, Fonts.Ui, SKTextAlign.Right);

        string action = _subtitleState.Starting ? "准备中…" :
            _subtitleState.Running ? "停止聆听" : "开始聆听";
        DrawPill(canvas, new SKRect(contentL, y + 28, contentL + 118, y + 58),
            action, alpha, IslandHit.SubtitleToggle, !_subtitleState.Starting);
        DrawPill(canvas, new SKRect(contentL + 128, y + 28, contentL + 200, y + 58),
            "清空", alpha, IslandHit.SubtitleClear,
            _subtitleState.History.Count > 0 || _subtitleState.Source.Length > 0);

        var current = new SKRect(contentL, y + 66, contentR, y + 174);
        DrawCard(canvas, current, 16f, alpha * 0.80f);
        TextUtil.Draw(canvas, "最近一句", current.Left + 16, current.Top + 21, 10.5f,
            _theme.Text3(alpha), Fonts.UiSemi);
        if (string.IsNullOrWhiteSpace(_subtitleState.Source))
        {
            TextUtil.Draw(canvas, _subtitles.HasKey
                    ? "播放视频或音频后点「开始聆听」"
                    : "请先在设置 → 翻译字幕中填写 MiMo API Key",
                current.Left + 16, current.Top + 58, 12f,
                _theme.Text2(alpha), Fonts.Ui);
        }
        else
        {
            string source = TextUtil.Ellipsize(_subtitleState.Source, current.Width - 32, 12f, Fonts.Ui);
            string translation = string.IsNullOrWhiteSpace(_subtitleState.Translation)
                ? "…"
                : TextUtil.Ellipsize(_subtitleState.Translation, current.Width - 32, 15f, Fonts.UiSemi);
            TextUtil.Draw(canvas, source, current.Left + 16, current.Top + 48, 12f,
                _theme.Text2(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, translation, current.Left + 16, current.Top + 79, 15f,
                _theme.Text(alpha), Fonts.UiSemi);
        }

        var history = new SKRect(contentL, y + 182, contentR, y + 320);
        DrawCard(canvas, history, 16f, alpha * 0.68f);
        TextUtil.Draw(canvas, "历史", history.Left + 16, history.Top + 21, 10.5f,
            _theme.Text3(alpha), Fonts.UiSemi);
        var lines = _subtitleState.History.TakeLast(4).ToArray();
        if (lines.Length == 0)
        {
            TextUtil.Draw(canvas, "还没有字幕", history.MidX, history.MidY, 12f,
                _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Center);
            return;
        }
        float rowY = history.Top + 32;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            string src = TextUtil.Ellipsize(line.Source, history.Width - 32, 10.5f, Fonts.Ui);
            string zh = string.IsNullOrWhiteSpace(line.Translation)
                ? "…"
                : TextUtil.Ellipsize(line.Translation, history.Width - 32, 11.5f, Fonts.UiSemi);
            TextUtil.Draw(canvas, src, history.Left + 16, rowY + 11, 10.5f,
                _theme.Text3(alpha), Fonts.Ui);
            TextUtil.Draw(canvas, zh, history.Left + 16, rowY + 27, 11.5f,
                _theme.Text(alpha), Fonts.UiSemi);
            rowY += 34;
        }
    }

    private void DrawPill(SKCanvas canvas, SKRect rect, string text, float alpha,
        IslandHit hit, bool enabled = true)
    {
        bool hover = enabled && rect.Contains(_mouseX, _mouseY);
        using var bg = new SKPaint
        {
            Color = hover ? _theme.Hover(alpha) : _theme.Card(alpha * (enabled ? 1.25f : 0.55f)),
            IsAntialias = true
        };
        canvas.DrawRoundRect(rect, rect.Height / 2f, rect.Height / 2f, bg);
        TextUtil.Draw(canvas, text, rect.MidX, rect.MidY + 4.2f, 11.5f,
            enabled ? _theme.Text(alpha) : _theme.Text3(alpha), Fonts.Ui, SKTextAlign.Center);
        if (enabled) _pageHits.Add((hit, rect, 0));
    }

    private static void DrawWrappedText(SKCanvas canvas, string text, SKRect rect,
        float size, SKColor color, SKTypeface typeface, int maxLines, float lineHeight)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width < 4) return;
        string normalized = text.Replace("\r", "");
        var lines = new List<string>();
        foreach (string paragraph in normalized.Split('\n'))
        {
            string remaining = paragraph.Trim();
            while (remaining.Length > 0 && lines.Count < maxLines)
            {
                if (TextUtil.Measure(remaining, size, typeface) <= rect.Width)
                {
                    lines.Add(remaining);
                    remaining = "";
                    break;
                }
                int lo = 1, hi = remaining.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (TextUtil.Measure(remaining[..mid], size, typeface) <= rect.Width)
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                lines.Add(remaining[..Math.Max(1, lo)]);
                remaining = remaining[Math.Max(1, lo)..].TrimStart();
                if (lines.Count == maxLines && remaining.Length > 0)
                {
                    string last = lines[^1];
                    while (last.Length > 1 &&
                           TextUtil.Measure(last + "…", size, typeface) > rect.Width)
                        last = last[..^1];
                    lines[^1] = last.TrimEnd() + "…";
                }
            }
            if (lines.Count >= maxLines) break;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            TextUtil.Draw(canvas, lines[i], rect.Left, rect.Top + (i + 1) * lineHeight,
                size, color, typeface);
        }
    }
}
