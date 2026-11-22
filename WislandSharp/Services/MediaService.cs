using SkiaSharp;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WislandSharp.Core;

namespace WislandSharp.Services;

/// <summary>
/// 轻量 SMTC 轮询：0.7 秒一次，不依赖 Python / Qt。
/// 读取标题、歌手、播放状态、进度和封面；控制命令直接交给系统媒体会话。
/// </summary>
public sealed class MediaService : IDisposable
{
    private readonly WislandConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaSnapshot _snapshot = new();

    public event Action<MediaSnapshot>? Changed;

    public MediaSnapshot Snapshot => _snapshot;

    public MediaService(WislandConfig config)
    {
        _config = config;
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland media" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _cts.Cancel();
        try { _thread.Join(1200); } catch { }
        _cts.Dispose();
        _snapshot.Cover?.Dispose();
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                PollAsync().GetAwaiter().GetResult();
            }
            catch
            {
                _manager = null;
                _session = null;
            }

            for (int i = 0; i < 7 && !_cts.IsCancellationRequested; i++)
                Thread.Sleep(100);
        }
    }

    private async Task PollAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = _manager.GetSessions();
        var chosen = Choose(sessions);
        _session = chosen;

        if (chosen == null)
        {
            Publish(new MediaSnapshot());
            return;
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await chosen.TryGetMediaPropertiesAsync(); } catch { }

        var playback = chosen.GetPlaybackInfo();
        bool playing = playback?.PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        bool canPrev = playback?.Controls?.IsPreviousEnabled == true;
        bool canNext = playback?.Controls?.IsNextEnabled == true;

        double pos = 0, dur = 0;
        try
        {
            var timeline = chosen.GetTimelineProperties();
            if (timeline != null)
            {
                double start = timeline.StartTime.TotalSeconds;
                dur = Math.Max(0, timeline.EndTime.TotalSeconds - start);
                pos = Math.Max(0, timeline.Position.TotalSeconds - start);
                if (playing)
                {
                    double age = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
                    if (age > 0 && age < 6 * 3600) pos += age;
                }
                if (dur > 0) pos = Math.Min(pos, dur);
            }
        }
        catch { }

        string appId = chosen.SourceAppUserModelId ?? "";
        string title = props?.Title ?? "";
        string artist = props?.Artist ?? "";
        string album = props?.AlbumTitle ?? "";
        SKBitmap? cover = null;
        if (_snapshot.Cover == null ||
            !string.Equals(_snapshot.Title, title, StringComparison.Ordinal) ||
            !string.Equals(_snapshot.Artist, artist, StringComparison.Ordinal))
        {
            cover = await ReadCoverAsync(props);
        }

        var next = new MediaSnapshot
        {
            HasSession = true,
            AppId = appId,
            AppName = FriendlyName(appId),
            Title = title,
            Artist = artist,
            Album = album,
            Playing = playing,
            PositionSeconds = pos,
            DurationSeconds = dur,
            StampTicks = Environment.TickCount64,
            CanPrevious = canPrev,
            CanNext = canNext,
            Cover = cover ?? _snapshot.Cover
        };
        Publish(next);
    }

    private GlobalSystemMediaTransportControlsSession? Choose(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        if (sessions.Count == 0) return null;
        var priority = _config.Media.Priority;
        int Rank(GlobalSystemMediaTransportControlsSession s)
        {
            string id = (s.SourceAppUserModelId ?? "").ToLowerInvariant();
            for (int i = 0; i < priority.Count; i++)
                if (id.Contains(priority[i], StringComparison.OrdinalIgnoreCase)) return i;
            return priority.Count;
        }

        return sessions
            .OrderBy(s =>
            {
                try
                {
                    return s.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? 0 : 1;
                }
                catch { return 1; }
            })
            .ThenBy(Rank)
            .FirstOrDefault();
    }

    private void Publish(MediaSnapshot next)
    {
        var previous = _snapshot;
        _snapshot = next;
        if (previous.Cover != null && !ReferenceEquals(previous.Cover, next.Cover))
            previous.Cover.Dispose();
        try { Changed?.Invoke(next); } catch { }
    }

    private static async Task<SKBitmap?> ReadCoverAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        try
        {
            if (props?.Thumbnail == null) return null;
            using IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync();
            uint size = (uint)Math.Min(stream.Size, 8 * 1024 * 1024);
            if (size == 0) return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            byte[] bytes = new byte[size];
            reader.ReadBytes(bytes);
            return SKBitmap.Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string FriendlyName(string appId)
    {
        string low = appId.ToLowerInvariant();
        (string Key, string Name)[] known =
        {
            ("cloudmusic", "网易云音乐"), ("bilibili", "哔哩哔哩"), ("chrome", "Chrome"),
            ("msedge", "Edge"), ("firefox", "Firefox"), ("spotify", "Spotify"),
            ("qqmusic", "QQ音乐"), ("kugou", "酷狗音乐")
        };
        foreach (var item in known)
            if (low.Contains(item.Key, StringComparison.Ordinal)) return item.Name;
        string s = appId;
        foreach (char sep in new[] { '!', '\\', '/' })
        {
            int i = s.LastIndexOf(sep);
            if (i >= 0) s = s[(i + 1)..];
        }
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return string.IsNullOrWhiteSpace(s) ? "媒体" : s;
    }

    public void TogglePlay()
    {
        var session = _session;
        if (session == null) return;
        _ = Task.Run(async () =>
        {
            try { await session.TryTogglePlayPauseAsync(); } catch { }
        });
    }

    public void Next()
    {
        var session = _session;
        if (session == null) return;
        _ = Task.Run(async () =>
        {
            try { await session.TrySkipNextAsync(); } catch { }
        });
    }

    public void Previous()
    {
        var session = _session;
        if (session == null) return;
        _ = Task.Run(async () =>
        {
            try { await session.TrySkipPreviousAsync(); } catch { }
        });
    }

    public void Seek(double seconds)
    {
        var session = _session;
        if (session == null) return;
        long ticks = (long)(Math.Max(0, seconds) * 10_000_000);
        _ = Task.Run(async () =>
        {
            try { await session.TryChangePlaybackPositionAsync(ticks); } catch { }
        });
    }
}
