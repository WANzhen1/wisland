using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WislandSharp.Core;

public sealed class IslandOptions
{
    [JsonPropertyName("edge")] public string Edge { get; set; } = "top";
    [JsonPropertyName("pos")] public double Position { get; set; } = 0.5;
    [JsonPropertyName("screen")] public int Screen { get; set; }
    [JsonPropertyName("theme")] public string Theme { get; set; } = "auto";
    [JsonPropertyName("hover_expand_ms")] public int HoverExpandMs { get; set; } = 120;
    [JsonPropertyName("collapse_delay_ms")] public int CollapseDelayMs { get; set; } = 320;
    [JsonPropertyName("exclude_from_capture")] public bool ExcludeFromCapture { get; set; }
    [JsonPropertyName("live_backdrop")] public bool LiveBackdrop { get; set; }
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;
    [JsonPropertyName("expand_mode")] public string ExpandMode { get; set; } = "hover";
    [JsonPropertyName("compact_view")] public string CompactView { get; set; } = "clock";
    [JsonPropertyName("auto_cycle_s")] public int AutoCycleSeconds { get; set; } = 30;
    [JsonPropertyName("show_idle_pet")] public bool ShowIdlePet { get; set; } = true;
    [JsonPropertyName("passthrough")] public bool Passthrough { get; set; }
}

public sealed class MediaOptions
{
    [JsonPropertyName("auto_show")] public bool AutoShow { get; set; } = true;
    [JsonPropertyName("lyrics")] public bool Lyrics { get; set; } = true;
    [JsonPropertyName("priority")] public List<string> Priority { get; set; } =
        new() { "cloudmusic", "bilibili", "chrome", "zen", "msedge", "firefox" };
}

public sealed class WeatherOptions
{
    [JsonPropertyName("city")] public string City { get; set; } = "上海";
    [JsonPropertyName("lat")] public double? Latitude { get; set; }
    [JsonPropertyName("lon")] public double? Longitude { get; set; }
    [JsonPropertyName("interval_min")] public int IntervalMinutes { get; set; } = 15;
}

public sealed class StocksOptions
{
    [JsonPropertyName("watchlist")] public List<string> Watchlist { get; set; } =
        new() { "sh000001", "sz399001", "sz399006", "hk00700", "usAAPL", "usNVDA", "usQQQ" };
    [JsonPropertyName("interval")] public int IntervalSeconds { get; set; } = 5;
}

public sealed class ClipboardOptions
{
    [JsonPropertyName("max")] public int MaxItems { get; set; } = 10;
}

public sealed class NotifyAppMap
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "other";
}

public sealed class NotifyOptions
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("show_preview")] public bool ShowPreview { get; set; } = true;
    [JsonPropertyName("toast_apps")] public Dictionary<string, NotifyAppMap> ToastApps { get; set; } = new()
    {
        ["QQ"] = new() { Name = "QQ", Kind = "qq" },
        ["QQNT"] = new() { Name = "QQ", Kind = "qq" },
        ["Weixin"] = new() { Name = "微信", Kind = "wechat" },
        ["WeChat"] = new() { Name = "微信", Kind = "wechat" },
        ["ThunderbirdToast"] = new() { Name = "邮件", Kind = "mail" },
        ["Thunderbird"] = new() { Name = "邮件", Kind = "mail" }
    };
    [JsonPropertyName("open")] public Dictionary<string, string> Open { get; set; } = new()
    {
        ["qq"] = "",
        ["wechat"] = "",
        ["mail"] = ""
    };
    [JsonPropertyName("flash_processes")] public Dictionary<string, NotifyAppMap> FlashProcesses { get; set; } = new()
    {
        ["qq.exe"] = new() { Name = "QQ", Kind = "qq" },
        ["weixin.exe"] = new() { Name = "微信", Kind = "wechat" },
        ["wechat.exe"] = new() { Name = "微信", Kind = "wechat" },
        ["thunderbird.exe"] = new() { Name = "邮件", Kind = "mail" }
    };
}

public sealed class LauncherItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("args")] public string Args { get; set; } = "";
}

public sealed class WislandConfig
{
    [JsonPropertyName("island")] public IslandOptions Island { get; set; } = new();
    [JsonPropertyName("media")] public MediaOptions Media { get; set; } = new();
    [JsonPropertyName("weather")] public WeatherOptions Weather { get; set; } = new();
    [JsonPropertyName("stocks")] public StocksOptions Stocks { get; set; } = new();
    [JsonPropertyName("clipboard")] public ClipboardOptions Clipboard { get; set; } = new();
    [JsonPropertyName("notify")] public NotifyOptions Notify { get; set; } = new();
    [JsonPropertyName("launcher")] public List<LauncherItem> Launcher { get; set; } = new()
    {
        new() { Name = "文件资源管理器", Path = "C:/Windows/explorer.exe" }
    };

    [JsonIgnore] public string FilePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static WislandConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfg = new WislandConfig { FilePath = path };
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<WislandConfig>(File.ReadAllText(path), JsonOptions);
                if (loaded != null)
                {
                    loaded.FilePath = path;
                    cfg = loaded;
                }
            }
            else
            {
                cfg.Save();
            }
        }
        catch
        {
            cfg.FilePath = path;
            try { cfg.Save(); } catch { }
        }
        return cfg;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public void Reload(WislandConfig next)
    {
        Island = next.Island;
        Media = next.Media;
        Weather = next.Weather;
        Stocks = next.Stocks;
        Clipboard = next.Clipboard;
        Notify = next.Notify;
        Launcher = next.Launcher;
    }
}
