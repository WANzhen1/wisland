using System.Text;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WislandSharp.Core;

namespace WislandSharp.Services;

public sealed class LiveSubtitleService : IDisposable
{
    private const int TargetRate = 16000;
    private readonly object _lock = new();
    private readonly WislandConfig _config;
    private readonly MimoApiClient _mimo;
    private readonly TranslationService _translate;
    private readonly List<SubtitleLine> _history = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private long _version;
    private bool _running;
    private bool _starting;
    private string _status = "未开始";
    private string _error = "";
    private string _source = "";
    private string _translation = "";
    private string _language = "";
    private long _updatedAt;
    private string _sessionId = "";
    private bool _disposed;

    public LiveSubtitleService(WislandConfig config, MimoApiClient mimo, TranslationService translate)
    {
        _config = config;
        _mimo = mimo;
        _translate = translate;
    }

    public event Action<SubtitleSnapshot>? Changed;

    public SubtitleSnapshot Snapshot
    {
        get { lock (_lock) return BuildSnapshotLocked(); }
    }

    public bool HasKey => _mimo.HasKey;

    public void Toggle()
    {
        bool start;
        lock (_lock) start = !_running && !_starting;
        if (start) Start();
        else Stop();
    }

    public void Start()
    {
        CancellationTokenSource cts;
        SubtitleSnapshot initial;
        lock (_lock)
        {
            if (_running || _starting) return;
            _history.Clear();
            _source = "";
            _translation = "";
            _language = "";
            _error = "";
            _updatedAt = Environment.TickCount64;

            if (!_config.Live.Enabled)
            {
                _running = false;
                _starting = false;
                _status = "实时字幕已关闭";
                _error = "请在设置 → 翻译字幕中启用实时字幕";
                initial = BuildSnapshotLocked();
                _version++;
            }
            else if (!_mimo.HasKey)
            {
                _running = false;
                _starting = false;
                _status = "未配置 MiMo API Key";
                _error = "请在设置 → 翻译字幕中填写 MiMo API Key";
                initial = BuildSnapshotLocked();
                _version++;
            }
            else
            {
                cts = new CancellationTokenSource();
                _cts = cts;
                _starting = true;
                _running = false;
                _status = "正在启动实时字幕…";
                _sessionId = Guid.NewGuid().ToString("N");
                initial = BuildSnapshotLocked();
                _version++;
                _runTask = Task.Run(() => RunSessionAsync(_sessionId, cts));
            }
        }
        Changed?.Invoke(initial);
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        SubtitleSnapshot snapshot;
        lock (_lock)
        {
            cts = _cts;
            _cts = null;
            _running = false;
            _starting = false;
            _status = "已停止";
            _source = "";
            _translation = "";
            _language = "";
            _updatedAt = Environment.TickCount64;
            snapshot = BuildSnapshotLocked();
            _version++;
        }
        try { cts?.Cancel(); } catch { }
        Changed?.Invoke(snapshot);
    }

    public void Clear()
    {
        SubtitleSnapshot snapshot;
        lock (_lock)
        {
            _history.Clear();
            _source = "";
            _translation = "";
            _language = "";
            _status = _running ? "正在聆听…" : "未开始";
            _error = "";
            _updatedAt = Environment.TickCount64;
            snapshot = BuildSnapshotLocked();
            _version++;
        }
        Changed?.Invoke(snapshot);
    }

    private async Task RunSessionAsync(string sessionId, CancellationTokenSource cts)
    {
        Channel<float[]>? audioChannel = null;
        Channel<float[]>? asrChannel = null;
        WasapiCapture? capture = null;
        Task? asrTask = null;
        try
        {
            audioChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
            asrChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            capture = string.Equals(_config.Live.Source, "mic", StringComparison.OrdinalIgnoreCase)
                ? new WasapiCapture()
                : new WasapiLoopbackCapture();
            var converter = new PcmTo16kMono(capture.WaveFormat);

            capture.DataAvailable += (_, e) =>
            {
                try
                {
                    float[] chunk = converter.Convert(e.Buffer, e.BytesRecorded);
                    if (chunk.Length > 0) audioChannel.Writer.TryWrite(chunk);
                }
                catch (Exception ex)
                {
                    SetError("音频格式不支持：" + ex.Message);
                }
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null && !cts.IsCancellationRequested)
                    SetError("音频采集停止：" + e.Exception.Message);
                audioChannel.Writer.TryComplete();
            };

            var segmenter = new SpeechSegmenter(_config.Live,
                samples => asrChannel.Writer.TryWrite(samples));
            asrTask = Task.Run(() => AsrLoopAsync(sessionId, asrChannel.Reader, cts));

            capture.StartRecording();
            lock (_lock)
            {
                if (_sessionId != sessionId || cts.IsCancellationRequested) return;
                _starting = false;
                _running = true;
                _status = "正在聆听系统声音…";
                _error = "";
                _updatedAt = Environment.TickCount64;
            }
            Publish();

            await foreach (float[] chunk in audioChannel.Reader.ReadAllAsync(cts.Token))
                segmenter.Feed(chunk);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) SetError("实时字幕启动失败：" + ex.Message);
        }
        finally
        {
            try { capture?.StopRecording(); } catch { }
            try { capture?.Dispose(); } catch { }
            try { audioChannel?.Writer.TryComplete(); } catch { }
            try { asrChannel?.Writer.TryComplete(); } catch { }
            if (asrTask != null)
            {
                try { await asrTask.ConfigureAwait(false); } catch { }
            }
            cts.Dispose();

            bool publishStopped = false;
            lock (_lock)
            {
                if (_sessionId == sessionId)
                {
                    _cts = null;
                    if (_running || _starting)
                    {
                        _running = false;
                        _starting = false;
                        _status = "已停止";
                        _source = "";
                        _translation = "";
                    }
                    _updatedAt = Environment.TickCount64;
                    publishStopped = true;
                }
            }
            if (publishStopped) Publish();
        }
    }

    private async Task AsrLoopAsync(string sessionId, ChannelReader<float[]> reader,
        CancellationTokenSource sessionCts)
    {
        CancellationToken ct = sessionCts.Token;
        await foreach (float[] samples in reader.ReadAllAsync(ct))
        {
            if (ct.IsCancellationRequested) break;
            string wavId = Guid.NewGuid().ToString("N");
            try
            {
                SetStatus("正在识别语音…", true);
                byte[] wav = ToWav(samples);
                string text = (await _mimo.TranscribeAsync(wav, ct).ConfigureAwait(false)).Trim();
                if (text.Length == 0) continue;

                var line = new SubtitleLine
                {
                    Id = wavId,
                    Source = text,
                    UpdatedAt = Environment.TickCount64
                };
                IReadOnlyList<string> context;
                lock (_lock)
                {
                    if (_sessionId != sessionId || ct.IsCancellationRequested) return;
                    _source = text;
                    _translation = "";
                    _language = _config.Mimo.AsrLanguage;
                    _updatedAt = Environment.TickCount64;
                    _status = _config.Live.Translate && _translate.CanTranslate ? "正在翻译字幕…" : "正在聆听…";
                    _history.Add(line);
                    int max = Math.Clamp(_config.Live.HistoryMax, 10, 200);
                    if (_history.Count > max) _history.RemoveRange(0, _history.Count - max);
                    context = _history.TakeLast(4).Select(x => x.Source).ToArray();
                }
                Publish();

                if (!_config.Live.Translate || !_translate.CanTranslate) continue;
                string? zh;
                try
                {
                    zh = await _translate.TranslateSubtitleAsync(text, context, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    SetStatus("字幕翻译失败，仅显示原文", true);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(zh)) continue;

                lock (_lock)
                {
                    if (_sessionId != sessionId || ct.IsCancellationRequested) return;
                    int index = _history.FindIndex(x => x.Id == line.Id);
                    if (index >= 0)
                    {
                        _history[index] = new SubtitleLine
                        {
                            Id = line.Id,
                            Source = line.Source,
                            Translation = zh,
                            Language = line.Language,
                            UpdatedAt = Environment.TickCount64
                        };
                    }
                    _translation = zh;
                    _updatedAt = Environment.TickCount64;
                    _status = _running ? "正在聆听…" : "已停止";
                }
                Publish();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (MimoApiException ex)
            {
                SetError(ex.Message);
                if (ex.Status == System.Net.HttpStatusCode.PaymentRequired ||
                    ex.Status == System.Net.HttpStatusCode.Unauthorized)
                {
                    sessionCts.Cancel();
                    return;
                }
            }
            catch (Exception ex)
            {
                SetError("字幕识别失败：" + ex.Message);
            }
        }
    }

    private void SetStatus(string status, bool keepRunning)
    {
        lock (_lock)
        {
            _status = status;
            _updatedAt = Environment.TickCount64;
            if (!keepRunning) _running = false;
        }
        Publish();
    }

    private void SetError(string error)
    {
        lock (_lock)
        {
            _error = error;
            _status = "实时字幕出错";
            _updatedAt = Environment.TickCount64;
        }
        Publish();
    }

    private void Publish()
    {
        SubtitleSnapshot snapshot;
        lock (_lock)
        {
            if (_disposed) return;
            _version++;
            snapshot = BuildSnapshotLocked();
        }
        Changed?.Invoke(snapshot);
    }

    private SubtitleSnapshot BuildSnapshotLocked()
        => new()
        {
            Version = _version,
            Running = _running,
            Starting = _starting,
            Status = _status,
            Error = _error,
            Source = _source,
            Translation = _translation,
            Language = _language,
            UpdatedAt = _updatedAt,
            History = _history.ToArray()
        };

    private static byte[] ToWav(float[] samples)
    {
        float peak = 0f;
        foreach (float sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));
        float gain = peak > 1e-4f ? Math.Min(6f, 0.86f / peak) : 1f;
        if (gain <= 1.02f) gain = 1f;

        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        int dataBytes = samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(TargetRate);
        writer.Write(TargetRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (float sample in samples)
        {
            float value = Math.Clamp(sample * gain, -1f, 1f);
            writer.Write((short)Math.Round(value * 32767f));
        }
        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _runTask?.Wait(1500); } catch { }
    }
}

internal sealed class PcmTo16kMono
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bits;
    private readonly bool _float;
    private readonly double _step;
    private double _phase;
    private double _sum;
    private int _count;

    public PcmTo16kMono(WaveFormat format)
    {
        _sampleRate = Math.Max(8000, format.SampleRate);
        _channels = Math.Max(1, format.Channels);
        _bits = Math.Max(8, format.BitsPerSample);
        _float = format.Encoding == WaveFormatEncoding.IeeeFloat;
        _step = _sampleRate / 16000.0;
    }

    public float[] Convert(byte[] buffer, int bytes)
    {
        int bytesPerSample = Math.Max(1, _bits / 8);
        int frameBytes = bytesPerSample * _channels;
        int frames = bytes / frameBytes;
        if (frames <= 0) return Array.Empty<float>();

        var output = new List<float>((int)(frames / Math.Max(1.0, _step)) + 2);
        for (int frame = 0; frame < frames; frame++)
        {
            int offset = frame * frameBytes;
            float mixed = 0f;
            for (int channel = 0; channel < _channels; channel++)
                mixed += ReadSample(buffer, offset + channel * bytesPerSample) / _channels;

            _sum += mixed;
            _count++;
            _phase += 1.0;
            if (_phase >= _step)
            {
                output.Add((float)(_sum / Math.Max(1, _count)));
                _phase -= _step;
                _sum = 0;
                _count = 0;
            }
        }
        return output.ToArray();
    }

    private float ReadSample(byte[] buffer, int offset)
    {
        if (_float && _bits == 32)
            return BitConverter.ToSingle(buffer, offset);
        if (_bits == 16)
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (_bits == 24)
        {
            int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0) value |= ~0xFFFFFF;
            return value / 8388608f;
        }
        if (_bits == 32)
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        if (_bits == 8)
            return (buffer[offset] - 128) / 128f;
        throw new NotSupportedException($"{_bits} bit audio");
    }
}

internal sealed class SpeechSegmenter
{
    private const int TargetRate = 16000;
    private const int FrameSamples = 320;
    private readonly List<float> _pending = new();
    private readonly Queue<float[]> _preRoll = new();
    private readonly List<float[]> _utterance = new();
    private readonly Action<float[]> _onSegment;
    private readonly int _silenceFrames;
    private readonly int _minSamples;
    private readonly int _maxFrames;
    private readonly int _preRollFrames;
    private double _noiseFloor = 0.005;
    private int _speechFrames;
    private int _silenceCount;
    private bool _active;

    public SpeechSegmenter(LiveOptions options, Action<float[]> onSegment)
    {
        _onSegment = onSegment;
        _silenceFrames = Math.Max(3, options.SilenceMs / 20);
        _minSamples = Math.Max(1600, TargetRate * options.MinSpeechMs / 1000);
        _maxFrames = Math.Max(10, options.MaxSegmentSeconds * 1000 / 20);
        _preRollFrames = 15;
    }

    public void Feed(float[] samples)
    {
        _pending.AddRange(samples);
        while (_pending.Count >= FrameSamples)
        {
            var frame = _pending.GetRange(0, FrameSamples).ToArray();
            _pending.RemoveRange(0, FrameSamples);
            Process(frame);
        }
    }

    public void Flush()
    {
        if (_active) Finalize(trimSilence: false);
    }

    private void Process(float[] frame)
    {
        double sum = 0;
        foreach (float sample in frame) sum += sample * sample;
        float rms = (float)Math.Sqrt(sum / frame.Length);
        float threshold = Math.Max(0.008f, (float)_noiseFloor * 2.6f + 0.003f);

        if (!_active)
        {
            if (rms >= threshold)
            {
                _speechFrames++;
                if (_speechFrames >= 3)
                {
                    _active = true;
                    _silenceCount = 0;
                    _utterance.Clear();
                    _utterance.AddRange(_preRoll);
                    _preRoll.Clear();
                    _utterance.Add(frame);
                    return;
                }
            }
            else
            {
                _speechFrames = 0;
                _noiseFloor = _noiseFloor * 0.98 + rms * 0.02;
            }

            _preRoll.Enqueue(frame);
            while (_preRoll.Count > _preRollFrames) _preRoll.Dequeue();
            return;
        }

        _utterance.Add(frame);
        if (rms < threshold) _silenceCount++;
        else _silenceCount = 0;

        if (_silenceCount >= _silenceFrames || _utterance.Count >= _maxFrames)
            Finalize(trimSilence: true);
    }

    private void Finalize(bool trimSilence)
    {
        int drop = trimSilence ? Math.Min(_silenceCount, Math.Max(0, _utterance.Count - 1)) : 0;
        int keepFrames = Math.Max(0, _utterance.Count - drop);
        int total = 0;
        for (int i = 0; i < keepFrames; i++) total += _utterance[i].Length;
        if (total >= _minSamples)
        {
            var audio = new float[total];
            int offset = 0;
            for (int i = 0; i < keepFrames; i++)
            {
                Array.Copy(_utterance[i], 0, audio, offset, _utterance[i].Length);
                offset += _utterance[i].Length;
            }
            _onSegment(audio);
        }

        _active = false;
        _speechFrames = 0;
        _silenceCount = 0;
        _utterance.Clear();
        _preRoll.Clear();
        _pending.Clear();
    }
}
