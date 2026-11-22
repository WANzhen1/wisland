using System.Net.Http;
using System.Text;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class StockService : IDisposable
{
    private readonly WislandConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public event Action<IReadOnlyList<StockQuote>>? Changed;
    public IReadOnlyList<StockQuote> Snapshot { get; private set; } = Array.Empty<StockQuote>();

    public StockService(WislandConfig config)
    {
        _config = config;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland stocks" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        try { _thread.Join(700); } catch { }
        _cts.Dispose();
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var quotes = FetchAsync().GetAwaiter().GetResult();
                if (quotes.Count > 0)
                {
                    Snapshot = quotes;
                    Changed?.Invoke(quotes);
                }
            }
            catch
            {
                // 网络抖动时保留上一份行情。
            }

            int seconds = Math.Clamp(_config.Stocks.IntervalSeconds, 5, 120);
            for (int i = 0; i < seconds * 10 && !_cts.IsCancellationRequested; i++)
                Thread.Sleep(100);
        }
    }

    private async Task<IReadOnlyList<StockQuote>> FetchAsync()
    {
        var codes = _config.Stocks.Watchlist
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct()
            .ToList();
        if (codes.Count == 0) return Array.Empty<StockQuote>();

        string url = "https://qt.gtimg.cn/q=" + string.Join(",", codes);
        using var response = await _http.GetAsync(url, _cts.Token);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(_cts.Token);
        string text = Encoding.GetEncoding("GBK").GetString(bytes);

        var map = new Dictionary<string, StockQuote>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = part.Trim();
            int eq = line.IndexOf('=');
            if (eq < 3 || !line.StartsWith("v_", StringComparison.Ordinal)) continue;
            string key = line[2..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim().Trim('"');
            string[] f = value.Split('~');
            if (f.Length < 35) continue;
            map[key] = new StockQuote
            {
                Code = key,
                Market = key.StartsWith("hk", StringComparison.Ordinal) ? "hk" :
                         key.StartsWith("us", StringComparison.Ordinal) ? "us" : "a",
                Name = f[1],
                Price = ParseDouble(f[3]),
                PreviousClose = ParseDouble(f[4]),
                Open = ParseDouble(f[5]),
                Time = f[30],
                Change = ParseDouble(f[31]),
                Percent = ParseDouble(f[32]),
                High = ParseDouble(f[33]),
                Low = ParseDouble(f[34])
            };
        }

        var result = new List<StockQuote>(codes.Count);
        foreach (string code in codes)
            if (map.TryGetValue(code, out var quote)) result.Add(quote);
        return result;
    }

    private static string Normalize(string code)
    {
        string c = code.Trim();
        if (c.Length > 2 && c[..2].ToLowerInvariant() is "sh" or "sz" or "bj" or "hk" or "us")
            c = c[..2].ToLowerInvariant() + c[2..];
        if (c.StartsWith("us", StringComparison.Ordinal))
            c = "us" + c[2..].Split('.')[0].ToUpperInvariant();
        return c;
    }

    private static double ParseDouble(string text)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : 0;
}
