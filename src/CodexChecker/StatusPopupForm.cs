using System.Diagnostics;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;

namespace CodexChecker;

public sealed class StatusPopupForm : Form
{
    private readonly Label _codexHeader = new();
    private readonly Button _closeButton = new();
    private readonly Label _statusLabel = new();
    private readonly LinkLabel _claudeLink = new();

    public event EventHandler? RefreshRequested;
    public event EventHandler? ExitRequested;

    public StatusPopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(460, 142);
        BackColor = Color.FromArgb(31, 34, 38);
        ForeColor = Color.White;
        Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

        _codexHeader.AutoSize = true;
        _codexHeader.Text = "<codex>";
        _codexHeader.Location = new Point(14, 12);
        _codexHeader.ForeColor = Color.FromArgb(230, 238, 247);

        _closeButton.Text = "x";
        _closeButton.Size = new Size(26, 24);
        _closeButton.Location = new Point(422, 8);
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.BackColor = Color.FromArgb(31, 34, 38);
        _closeButton.ForeColor = Color.FromArgb(210, 218, 226);
        _closeButton.TabStop = false;
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.Click += (_, _) => Hide();

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(14, 35);
        _statusLabel.Size = new Size(432, 46);
        _statusLabel.ForeColor = Color.FromArgb(246, 248, 250);
        _statusLabel.Text = "取得中...";

        _claudeLink.AutoSize = true;
        _claudeLink.Location = new Point(14, 92);
        _claudeLink.Text = "Claude Usage を開く";
        _claudeLink.LinkColor = Color.FromArgb(126, 200, 255);
        _claudeLink.ActiveLinkColor = Color.FromArgb(255, 214, 102);
        _claudeLink.VisitedLinkColor = Color.FromArgb(126, 200, 255);
        _claudeLink.LinkClicked += (_, _) => OpenClaudeUsage();

        Controls.Add(_codexHeader);
        Controls.Add(_closeButton);
        Controls.Add(_statusLabel);
        Controls.Add(_claudeLink);

        var menu = new ContextMenuStrip();
        menu.Items.Add("再取得", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("常駐を終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;
        _codexHeader.ContextMenuStrip = menu;
        _statusLabel.ContextMenuStrip = menu;
        _claudeLink.ContextMenuStrip = menu;

        MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                Hide();
            }
        };

        foreach (Control control in Controls)
        {
            control.MouseClick += (_, args) =>
            {
                if (args.Button == MouseButtons.Left && control != _claudeLink && control != _closeButton)
                {
                    Hide();
                }
            };
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            var createParams = base.CreateParams;
            createParams.ExStyle |= WS_EX_NOACTIVATE;
            return createParams;
        }
    }

    public void ShowLoading()
    {
        _statusLabel.Text = "取得中...";
    }

    public void ShowSnapshot(RateLimitSnapshot snapshot)
    {
        _statusLabel.Text = RateLimitFormatter.FormatSnapshot(snapshot);
    }

    public void ShowError()
    {
        _statusLabel.Text = "利用量を取得できませんでした。";
    }

    public void PositionAtBottomRight()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    private static void OpenClaudeUsage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://claude.ai/new#settings/usage",
                UseShellExecute = true
            });
        }
        catch (Win32Exception)
        {
        }
    }
}
