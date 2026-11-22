using System.Windows.Forms;
using WislandSharp.Core;

namespace WislandSharp.Services;

/// <summary>最近文本剪贴板。轮询放在 WinForms 主线程定时器上，避免跨线程访问剪贴板。</summary>
public sealed class ClipboardService : IDisposable
{
    private readonly List<ClipboardEntry> _items = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _max;
    private string _last = "";
    private long _nextId = 1;

    public event Action<IReadOnlyList<ClipboardEntry>>? Changed;

    public IReadOnlyList<ClipboardEntry> Items
    {
        get { lock (_items) return _items.ToArray(); }
    }

    public ClipboardService(WislandConfig config)
    {
        _max = Math.Clamp(config.Clipboard.MaxItems, 1, 30);
        _timer = new System.Windows.Forms.Timer { Interval = 650 };
        _timer.Tick += (_, _) => Poll();
        try { _last = Clipboard.GetText(); } catch { }
    }

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Dispose();

    private void Poll()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText().Trim();
            if (string.IsNullOrWhiteSpace(text) || text == _last) return;
            _last = text;
            if (text.Length > 4000) text = text[..4000];
            lock (_items)
            {
                if (_items.Any(x => string.Equals(x.Text, text, StringComparison.Ordinal))) return;
                _items.Insert(0, new ClipboardEntry { Id = _nextId++, Text = text });
                while (_items.Count > _max) _items.RemoveAt(_items.Count - 1);
            }
            Changed?.Invoke(Items);
        }
        catch
        {
            // 剪贴板被其他进程占用时跳过这一拍。
        }
    }

    public void Copy(ClipboardEntry entry)
    {
        try { Clipboard.SetText(entry.Text); _last = entry.Text; } catch { }
    }

    public void Clear()
    {
        lock (_items) _items.Clear();
        Changed?.Invoke(Items);
    }
}
