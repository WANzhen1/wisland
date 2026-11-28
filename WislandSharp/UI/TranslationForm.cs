using System.Windows.Forms;
using WislandSharp.Core;
using WislandSharp.Services;

namespace WislandSharp.UI;

public sealed class TranslationForm : Form
{
    private static readonly Color Bg = Color.FromArgb(31, 33, 42);
    private static readonly Color Panel = Color.FromArgb(39, 42, 53);
    private static readonly Color Input = Color.FromArgb(21, 23, 30);
    private static readonly Color Fg = Color.FromArgb(235, 237, 245);
    private static readonly Color Muted = Color.FromArgb(160, 169, 188);
    private static readonly Color Accent = Color.FromArgb(48, 126, 235);

    private readonly TranslationService _service;
    private readonly TextBox _input = new();
    private readonly TextBox _output = new();
    private readonly Label _status = new();
    private readonly Button _translate;
    private readonly Button _clipboard;
    private readonly Button _paste;
    private readonly Button _clear;
    private readonly Button _copy;
    private readonly Button _speak;

    public TranslationForm(WislandConfig config, TranslationService service)
    {
        _service = service;
        Text = "Wisland 翻译";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(720, 500);
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        TopMost = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16, 12, 16, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Text = "翻译",
            Dock = DockStyle.Left,
            Width = 120,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            ForeColor = Fg
        });
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.ForeColor = Muted;
        _status.Text = "自动识别源语言 → 简体中文";
        header.Controls.Add(_status);
        root.Controls.Add(header, 0, 0);

        ConfigureBox(_input, false);
        _input.PlaceholderText = "输入日语、英语或其他语言；Ctrl+Enter 翻译";
        _input.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = TranslateInputAsync();
            }
        };
        root.Controls.Add(_input, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 5, 0, 5)
        };
        _translate = MakeButton("翻译", 92, true);
        _translate.Click += (_, _) => _ = TranslateInputAsync();
        _clipboard = MakeButton("翻译剪贴板", 116);
        _clipboard.Click += (_, _) => _ = TranslateClipboardAsync();
        _paste = MakeButton("粘贴", 82);
        _paste.Click += (_, _) => PasteClipboard();
        _clear = MakeButton("清空", 82);
        _clear.Click += (_, _) =>
        {
            _input.Clear();
            _output.Clear();
            _status.ForeColor = Muted;
            _status.Text = "自动识别源语言 → 简体中文";
        };
        actions.Controls.AddRange(new Control[] { _translate, _clipboard, _paste, _clear });
        root.Controls.Add(actions, 0, 2);

        ConfigureBox(_output, true);
        _output.PlaceholderText = "译文会显示在这里";
        root.Controls.Add(_output, 0, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = MakeButton("关闭", 86);
        close.Click += (_, _) => Close();
        _speak = MakeButton("朗读译文", 104);
        _speak.Click += (_, _) => _ = SpeakAsync();
        _copy = MakeButton("复制译文", 104);
        _copy.Click += (_, _) => CopyOutput();
        footer.Controls.AddRange(new Control[] { close, _speak, _copy });
        root.Controls.Add(footer, 0, 4);

        Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_input.Text))
            {
                string clip = SafeClipboardText();
                if (!string.IsNullOrWhiteSpace(clip)) _input.Text = clip;
            }
            _input.Focus();
        };
    }

    private static void ConfigureBox(TextBox box, bool readOnly)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.AcceptsReturn = true;
        box.WordWrap = true;
        box.ReadOnly = readOnly;
        box.BackColor = readOnly ? Panel : Input;
        box.ForeColor = Fg;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Microsoft YaHei UI", 12f);
    }

    private async Task TranslateInputAsync()
    {
        string text = _input.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("没有要翻译的文字", true);
            return;
        }
        if (!_service.CanTranslate)
        {
            SetStatus("请先在设置中填写翻译 API Key 或 MiMo API Key", true);
            return;
        }

        SetBusy(true, "翻译中…");
        _output.Clear();
        string? result = await _service.TranslateTextAsync(text);
        SetBusy(false, result == null ? "翻译失败" : "翻译完成");
        if (result != null)
        {
            _output.Text = result;
        }
        else
        {
            var snapshot = _service.Snapshot;
            SetStatus(string.IsNullOrWhiteSpace(snapshot.Error) ? "翻译失败" : snapshot.Error, true);
        }
    }

    private async Task TranslateClipboardAsync()
    {
        string text = SafeClipboardText().Trim();
        if (text.Length == 0)
        {
            SetStatus("剪贴板里没有文字", true);
            return;
        }
        _input.Text = text;
        await TranslateInputAsync();
    }

    private void PasteClipboard()
    {
        string text = SafeClipboardText();
        if (text.Length > 0) _input.Text = text;
    }

    private void CopyOutput()
    {
        if (string.IsNullOrWhiteSpace(_output.Text)) return;
        try
        {
            Clipboard.SetText(_output.Text);
            SetStatus("已复制译文", false);
        }
        catch (Exception ex)
        {
            SetStatus("复制失败：" + ex.Message, true);
        }
    }

    private async Task SpeakAsync()
    {
        string text = _output.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("还没有译文可朗读", true);
            return;
        }
        try
        {
            _speak.Enabled = false;
            SetStatus("正在合成语音…", false);
            await _service.SpeakAsync(text);
            SetStatus("朗读完成", false);
        }
        catch (Exception ex)
        {
            SetStatus("朗读失败：" + ex.Message, true);
        }
        finally
        {
            _speak.Enabled = true;
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _translate.Enabled = !busy;
        _clipboard.Enabled = !busy;
        _speak.Enabled = !busy;
        SetStatus(status, false);
    }

    private void SetStatus(string text, bool error)
    {
        _status.ForeColor = error ? Color.FromArgb(255, 112, 112) : Muted;
        _status.Text = text;
    }

    private static string SafeClipboardText()
    {
        try { return Clipboard.GetText() ?? ""; } catch { return ""; }
    }

    private static Button MakeButton(string text, int width, bool primary = false)
        => new()
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Panel,
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = primary ? Color.FromArgb(90, 150, 240) : Color.FromArgb(78, 84, 102) },
            Margin = new Padding(6, 0, 0, 0)
        };
}
