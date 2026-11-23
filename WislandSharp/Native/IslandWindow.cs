using System.Diagnostics;
using System.Windows.Forms;
using SkiaSharp;
using WislandSharp.Core;
using WislandSharp.UI;

namespace WislandSharp.Native;

/// <summary>
/// 透明分层窗口 + SkiaSharp 32 位 DIB 渲染。
/// 与 NotchPeninsula 同一条技术路线：Win32 窗口、UpdateLayeredWindow、Skia 绘制。
/// </summary>
public sealed class IslandWindow : NativeWindow, IDisposable
{
    public const int LogicalWidth = 700;
    public const int LogicalHeight = 500;

    private readonly IslandRenderer _renderer;
    private readonly Action<Point> _showMenu;
    private readonly float _scale;
    private readonly int _physW;
    private readonly int _physH;
    private readonly int _screenX;
    private readonly int _screenY;
    private readonly Thread _renderThread;
    private volatile bool _stop;
    private bool _disposed;
    private bool _shown;

    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private SKSurface? _surface;
    private Backdrop? _backdrop;
    private bool _passthrough;
    private int _snapshotRequest;
    private string _snapshotPath = Path.Combine(AppContext.BaseDirectory, "snapshot.png");
    private uint _shellHookMessage;
    private const int WM_APP_PAGE = 0x8000 + 0x57;
    private const int WM_APP_CLICK = 0x8000 + 0x58;
    private const int WM_APP_DEMO = 0x8000 + 0x59;

    public IslandRenderer Renderer => _renderer;

    private readonly bool _excludeFromCapture;
    private readonly bool _liveBackdrop;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private readonly double _fastFrameSeconds;
    private readonly double _mediumFrameSeconds;
    private readonly double _slowFrameSeconds;
    private readonly double _idleFrameSeconds;
    private readonly IntPtr _frameTimer;
    private readonly IntPtr[]? _timerWaitHandles;
    private readonly int _displayHz;

    public IslandWindow(IslandRenderer renderer, Action<Point> showMenu, bool excludeFromCapture,
        bool liveBackdrop)
    {
        _renderer = renderer;
        _showMenu = showMenu;
        _excludeFromCapture = excludeFromCapture;
        _liveBackdrop = liveBackdrop;

        Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        float dpi = 96f;
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            dpi = g.DpiX;
        }
        catch { }
        _scale = Math.Clamp(dpi / 96f, 0.75f, 3f);
        _physW = (int)Math.Round(LogicalWidth * _scale);
        _physH = (int)Math.Round(LogicalHeight * _scale);
        _screenX = screen.Bounds.Left + (screen.Bounds.Width - _physW) / 2;
        _screenY = screen.Bounds.Top;
        _renderer.SetDpiScale(_scale);

        _displayHz = QueryDisplayRefreshRate();
        int fastDivisor = Math.Max(1,
            (int)Math.Round(_displayHz / 60.0, MidpointRounding.AwayFromZero));
        while (fastDivisor > 1 && _displayHz / (double)fastDivisor < 50.0)
            fastDivisor--;
        double vblank = 1.0 / _displayHz;
        _fastFrameSeconds = vblank * fastDivisor;
        _mediumFrameSeconds = _fastFrameSeconds * 2.0;
        _slowFrameSeconds = _fastFrameSeconds * 6.0;
        _idleFrameSeconds = _fastFrameSeconds * 15.0;

        _frameTimer = Win32.CreateWaitableTimerEx(IntPtr.Zero, null,
            Win32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Win32.TIMER_ALL_ACCESS);
        if (_frameTimer != IntPtr.Zero)
        {
            _timerWaitHandles = new[]
            {
                _frameTimer,
                _wake.SafeWaitHandle.DangerousGetHandle()
            };
        }
        var cp = new CreateParams
        {
            Caption = "Wisland",
            X = _screenX,
            Y = _screenY,
            Width = _physW,
            Height = _physH,
            Style = Win32.WS_POPUP,
            ExStyle = Win32.WS_EX_LAYERED | Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE
        };
        CreateHandle(cp);
        Win32.SetWindowDisplayAffinity(Handle,
            _liveBackdrop && _excludeFromCapture ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE);
        int corner = Win32.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        InitSurface();
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "Wisland render",
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.Start();

        ShowWindow(true);
        if (_renderer.Passthrough) SetPassthrough(true);

        Win32.RegisterHotKey(Handle, 1, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)'I');
        Win32.RegisterHotKey(Handle, 2, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)'V');
        Win32.RegisterHotKey(Handle, 3, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)'S');
        Win32.RegisterHotKey(Handle, 4, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)'M');
        Win32.RegisterHotKey(Handle, 5, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)'O');
        _shellHookMessage = Win32.RegisterWindowMessage("SHELLHOOK");
        if (_shellHookMessage != 0) Win32.RegisterShellHookWindow(Handle);
    }

    private void InitSurface()
    {
        _screenDc = Win32.GetDC(IntPtr.Zero);
        _memDc = Win32.CreateCompatibleDC(_screenDc);
        var bmi = new Win32.BITMAPINFO
        {
            bmiHeader = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = _physW,
                biHeight = -_physH,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = (uint)(_physW * _physH * 4)
            }
        };
        _dib = Win32.CreateDIBSection(_screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
        _oldBitmap = Win32.SelectObject(_memDc, _dib);
        _surface = SKSurface.Create(new SKImageInfo(_physW, _physH, SKColorType.Bgra8888, SKAlphaType.Premul),
            _bits, _physW * 4);
        if (_surface == null) throw new InvalidOperationException("SKSurface.Create failed");

        if (!_liveBackdrop) return;
        _backdrop = new Backdrop(() =>
        {
            if (!Win32.GetWindowRect(Handle, out Win32.RECT rect)) return (0, 0, 0, 0, _scale);
            return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, _scale);
        }, () => _renderer.NeedsFastFrames || _renderer.NeedsMediumFrames,
            on => Win32.SetWindowDisplayAffinity(Handle,
                on ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE),
            _excludeFromCapture);
        _renderer.SetBackdrop(_backdrop);
        _backdrop.Start();
    }

    public void ShowWindow(bool show)
    {
        if (_disposed) return;
        Win32.ShowWindow(Handle, show ? Win32.SW_SHOWNOACTIVATE : Win32.SW_HIDE);
        _shown = show;
    }

    public void ToggleVisible()
    {
        ShowWindow(!_shown);
    }

    public void SetPassthrough(bool on)
    {
        if (_disposed) return;
        _passthrough = on;
        IntPtr ex = Win32.GetWindowLongPtr(Handle, Win32.GWL_EXSTYLE);
        long value = ex.ToInt64();
        if (on) value |= Win32.WS_EX_TRANSPARENT;
        else value &= ~(long)Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLongPtr(Handle, Win32.GWL_EXSTYLE, new IntPtr(value));
    }

    private void RenderLoop()
    {
        double lastTick = _frameClock.Elapsed.TotalSeconds;
        float maxSpringDt = (float)Math.Min(0.05, _fastFrameSeconds * 1.25);
        while (!_stop)
        {
            double frameStart = _frameClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Clamp(frameStart - lastTick, 0.001, 0.05);
            lastTick = frameStart;
            try
            {
                _renderer.Tick(dt, maxSpringDt);
                var canvas = _surface!.Canvas;
                canvas.Save();
                canvas.Scale(_scale);
                _renderer.Draw(canvas);
                canvas.Restore();
                canvas.Flush();
                CommitFrame();
                if (Interlocked.Exchange(ref _snapshotRequest, 0) != 0)
                    SaveSnapshot();
            }
            catch
            {
                // 一帧失败不能让常驻进程退出；下一帧重试。
            }
            if (_stop) break;

            double interval = _renderer.NeedsFastFrames ? _fastFrameSeconds :
                _renderer.NeedsMediumFrames ? _mediumFrameSeconds :
                _renderer.NeedsSlowFrames ? _slowFrameSeconds : _idleFrameSeconds;
            double deadline = frameStart + interval;
            if (deadline > _frameClock.Elapsed.TotalSeconds)
                WaitForFrame(deadline);
        }
    }

    private void WaitForFrame(double deadline)
    {
        while (!_stop)
        {
            double remaining = deadline - _frameClock.Elapsed.TotalSeconds;
            if (remaining <= 0.0002) return;

            if (_timerWaitHandles != null)
            {
                long dueTime = -Math.Max(1L, (long)Math.Round(remaining * 10_000_000.0));
                if (Win32.SetWaitableTimer(_frameTimer, ref dueTime, 0,
                        IntPtr.Zero, IntPtr.Zero, false))
                {
                    uint result = Win32.WaitForMultipleObjects(2, _timerWaitHandles,
                        false, Win32.INFINITE);
                    if (result == Win32.WAIT_OBJECT_0)
                        continue;
                    if (result == Win32.WAIT_OBJECT_0 + 1)
                        return;
                }
            }

            int timeout = Math.Max(1, (int)Math.Ceiling(remaining * 1000.0));
            _wake.WaitOne(timeout);
            return;
        }
    }

    private static int QueryDisplayRefreshRate()
    {
        IntPtr dc = IntPtr.Zero;
        try
        {
            dc = Win32.GetDC(IntPtr.Zero);
            if (dc != IntPtr.Zero)
            {
                int hz = Win32.GetDeviceCaps(dc, Win32.VREFRESH);
                if (hz is >= 30 and <= 360) return hz;
            }
        }
        catch { }
        finally
        {
            if (dc != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, dc);
        }
        return 60;
    }

    public void RequestSnapshot(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path)) _snapshotPath = path;
        Interlocked.Exchange(ref _snapshotRequest, 1);
    }

    private void SaveSnapshot()
    {
        try
        {
            using var image = _surface!.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(_snapshotPath, data.ToArray());
        }
        catch { }
    }

    private void CommitFrame()
    {
        if (!Win32.GetWindowRect(Handle, out Win32.RECT rect)) return;
        var dst = new Win32.POINT(rect.Left, rect.Top);
        var size = new Win32.SIZE(rect.Right - rect.Left, rect.Bottom - rect.Top);
        var src = new Win32.POINT(0, 0);
        var blend = new Win32.BLENDFUNCTION
        {
            BlendOp = Win32.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Win32.AC_SRC_ALPHA
        };
        Win32.UpdateLayeredWindow(Handle, _screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
    }

    protected override void WndProc(ref Message m)
    {
        if (_shellHookMessage != 0 && (uint)m.Msg == _shellHookMessage)
        {
            if (m.WParam.ToInt32() == Win32.HSHELL_FLASH)
                _renderer.OnShellFlash(m.LParam);
            return;
        }
        switch (m.Msg)
        {
            case Win32.WM_NCHITTEST:
                {
                    var pt = new Win32.POINT((short)(m.LParam.ToInt64() & 0xFFFF),
                        (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                    Win32.ScreenToClient(Handle, ref pt);
                    float lx = pt.X / _scale;
                    float ly = pt.Y / _scale;
                    m.Result = _passthrough || !_renderer.HitTestShape(lx, ly)
                        ? new IntPtr(Win32.HTTRANSPARENT)
                        : new IntPtr(Win32.HTCLIENT);
                    return;
                }
            case Win32.WM_MOUSEMOVE:
                {
                    TrackLeave();
                    var (x, y) = ClientPoint(m.LParam);
                    _renderer.MouseMove(x, y);
                    _wake.Set();
                    return;
                }
            case Win32.WM_MOUSELEAVE:
                _renderer.MouseLeave();
                _wake.Set();
                return;
            case Win32.WM_LBUTTONDOWN:
                {
                    var (x, y) = ClientPoint(m.LParam);
                    _renderer.MouseDown(x, y, true);
                    Win32.SetCapture(Handle);
                    _wake.Set();
                    return;
                }
            case Win32.WM_LBUTTONUP:
                {
                    var (x, y) = ClientPoint(m.LParam);
                    _renderer.MouseUp(x, y, true);
                    Win32.ReleaseCapture();
                    _wake.Set();
                    return;
                }
            case Win32.WM_CAPTURECHANGED:
                _renderer.MouseCaptureLost();
                _wake.Set();
                return;
            case Win32.WM_RBUTTONUP:
                _renderer.RightClick();
                _wake.Set();
                return;
            case Win32.WM_MOUSEWHEEL:
                {
                    int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    _renderer.Wheel(delta);
                    _wake.Set();
                    return;
                }
            case Win32.WM_HOTKEY:
                {
                    int id = m.WParam.ToInt32();
                    if (id == 1) _renderer.ToggleExpanded();
                    else if (id == 2) _renderer.ShowPage(5);
                    else if (id == 3) RequestSnapshot();
                    else if (id == 4) _renderer.ShowPage(1);
                    else if (id == 5) _renderer.OpenConfig();
                    _wake.Set();
                    return;
                }
            case WM_APP_PAGE:
                _renderer.ShowPage(m.WParam.ToInt32());
                _wake.Set();
                return;
            case WM_APP_CLICK:
                _renderer.DebugClick((short)(m.WParam.ToInt64() & 0xFFFF),
                    (short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                _wake.Set();
                return;
            case WM_APP_DEMO:
                _renderer.LoadDemoData();
                _wake.Set();
                return;
        }
        base.WndProc(ref m);
    }

    private (float X, float Y) ClientPoint(IntPtr lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return (x / _scale, y / _scale);
    }

    private void TrackLeave()
    {
        var tme = new Win32.TRACKMOUSEEVENT
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
            dwFlags = Win32.TME_LEAVE,
            hwndTrack = Handle
        };
        Win32.TrackMouseEvent(ref tme);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop = true;
        _wake.Set();
        try { Win32.UnregisterHotKey(Handle, 1); } catch { }
        try { Win32.UnregisterHotKey(Handle, 2); } catch { }
        try { Win32.UnregisterHotKey(Handle, 3); } catch { }
        try { Win32.UnregisterHotKey(Handle, 4); } catch { }
        try { Win32.UnregisterHotKey(Handle, 5); } catch { }
        try { if (_shellHookMessage != 0) Win32.DeregisterShellHookWindow(Handle); } catch { }
        try { _renderThread.Join(700); } catch { }
        try { if (_frameTimer != IntPtr.Zero) Win32.CloseHandle(_frameTimer); } catch { }
        try { _backdrop?.Dispose(); } catch { }
        try { _surface?.Dispose(); } catch { }
        try { if (_oldBitmap != IntPtr.Zero) Win32.SelectObject(_memDc, _oldBitmap); } catch { }
        try { if (_dib != IntPtr.Zero) Win32.DeleteObject(_dib); } catch { }
        try { if (_memDc != IntPtr.Zero) Win32.DeleteDC(_memDc); } catch { }
        try { if (_screenDc != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, _screenDc); } catch { }
        _wake.Dispose();
        try { DestroyHandle(); } catch { }
    }
}
