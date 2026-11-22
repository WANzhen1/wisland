using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class WeatherService : IDisposable
{
    private readonly WislandConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private bool _coordsResolved;
    private double _lat;
    private double _lon;
    private string _city;

    public event Action<WeatherSnapshot>? Changed;
    public WeatherSnapshot Snapshot { get; private set; } = new();

    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [0] = "晴", [1] = "晴间多云", [2] = "多云", [3] = "阴",
        [45] = "雾", [48] = "雾凇", [51] = "小毛毛雨", [53] = "毛毛雨", [55] = "大毛毛雨",
        [56] = "冻毛毛雨", [57] = "冻毛毛雨", [61] = "小雨", [63] = "中雨", [65] = "大雨",
        [66] = "冻雨", [67] = "冻雨", [71] = "小雪", [73] = "中雪", [75] = "大雪",
        [77] = "雪粒", [80] = "阵雨", [81] = "阵雨", [82] = "强阵雨",
        [85] = "阵雪", [86] = "强阵雪", [95] = "雷暴", [96] = "雷暴伴冰雹", [99] = "雷暴伴冰雹"
    };

    public WeatherService(WislandConfig config)
    {
        _config = config;
        _city = string.IsNullOrWhiteSpace(config.Weather.City) ? "上海" : config.Weather.City;
        _lat = config.Weather.Latitude ?? 31.2304;
        _lon = config.Weather.Longitude ?? 121.4737;
        _coordsResolved = config.Weather.Latitude.HasValue && config.Weather.Longitude.HasValue;
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland weather" };
    }

    public void Start() => _thread.Start();

    public void ReloadConfig()
    {
        _city = string.IsNullOrWhiteSpace(_config.Weather.City) ? "上海" : _config.Weather.City;
        _lat = _config.Weather.Latitude ?? 31.2304;
        _lon = _config.Weather.Longitude ?? 121.4737;
        _coordsResolved = _config.Weather.Latitude.HasValue && _config.Weather.Longitude.HasValue;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        try { _thread.Join(800); } catch { }
        _cts.Dispose();
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var next = FetchAsync().GetAwaiter().GetResult();
                Snapshot = next;
                Changed?.Invoke(next);
            }
            catch (Exception ex)
            {
                if (!Snapshot.Ok)
                {
                    var failed = new WeatherSnapshot
                    {
                        Ok = false,
                        Error = ex.Message,
                        City = _city,
                        Temperature = double.NaN,
                        Description = "天气获取失败"
                    };
                    Snapshot = failed;
                    Changed?.Invoke(failed);
                }
            }

            int minutes = Math.Clamp(_config.Weather.IntervalMinutes, 1, 180);
            for (int i = 0; i < minutes * 60 && !_cts.IsCancellationRequested; i++)
                Thread.Sleep(1000);
        }
    }

    private async Task<WeatherSnapshot> FetchAsync()
    {
        if (!_coordsResolved)
            await GeocodeAsync();

        string lat = _lat.ToString("F4", CultureInfo.InvariantCulture);
        string lon = _lon.ToString("F4", CultureInfo.InvariantCulture);
        string url = "https://api.open-meteo.com/v1/forecast" +
                     $"?latitude={lat}&longitude={lon}" +
                     "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m" +
                     "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
                     "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                     "&timezone=auto&forecast_days=5";

        using JsonDocument doc = JsonDocument.Parse(await _http.GetStringAsync(url, _cts.Token));
        JsonElement root = doc.RootElement;
        JsonElement cur = root.GetProperty("current");
        JsonElement daily = root.GetProperty("daily");
        JsonElement hourly = root.GetProperty("hourly");

        int code = cur.GetProperty("weather_code").GetInt32();
        bool isDay = cur.GetProperty("is_day").GetInt32() != 0;
        double temp = cur.GetProperty("temperature_2m").GetDouble();
        double feels = cur.GetProperty("apparent_temperature").GetDouble();
        double hum = cur.GetProperty("relative_humidity_2m").GetDouble();
        double wind = cur.GetProperty("wind_speed_10m").GetDouble();

        var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(x => x.GetDouble()).ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToList();
        var pops = hourly.GetProperty("precipitation_probability").EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.Null ? 0 : x.GetDouble()).ToList();
        var days = hourly.GetProperty("is_day").EnumerateArray().Select(x => x.GetInt32() != 0).ToList();

        int start = 0;
        for (int i = 0; i < times.Count; i++)
        {
            if (DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) &&
                dt >= DateTime.Now.AddMinutes(-30))
            {
                start = i;
                break;
            }
        }

        var hourlyOut = new List<HourlyWeather>();
        for (int i = start; i < times.Count && hourlyOut.Count < 8; i += 3)
        {
            DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
            hourlyOut.Add(new HourlyWeather
            {
                Time = dt,
                Temperature = i < temps.Count ? temps[i] : double.NaN,
                Code = i < codes.Count ? codes[i] : 0,
                Precipitation = i < pops.Count ? pops[i] / 100.0 : 0,
                IsDay = i < days.Count && days[i]
            });
        }

        var dailyOut = new List<DailyWeather>();
        var dTimes = daily.GetProperty("time").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var dCodes = daily.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToList();
        var dMax = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(x => x.GetDouble()).ToList();
        var dMin = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(x => x.GetDouble()).ToList();
        for (int i = 0; i < dTimes.Count && i < 5; i++)
        {
            DateTime.TryParse(dTimes[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
            dailyOut.Add(new DailyWeather
            {
                Date = dt,
                Code = i < dCodes.Count ? dCodes[i] : 0,
                Max = i < dMax.Count ? dMax[i] : double.NaN,
                Min = i < dMin.Count ? dMin[i] : double.NaN
            });
        }

        return new WeatherSnapshot
        {
            Ok = true,
            City = _city,
            Latitude = _lat,
            Longitude = _lon,
            Temperature = temp,
            FeelsLike = feels,
            Humidity = hum,
            Wind = wind,
            Code = code,
            IsDay = isDay,
            Description = Descriptions.TryGetValue(code, out string? desc) ? desc : "未知",
            TempMin = dailyOut.Count > 0 ? dailyOut[0].Min : double.NaN,
            TempMax = dailyOut.Count > 0 ? dailyOut[0].Max : double.NaN,
            Hourly = hourlyOut,
            Daily = dailyOut
        };
    }

    private async Task GeocodeAsync()
    {
        try
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=zh&format=json&name=" +
                         Uri.EscapeDataString(_city);
            using JsonDocument doc = JsonDocument.Parse(await _http.GetStringAsync(url, _cts.Token));
            if (doc.RootElement.TryGetProperty("results", out JsonElement results) &&
                results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
            {
                JsonElement first = results[0];
                _lat = first.GetProperty("latitude").GetDouble();
                _lon = first.GetProperty("longitude").GetDouble();
                if (first.TryGetProperty("name", out JsonElement name))
                    _city = name.GetString() ?? _city;
            }
        }
        catch
        {
            // 使用默认坐标继续。
        }
        _coordsResolved = true;
    }
}
