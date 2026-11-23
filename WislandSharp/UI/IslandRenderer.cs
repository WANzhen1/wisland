using System.Diagnostics;
using SkiaSharp;
using WislandSharp.Core;
using WislandSharp.Services;

namespace WislandSharp.UI;

public sealed partial class IslandRenderer
{
    public const float WinW = 700f;
    public const float WinH = 500f;
    private const float ExpW = 640f;
    private const float Pad = 12f;
    private const float NavH = 36f;
    private const float PageGap = 8f;
    private const float Fillet = 13f;

    private readonly object _lock = new();
    private readonly WislandConfig _config;
    private readonly IslandActions _actions;
    private readonly MediaService _media;
    private readonly WeatherService _weather;
    private readonly StockService _stocks;
    private readonly SystemService _system;
    private readonly ClipboardService _clipboard;
    private readonly LyricsService _lyricsService;
    private readonly NotificationService _notifications;
    private readonly ThemeEngine _theme;
    private readonly PetFace _pet = new();
    private Backdrop? _backdrop;
    private SKBitmap? _mediaCover;
    private SongLyrics? _lyrics;
    private float _lyricWidth;
    private string _pendingLyricsKey = "";
    private SongLyrics? _pendingLyrics;

    private MediaSnapshot _mediaState = new();
    private WeatherSnapshot _weatherState = new();
    private IReadOnlyList<StockQuote> _stockState = Array.Empty<StockQuote>();
    private SystemSnapshot _systemState = new();
    private IReadOnlyList<ClipboardEntry> _clipState = Array.Empty<ClipboardEntry>();
    private NotificationSnapshot _notificationState = new();
    private long _notificationVersion;

    private readonly Spring _expand = new(0);
    private readonly Spring _pageHeight = new(252);
    private readonly Spring _compactW = new(214);
    private readonly Spring _compactH = new(34);
    private Spring _volumeGlow = new(0);

    private bool _expanded;
    private bool _hover;
    private bool _pinned;
    private bool _passthrough;
    private bool _longOff;
    private long _hoverSince;
    private long _leaveSince;
    private long _lastClick;
    private float _lastClickX, _lastClickY;
    private float _mouseX, _mouseY;
    private float _peak;
    private float _dpiScale = 1f;

    private string _view;
    private string _previousView = "clock";
    private long _lastPlayingAt;
    private long _viewChangedAt;
    private long _mediaStoppedAt;

    private int _navIndex;
    private int _hoverNav = -1;
    private int _hoverClipboardRow = -1;
    private bool _draggingVolume;
    private bool _draggingBrightness;
    private float _volumeUi;
    private float _brightnessUi = -1f;
    private long _volumeUiAt;
    private long _brightnessUiAt;
    private long _lastVolumeApplyAt;

    private SKPath _path = new();
    private SKRect _body;
    private readonly NavHit[] _navHits =
    {
        new(), new(), new(), new(), new(), new()
    };
    private readonly List<(IslandHit Hit, SKRect Rect, int Index)> _pageHits = new();
    private SKRect _compactPlayRect;
    private SKRect _mediaProgressRect;
    private SKRect _volumeRect;
    private SKRect _brightnessRect;

    private string _activityTitle = "";
    private string _activitySub = "";
    private long _activityUntil;
    private long _activityStart;
    private SKColor _activityColor;
    private string _activityGlyph = "\uE946";

    private readonly Dictionary<string, SKBitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _navIcons = { "\uE80F", "\uE8BD", "\uEAFC", "\uE706", "\uE9E9", "\uE77F" };
    private readonly string[] _navLabels = { "主页", "消息", "股票", "天气", "控制", "剪贴板" };

    private sealed class NavHit
    {
        public SKRect Rect;
        public IslandHit Hit;
        public string Label = "";
    }

    public IslandRenderer(WislandConfig config, IslandActions actions, MediaService media,
        WeatherService weather, StockService stocks, SystemService system, ClipboardService clipboard,
        LyricsService lyricsService, NotificationService notifications)
    {
        _config = config;
        _actions = actions;
        _media = media;
        _weather = weather;
        _stocks = stocks;
        _system = system;
        _clipboard = clipboard;
        _lyricsService = lyricsService;
        _notifications = notifications;
        _theme = new ThemeEngine(config.Island.Theme);
        _view = string.IsNullOrWhiteSpace(config.Island.CompactView) ? "clock" : config.Island.CompactView;
        if (!config.Island.ShowIdlePet && _view == "pet") _view = "clock";
        _pinned = string.Equals(config.Island.ExpandMode, "always", StringComparison.OrdinalIgnoreCase);
        _passthrough = config.Island.Passthrough;
        _viewChangedAt = Environment.TickCount64;

        _media.Changed += SetMedia;
        _weather.Changed += SetWeather;
        _stocks.Changed += SetStocks;
        _system.Changed += SetSystem;
        _clipboard.Changed += SetClipboard;
        _lyricsService.Changed += SetLyrics;
        _notifications.Changed += SetNotifications;
        _notifications.NewNotification += OnNewNotification;
        _weatherState = _weather.Snapshot;
        _stockState = _stocks.Snapshot;
        _systemState = _system.Snapshot;
        _volumeUi = _systemState.Volume;
        _brightnessUi = _systemState.Brightness >= 0 ? _systemState.Brightness / 100f : -1f;
        long now = Environment.TickCount64;
        _volumeUiAt = now - 1000;
        _brightnessUiAt = now - 1000;
        _clipState = _clipboard.Items;
        _notificationState = _notifications.Snapshot;
    }

    public void SetBackdrop(Backdrop backdrop) => _backdrop = backdrop;
    public ThemeEngine Theme => _theme;
    public bool Expanded { get { lock (_lock) return _expanded; } }
    public bool Pinned { get { lock (_lock) return _pinned; } }
    public bool Passthrough { get { lock (_lock) return _passthrough; } }
    public int NavIndex { get { lock (_lock) return _navIndex; } }
    public string ThemeMode => _theme.Mode;
    public bool NeedsFastFrames
    {
        get
        {
            lock (_lock)
            {
                return !_expand.Rest || !_compactW.Rest || !_compactH.Rest || !_pageHeight.Rest ||
                       _activityUntil > Environment.TickCount64 ||
                       _draggingVolume || _draggingBrightness;
            }
        }
    }

    public bool NeedsMediumFrames
    {
        get
        {
            lock (_lock)
            {
                return _hover ||
                       (_mediaState.HasSession && (_mediaState.Playing || _view == "music")) ||
                       _view == "pet";
            }
        }
    }

    public bool NeedsSlowFrames
    {
        get
        {
            lock (_lock) return _expanded;
        }
    }

    public void SetDpiScale(float scale)
    {
        lock (_lock) _dpiScale = Math.Clamp(scale, 0.75f, 3f);
    }

    public void SetMedia(MediaSnapshot state)
    {
        lock (_lock)
        {
            if (_lyrics != null &&
                (!string.Equals(_mediaState.Title, state.Title, StringComparison.Ordinal) ||
                 !string.Equals(_mediaState.Artist, state.Artist, StringComparison.Ordinal)))
            {
                _lyrics = null;
                _lyricWidth = 0;
            }
            SKBitmap? clone = null;
            try { clone = state.Cover?.Copy(); } catch { }
            _mediaCover?.Dispose();
            _mediaCover = clone;
            _mediaState = state;
            ApplyLyricsLocked();
            bool playing = state.HasSession && state.Playing && !string.IsNullOrWhiteSpace(state.Title);
            if (playing)
            {
                _lastPlayingAt = Environment.TickCount64;
                _mediaStoppedAt = 0;
                if (_config.Media.AutoShow && _view != "music")
                {
                    _previousView = _view;
                    _view = "music";
                    _viewChangedAt = Environment.TickCount64;
                }
            }
            else if (_mediaStoppedAt == 0)
            {
                _mediaStoppedAt = Environment.TickCount64;
            }
        }
    }

    public void SetWeather(WeatherSnapshot state) { lock (_lock) _weatherState = state; }
    public void SetStocks(IReadOnlyList<StockQuote> state) { lock (_lock) _stockState = state; }
    public void SetSystem(SystemSnapshot state)
    {
        lock (_lock)
        {
            _systemState = state;
            long now = Environment.TickCount64;
            if (!_draggingVolume && now - _volumeUiAt > 250)
                _volumeUi = state.Volume;
            if (!_draggingBrightness && now - _brightnessUiAt > 250)
                _brightnessUi = state.Brightness >= 0 ? state.Brightness / 100f : -1f;
        }
    }
    public void SetClipboard(IReadOnlyList<ClipboardEntry> state) { lock (_lock) _clipState = state; }
    public void SetNotifications(NotificationSnapshot state)
    {
        lock (_lock)
        {
            if (state.Version < _notificationVersion) return;
            _notificationVersion = state.Version;
            _notificationState = state;
        }
    }

    private void OnNewNotification(NotificationItem item)
    {
        lock (_lock)
        {
            _notificationState = _notifications.Snapshot;
            _notificationVersion = _notificationState.Version;
        }
        if (item.Kind is "qq" or "wechat" or "mail")
        {
            string sub = string.IsNullOrWhiteSpace(item.Sender) ? item.Preview : item.Sender;
            ShowActivity($"{item.AppName} 新消息", sub, "\uE8BD",
                item.Kind == "qq" ? new SKColor(18, 183, 245) :
                item.Kind == "wechat" ? new SKColor(7, 193, 96) : new SKColor(255, 159, 10),
                seconds: 3.2);
        }
    }

    private void SetLyrics(string key, SongLyrics? lyrics)
    {
        lock (_lock)
        {
            _pendingLyricsKey = key;
            _pendingLyrics = lyrics;
            ApplyLyricsLocked();
        }
    }

    private void ApplyLyricsLocked()
    {
        string current = $"{_mediaState.Title}|{_mediaState.Artist}|{Math.Round(_mediaState.DurationSeconds)}";
        if (!string.Equals(_pendingLyricsKey, current, StringComparison.Ordinal)) return;
        _lyrics = _pendingLyrics;
        _lyricWidth = _lyrics == null ? 0 : Math.Clamp(_lyrics.MaxLineWidth + 170, 360, 760);
    }

    public void ShowActivity(string title, string sub = "", string glyph = "\uE946", SKColor? color = null, double seconds = 2.4)
    {
        lock (_lock)
        {
            _activityTitle = title;
            _activitySub = sub;
            _activityGlyph = glyph;
            _activityColor = color ?? SKColors.Transparent;
            _activityStart = Environment.TickCount64;
            _activityUntil = _activityStart + (long)(seconds * 1000);
        }
    }

    public void SetTheme(string mode)
    {
        lock (_lock)
        {
            _theme.SetMode(mode);
            _theme.FallbackToSystemTheme();
            _config.Island.Theme = mode;
            try { _config.Save(); } catch { }
        }
    }

    public void SetPinned(bool pinned)
    {
        lock (_lock)
        {
            _pinned = pinned;
            _config.Island.ExpandMode = pinned ? "always" : "hover";
            try { _config.Save(); } catch { }
            _actions.SetPinned?.Invoke(pinned);
        }
    }

    public void SetPassthrough(bool on)
    {
        lock (_lock)
        {
            _passthrough = on;
            _config.Island.Passthrough = on;
            try { _config.Save(); } catch { }
            _actions.SetPassthrough?.Invoke(on);
        }
    }

    public void ToggleExpanded()
    {
        lock (_lock)
        {
            _expanded = !_expanded;
            if (_expanded) _leaveSince = 0;
        }
    }

    public void ShowPage(int index)
    {
        lock (_lock)
        {
            _navIndex = Math.Clamp(index, 0, _navIcons.Length - 1);
            _expanded = true;
            _leaveSince = 0;
        }
    }

    public void ReloadConfig(WislandConfig config)
    {
        lock (_lock)
        {
            _config.Reload(config);
            _theme.SetMode(config.Island.Theme);
            _theme.FallbackToSystemTheme();
            _view = string.IsNullOrWhiteSpace(config.Island.CompactView) ? "clock" : config.Island.CompactView;
            if (!config.Island.ShowIdlePet && _view == "pet") _view = "clock";
            _pinned = string.Equals(config.Island.ExpandMode, "always", StringComparison.OrdinalIgnoreCase);
            _passthrough = config.Island.Passthrough;
            _actions.SetPassthrough?.Invoke(_passthrough);
        }
    }

    public bool HitTestShape(float x, float y)
    {
        lock (_lock)
        {
            if (_body.Width < 1f || _body.Height < 1f) return false;
            if (_body.Contains(x, y)) return true;
            if (x >= _body.Left - Fillet && x <= _body.Right + Fillet && y >= 0 && y <= _body.Top + 2f)
                return true;
            return false;
        }
    }

    public void MouseMove(float x, float y)
    {
        lock (_lock)
        {
            _mouseX = x;
            _mouseY = y;
            _hover = true;
            if (_hoverSince == 0) _hoverSince = Environment.TickCount64;
            _leaveSince = 0;

            _hoverNav = -1;
            for (int i = 0; i < _navHits.Length; i++)
                if (_navHits[i].Rect.Contains(x, y)) _hoverNav = i;

            _hoverClipboardRow = -1;
            if (_expanded && _navIndex == 5)
            {
                for (int i = 0; i < _clipState.Count; i++)
                {
                    var r = ClipboardRowRect(i);
                    if (r.Contains(x, y)) { _hoverClipboardRow = i; break; }
                }
            }

            if (_draggingVolume) SetVolumeLocked(x);
            else if (_draggingBrightness) SetBrightnessLocked(x);
        }
    }

    public void MouseLeave()
    {
        lock (_lock)
        {
            _hover = false;
            _hoverSince = 0;
            _leaveSince = Environment.TickCount64;
            _hoverNav = -1;
            _hoverClipboardRow = -1;
        }
    }

    private long _downAt;
    private float _downX, _downY;
    private bool _downLeft;

    public void MouseDown(float x, float y, bool left)
    {
        lock (_lock)
        {
            _downAt = Environment.TickCount64;
            _downX = x;
            _downY = y;
            _downLeft = left;
            if (left && _expanded && _navIndex == 4)
            {
                if (TrackContains(_volumeRect, x, y))
                {
                    _draggingVolume = true;
                    SetVolumeLocked(x, true);
                }
                else if (_brightnessUi >= 0f && TrackContains(_brightnessRect, x, y))
                {
                    _draggingBrightness = true;
                    SetBrightnessLocked(x);
                }
            }
        }
    }

    public void MouseUp(float x, float y, bool left)
    {
        lock (_lock)
        {
            bool wasDragging = _draggingVolume || _draggingBrightness;
            if (_draggingVolume) SetVolumeLocked(x, true);
            else if (_draggingBrightness) SetBrightnessLocked(x);
            _draggingVolume = false;
            _draggingBrightness = false;
            if (!left) return;
            if (wasDragging) return;
            if (Math.Abs(x - _downX) > 6 || Math.Abs(y - _downY) > 6) return;
            ClickLocked(x, y);
        }
    }

    public void MouseCaptureLost()
    {
        lock (_lock)
        {
            _draggingVolume = false;
            _draggingBrightness = false;
        }
    }

    private static bool TrackContains(SKRect track, float x, float y)
        => track.Width > 0f &&
           x >= track.Left - 8f && x <= track.Right + 8f &&
           y >= track.Top - 10f && y <= track.Bottom + 10f;

    private void SetVolumeLocked(float x, bool force = false)
    {
        if (_volumeRect.Width <= 0f) return;
        float value = Math.Clamp((x - _volumeRect.Left) / _volumeRect.Width, 0f, 1f);
        _volumeUi = value;
        long now = Environment.TickCount64;
        _volumeUiAt = now;
        if (force || now - _lastVolumeApplyAt >= 16)
        {
            _lastVolumeApplyAt = now;
            _system.SetVolume(value);
        }
    }

    private void SetBrightnessLocked(float x)
    {
        if (_brightnessRect.Width <= 0f) return;
        float value = Math.Clamp((x - _brightnessRect.Left) / _brightnessRect.Width, 0f, 1f);
        _brightnessUi = value;
        _brightnessUiAt = Environment.TickCount64;
        _system.SetBrightness((int)Math.Round(value * 100f));
    }

    public void RightClick()
    {
        _actions.ShowMenu?.Invoke();
    }

    public void OnShellFlash(IntPtr hwnd) => _notifications.OnShellFlash(hwnd);

    public void OpenConfig() => _actions.EditConfig?.Invoke();

    public void DebugClick(float x, float y)
    {
        MouseMove(x, y);
        MouseDown(x, y, true);
        MouseUp(x, y, true);
    }

    public void LoadDemoData()
    {
        lock (_lock)
        {
            var cover = new SKBitmap(160, 160);
            using (var c = new SKCanvas(cover))
            using (var shader = SKShader.CreateLinearGradient(
                       new SKPoint(0, 0), new SKPoint(160, 160),
                       new[]
                       {
                           new SKColor(72, 92, 220), new SKColor(180, 112, 232),
                           new SKColor(255, 148, 178)
                       }, null, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            {
                c.DrawRect(new SKRect(0, 0, 160, 160), paint);
                TextUtil.Draw(c, "\uE8D6", 80, 105, 58, SKColors.White.WithAlpha(225),
                    Fonts.Icon, SKTextAlign.Center);
            }
            _mediaCover?.Dispose();
            _mediaCover = cover;
            _mediaState = new MediaSnapshot
            {
                HasSession = true,
                AppId = "demo",
                AppName = "演示",
                Title = "示例歌曲 Sample Song",
                Artist = "wisland",
                Album = "Demo",
                Playing = true,
                PositionSeconds = 85,
                DurationSeconds = 213,
                StampTicks = Environment.TickCount64,
                CanPrevious = true,
                CanNext = true
            };
            var demoLines = new[]
            {
                "沿着光照进来的方向",
                "把整座城市的噪音调成静音",
                "让时间在指尖慢慢发光",
                "这一刻只听见心跳"
            }.Select((text, i) => new LyricLine
            {
                Time = i * 12,
                Text = text,
                Translation = ""
            }).ToList();
            _lyrics = new SongLyrics
            {
                Times = demoLines.Select(x => x.Time).ToList(),
                Lines = demoLines,
                MaxLineWidth = demoLines.Max(x => TextUtil.Measure(x.Text, 13f, Fonts.UiSemi))
            };
            _lyricWidth = Math.Clamp(_lyrics.MaxLineWidth + 170, 360, 760);
            _pendingLyricsKey = "";
            _pendingLyrics = null;

            var now = DateTime.Now;
            _weatherState = new WeatherSnapshot
            {
                Ok = true,
                City = "上海",
                Temperature = 18,
                FeelsLike = 17,
                Code = 1,
                IsDay = now.Hour is >= 7 and < 19,
                Description = "晴间多云",
                Humidity = 62,
                Wind = 12,
                TempMin = 12,
                TempMax = 23,
                Hourly = Enumerable.Range(0, 8).Select(i => new HourlyWeather
                {
                    Time = now.AddHours(i * 3),
                    Temperature = 18 + Math.Sin(i * 0.8) * 3,
                    Code = i % 4 == 0 ? 1 : i % 4 == 1 ? 2 : i % 4 == 2 ? 61 : 3,
                    Precipitation = i % 4 == 2 ? 0.35 : 0,
                    IsDay = true
                }).ToList(),
                Daily = Enumerable.Range(0, 5).Select(i => new DailyWeather
                {
                    Date = now.Date.AddDays(i),
                    Min = 11 + i,
                    Max = 22 + i,
                    Code = i % 3 == 0 ? 1 : i % 3 == 1 ? 2 : 61
                }).ToList()
            };

            _stockState = new[]
            {
                new StockQuote { Code = "sh000001", Name = "上证指数", Price = 3388.12, Percent = 0.72, Change = 24.1, Time = "15:00" },
                new StockQuote { Code = "sz399001", Name = "深证成指", Price = 10821.44, Percent = -0.35, Change = -38.2, Time = "15:00" },
                new StockQuote { Code = "sz399006", Name = "创业板指", Price = 2241.76, Percent = 1.12, Change = 24.7, Time = "15:00" },
                new StockQuote { Code = "hk00700", Name = "腾讯控股", Price = 401.20, Percent = 0.44, Change = 1.8, Time = "16:08" },
                new StockQuote { Code = "usAAPL", Name = "苹果", Price = 227.52, Percent = -0.23, Change = -0.52, Time = "05:00" },
                new StockQuote { Code = "usNVDA", Name = "英伟达", Price = 142.11, Percent = 2.04, Change = 2.84, Time = "05:00" },
                new StockQuote { Code = "usQQQ", Name = "纳指100ETF", Price = 498.31, Percent = 0.60, Change = 2.97, Time = "05:00" }
            };

            _systemState = new SystemSnapshot { Volume = 0.62f, Muted = false, Peak = 0.5f, Brightness = 72 };
            _volumeUi = 0.62f;
            _brightnessUi = 0.72f;
            _clipState = new[]
            {
                new ClipboardEntry { Id = 1, Text = "把 Wisland 的视觉外壳移植到 C# + SkiaSharp，去掉 PySide6 常驻开销。", Time = now.AddMinutes(-2) },
                new ClipboardEntry { Id = 2, Text = "https://github.com/AsamiAkito/wisland", Time = now.AddMinutes(-12) },
                new ClipboardEntry { Id = 3, Text = "今晚 22:00 前提交第一版可运行预览。", Time = now.AddMinutes(-36) },
                new ClipboardEntry { Id = 4, Text = "示例复制文本：液态玻璃 / 悬停展开 / 媒体控制 / 天气 / 股票", Time = now.AddHours(-1) }
            };
            _notificationState = new NotificationSnapshot
            {
                Available = true,
                History = new[]
                {
                    new NotificationItem { Id = 4, Kind = "qq", AppName = "QQ", Sender = "张同学", Preview = "晚上一起开黑吗？", Time = now.AddMinutes(-3) },
                    new NotificationItem { Id = 3, Kind = "wechat", AppName = "微信", Sender = "项目群", Preview = "新的设计稿已经发到群文件", Time = now.AddMinutes(-11) },
                    new NotificationItem { Id = 2, Kind = "mail", AppName = "邮件", Sender = "GitHub", Preview = "你的 Pull Request 已被合并", Time = now.AddMinutes(-26) },
                    new NotificationItem { Id = 1, Kind = "qq", AppName = "QQ", Sender = "产品经理", Preview = "消息页的视觉再对一下", Time = now.AddHours(-1) }
                },
                Counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["qq"] = 2, ["wechat"] = 1, ["mail"] = 1
                }
            };
            _view = "music";
            _expanded = false;
            _expand.Snap(0);
            _viewChangedAt = Environment.TickCount64;
        }
    }

    public void Wheel(int delta)
    {
        lock (_lock)
        {
            if (_expanded)
            {
                _navIndex = (_navIndex + (delta < 0 ? 1 : _navIcons.Length - 1)) % _navIcons.Length;
                return;
            }
            var views = new[] { "clock", "pet", "music" };
            int i = Array.IndexOf(views, _view);
            if (i < 0) i = 0;
            _view = views[(i + (delta < 0 ? 1 : views.Length - 1)) % views.Length];
            _viewChangedAt = Environment.TickCount64;
        }
    }

    private void ClickLocked(float x, float y)
    {
        long now = Environment.TickCount64;
        bool dbl = now - _lastClick < 360 &&
                   Math.Abs(x - _lastClickX) < 8 && Math.Abs(y - _lastClickY) < 8;
        _lastClick = now;
        _lastClickX = x;
        _lastClickY = y;

        if (!_expanded)
        {
            if (_compactPlayRect.Contains(x, y))
            {
                _media.TogglePlay();
                return;
            }
            if (dbl)
            {
                _longOff = !_longOff;
                if (_longOff) _expanded = false;
                ShowActivity(_longOff ? "已长关" : "已恢复悬停展开", _longOff ? "双击灵动岛可恢复" : "", "\uE7E8");
                return;
            }
            _expanded = true;
            return;
        }

        for (int i = 0; i < _navHits.Length; i++)
        {
            if (_navHits[i].Rect.Contains(x, y))
            {
                _navIndex = i;
                return;
            }
        }

        foreach (var hit in _pageHits)
        {
            if (!hit.Rect.Contains(x, y)) continue;
            switch (hit.Hit)
            {
                case IslandHit.Settings:
                    _actions.ShowMenu?.Invoke();
                    return;
                case IslandHit.CompactPlay:
                    _media.TogglePlay();
                    return;
                case IslandHit.MediaPrevious:
                    _media.Previous();
                    return;
                case IslandHit.MediaPlay:
                    _media.TogglePlay();
                    return;
                case IslandHit.MediaNext:
                    _media.Next();
                    return;
                case IslandHit.MediaProgress:
                    if (_mediaState.DurationSeconds > 0)
                    {
                        double ratio = Math.Clamp((x - hit.Rect.Left) / hit.Rect.Width, 0, 1);
                        _media.Seek(ratio * _mediaState.DurationSeconds);
                    }
                    return;
                case IslandHit.ActionPin:
                    SetPinnedLocked(!_pinned);
                    return;
                case IslandHit.ActionPassthrough:
                    SetPassthroughLocked(!_passthrough);
                    return;
                case IslandHit.ActionMute:
                    _system.ToggleMute();
                    ShowActivity(_system.Snapshot.Muted ? "已静音" : "已取消静音", "", "\uE767");
                    return;
                case IslandHit.ActionLive:
                    ShowActivity("实时字幕将在下一阶段移植", "需要语音识别与翻译模型", "\uE720", seconds: 3.2);
                    return;
                case IslandHit.Launcher:
                    Launch(_config.Launcher.ElementAtOrDefault(hit.Index));
                    return;
                case IslandHit.ClipboardRow:
                    if (hit.Index >= 0 && hit.Index < _clipState.Count)
                    {
                        var entry = _clipState[hit.Index];
                        _clipboard.Copy(entry);
                        ShowActivity("已复制到剪贴板", entry.Text, "\uE8C8");
                    }
                    return;
                case IslandHit.MuteTile:
                    _system.ToggleMute();
                    return;
                case IslandHit.WifiTile:
                    OpenSettings("ms-settings:network-wifi");
                    return;
                case IslandHit.BluetoothTile:
                    OpenSettings("ms-settings:bluetooth");
                    return;
                case IslandHit.VolumeTrack:
                    SetVolumeLocked(x, true);
                    return;
                case IslandHit.BrightnessTrack:
                    SetBrightnessLocked(x);
                    return;
                case IslandHit.WeatherAdd:
                    ShowActivity("城市管理请编辑 config.json", "weather.city / weather.lat / weather.lon", "\uE710");
                    return;
                case IslandHit.MessageQq:
                    OpenNotification("qq");
                    return;
                case IslandHit.MessageWechat:
                    OpenNotification("wechat");
                    return;
                case IslandHit.MessageMail:
                    OpenNotification("mail");
                    return;
                case IslandHit.MessageClear:
                    _notifications.ClearAll();
                    ShowActivity("消息已清空", "", "\uE74D");
                    return;
                case IslandHit.NotificationRow:
                    if (hit.Index >= 0 && hit.Index < _notificationState.History.Count)
                        OpenNotification(_notificationState.History[hit.Index].Kind);
                    return;
            }
        }
    }

    private void OpenNotification(string kind)
    {
        bool ok = _notifications.Open(kind);
        string name = kind switch { "qq" => "QQ", "wechat" => "微信", "mail" => "邮件", _ => "通知应用" };
        ShowActivity(ok ? $"已切换到{name}" : $"未找到{name}",
            ok ? "" : "请先启动对应应用，或在 config.json 的 notify.open 里填写路径",
            kind == "qq" ? "\uE8BD" : kind == "wechat" ? "\uE8F2" : "\uE715",
            seconds: ok ? 2.0 : 3.6);
    }

    private void SetPinnedLocked(bool pinned)
    {
        _pinned = pinned;
        _config.Island.ExpandMode = pinned ? "always" : "hover";
        try { _config.Save(); } catch { }
        _actions.SetPinned?.Invoke(pinned);
    }

    private void SetPassthroughLocked(bool on)
    {
        _passthrough = on;
        _config.Island.Passthrough = on;
        try { _config.Save(); } catch { }
        _actions.SetPassthrough?.Invoke(on);
        if (!on) ShowActivity("已关闭鼠标穿透", "现在可以操作灵动岛", "\uE8B0");
    }

    private static void Launch(LauncherItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;
        try
        {
            var psi = new ProcessStartInfo(item.Path) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(item.Args)) psi.Arguments = item.Args;
            Process.Start(psi);
        }
        catch { }
    }

    private static void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
    }

    public void Tick(float dt, float maxSpringDt)
    {
        lock (_lock)
        {
            long now = Environment.TickCount64;
            float springDt = Math.Min(dt, maxSpringDt);

            if (_mediaStoppedAt > 0 && now - _mediaStoppedAt > 8000 && _view == "music" &&
                _previousView is "clock" or "pet")
            {
                _view = !_config.Island.ShowIdlePet && _previousView == "pet" ? "clock" : _previousView;
                _viewChangedAt = now;
                _mediaStoppedAt = 0;
            }

            if (_config.Island.AutoCycleSeconds > 0 && !_expanded &&
                !(_mediaState.HasSession && _mediaState.Playing) &&
                (_view is "clock" or "pet") &&
                now - _viewChangedAt > _config.Island.AutoCycleSeconds * 1000L)
            {
                _view = _view == "clock" && _config.Island.ShowIdlePet ? "pet" : "clock";
                _viewChangedAt = now;
            }

            if (_longOff)
            {
                if (_expanded && !_pinned) _expanded = false;
            }
            else if (_config.Island.ExpandMode.Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                _expanded = true;
            }
            else if (_config.Island.ExpandMode.Equals("never", StringComparison.OrdinalIgnoreCase))
            {
                if (!_pinned) _expanded = false;
            }
            else
            {
                if (!_expanded && _hover && now - _hoverSince >= _config.Island.HoverExpandMs)
                    _expanded = true;
                if (_expanded && !_pinned && !_hover && _leaveSince > 0 &&
                    now - _leaveSince >= _config.Island.CollapseDelayMs)
                    _expanded = false;
            }

            _expand.To(_expanded ? 1f : 0f);
            _expand.Step(springDt, 260f, 30f);

            string mode = CompactMode(now);
            var (cw, ch) = CompactSize(mode);
            _compactW.To(cw);
            _compactH.To(ch);
            _compactW.Step(springDt, 260f, 30f);
            _compactH.Step(springDt, 260f, 30f);

            int pageHeight = PageHeight(_navIndex);
            _pageHeight.To(pageHeight);
            _pageHeight.Step(springDt, 260f, 30f);

            float e = Ease.InOut(Math.Clamp(_expand.Value, 0f, 1f));
            float w = Lerp(_compactW.Value, ExpW, e);
            float h = Lerp(_compactH.Value, Pad * 2 + NavH + PageGap + _pageHeight.Value, e);
            w = Math.Max(140f, w);
            h = Math.Max(30f, h);
            _body = new SKRect((WinW - w) / 2f, 2f, (WinW + w) / 2f, 2f + h);
            var shape = new SKRect(_body.Left, 0f, _body.Right, _body.Bottom);
            Shapes.TopAttachedInto(_path, shape, Fillet,
                Math.Min(30f, _body.Height / 2f), 2f);

            float mx = (_mouseX - _body.MidX) / Math.Max(1f, _body.Width / 2f);
            float my = (_mouseY - _body.MidY) / Math.Max(1f, _body.Height / 2f);
            _pet.Update(dt, mx, my, _hover && _body.Contains(_mouseX, _mouseY));

            _volumeGlow.To(_draggingVolume ? 1f : 0f);
            _volumeGlow.Step(springDt, 300f, 34f);
            _peak = _systemState.Peak;

            if (_backdrop != null) _theme.FeedLuminance(_backdrop.Luminance);
            UpdateLayoutLocked(e);
        }
    }

    private string CompactMode(long now)
    {
        if (_activityUntil > now) return "activity";
        if (_mediaState.HasSession && !string.IsNullOrWhiteSpace(_mediaState.Title) &&
            (_mediaState.Playing || _view == "music"))
            return "media";
        return _view == "pet" ? "pet" : "clock";
    }

    private (float W, float H) CompactSize(string mode)
    {
        if (mode == "activity")
        {
            float tw = TextUtil.Measure(_activityTitle, 13f, Fonts.UiSemi);
            float sw = TextUtil.Measure(_activitySub, 12f, Fonts.Ui);
            return (Math.Clamp(74 + tw + sw, 220, 460), 40);
        }
        if (mode == "media")
        {
            int badgeW = _notificationState.TotalCount > 0 ? 12 : 0;
            if (_lyrics != null && _lyricWidth > 0)
                return (Math.Clamp(_lyricWidth, 340, 760) + badgeW, 36);
            string text = string.IsNullOrWhiteSpace(_mediaState.Title) ? "未在播放" :
                (_mediaState.Artist.Length > 0 ? $"{_mediaState.Title}  ·  {_mediaState.Artist}" : _mediaState.Title);
            float tw = TextUtil.Measure(text, 13f, Fonts.UiSemi);
            return (Math.Clamp(tw + 160, 330, 620) + badgeW, 36);
        }
        if (mode == "pet") return (150 + (_notificationState.TotalCount > 0 ? 12 : 0), 34);
        int badge = _notificationState.TotalCount > 0 ? 12 : 0;
        float w = 150 + (_weatherState.Ok ? 70 : 0) + badge;
        return (w, 34);
    }

    private int PageHeight(int page) => page switch
    {
        0 => 252,
        1 => 300,
        2 => 300,
        3 => 338,
        4 => 300,
        5 => 300,
        _ => 260
    };

    private void UpdateLayoutLocked(float expand)
    {
        for (int i = 0; i < _navHits.Length; i++)
        {
            _navHits[i].Rect = default;
            _navHits[i].Hit = IslandHit.None;
        }
        _pageHits.Clear();
        _compactPlayRect = default;
        _mediaProgressRect = default;
        _volumeRect = default;
        _brightnessRect = default;
        if (_body.Width < 1f) return;

        if (expand < 0.05f)
        {
            var crash = _body;
            _compactPlayRect = new SKRect(crash.Right - 38, crash.MidY - 14, crash.Right - 10, crash.MidY + 14);
            return;
        }

        float contentL = _body.Left + Pad;
        float contentR = _body.Right - Pad;
        float navY = _body.Top + Pad;
        float contentW = contentR - contentL;

        // 主页标签对准左侧播放卡中心，最后一个标签与右侧对称，整组按比例铺开。
        float firstCenter = contentL + 106f;
        float lastCenter = contentR - 112f;
        float[] widths = new float[_navIcons.Length];
        for (int i = 0; i < widths.Length; i++)
            widths[i] = i == _navIndex ? 34f + TextUtil.Measure(_navLabels[i], 12.5f, Fonts.UiSemi) + 46f : 34f;
        float sum = widths.Sum();
        float gap = Math.Max(10f, (lastCenter - firstCenter - sum) / (_navIcons.Length - 1));
        float x = firstCenter - widths[0] / 2f;
        for (int i = 0; i < widths.Length; i++)
        {
            _navHits[i].Rect = new SKRect(x, navY + 2, x + widths[i], navY + NavH - 2);
            _navHits[i].Hit = (IslandHit)((int)IslandHit.NavHome + i);
            _navHits[i].Label = _navLabels[i];
            x += widths[i] + gap;
        }
        _pageHits.Add((IslandHit.Settings, new SKRect(contentR - 34, navY + 2, contentR, navY + NavH - 2), 0));

        switch (_navIndex)
        {
            case 0: BuildHomeHits(contentL, contentR, navY + NavH + PageGap); break;
            case 1: BuildMessageHits(contentL, contentR, navY + NavH + PageGap); break;
            case 2: break;
            case 3:
                _pageHits.Add((IslandHit.WeatherAdd,
                    new SKRect(contentR - 34, navY + NavH + PageGap, contentR, navY + NavH + PageGap + 30), 0));
                break;
            case 4: BuildControlHits(contentL, contentR, navY + NavH + PageGap); break;
            case 5: BuildClipboardHits(contentL, contentR, navY + NavH + PageGap); break;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
