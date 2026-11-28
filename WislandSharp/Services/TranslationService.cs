using System.Media;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class TranslationException : Exception
{
    public TranslationException(string message, HttpStatusCode? status = null) : base(message)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }
}

public sealed class TranslationService : IDisposable
{
    private const string TextPrompt =
        "你是专业翻译。把用户给出的文本翻译成简体中文。\n" +
        "源文本可能是日语、英语或其他语言，请自动判断语言并直接翻译。\n" +
        "只输出译文，不加解释、原文、引号或前后缀。保留原文语气、情绪、时态、否定和疑问，保留段落与换行。\n" +
        "如果输入只是一个单词或短词组：第一行给出中文释义；第二行写读音（日语用假名，英语用 IPA）并附一句简短用法。";

    private const string SubtitlePrompt =
        "你是视频字幕翻译。把用户给出的一句台词翻译成简体中文。\n" +
        "源语言可能是日语、英语或其他语言，请自动判断。只输出译文，不加解释、注音、原文或引号。\n" +
        "译文要口语化、简洁，符合中文字幕习惯，保留语气、情绪、否定和疑问。上文只用于理解指代，不要翻译上文。";

    private readonly object _lock = new();
    private readonly WislandConfig _config;
    private readonly MimoApiClient _mimo;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(70) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TranslationSnapshot _snapshot = new();
    private long _version;
    private bool _disposed;

    public TranslationService(WislandConfig config, MimoApiClient mimo)
    {
        _config = config;
        _mimo = mimo;
    }

    public event Action<TranslationSnapshot>? Changed;

    public TranslationSnapshot Snapshot
    {
        get { lock (_lock) return _snapshot; }
    }

    public bool CanTranslate =>
        _config.Translate.Enabled &&
        (!string.IsNullOrWhiteSpace(_config.Translate.ApiKey) ||
         (_config.Translate.FallbackToMimo && _mimo.HasKey));

    public async Task<string?> TranslateTextAsync(string text, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        text = text[..Math.Min(text.Length, 4000)];

        Publish(new TranslationSnapshot
        {
            Version = Interlocked.Increment(ref _version),
            Busy = true,
            Source = text,
            Status = "翻译中…"
        });

        try
        {
            string result = await TranslateCoreAsync(text, null, false, ct).ConfigureAwait(false);
            Publish(new TranslationSnapshot
            {
                Version = Interlocked.Increment(ref _version),
                Source = text,
                Result = result,
                Status = "翻译完成",
                Provider = ProviderName
            });
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            PublishError(text, "翻译超时");
            return null;
        }
        catch (Exception ex)
        {
            PublishError(text, Friendly(ex));
            return null;
        }
    }

    public async Task<string?> TranslateSubtitleAsync(string text, IReadOnlyList<string> context,
        CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        return await TranslateCoreAsync(text, context, true, ct).ConfigureAwait(false);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        text = text[..Math.Min(text.Length, 1200)];
        byte[] wav = await _mimo.SynthesizeAsync(text, ct).ConfigureAwait(false);
        await Task.Run(() =>
        {
            using var stream = new MemoryStream(wav, writable: false);
            using var player = new SoundPlayer(stream);
            player.PlaySync();
        }, ct);
    }

    private async Task<string> TranslateCoreAsync(string text, IReadOnlyList<string>? context,
        bool subtitle, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_config.Translate.Enabled)
                throw new TranslationException("翻译功能已在设置中关闭");
            if (!string.IsNullOrWhiteSpace(_config.Translate.ApiKey))
                return await TranslateDeepSeekAsync(text, context, subtitle, ct).ConfigureAwait(false);
            if (_config.Translate.FallbackToMimo && _mimo.HasKey)
                return await TranslateMimoAsync(text, context, subtitle, ct).ConfigureAwait(false);
            throw new TranslationException("未配置翻译 API Key（可在设置中填写，或配置 MiMo Key 使用回退翻译）");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> TranslateDeepSeekAsync(string text, IReadOnlyList<string>? context,
        bool subtitle, CancellationToken ct)
    {
        string baseUrl = string.IsNullOrWhiteSpace(_config.Translate.BaseUrl)
            ? "https://api.deepseek.com"
            : _config.Translate.BaseUrl.TrimEnd('/');
        string model = string.IsNullOrWhiteSpace(_config.Translate.Model)
            ? "deepseek-chat"
            : _config.Translate.Model;
        var payload = BuildPayload(model, text, context, subtitle, false);
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.Translate.ApiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw BuildHttpError(response.StatusCode, body, "翻译");
        using var doc = JsonDocument.Parse(body);
        return ReadContent(doc.RootElement, "翻译");
    }

    private async Task<string> TranslateMimoAsync(string text, IReadOnlyList<string>? context,
        bool subtitle, CancellationToken ct)
    {
        string system = subtitle ? SubtitlePrompt : TextPrompt;
        string user = BuildUserContent(text, context);
        return await _mimo.ChatAsync(system, user, ct).ConfigureAwait(false);
    }

    private static object BuildPayload(string model, string text, IReadOnlyList<string>? context,
        bool subtitle, bool includeThinking)
    {
        var messages = new object[]
        {
            new { role = "system", content = subtitle ? SubtitlePrompt : TextPrompt },
            new { role = "user", content = BuildUserContent(text, context) }
        };
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = 0.7,
            ["stream"] = false,
            ["max_tokens"] = subtitle ? 220 : Math.Clamp(text.Length * 3 + 240, 320, 4000)
        };
        if (includeThinking) payload["thinking"] = new { type = "disabled" };
        return payload;
    }

    private static string BuildUserContent(string text, IReadOnlyList<string>? context)
    {
        if (context == null || context.Count == 0) return text;
        string ctx = string.Join("\n", context.TakeLast(3).Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(ctx) ? text : "【上文】\n" + ctx + "\n\n【台词】\n" + text;
    }

    private static string ReadContent(JsonElement root, string source)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new TranslationException(source + "未返回结果");
        var message = choices[0].GetProperty("message");
        string? content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(content))
            throw new TranslationException(source + "返回了空结果");
        return content.Trim();
    }

    private string Friendly(Exception ex) => ex switch
    {
        TranslationException => ex.Message,
        MimoApiException => ex.Message,
        TaskCanceledException => "翻译超时",
        HttpRequestException => "无法连接翻译服务",
        _ => string.IsNullOrWhiteSpace(ex.Message) ? "翻译失败" : ex.Message[..Math.Min(160, ex.Message.Length)]
    };

    private static TranslationException BuildHttpError(HttpStatusCode status, string body, string source)
    {
        string detail = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                detail = message.GetString() ?? "";
        }
        catch { }
        string friendly = (int)status switch
        {
            401 => "API Key 无效",
            402 => "账户余额不足",
            403 => "无权限",
            404 => "模型名不正确",
            429 => "请求过于频繁",
            500 => "服务端出错",
            503 => "服务繁忙",
            _ => "HTTP " + (int)status
        };
        if (!string.IsNullOrWhiteSpace(detail)) friendly += "：" + detail;
        return new TranslationException(source + "失败：" + friendly, status);
    }

    private void PublishError(string source, string error)
    {
        Publish(new TranslationSnapshot
        {
            Version = Interlocked.Increment(ref _version),
            Source = source,
            Status = "翻译失败",
            Error = error
        });
    }

    private string ProviderName =>
        !string.IsNullOrWhiteSpace(_config.Translate.ApiKey) ? "DeepSeek" : "MiMo";

    private void Publish(TranslationSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _snapshot = snapshot;
        }
        Changed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        _http.Dispose();
    }
}
