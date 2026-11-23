using System.Management;
using NAudio.CoreAudioApi;
using WislandSharp.Core;

namespace WislandSharp.Services;

/// <summary>音量 / 静音 / 峰值 / 亮度。全部走本机 API，不启动额外的 Python 进程。</summary>
public sealed class SystemService : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private readonly Thread _brightnessThread;
    private readonly AutoResetEvent _brightnessWake = new(false);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioEndpointVolume? _volume;
    private AudioMeterInformation? _meter;
    private float _peak;
    private int _brightness = -1;
    private int _brightnessRequest = -1;

    public event Action<SystemSnapshot>? Changed;

    public SystemSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                float v = 0;
                bool muted = false;
                try
                {
                    if (_volume != null)
                    {
                        v = _volume.MasterVolumeLevelScalar;
                        muted = _volume.Mute;
                    }
                }
                catch { }
                return new SystemSnapshot
                {
                    Volume = v,
                    Muted = muted,
                    Peak = _peak,
                    Brightness = _brightness
                };
            }
        }
    }

    public SystemService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland system" };
        _brightnessThread = new Thread(BrightnessLoop)
        {
            IsBackground = true,
            Name = "Wisland brightness"
        };
    }

    public void Start()
    {
        _thread.Start();
        _brightnessThread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _brightnessWake.Set();
        bool systemStopped = false;
        bool brightnessStopped = false;
        try { systemStopped = _thread.Join(500); } catch { }
        try { brightnessStopped = _brightnessThread.Join(1000); } catch { }
        lock (_lock)
        {
            _meter = null;
            try { _device?.Dispose(); } catch { }
            try { _enumerator?.Dispose(); } catch { }
        }
        if (systemStopped && brightnessStopped)
        {
            _brightnessWake.Dispose();
            _cts.Dispose();
        }
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                EnsureDevice();
                float peak = 0;
                if (_meter != null)
                    peak = Math.Clamp(_meter.MasterPeakValue, 0f, 1f);
                lock (_lock) _peak = _peak * 0.55f + peak * 0.45f;

                Changed?.Invoke(Snapshot);
            }
            catch
            {
                CloseDevice();
            }
            Thread.Sleep(50);
        }
    }

    private void BrightnessLoop()
    {
        long nextRefresh = 0;
        while (!_cts.IsCancellationRequested)
        {
            int requested;
            lock (_lock)
            {
                requested = _brightnessRequest;
                _brightnessRequest = -1;
            }

            long now = Environment.TickCount64;
            if (requested >= 0)
            {
                ApplyBrightness(requested);
                nextRefresh = now + 30_000;
            }
            else if (now >= nextRefresh)
            {
                RefreshBrightness();
                nextRefresh = now + 30_000;
            }
            else
            {
                _brightnessWake.WaitOne(250);
            }
        }
    }

    private void EnsureDevice()
    {
        if (_volume != null) return;
        _enumerator ??= new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _volume = _device.AudioEndpointVolume;
        _meter = _device.AudioMeterInformation;
    }

    private void CloseDevice()
    {
        lock (_lock)
        {
            _meter = null;
            try { _device?.Dispose(); } catch { }
            try { _enumerator?.Dispose(); } catch { }
            _meter = null;
            _device = null;
            _enumerator = null;
            _volume = null;
        }
    }

    public void SetVolume(float value)
    {
        try
        {
            if (_volume != null)
            {
                _volume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
                if (_volume.Mute && value > 0.001f) _volume.Mute = false;
            }
        }
        catch { }
    }

    public void ToggleMute()
    {
        try { if (_volume != null) _volume.Mute = !_volume.Mute; } catch { }
    }

    private void RefreshBrightness()
    {
        int value = -1;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                value = Convert.ToInt32(obj["CurrentBrightness"]);
                break;
            }
        }
        catch { value = -1; }
        lock (_lock) _brightness = value;
    }

    public void SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_lock)
        {
            _brightness = percent;
            _brightnessRequest = percent;
        }
        _brightnessWake.Set();
    }

    private void ApplyBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { 1, percent });
                lock (_lock) _brightness = percent;
                return;
            }
        }
        catch { }
    }
}
