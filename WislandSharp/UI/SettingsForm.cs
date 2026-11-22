using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using WislandSharp.Core;

namespace WislandSharp.UI;

/// <summary>
/// 普通用户使用的可视化设置窗口。JSON 编辑器只放在“高级”页，不再作为默认入口。
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Color Bg = Color.FromArgb(31, 33, 42);
    private static readonly Color Panel = Color.FromArgb(39, 42, 53);
    private static readonly Color Input = Color.FromArgb(21, 23, 30);
    private static readonly Color Fg = Color.FromArgb(235, 237, 245);
    private static readonly Color Muted = Color.FromArgb(160, 169, 188);
    private static readonly Color Accent = Color.FromArgb(48, 126, 235);

    private readonly WislandConfig _config;
    private readonly TabControl _tabs;
    private readonly TableLayoutPanel _commonFlow;
    private readonly TableLayoutPanel _notifyFlow;
    private readonly Label _hint;

    private readonly ComboBox _theme = new();
    private readonly ComboBox _expandMode = new();
    private readonly ComboBox _compactView = new();
    private readonly NumericUpDown _hoverMs = new();
    private readonly NumericUpDown _collapseMs = new();
    private readonly NumericUpDown _autoCycle = new();
    private readonly CheckBox _liveBackdrop = new();
    private readonly CheckBox _excludeCapture = new();
    private readonly CheckBox _mediaAutoShow = new();
    private readonly CheckBox _mediaLyrics = new();
    private readonly NumericUpDown _clipboardMax = new();
    private readonly NumericUpDown _stockInterval = new();
    private readonly TextBox _city = new();
    private readonly TextBox _lat = new();
    private readonly TextBox _lon = new();
    private readonly NumericUpDown _weatherInterval = new();
    private readonly DataGridView _launchers = new();

    private readonly CheckBox _notifyEnabled = new();
    private readonly CheckBox _notifyPreview = new();
    private readonly TextBox _qqPath = new();
    private readonly TextBox _wechatPath = new();
    private readonly TextBox _mailPath = new();

    private bool _saved;

    public SettingsForm(WislandConfig config)
    {
        _config = config;
        Text = "Wisland 设置";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1050, 800);
        MinimumSize = new Size(880, 620);
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        TopMost = true;
        KeyPreview = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(header, 0, 0);
        header.Controls.Add(new Label
        {
            Text = "Wisland 设置",
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 240,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Fg
        });
        header.Controls.Add(new Label
        {
            Text = config.FilePath,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            ForeColor = Muted,
            Font = new Font("Consolas", 8.5f)
        });

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(132, 34),
            SizeMode = TabSizeMode.Fixed,
            Font = new Font("Microsoft YaHei UI", 10f)
        };
        _tabs.DrawItem += DrawTab;
        root.Controls.Add(_tabs, 0, 1);

        var commonPage = NewPage("常用");
        _commonFlow = NewFlow(commonPage);
        BuildCommonTab();
        _tabs.TabPages.Add(commonPage);

        var notifyPage = NewPage("消息");
        _notifyFlow = NewFlow(notifyPage);
        BuildNotifyTab();
        _tabs.TabPages.Add(notifyPage);

        var advancedPage = NewPage("高级");
        BuildAdvancedTab(advancedPage);
        _tabs.TabPages.Add(advancedPage);

        var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        root.Controls.Add(footer, 0, 2);
        _hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Muted,
            AutoEllipsis = true
        };
        footer.Controls.Add(_hint);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 340,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        footer.Controls.Add(buttons);

        var cancel = MakeButton("取消", 96);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);

        var save = MakeButton("保存并应用", 146, primary: true);
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (_saved || e.CloseReason == CloseReason.ApplicationExitCall) return;
            if (e.CloseReason != CloseReason.UserClosing) return;
            var result = MessageBox.Show(this, "设置还没有保存，确定关闭？", "Wisland",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) e.Cancel = true;
        };

        LoadValuesFrom(_config);
    }

    private void BuildCommonTab()
    {
        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        AddCombo(_theme,
            ("auto", "跟随系统 / 自动"), ("dark", "深色毛玻璃"), ("light", "浅色毛玻璃"),
            ("rouge", "胭脂撞色"), ("sky", "晴空渐变"));
        StyleInput(_theme);

        _expandMode.DropDownStyle = ComboBoxStyle.DropDownList;
        AddCombo(_expandMode, ("hover", "悬停展开"), ("always", "常驻展开"), ("never", "保持收起"));
        StyleInput(_expandMode);

        _compactView.DropDownStyle = ComboBoxStyle.DropDownList;
        AddCombo(_compactView, ("clock", "时间天气"), ("pet", "小形象"), ("music", "正在播放"));
        StyleInput(_compactView);

        ConfigureNumber(_hoverMs, 0, 2000, 20);
        ConfigureNumber(_collapseMs, 0, 5000, 20);
        ConfigureNumber(_autoCycle, 0, 600, 5);

        _liveBackdrop.Text = "实时桌面毛玻璃（更耗性能）";
        _liveBackdrop.ForeColor = Fg;
        _excludeCapture.Text = "截图 / 录屏时隐藏灵动岛";
        _excludeCapture.ForeColor = Fg;
        _mediaAutoShow.Text = "开始播放时自动切到媒体视图";
        _mediaAutoShow.ForeColor = Fg;
        _mediaLyrics.Text = "显示网易云歌词";
        _mediaLyrics.ForeColor = Fg;

        ConfigureNumber(_clipboardMax, 1, 30, 1);
        ConfigureNumber(_stockInterval, 5, 120, 5);
        ConfigureNumber(_weatherInterval, 1, 180, 5);
        StyleInput(_city);
        StyleInput(_lat);
        StyleInput(_lon);

        var appearance = Group("外观与行为");
        var a = Grid(9);
        AddRow(a, 0, "主题", _theme);
        AddRow(a, 1, "显示模式", _expandMode);
        AddRow(a, 2, "收起态视图", _compactView);
        AddRow(a, 3, "自动轮换（秒，0 = 不轮换）", _autoCycle);
        AddRow(a, 4, "悬停展开延迟（毫秒）", _hoverMs);
        AddRow(a, 5, "离开收起延迟（毫秒）", _collapseMs);
        AddRow(a, 6, "", _liveBackdrop);
        AddRow(a, 7, "", _excludeCapture);
        var perfHint = new Label
        {
            Text = "关闭实时毛玻璃会使用静态渐变，优先保证流畅；开启后会低频抓取桌面。",
            AutoSize = true,
            ForeColor = Muted,
            Padding = new Padding(0, 2, 0, 6)
        };
        a.Controls.Add(perfHint, 1, 8);
        appearance.Controls.Add(a);
        AddGroup(_commonFlow, appearance);

        var content = Group("显示内容");
        var c = Grid(4);
        AddRow(c, 0, "", _mediaAutoShow);
        AddRow(c, 1, "", _mediaLyrics);
        AddRow(c, 2, "剪贴板保留条数", _clipboardMax);
        AddRow(c, 3, "股票刷新间隔（秒）", _stockInterval);
        content.Controls.Add(c);
        AddGroup(_commonFlow, content);

        var weather = Group("天气");
        var w = Grid(4);
        AddRow(w, 0, "城市", _city);
        AddRow(w, 1, "纬度（可选）", _lat);
        AddRow(w, 2, "经度（可选）", _lon);
        AddRow(w, 3, "刷新间隔（分钟）", _weatherInterval);
        weather.Controls.Add(w);
        AddGroup(_commonFlow, weather);

        var launcher = Group("应用启动器");
        var lp = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        lp.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        lp.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _launchers.Dock = DockStyle.Fill;
        _launchers.AllowUserToAddRows = false;
        _launchers.AllowUserToDeleteRows = false;
        _launchers.RowHeadersVisible = false;
        _launchers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _launchers.BackgroundColor = Input;
        _launchers.GridColor = Color.FromArgb(62, 68, 84);
        _launchers.DefaultCellStyle.BackColor = Input;
        _launchers.DefaultCellStyle.ForeColor = Fg;
        _launchers.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 82, 132);
        _launchers.DefaultCellStyle.SelectionForeColor = Color.White;
        _launchers.ColumnHeadersDefaultCellStyle.BackColor = Panel;
        _launchers.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
        _launchers.EnableHeadersVisualStyles = false;
        _launchers.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "名称", FillWeight = 28 });
        _launchers.Columns.Add(new DataGridViewTextBoxColumn { Name = "path", HeaderText = "程序 / 文件路径", FillWeight = 52 });
        _launchers.Columns.Add(new DataGridViewTextBoxColumn { Name = "args", HeaderText = "参数", FillWeight = 20 });
        lp.Controls.Add(_launchers, 0, 0);

        var lb = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var add = MakeButton("添加启动项", 116);
        add.Click += (_, _) => _launchers.Rows.Add("新启动项", "", "");
        lb.Controls.Add(add);
        var del = MakeButton("删除选中", 104);
        del.Click += (_, _) =>
        {
            if (_launchers.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in _launchers.SelectedRows)
                    if (!row.IsNewRow) _launchers.Rows.Remove(row);
            }
        };
        lb.Controls.Add(del);
        var browse = MakeButton("浏览程序…", 110);
        browse.Click += (_, _) => BrowseIntoSelectedLauncher();
        lb.Controls.Add(browse);
        lp.Controls.Add(lb, 0, 1);

        launcher.Controls.Add(lp);
        AddGroup(_commonFlow, launcher);
    }

    private void BuildNotifyTab()
    {
        _notifyEnabled.Text = "启用 Windows 消息通知";
        _notifyEnabled.ForeColor = Fg;
        _notifyPreview.Text = "在消息页显示消息预览文字";
        _notifyPreview.ForeColor = Fg;
        StyleInput(_qqPath);
        StyleInput(_wechatPath);
        StyleInput(_mailPath);

        var main = Group("消息通知");
        var mg = Grid(2);
        AddRow(mg, 0, "", _notifyEnabled);
        AddRow(mg, 1, "", _notifyPreview);
        main.Controls.Add(mg);
        AddGroup(_notifyFlow, main);

        var open = Group("点击消息时打开的应用");
        var og = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3
        };
        og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        og.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        for (int i = 0; i < 3; i++) og.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        AddPathRow(og, 0, "QQ", _qqPath);
        AddPathRow(og, 1, "微信", _wechatPath);
        AddPathRow(og, 2, "邮件", _mailPath);
        open.Controls.Add(og);
        AddGroup(_notifyFlow, open);

        var mapping = Group("高级匹配");
        var mapPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        mapPanel.Controls.Add(new Label
        {
            Text = "默认已覆盖 QQ、微信、Thunderbird。其他通知前缀或任务栏闪烁进程，请用高级页的 JSON 编辑器修改。",
            AutoSize = true,
            ForeColor = Muted,
            Padding = new Padding(0, 2, 0, 10)
        }, 0, 0);
        var raw = MakeButton("打开 JSON 编辑器（高级）", 194);
        raw.Click += (_, _) => OpenRawEditor();
        mapPanel.Controls.Add(raw, 0, 1);
        mapping.Controls.Add(mapPanel);
        AddGroup(_notifyFlow, mapping);
    }

    private void BuildAdvancedTab(TabPage page)
    {
        var flow = NewFlow(page);
        var box = Group("开发者选项");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5
        };
        panel.Controls.Add(new Label
        {
            Text = "日常修改请使用“常用”和“消息”页。这里只用于插件前缀、媒体优先级等高级字段。",
            AutoSize = true,
            ForeColor = Muted,
            Padding = new Padding(0, 2, 0, 10)
        }, 0, 0);
        var raw = MakeButton("打开 JSON 编辑器（高级）", 194);
        raw.Click += (_, _) => OpenRawEditor();
        panel.Controls.Add(raw, 0, 1);
        var folder = MakeButton("打开配置文件所在文件夹", 194);
        folder.Click += (_, _) => OpenConfigFolder();
        panel.Controls.Add(folder, 0, 2);
        var reset = MakeButton("恢复默认值（不会立即保存）", 210);
        reset.Click += (_, _) => ResetToDefaults();
        panel.Controls.Add(reset, 0, 3);
        panel.Controls.Add(new Label
        {
            Text = "配置文件：" + _config.FilePath,
            AutoSize = true,
            ForeColor = Muted,
            Padding = new Padding(0, 14, 0, 0)
        }, 0, 4);
        box.Controls.Add(panel);
        AddGroup(flow, box);
    }

    private void LoadValuesFrom(WislandConfig source)
    {
        SelectKey(_theme, source.Island.Theme);
        SelectKey(_expandMode, source.Island.ExpandMode);
        SelectKey(_compactView, source.Island.CompactView);
        _hoverMs.Value = Clamp(source.Island.HoverExpandMs, _hoverMs.Minimum, _hoverMs.Maximum);
        _collapseMs.Value = Clamp(source.Island.CollapseDelayMs, _collapseMs.Minimum, _collapseMs.Maximum);
        _autoCycle.Value = Clamp(source.Island.AutoCycleSeconds, _autoCycle.Minimum, _autoCycle.Maximum);
        _liveBackdrop.Checked = source.Island.LiveBackdrop;
        _excludeCapture.Checked = source.Island.ExcludeFromCapture;
        _mediaAutoShow.Checked = source.Media.AutoShow;
        _mediaLyrics.Checked = source.Media.Lyrics;
        _clipboardMax.Value = Clamp(source.Clipboard.MaxItems, _clipboardMax.Minimum, _clipboardMax.Maximum);
        _stockInterval.Value = Clamp(source.Stocks.IntervalSeconds, _stockInterval.Minimum, _stockInterval.Maximum);
        _city.Text = source.Weather.City;
        _lat.Text = source.Weather.Latitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        _lon.Text = source.Weather.Longitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        _weatherInterval.Value = Clamp(source.Weather.IntervalMinutes, _weatherInterval.Minimum, _weatherInterval.Maximum);
        _notifyEnabled.Checked = source.Notify.Enabled;
        _notifyPreview.Checked = source.Notify.ShowPreview;
        _qqPath.Text = GetDictionary(source.Notify.Open, "qq");
        _wechatPath.Text = GetDictionary(source.Notify.Open, "wechat");
        _mailPath.Text = GetDictionary(source.Notify.Open, "mail");

        _launchers.Rows.Clear();
        foreach (var item in source.Launcher)
            _launchers.Rows.Add(item.Name, item.Path, item.Args);
    }

    private void SaveAndClose()
    {
        if (!TryParseNullable(_lat.Text, out double? lat) ||
            !TryParseNullable(_lon.Text, out double? lon))
        {
            _hint.ForeColor = Color.FromArgb(255, 112, 112);
            _hint.Text = "纬度 / 经度必须是数字，或者留空让天气服务自动查询城市。";
            return;
        }
        if (lat.HasValue != lon.HasValue)
        {
            _hint.ForeColor = Color.FromArgb(255, 112, 112);
            _hint.Text = "纬度和经度要么都填写，要么都留空。";
            return;
        }

        _config.Island.Theme = SelectedKey(_theme);
        _config.Island.ExpandMode = SelectedKey(_expandMode);
        _config.Island.CompactView = SelectedKey(_compactView);
        _config.Island.HoverExpandMs = (int)_hoverMs.Value;
        _config.Island.CollapseDelayMs = (int)_collapseMs.Value;
        _config.Island.AutoCycleSeconds = (int)_autoCycle.Value;
        _config.Island.LiveBackdrop = _liveBackdrop.Checked;
        _config.Island.ExcludeFromCapture = _excludeCapture.Checked;
        _config.Media.AutoShow = _mediaAutoShow.Checked;
        _config.Media.Lyrics = _mediaLyrics.Checked;
        _config.Clipboard.MaxItems = (int)_clipboardMax.Value;
        _config.Stocks.IntervalSeconds = (int)_stockInterval.Value;
        _config.Weather.City = string.IsNullOrWhiteSpace(_city.Text) ? "上海" : _city.Text.Trim();
        _config.Weather.Latitude = lat;
        _config.Weather.Longitude = lon;
        _config.Weather.IntervalMinutes = (int)_weatherInterval.Value;
        _config.Notify.Enabled = _notifyEnabled.Checked;
        _config.Notify.ShowPreview = _notifyPreview.Checked;
        SetDictionary(_config.Notify.Open, "qq", _qqPath.Text.Trim());
        SetDictionary(_config.Notify.Open, "wechat", _wechatPath.Text.Trim());
        SetDictionary(_config.Notify.Open, "mail", _mailPath.Text.Trim());

        var launchers = new List<LauncherItem>();
        foreach (DataGridViewRow row in _launchers.Rows)
        {
            if (row.IsNewRow) continue;
            string path = Convert.ToString(row.Cells["path"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(path)) continue;
            string name = Convert.ToString(row.Cells["name"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(path);
            launchers.Add(new LauncherItem
            {
                Name = name,
                Path = path,
                Args = Convert.ToString(row.Cells["args"].Value)?.Trim() ?? ""
            });
        }
        _config.Launcher = launchers;

        try
        {
            _config.Save();
            _saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _hint.ForeColor = Color.FromArgb(255, 112, 112);
            _hint.Text = "保存失败：" + ex.Message;
        }
    }

    private void OpenRawEditor()
    {
        try
        {
            using var editor = new ConfigEditorForm(_config.FilePath);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            var next = WislandConfig.Load();
            _config.Reload(next);
            LoadValuesFrom(_config);
            _hint.ForeColor = Color.FromArgb(140, 210, 160);
            _hint.Text = "JSON 已保存并重新读取。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开 JSON 编辑器失败：" + ex.Message, "Wisland",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_config.FilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ResetToDefaults()
    {
        var result = MessageBox.Show(this, "把表单恢复为默认值？点击“保存并应用”后才会写入。", "Wisland",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.Yes) LoadValuesFrom(new WislandConfig());
    }

    private void BrowseIntoSelectedLauncher()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择要启动的程序",
            Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (_launchers.SelectedRows.Count == 0 && _launchers.Rows.Count > 0)
            _launchers.Rows[0].Selected = true;
        if (_launchers.SelectedRows.Count == 0) _launchers.Rows.Add();
        var row = _launchers.SelectedRows.Count > 0
            ? _launchers.SelectedRows[0]
            : _launchers.Rows[^1];
        row.Cells["path"].Value = dialog.FileName;
        if (string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["name"].Value)))
            row.Cells["name"].Value = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        bool selected = e.Index == _tabs.SelectedIndex;
        using var back = new SolidBrush(selected ? Color.FromArgb(49, 53, 68) : Bg);
        e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.Left + 16, e.Bounds.Bottom - 3, e.Bounds.Width - 32, 3);
        }
        string text = _tabs.TabPages[e.Index].Text;
        TextRenderer.DrawText(e.Graphics, text, _tabs.Font, e.Bounds,
            selected ? Fg : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static TabPage NewPage(string text)
        => new(text)
        {
            BackColor = Bg,
            ForeColor = Fg,
            Padding = new Padding(0),
            AutoScroll = true
        };

    private static TableLayoutPanel NewFlow(TabPage page)
    {
        var flow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoScroll = true,
            Padding = new Padding(12),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(flow);
        return flow;
    }

    private void AddGroup(TableLayoutPanel flow, GroupBox group)
    {
        group.Dock = DockStyle.Fill;
        group.Margin = new Padding(0, 0, 0, 12);
        flow.RowCount++;
        flow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        flow.Controls.Add(group, 0, flow.RowCount - 1);
    }

    private static GroupBox Group(string text)
        => new()
        {
            Text = text,
            ForeColor = Fg,
            BackColor = Panel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Padding = new Padding(12, 8, 12, 12),
            Margin = new Padding(0)
        };

    private static TableLayoutPanel Grid(int rows)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = rows
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        if (label.Length > 0)
        {
            grid.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg
            }, 0, row);
        }
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 5, 0, 5);
        grid.Controls.Add(control, 1, row);
    }

    private static void AddPathRow(TableLayoutPanel grid, int row, string label, TextBox box)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Fg
        }, 0, row);
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 5, 8, 5);
        grid.Controls.Add(box, 1, row);
        var browse = MakeButton("浏览…", 78);
        browse.Margin = new Padding(0, 5, 0, 5);
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择应用",
                Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.FileName;
        };
        grid.Controls.Add(browse, 2, row);
    }

    private static void ConfigureNumber(NumericUpDown number, decimal min, decimal max, decimal step)
    {
        number.Minimum = min;
        number.Maximum = max;
        number.Increment = step;
        number.DecimalPlaces = 0;
        number.ThousandsSeparator = false;
        number.BorderStyle = BorderStyle.FixedSingle;
        number.BackColor = Input;
        number.ForeColor = Fg;
    }

    private static void StyleInput(Control control)
    {
        control.BackColor = Input;
        control.ForeColor = Fg;
        if (control is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
        if (control is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
    }

    private static Button MakeButton(string text, int width, bool primary = false)
        => new()
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.FromArgb(49, 53, 68),
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = primary ? Color.FromArgb(90, 150, 240) : Color.FromArgb(78, 84, 102) },
            Margin = new Padding(8, 0, 0, 0)
        };

    private static void AddCombo(ComboBox combo, params (string Key, string Text)[] items)
    {
        foreach (var item in items) combo.Items.Add(new ComboItem(item.Key, item.Text));
    }

    private static void SelectKey(ComboBox combo, string key)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboItem item && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedKey(ComboBox combo)
        => combo.SelectedItem is ComboItem item ? item.Key : "";

    private static decimal Clamp(int value, decimal min, decimal max)
        => Math.Clamp(value, (int)min, (int)max);

    private static string GetDictionary(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out string? value) ? value ?? "" : "";

    private static void SetDictionary(Dictionary<string, string> map, string key, string value)
        => map[key] = value;

    private static bool TryParseNullable(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            value = v;
            return true;
        }
        return false;
    }

    private sealed class ComboItem
    {
        public string Key { get; }
        private string Text { get; }
        public ComboItem(string key, string text) { Key = key; Text = text; }
        public override string ToString() => Text;
    }
}
