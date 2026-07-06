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
    private readonly Label _primaryLabel = new();
    private readonly RemainingBar _primaryBar = new();
    private readonly Label _primaryDetail = new();
    private readonly Label _secondaryLabel = new();
    private readonly RemainingBar _secondaryBar = new();
    private readonly Label _secondaryDetail = new();
    private readonly Label _claudeLabel = new();
    private readonly LinkLabel _claudeLink = new();

    public event EventHandler? RefreshRequested;
    public event EventHandler? ExitRequested;

    public StatusPopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(500, 142);
        BackColor = Color.FromArgb(31, 34, 38);
        ForeColor = Color.White;
        Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

        _codexHeader.AutoSize = true;
        _codexHeader.Text = "Codex";
        _codexHeader.Location = new Point(14, 12);
        _codexHeader.ForeColor = Color.FromArgb(230, 238, 247);

        _closeButton.Text = "x";
        _closeButton.Size = new Size(26, 24);
        _closeButton.Location = new Point(462, 8);
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.BackColor = Color.FromArgb(31, 34, 38);
        _closeButton.ForeColor = Color.FromArgb(210, 218, 226);
        _closeButton.TabStop = false;
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.Click += (_, _) => Hide();

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(14, 38);
        _statusLabel.Size = new Size(472, 46);
        _statusLabel.ForeColor = Color.FromArgb(246, 248, 250);
        _statusLabel.Text = "取得中...";

        ConfigureRow(_primaryLabel, _primaryBar, _primaryDetail, "5h", top: 40);
        ConfigureRow(_secondaryLabel, _secondaryBar, _secondaryDetail, "1w", top: 64);

        _claudeLabel.AutoSize = true;
        _claudeLabel.Location = new Point(14, 92);
        _claudeLabel.Text = "Claude";
        _claudeLabel.ForeColor = Color.FromArgb(246, 248, 250);

        _claudeLink.AutoSize = true;
        _claudeLink.Location = new Point(70, 92);
        _claudeLink.Text = "Usage を開く";
        _claudeLink.LinkColor = Color.FromArgb(126, 200, 255);
        _claudeLink.ActiveLinkColor = Color.FromArgb(255, 214, 102);
        _claudeLink.VisitedLinkColor = Color.FromArgb(126, 200, 255);
        _claudeLink.LinkClicked += (_, _) => OpenClaudeUsage();

        Controls.Add(_codexHeader);
        Controls.Add(_closeButton);
        Controls.Add(_statusLabel);
        Controls.Add(_primaryLabel);
        Controls.Add(_primaryBar);
        Controls.Add(_primaryDetail);
        Controls.Add(_secondaryLabel);
        Controls.Add(_secondaryBar);
        Controls.Add(_secondaryDetail);
        Controls.Add(_claudeLabel);
        Controls.Add(_claudeLink);

        var menu = new ContextMenuStrip();
        menu.Items.Add("再取得", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("常駐を終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;

        MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                Hide();
            }
        };

        foreach (Control control in Controls)
        {
            if (control != _closeButton)
            {
                control.ContextMenuStrip = menu;
            }

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
        ShowMessage("取得中...");
    }

    public void ShowSnapshot(RateLimitSnapshot snapshot)
    {
        _statusLabel.Visible = false;
        ApplyWindow(_primaryBar, _primaryDetail, "5h", snapshot.Primary);
        ApplyWindow(_secondaryBar, _secondaryDetail, "1w", snapshot.Secondary);
        SetRowsVisible(true);
    }

    public void ShowError()
    {
        ShowMessage("利用量を取得できませんでした。");
    }

    public void PositionAtBottomRight()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = new Point(area.Right - Width, area.Bottom - Height);
    }

    private void ConfigureRow(Label label, RemainingBar bar, Label detail, string text, int top)
    {
        label.AutoSize = true;
        label.Text = text;
        label.Location = new Point(14, top);

        bar.Location = new Point(48, top + 2);
        bar.Size = new Size(150, 12);

        detail.AutoSize = true;
        detail.Location = new Point(210, top);
        detail.ForeColor = Color.FromArgb(246, 248, 250);
    }

    private void ShowMessage(string text)
    {
        SetRowsVisible(false);
        _statusLabel.Text = text;
        _statusLabel.Visible = true;
    }

    private void SetRowsVisible(bool visible)
    {
        _primaryLabel.Visible = visible;
        _primaryBar.Visible = visible;
        _primaryDetail.Visible = visible;
        _secondaryLabel.Visible = visible;
        _secondaryBar.Visible = visible;
        _secondaryDetail.Visible = visible;
    }

    private static void ApplyWindow(RemainingBar bar, Label detail, string label, RateLimitWindow? window)
    {
        bar.Value = window is null ? 0 : RateLimitFormatter.RemainingPercent(window);
        detail.Text = RateLimitFormatter.FormatDetail(label, window);
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

    private sealed class RemainingBar : Control
    {
        private static readonly Color TrackColor = Color.FromArgb(55, 60, 66);
        private static readonly Color FillColor = Color.FromArgb(84, 169, 255);
        private int _value;

        public RemainingBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, 0, 100);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var track = new SolidBrush(TrackColor);
            e.Graphics.FillRectangle(track, ClientRectangle);

            var fillWidth = (int)Math.Round(ClientRectangle.Width * (_value / 100.0));
            if (fillWidth > 0)
            {
                using var fill = new SolidBrush(FillColor);
                e.Graphics.FillRectangle(fill, new Rectangle(0, 0, fillWidth, ClientRectangle.Height));
            }
        }
    }
}
