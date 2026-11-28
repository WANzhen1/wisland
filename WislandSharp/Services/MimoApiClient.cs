using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class MimoApiException : Exception
{
    public MimoApiException(string message, HttpStatusCode? status = null) : base(message)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }
}

public sealed class MimoApiClient : IDisposable
{
    private readonly WislandConfig _config;
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(75)
    };

    public MimoApiClient(WislandConfig config)
    {
        _config = config;
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(_config.Mimo.ApiKey);

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        string key = RequireKey();
        string audio = Convert.ToBase64String(wav);
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_config.Mimo.AsrModel) ? "mimo-v2.5-asr" : _config.Mimo.AsrModel,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_audio",
                            input_audio = new { data = "data:audio/wav;base64," + audio }
                        }
                    }
                }
            },
            asr_options = new
            {
                language = string.IsNullOrWhiteSpace(_config.Mimo.AsrLanguage)
                    ? "auto"
                    : _config.Mimo.AsrLanguage
            }
        };

        using var doc = await PostChatAsync(key, payload, ct);
        return ReadMessageContent(doc.RootElement);
    }

    public async Task<string> ChatAsync(string systemPrompt, string userContent,
        CancellationToken ct = default)
    {
        string key = RequireKey();
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_config.Mimo.ChatModel) ? "mimo-v2.6-flash" : _config.Mimo.ChatModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            temperature = 0.7,
            stream = false
        };

        using var doc = await PostChatAsync(key, payload, ct);
        return ReadMessageContent(doc.RootElement);
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        string key = RequireKey();
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_config.Mimo.TtsModel) ? "mimo-v2.5-tts" : _config.Mimo.TtsModel,
            messages = new object[]
            {
                new { role = "user", content = "Use a natural, clear and friendly reading tone." },
                new { role = "assistant", content = text }
            },
            audio = new
            {
                format = "wav",
                voice = string.IsNullOrWhiteSpace(_config.Mimo.Voice) ? "冰糖" : _config.Mimo.Voice
            }
        };

        using var doc = await PostChatAsync(key, payload, ct);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new MimoApiException("MiMo TTS 未返回音频");

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("audio", out var audio) ||
            audio.ValueKind != JsonValueKind.Object ||
            !audio.TryGetProperty("data", out var data))
            throw new MimoApiException("MiMo TTS 未返回音频");

        string? b64 = data.GetString();
        if (string.IsNullOrWhiteSpace(b64))
            throw new MimoApiException("MiMo TTS 返回了空音频");
        return Convert.FromBase64String(b64);
    }

    private async Task<JsonDocument> PostChatAsync(string key, object payload, CancellationToken ct)
    {
        string baseUrl = string.IsNullOrWhiteSpace(_config.Mimo.BaseUrl)
            ? "https://api.xiaomimimo.com/v1"
            : _config.Mimo.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
        request.Headers.TryAddWithoutValidation("api-key", key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MimoApiException("MiMo 请求超时");
        }
        catch (HttpRequestException ex)
        {
            throw new MimoApiException("无法连接 MiMo：" + ex.Message);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw BuildError(response.StatusCode, body);

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new MimoApiException("MiMo 返回了无法解析的响应");
            }
        }
    }

    private static string ReadMessageContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new MimoApiException("MiMo 未返回结果");
        var message = choices[0].GetProperty("message");
        string? content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(content))
            throw new MimoApiException("MiMo 返回了空结果");
        return content.Trim();
    }

    private static MimoApiException BuildError(HttpStatusCode status, string body)
    {
        string message = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var m))
                message = m.GetString() ?? "";
        }
        catch { }

        string friendly = (int)status switch
        {
            401 => "MiMo API Key 无效",
            402 => "MiMo 账户余额不足，请充值后再试",
            403 => "MiMo 无权限",
            404 => "MiMo 模型名不正确",
            429 => "MiMo 请求过于频繁",
            500 => "MiMo 服务端出错",
            503 => "MiMo 服务繁忙",
            _ => "MiMo 请求失败（HTTP " + (int)status + "）"
        };
        if (!string.IsNullOrWhiteSpace(message)) friendly += "：" + message;
        return new MimoApiException(friendly, status);
    }

    private string RequireKey()
    {
        if (!HasKey) throw new MimoApiException("未配置 MiMo API Key");
        return _config.Mimo.ApiKey.Trim();
    }

    public void Dispose() => _http.Dispose();
}
