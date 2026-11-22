using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using WislandSharp.Core;
using WislandSharp.Native;

namespace WislandSharp.Services;

/// <summary>
/// Windows 通知读取：优先只读打开系统 wpndatabase.db（和 wisland Python 版同一来源），
/// 不依赖应用打包身份。数据库不存在时给出明确提示，不影响灵动岛其它功能。
/// </summary>
public sealed class NotificationService : IDisposable
{
    private const int MAX_HISTORY = 50;
    private static readonly Regex TextTag = new(@"<text\b[^>]*>(.*?)</text>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled);

    private readonly WislandConfig _config;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly List<NotificationItem> _history = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["qq"] = 0,
        ["wechat"] = 0,
        ["mail"] = 0
    };
    private readonly HashSet<long> _seenIds = new();
    private readonly Dictionary<string, long> _lastByKind = new(StringComparer.OrdinalIgnoreCase);
    private long _maxId;
    private bool _initialized;
    private bool _available;
    private string _error = "";
    private bool _shellHookSeen;
    private NotificationSnapshot _snapshot = new();
    private bool _disposed;
    private long _version;

    public event Action<NotificationSnapshot>? Changed;
    public event Action<NotificationItem>? NewNotification;
    public NotificationSnapshot Snapshot { get { lock (_lock) return _snapshot; } }

    private static string DatabasePath
    {
        get
        {
            string? test = Environment.GetEnvironmentVariable("WISLAND_WPN_DB");
            if (!string.IsNullOrWhiteSpace(test)) return test;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Notifications", "wpndatabase.db");
        }
    }

    public NotificationService(WislandConfig config)
    {
        _config = config;
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland notifications" };
    }

    public void Start()
    {
        if (_config.Notify.Enabled) _thread.Start();
        else
        {
            _available = false;
            _error = "消息通知已在配置中关闭";
            Publish();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _thread.Join(1200); } catch { }
        _cts.Dispose();
    }

    private void Run()
    {
        // 数据库可能刚被系统替换 / 锁定；失败后退避重试，不拖累主进程。
        int failures = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (!_config.Notify.Enabled)
                {
                    SetUnavailable("消息通知已在配置中关闭");
                }
                else if (!File.Exists(DatabasePath))
                {
                    if (!_shellHookSeen)
                        SetUnavailable("未找到 Windows 通知数据库。请先在系统里收到一条通知，或检查通知权限。");
                }
                else
                {
                    PollDatabase();
                    failures = 0;
                }
            }
            catch (Exception ex)
            {
                failures++;
                if (!_shellHookSeen)
                    SetUnavailable($"读取 Windows 通知失败：{ex.Message}");
            }

            int delay = failures > 0 ? 5000 : 1500;
            for (int i = 0; i < delay && !_cts.IsCancellationRequested; i += 100)
                Thread.Sleep(100);
        }
    }

    private void PollDatabase()
    {
        if (!_initialized)
        {
            var recent = Sqlite3.Query(DatabasePath,
                "SELECT n.Id,h.PrimaryId,n.ArrivalTime,n.Type,n.Payload " +
                "FROM Notification n JOIN NotificationHandler h ON h.RecordId=n.HandlerId " +
                "ORDER BY n.Id DESC LIMIT 30");

            var parsed = recent.Select(ParseRow).Where(x => x != null).Select(x => x!).ToList();
            lock (_lock)
            {
                foreach (var item in parsed.OrderByDescending(x => x.Time))
                {
                    if (_seenIds.Add(item.Id)) _history.Add(item);
                    if (item.Time >= DateTime.Now.AddDays(-1) && _counts.ContainsKey(item.Kind))
                        _counts[item.Kind]++;
                }
                TrimHistoryLocked();
                _maxId = recent.Count > 0 ? recent.Max(r => r.GetInt64(0)) : 0;
                _available = true;
                _error = "";
                _initialized = true;
                PublishLocked();
            }
            return;
        }

        var rows = Sqlite3.Query(DatabasePath,
            "SELECT n.Id,h.PrimaryId,n.ArrivalTime,n.Type,n.Payload " +
            "FROM Notification n JOIN NotificationHandler h ON h.RecordId=n.HandlerId " +
            "WHERE n.Id > " + _maxId + " ORDER BY n.Id");

        var fresh = new List<NotificationItem>();
        foreach (var row in rows)
        {
            long rowId = row.GetInt64(0);
            if (rowId > _maxId) _maxId = rowId;
            var item = ParseRow(row);
            if (item == null) continue;
            fresh.Add(item);
        }

        if (fresh.Count == 0)
        {
            lock (_lock)
            {
                if (!_available || _error.Length > 0)
                {
                    _available = true;
                    _error = "";
                    PublishLocked();
                }
            }
            return;
        }

        lock (_lock)
        {
            foreach (var item in fresh.OrderBy(x => x.Id))
            {
                if (!_seenIds.Add(item.Id)) continue;
                _history.Insert(0, item);
                if (_counts.ContainsKey(item.Kind)) _counts[item.Kind]++;
                _lastByKind[item.Kind] = Environment.TickCount64;
            }
            _history.Sort((a, b) => b.Time.CompareTo(a.Time));
            TrimHistoryLocked();
            _available = true;
            _error = "";
            PublishLocked();
        }

        foreach (var item in fresh.OrderBy(x => x.Id))
        {
            try { NewNotification?.Invoke(item); } catch { }
        }
    }

    private NotificationItem? ParseRow(Sqlite3.Row row)
    {
        try
        {
            long id = row.GetInt64(0);
            string primary = row.GetString(1);
            long arrival = row.GetInt64(2);
            string type = row.GetString(3);
            if (!string.IsNullOrWhiteSpace(type) &&
                !type.Equals("toast", StringComparison.OrdinalIgnoreCase))
                return null;

            var map = FindApp(primary);
            if (map == null) return null;
            if (_seenIds.Contains(id)) return null;

            byte[] payload = row.GetBytes(4);
            string xml = System.Text.Encoding.UTF8.GetString(payload);
            var texts = TextTag.Matches(xml)
                .Select(m => Clean(m.Groups[1].Value))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            string sender = texts.ElementAtOrDefault(0) ?? map.Name;
            string preview = _config.Notify.ShowPreview ? (texts.ElementAtOrDefault(1) ?? "") : "";
            if (sender.Length > 60) sender = sender[..60];
            if (preview.Length > 160) preview = preview[..160] + "…";
            DateTime time;
            try { time = DateTime.FromFileTimeUtc(arrival).ToLocalTime(); }
            catch { time = DateTime.Now; }

            return new NotificationItem
            {
                Id = id,
                Kind = map.Kind,
                AppName = map.Name,
                Sender = sender,
                Preview = preview,
                PrimaryId = primary,
                Time = time
            };
        }
        catch
        {
            return null;
        }
    }

    private NotifyAppMap? FindApp(string primary)
    {
        if (string.IsNullOrWhiteSpace(primary)) return null;
        foreach (var pair in _config.Notify.ToastApps.OrderByDescending(x => x.Key.Length))
        {
            string prefix = pair.Key;
            if (!primary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = primary[prefix.Length..];
            if (rest.Length == 0 || !char.IsLetterOrDigit(rest[0])) return pair.Value;
        }

        string low = primary.ToLowerInvariant();
        if (low.StartsWith("qq") || low.StartsWith("tencent.qq")) return new NotifyAppMap { Name = "QQ", Kind = "qq" };
        if (low.Contains("weixin") || low.Contains("wechat")) return new NotifyAppMap { Name = "微信", Kind = "wechat" };
        if (low.Contains("thunderbird")) return new NotifyAppMap { Name = "邮件", Kind = "mail" };
        return null;
    }

    private static string Clean(string text)
    {
        string s = AnyTag.Replace(text ?? "", "");
        s = WebUtility.HtmlDecode(s);
        return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>任务栏闪烁兜底：通知数据库不存在或应用没写 toast 时仍能提示 QQ/微信/邮件。</summary>
    public void OnShellFlash(IntPtr hwnd)
    {
        if (!_config.Notify.Enabled || hwnd == IntPtr.Zero) return;
        string exe = ProcessNameOf(hwnd);
        if (string.IsNullOrWhiteSpace(exe)) return;
        if (!_config.Notify.FlashProcesses.TryGetValue(exe, out var map)) return;

        NotificationItem? item = null;
        lock (_lock)
        {
            _shellHookSeen = true;
            long now = Environment.TickCount64;
            if (_lastByKind.TryGetValue(map.Kind, out long last) && now - last < 8000) return;
            _lastByKind[map.Kind] = now;
            item = new NotificationItem
            {
                Id = -now,
                Kind = map.Kind,
                AppName = map.Name,
                Sender = "",
                Preview = "窗口有新活动",
                PrimaryId = exe,
                Time = DateTime.Now
            };
            _history.Insert(0, item);
            if (_counts.ContainsKey(item.Kind)) _counts[item.Kind]++;
            _history.Sort((a, b) => b.Time.CompareTo(a.Time));
            TrimHistoryLocked();
            _available = true;
            _error = "";
            PublishLocked();
        }
        if (item != null)
        {
            try { NewNotification?.Invoke(item); } catch { }
        }
    }

    private static string ProcessNameOf(IntPtr hwnd)
    {
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch { return ""; }
    }

    private void TrimHistoryLocked()
    {
        while (_history.Count > MAX_HISTORY) _history.RemoveAt(_history.Count - 1);
        while (_seenIds.Count > 600)
        {
            var oldest = _history.LastOrDefault();
            if (oldest == null) break;
            _seenIds.Remove(oldest.Id);
            _history.RemoveAt(_history.Count - 1);
        }
    }

    private void SetUnavailable(string error)
    {
        lock (_lock)
        {
            bool changed = _available || !string.Equals(_error, error, StringComparison.Ordinal);
            _available = false;
            _error = error;
            if (changed) PublishLocked();
        }
    }

    private void Publish()
    {
        lock (_lock) PublishLocked();
    }

    private void PublishLocked()
    {
        _snapshot = new NotificationSnapshot
        {
            Version = ++_version,
            Available = _available,
            Error = _error,
            History = _history.ToArray(),
            Counts = new Dictionary<string, int>(_counts, StringComparer.OrdinalIgnoreCase)
        };
        var snap = _snapshot;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Changed?.Invoke(snap); } catch { }
        });
    }

    public void MarkRead(string kind)
    {
        lock (_lock)
        {
            if (!_counts.ContainsKey(kind) || _counts[kind] == 0) return;
            _counts[kind] = 0;
            PublishLocked();
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _history.Clear();
            foreach (string key in _counts.Keys.ToList()) _counts[key] = 0;
            PublishLocked();
        }
    }

    public bool Open(string kind)
    {
        MarkRead(kind);
        if (_config.Notify.Open.TryGetValue(kind, out string? path) && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(path)) { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        string[] names = kind switch
        {
            "qq" => new[] { "QQ" },
            "wechat" => new[] { "Weixin", "WeChat" },
            "mail" => new[] { "thunderbird" },
            _ => Array.Empty<string>()
        };
        foreach (string name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                IntPtr hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero) hwnd = FindWindowForProcess(process.Id);
                if (hwnd != IntPtr.Zero)
                {
                    Win32.ShowWindowAsync(hwnd, Win32.SW_RESTORE);
                    Win32.SetForegroundWindow(hwnd);
                    return true;
                }
                try
                {
                    string? exe = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                        return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }

    private static IntPtr FindWindowForProcess(int pid)
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            Win32.EnumWindows((hwnd, _) =>
            {
                Win32.GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != (uint)pid || !Win32.IsWindowVisible(hwnd) || Win32.GetWindowTextLength(hwnd) <= 0)
                    return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }
}
