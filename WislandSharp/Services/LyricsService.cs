using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class LyricLine
{
    public double Time { get; init; }
    public string Text { get; init; } = "";
    public string Translation { get; init; } = "";
}

public sealed class SongLyrics
{
    public IReadOnlyList<double> Times { get; init; } = Array.Empty<double>();
    public IReadOnlyList<LyricLine> Lines { get; init; } = Array.Empty<LyricLine>();
    public float MaxLineWidth { get; init; }

    public LyricLine? At(double position)
    {
        if (Lines.Count == 0) return null;
        int lo = 0, hi = Times.Count - 1, found = -1;
        double t = position + 0.15;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Times[mid] <= t) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 ? Lines[found] : null;
    }
}

/// <summary>
/// 网易云公开歌词接口：先按「歌名 + 歌手」搜索，再用时长做二次校验，
/// 取 LRC 与翻译。全部在后台线程完成，界面只读缓存。
/// </summary>
public sealed class LyricsService : IDisposable
{
    private const string Api = "https://music.163.com/api";
    private static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex Credit = new(@"^\s*[^\s:：]{1,12}\s*[:：]", RegexOptions.Compiled);
    private static readonly Regex Bracket = new(@"\s*[\(（\[【].*?[\)）\]】]\s*", RegexOptions.Compiled);

    private readonly WislandConfig _config;
    private readonly HttpClient _http;
    private readonly MediaService _media;
    private readonly Dictionary<string, SongLyrics?> _cache = new();
    private readonly HashSet<string> _pending = new();
    private readonly object _lock = new();
    private string _lastKey = "";
    private bool _disposed;

    public event Action<string, SongLyrics?>? Changed;

    public LyricsService(WislandConfig config, MediaService media)
    {
        _config = config;
        _media = media;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        _media.Changed += OnMediaChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _media.Changed -= OnMediaChanged;
        _http.Dispose();
    }

    private void OnMediaChanged(MediaSnapshot state)
    {
        if (_disposed || !_config.Media.Lyrics) return;
        if (string.IsNullOrWhiteSpace(state.Title) || !state.HasSession)
        {
            _lastKey = "";
            return;
        }
        if (state.DurationSeconds < 30 || state.DurationSeconds > 15 * 60)
        {
            _lastKey = "";
            return;
        }

        string key = $"{state.Title}|{state.Artist}|{Math.Round(state.DurationSeconds)}";
        if (key == _lastKey) return;
        _lastKey = key;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                Changed?.Invoke(key, cached);
                return;
            }
            if (!_pending.Add(key)) return;
        }

        _ = Task.Run(async () =>
        {
            SongLyrics? result = null;
            try { result = await FetchAsync(state); } catch { }
            lock (_lock)
            {
                _pending.Remove(key);
                _cache[key] = result;
                if (_cache.Count > 80)
                    _cache.Remove(_cache.Keys.First());
            }
            try { Changed?.Invoke(key, result); } catch { }
        });
    }

    private async Task<SongLyrics?> FetchAsync(MediaSnapshot media)
    {
        string id = await SearchAsync(media);
        if (string.IsNullOrWhiteSpace(id)) return null;
        string url = $"{Api}/song/lyric?id={id}&lv=1&tv=-1";
        using JsonDocument doc = JsonDocument.Parse(await _http.GetStringAsync(url));
        if (doc.RootElement.TryGetProperty("nolyric", out var nolyric) && nolyric.ValueKind == JsonValueKind.True)
            return null;
        if (doc.RootElement.TryGetProperty("uncollected", out var uncollected) && uncollected.ValueKind == JsonValueKind.True)
            return null;

        string lrc = doc.RootElement.TryGetProperty("lrc", out var l) &&
                     l.TryGetProperty("lyric", out var lt) ? lt.GetString() ?? "" : "";
        string trans = doc.RootElement.TryGetProperty("tlyric", out var tl) &&
                       tl.TryGetProperty("lyric", out var tlt) ? tlt.GetString() ?? "" : "";
        var lines = ParseLrc(lrc);
        if (lines.Count == 0) return null;
        var transMap = ParseLrc(trans).GroupBy(x => Math.Round(x.Time, 1)).ToDictionary(g => g.Key, g => g.First().Text);
        var output = lines.Select(x => new LyricLine
        {
            Time = x.Time,
            Text = x.Text,
            Translation = transMap.TryGetValue(Math.Round(x.Time, 1), out string? tr) ? tr : ""
        }).ToList();
        float maxW = output.Count == 0 ? 0 : output.Max(x => TextUtilMeasure(x.Text));
        return new SongLyrics
        {
            Times = output.Select(x => x.Time).ToList(),
            Lines = output,
            MaxLineWidth = maxW
        };
    }

    private static float TextUtilMeasure(string text)
    {
        using var paint = new SkiaSharp.SKPaint
        {
            Typeface = SkiaSharp.SKTypeface.FromFamilyName("Microsoft YaHei UI")
                ?? SkiaSharp.SKTypeface.Default,
            TextSize = 13f,
            IsAntialias = true
        };
        return paint.MeasureText(text ?? "");
    }

    private async Task<string> SearchAsync(MediaSnapshot media)
    {
        string title = Bracket.Replace(media.Title, " ").Trim();
        if (string.IsNullOrWhiteSpace(title)) title = media.Title;
        string query = $"{title} {media.Artist}".Trim();
        string url = $"{Api}/search/get/web?s={Uri.EscapeDataString(query)}&type=1&limit=8";
        using JsonDocument doc = JsonDocument.Parse(await _http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
            return "";

        string lower = title.ToLowerInvariant();
        string bestId = "";
        double bestScore = double.MaxValue;
        double bestDiff = double.MaxValue;
        foreach (JsonElement song in songs.EnumerateArray())
        {
            string name = song.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            double duration = song.TryGetProperty("duration", out var d) ? d.GetDouble() / 1000.0 : 0;
            double diff = Math.Abs(duration - media.DurationSeconds);
            bool same = name.ToLowerInvariant().Trim() == lower;
            double score = (same ? 0 : 1000) + diff;
            if (score >= bestScore) continue;
            bestScore = score;
            bestDiff = diff;
            bestId = song.TryGetProperty("id", out var id) ? id.ToString() : "";
        }
        return bestDiff > 8 ? "" : bestId;
    }

    private static List<(double Time, string Text)> ParseLrc(string lrc)
    {
        var output = new List<(double, string)>();
        foreach (string raw in (lrc ?? "").Split('\n'))
        {
            var stamps = Stamp.Matches(raw);
            if (stamps.Count == 0) continue;
            string body = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match m in stamps)
            {
                int min = int.Parse(m.Groups[1].Value);
                int sec = int.Parse(m.Groups[2].Value);
                string frac = m.Groups[3].Success ? m.Groups[3].Value : "0";
                double t = min * 60 + sec + int.Parse(frac) / Math.Pow(10, frac.Length);
                output.Add((t, body));
            }
        }
        output.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        int i = 0;
        while (i < output.Count &&
               (string.IsNullOrWhiteSpace(output[i].Item2) ||
                (output[i].Item1 < 40 && Credit.IsMatch(output[i].Item2))))
            i++;
        return output.Skip(i).ToList();
    }
}
