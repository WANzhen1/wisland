using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WislandSharp.Core;
using WislandSharp.Native;
using WislandSharp.Services;
using WislandSharp.UI;

namespace WislandSharp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\WislandSharp_SingleInstance", out bool created);
        if (!created) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => Debug.WriteLine(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Debug.WriteLine(e.ExceptionObject);

        Application.Run(new WislandContext());
    }
}

internal sealed class WislandContext : ApplicationContext
{
    private readonly WislandConfig _config;
    private readonly MediaService _media;
    private readonly WeatherService _weather;
    private readonly StockService _stocks;
    private readonly SystemService _system;
    private readonly ClipboardService _clipboard;
    private readonly LyricsService _lyrics;
    private readonly NotificationService _notifications;
    private readonly IslandRenderer _renderer;
    private readonly IslandWindow _window;
    private readonly NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private bool _disposed;

    public WislandContext()
    {
        _config = WislandConfig.Load();
        _media = new MediaService(_config);
        _weather = new WeatherService(_config);
        _stocks = new StockService(_config);
        _system = new SystemService();
        _clipboard = new ClipboardService(_config);
        _lyrics = new LyricsService(_config, _media);
        _notifications = new NotificationService(_config);

        IslandWindow? window = null;
        var actions = new IslandActions
        {
            SetPassthrough = on => window?.SetPassthrough(on),
            ShowMenu = () => ShowMenu(Cursor.Position),
            EditConfig = OpenConfig,
            ReloadConfig = Reload,
            Exit = ExitApplication
        };
        _renderer = new IslandRenderer(_config, actions, _media, _weather, _stocks, _system,
            _clipboard, _lyrics, _notifications);
        _window = new IslandWindow(_renderer, ShowMenu, _config.Island.ExcludeFromCapture,
            _config.Island.LiveBackdrop);
        window = _window;

        _menu = BuildMenu();
        _trayIcon = CreateTrayIcon();
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Wisland · 液态灵动岛",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) => _renderer.ToggleExpanded();

        _media.Start();
        _weather.Start();
        _stocks.Start();
        _system.Start();
        _clipboard.Start();
        _notifications.Start();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var toggle = new ToolStripMenuItem("显示 / 隐藏灵动岛");
        toggle.Click += (_, _) => _window.ToggleVisible();
        menu.Items.Add(toggle);

        var theme = new ToolStripMenuItem("主题");
        (string Key, string Label)[] themes =
        {
            ("auto", "跟随背景自动"), ("light", "浅色毛玻璃"), ("dark", "深色毛玻璃"),
            ("rouge", "胭脂撞色"), ("sky", "晴空渐变")
        };
        foreach (var item in themes)
        {
            var mi = new ToolStripMenuItem(item.Label) { Tag = item.Key };
            mi.Click += (_, _) =>
            {
                _renderer.SetTheme(item.Key);
                foreach (ToolStripMenuItem child in theme.DropDownItems)
                    child.Checked = (string?)child.Tag == item.Key;
            };
            mi.Checked = item.Key == _config.Island.Theme;
            theme.DropDownItems.Add(mi);
        }
        menu.Items.Add(theme);
        menu.Items.Add(new ToolStripSeparator());

        var edit = new ToolStripMenuItem("编辑配置");
        edit.Click += (_, _) => OpenConfig();
        menu.Items.Add(edit);

        var reload = new ToolStripMenuItem("重新加载配置");
        reload.Click += (_, _) => Reload();
        menu.Items.Add(reload);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("退出 Wisland");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);
        return menu;
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = new GraphicsPath();
            const float r = 9f;
            path.AddArc(2, 2, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 2, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(2, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var bg = new SolidBrush(Color.FromArgb(255, 30, 32, 40));
            g.FillPath(bg, path);
            using var eye = new SolidBrush(Color.White);
            g.FillEllipse(eye, new RectangleF(9, 10, 5, 13));
            g.FillEllipse(eye, new RectangleF(18, 10, 5, 13));
        }
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Win32.DestroyIcon(handle);
        }
    }

    private void ShowMenu(Point point)
    {
        _menu.Show(point);
        _menu.Focus();
    }

    private void OpenConfig()
    {
        try
        {
            string path = string.IsNullOrWhiteSpace(_config.FilePath)
                ? Path.Combine(AppContext.BaseDirectory, "config.json")
                : _config.FilePath;
            if (!File.Exists(path)) _config.Save();
            using var form = new SettingsForm(_config);
            if (form.ShowDialog() == DialogResult.OK) Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开配置编辑器失败：" + ex.Message, "Wisland",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Reload()
    {
        var next = WislandConfig.Load();
        _renderer.ReloadConfig(next);
        _config.Reload(next);
        _weather.ReloadConfig();
        _renderer.ShowActivity("配置已重新加载", "主题 / 启动项等设置已生效", "\uE72C");
    }

    private void ExitApplication()
    {
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (_disposed) return;
        _disposed = true;
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        try { _trayIcon.Dispose(); } catch { }
        try { _clipboard.Dispose(); } catch { }
        try { _lyrics.Dispose(); } catch { }
        try { _notifications.Dispose(); } catch { }
        try { _system.Dispose(); } catch { }
        try { _stocks.Dispose(); } catch { }
        try { _weather.Dispose(); } catch { }
        try { _media.Dispose(); } catch { }
        try { _window.Dispose(); } catch { }
        try { _menu.Dispose(); } catch { }
        base.ExitThreadCore();
    }
}
