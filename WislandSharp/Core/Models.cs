using SkiaSharp;

namespace WislandSharp.Core;

public sealed class MediaSnapshot
{
    public bool HasSession { get; init; }
    public string AppId { get; init; } = "";
    public string AppName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public bool Playing { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public long StampTicks { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
    public SKBitmap? Cover { get; init; }

    public double EstimatedPosition()
    {
        double pos = PositionSeconds;
        if (Playing)
            pos += Math.Max(0, (Environment.TickCount64 - StampTicks) / 1000.0);
        return DurationSeconds > 0 ? Math.Min(pos, DurationSeconds) : pos;
    }
}

public sealed class HourlyWeather
{
    public DateTime Time { get; init; }
    public double Temperature { get; init; }
    public int Code { get; init; }
    public double Precipitation { get; init; }
    public bool IsDay { get; init; }
}

public sealed class DailyWeather
{
    public DateTime Date { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public int Code { get; init; }
}

public sealed class WeatherSnapshot
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string City { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Temperature { get; init; }
    public double FeelsLike { get; init; }
    public int Code { get; init; }
    public bool IsDay { get; init; }
    public string Description { get; init; } = "";
    public double Humidity { get; init; }
    public double Wind { get; init; }
    public double TempMin { get; init; }
    public double TempMax { get; init; }
    public IReadOnlyList<HourlyWeather> Hourly { get; init; } = Array.Empty<HourlyWeather>();
    public IReadOnlyList<DailyWeather> Daily { get; init; } = Array.Empty<DailyWeather>();
}

public sealed class StockQuote
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Market { get; init; } = "a";
    public double Price { get; init; }
    public double Change { get; init; }
    public double Percent { get; init; }
    public double Open { get; init; }
    public double PreviousClose { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public string Time { get; init; } = "";
}

public sealed class SystemSnapshot
{
    public float Volume { get; init; }
    public bool Muted { get; init; }
    public float Peak { get; init; }
    public int Brightness { get; init; } = -1;
}

public sealed class ClipboardEntry
{
    public long Id { get; init; }
    public string Text { get; init; } = "";
    public DateTime Time { get; init; } = DateTime.Now;
}

public sealed class NotificationItem
{
    public long Id { get; init; }
    public string Kind { get; init; } = "other";
    public string AppName { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Preview { get; init; } = "";
    public string PrimaryId { get; init; } = "";
    public DateTime Time { get; init; } = DateTime.Now;
}

public sealed class NotificationSnapshot
{
    public long Version { get; init; }
    public bool Available { get; init; }
    public string Error { get; init; } = "";
    public IReadOnlyList<NotificationItem> History { get; init; } = Array.Empty<NotificationItem>();
    public IReadOnlyDictionary<string, int> Counts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int TotalCount => Counts.Values.Sum();
}

public sealed class TranslationSnapshot
{
    public long Version { get; init; }
    public bool Busy { get; init; }
    public string Source { get; init; } = "";
    public string Result { get; init; } = "";
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public string Provider { get; init; } = "";
}

public sealed class SubtitleLine
{
    public string Id { get; init; } = "";
    public string Source { get; init; } = "";
    public string Translation { get; init; } = "";
    public string Language { get; init; } = "";
    public long UpdatedAt { get; init; }
}

public sealed class SubtitleSnapshot
{
    public long Version { get; init; }
    public bool Running { get; init; }
    public bool Starting { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public string Source { get; init; } = "";
    public string Translation { get; init; } = "";
    public string Language { get; init; } = "";
    public long UpdatedAt { get; init; }
    public IReadOnlyList<SubtitleLine> History { get; init; } = Array.Empty<SubtitleLine>();
}

public enum IslandHit
{
    None,
    Shape,
    NavHome,
    NavMessages,
    NavStocks,
    NavWeather,
    NavControls,
    NavClipboard,
    NavTranslate,
    NavSubtitles,
    Settings,
    CompactPlay,
    MediaPrevious,
    MediaPlay,
    MediaNext,
    MediaProgress,
    ActionPin,
    ActionPassthrough,
    ActionMute,
    ActionLive,
    WeatherAdd,
    StockAdd,
    ClipboardRow,
    VolumeTrack,
    BrightnessTrack,
    MuteTile,
    WifiTile,
    BluetoothTile,
    Launcher,
    MessageQq,
    MessageWechat,
    MessageMail,
    MessageClear,
    NotificationRow,
    TranslateClipboard,
    TranslateOpen,
    TranslateCopy,
    TranslateSpeak,
    SubtitleToggle,
    SubtitleClear
}
