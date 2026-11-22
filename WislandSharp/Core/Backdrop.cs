using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace WislandSharp.Core;

/// <summary>
/// 低频抓取窗口背后的桌面，缩小后交给 Skia 做模糊。
/// 窗口本身用 WDA_EXCLUDEFROMCAPTURE 排除，抓屏里不会拍到自己。
/// </summary>
public sealed class Backdrop : IDisposable
{
    private readonly object _lock = new();
    private readonly Thread _thread;
    private readonly Func<(int X, int Y, int W, int H, float Scale)> _bounds;
    private readonly Func<bool> _isAnimating;
    private readonly Action<bool>? _setCaptureExclusion;
    private readonly bool _alwaysExcluded;
    private volatile bool _stop;
    private SKBitmap? _bitmap;
    private readonly SKPaint _drawPaint;
    private Bitmap? _fullBuffer;
    private long _version;
    private float _luminance = 0.5f;

    public float Luminance
    {
        get { lock (_lock) return _luminance; }
    }

    public Backdrop(Func<(int X, int Y, int W, int H, float Scale)> bounds, Func<bool> isAnimating,
        Action<bool>? setCaptureExclusion = null, bool alwaysExcluded = false)
    {
        _bounds = bounds;
        _isAnimating = isAnimating;
        _setCaptureExclusion = setCaptureExclusion;
        _alwaysExcluded = alwaysExcluded;
        _drawPaint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Medium
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "Wisland backdrop" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop = true;
        try { _thread.Join(500); } catch { }
        lock (_lock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        _drawPaint.Dispose();
        _fullBuffer?.Dispose();
        _fullBuffer = null;
    }

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                var (x, y, w, h, scale) = _bounds();
                if (w > 8 && h > 8)
                {
                    int dw = Math.Max(8, (int)Math.Round(w / scale / 4f));
                    int dh = Math.Max(8, (int)Math.Round(h / scale / 4f));
                    SKBitmap? next = null;
                    try
                    {
                        if (!_alwaysExcluded) _setCaptureExclusion?.Invoke(true);
                        next = Capture(x, y, w, h, dw, dh);
                    }
                    finally
                    {
                        if (!_alwaysExcluded) _setCaptureExclusion?.Invoke(false);
                    }
                    if (next != null)
                    {
                        lock (_lock)
                        {
                            _bitmap?.Dispose();
                            _bitmap = next;
                            _version++;
                            _luminance = EstimateLuminance(next);
                        }
                    }
                }
            }
            catch
            {
                // 抓屏失败（全屏独占 / DWM 重置）时保留上一帧。
            }

            int sleep = _isAnimating() ? 180 : 450;
            for (int i = 0; i < sleep && !_stop; i += 20)
                Thread.Sleep(20);
        }
    }

    private SKBitmap? Capture(int x, int y, int w, int h, int dw, int dh)
    {
        if (_fullBuffer == null || _fullBuffer.Width != w || _fullBuffer.Height != h)
        {
            _fullBuffer?.Dispose();
            _fullBuffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }
        using (var fg = Graphics.FromImage(_fullBuffer))
        {
            fg.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        using var small = new Bitmap(dw, dh, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_fullBuffer, new Rectangle(0, 0, dw, dh));
        }

        var info = new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul);
        var raw = new SKBitmap(info);
        var rect = new Rectangle(0, 0, dw, dh);
        BitmapData data = small.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int copy = Math.Min(raw.RowBytes, Math.Abs(data.Stride));
            for (int row = 0; row < dh; row++)
            {
                IntPtr src = IntPtr.Add(data.Scan0, row * data.Stride);
                IntPtr dst = IntPtr.Add(raw.GetPixels(), row * raw.RowBytes);
                CopyMemory(dst, src, (uint)copy);
            }
        }
        finally
        {
            small.UnlockBits(data);
        }
        var blurred = new SKBitmap(info);
        using (var canvas = new SKCanvas(blurred))
        using (var paint = new SKPaint
               {
                   IsAntialias = true,
                   FilterQuality = SKFilterQuality.Medium,
                   ImageFilter = SKImageFilter.CreateBlur(3f, 3f)
               })
            canvas.DrawBitmap(raw, 0, 0, paint);
        raw.Dispose();
        return blurred;
    }

    private static float EstimateLuminance(SKBitmap bitmap)
    {
        double sum = 0;
        int count = 0;
        int stepX = Math.Max(1, bitmap.Width / 24);
        int stepY = Math.Max(1, bitmap.Height / 16);
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                sum += (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;
                count++;
            }
        }
        return count == 0 ? 0.5f : (float)(sum / count);
    }

    public bool TryDraw(SKCanvas canvas, SKRect dst)
    {
        lock (_lock)
        {
            if (_bitmap == null) return false;
            canvas.DrawBitmap(_bitmap, dst, _drawPaint);
            return true;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, uint length);
}
