using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;
using WislandSharp.Core;

namespace WislandSharp.UI;

/// <summary>
/// 内置配置编辑器：不依赖系统 Notepad / WinUI，避免系统记事本损坏时“编辑配置”直接报错。
/// 保存前做 JSON 结构校验，写盘采用临时文件替换。
/// </summary>
public sealed class ConfigEditorForm : Form
{
    private readonly string _path;
    private readonly TextBox _editor;
    private readonly Label _hint;
    private readonly string _original;
    private bool _saved;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ConfigEditorForm(string path)
    {
        _path = path;
        string raw = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "{}";
        _original = PrettyJson(raw);

        Text = "编辑 Wisland 配置";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(680, 480);
        BackColor = Color.FromArgb(31, 33, 42);
        ForeColor = Color.FromArgb(235, 237, 245);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        KeyPreview = true;
        TopMost = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        var top = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(top, 0, 0);
        var pathLabel = new Label
        {
            Text = path,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(170, 180, 198),
            AutoEllipsis = true
        };
        top.Controls.Add(pathLabel);
        var folder = new Button
        {
            Text = "打开所在文件夹",
            Dock = DockStyle.Right,
            Width = 170,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 60, 76),
            ForeColor = Color.White
        };
        folder.FlatAppearance.BorderColor = Color.FromArgb(92, 99, 120);
        folder.Click += (_, _) => OpenFolder();
        top.Controls.Add(folder);

        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10f),
            BackColor = Color.FromArgb(21, 23, 30),
            ForeColor = Color.FromArgb(232, 235, 242),
            BorderStyle = BorderStyle.FixedSingle,
            Text = _original
        };
        _hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(150, 158, 176),
            Text = "Ctrl+S 保存；保存后自动重新加载配置"
        };
        _editor.TextChanged += (_, _) =>
        {
            _hint.Text = "";
            _hint.ForeColor = Color.FromArgb(150, 158, 176);
        };
        root.Controls.Add(_editor, 0, 1);

        root.Controls.Add(_hint, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };
        root.Controls.Add(buttons, 0, 3);

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 104,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(46, 49, 61),
            ForeColor = Color.White
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(82, 88, 106);
        buttons.Controls.Add(cancel);

        var save = new Button
        {
            Text = "保存并重新加载",
            Width = 164,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(36, 112, 220),
            ForeColor = Color.White
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(80, 140, 235);
        save.Click += (_, _) => SaveConfig();
        buttons.Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                SaveConfig();
            }
        };
        FormClosing += (_, e) =>
        {
            if (_saved || !IsDirty()) return;
            var result = MessageBox.Show(this, "配置还没有保存，确定关闭？", "Wisland",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) e.Cancel = true;
        };
    }

    private bool IsDirty() => !string.Equals(_original, _editor.Text, StringComparison.Ordinal);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PerformLayout();
        _editor.Focus();
        _editor.SelectionStart = 0;
        _editor.SelectionLength = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PerformLayout();
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw, DocumentOptions);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return raw;
        }
    }

    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void SaveConfig()
    {
        string text = _editor.Text;
        try
        {
            using var doc = JsonDocument.Parse(text, DocumentOptions);
            var parsed = JsonSerializer.Deserialize<WislandConfig>(text, SerializerOptions);
            if (parsed == null) throw new JsonException("配置解析结果为空");

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, _path, overwrite: true);
            _saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (JsonException ex)
        {
            _hint.ForeColor = Color.FromArgb(255, 110, 110);
            _hint.Text = $"JSON 格式错误：第 {(ex.LineNumber ?? 0) + 1} 行，第 {(ex.BytePositionInLine ?? 0) + 1} 列 — {ex.Message}";
        }
        catch (Exception ex)
        {
            _hint.ForeColor = Color.FromArgb(255, 110, 110);
            _hint.Text = "保存失败：" + ex.Message;
        }
    }
}
