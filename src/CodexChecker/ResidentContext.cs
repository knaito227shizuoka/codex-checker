using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexChecker;

public sealed class HotKeyRegistrationException : Exception
{
    public HotKeyRegistrationException()
        : base("ホットキー Ctrl + Alt + X を登録できませんでした。他のアプリが使用している可能性があります。")
    {
    }
}

public sealed class ResidentContext : ApplicationContext
{
    private const int HotKeyId = 0x5843;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModNoRepeat = 0x4000;
    private const Keys HotKey = Keys.X;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RpcTimeout = TimeSpan.FromSeconds(5);

    private readonly AppServerClient _client = new();
    private readonly StatusPopupForm _popup = new();
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Icon _trayIcon;
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _autoHideTimer = new();
    private bool _manualDisplay;
    private bool _disposed;

    public ResidentContext()
    {
        _trayIcon = CreateTrayIcon();
        _hotKeyWindow = new HotKeyWindow(OnHotKeyPressed);
        if (!RegisterHotKey(_hotKeyWindow.Handle, HotKeyId, ModControl | ModAlt | ModNoRepeat, (int)HotKey))
        {
            throw new HotKeyRegistrationException();
        }

        ConfigureNotifyIcon();

        _popup.RefreshRequested += async (_, _) => await ShowStatusAsync(autoHide: !_manualDisplay);
        _popup.ExitRequested += (_, _) => ExitThread();
        _popup.VisibleChanged += (_, _) =>
        {
            if (!_popup.Visible)
            {
                _autoHideTimer.Stop();
                _manualDisplay = false;
            }
        };

        _pollTimer.Interval = (int)PollInterval.TotalMilliseconds;
        _pollTimer.Tick += async (_, _) => await ShowStatusAsync(autoHide: !_manualDisplay);
        _pollTimer.Start();

        _autoHideTimer.Interval = (int)AutoHideDelay.TotalMilliseconds;
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (!_manualDisplay)
            {
                _popup.Hide();
            }
        };

        Application.Idle += OnFirstIdle;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _pollTimer.Stop();
            _autoHideTimer.Stop();
            _pollTimer.Dispose();
            _autoHideTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayIcon.Dispose();
            _popup.Dispose();
            _client.Dispose();
            UnregisterHotKey(_hotKeyWindow.Handle, HotKeyId);
            _hotKeyWindow.DestroyHandle();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private async void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        await ShowStatusAsync(autoHide: true);
    }

    private async void OnHotKeyPressed()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            return;
        }

        await ShowStatusAsync(autoHide: false);
    }

    private void ConfigureNotifyIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("表示", null, async (_, _) => await ShowStatusAsync(autoHide: false));
        menu.Items.Add("再取得", null, async (_, _) => await ShowStatusAsync(autoHide: !_manualDisplay));
        menu.Items.Add("常駐を終了", null, (_, _) => ExitThread());

        _notifyIcon.Icon = _trayIcon;
        _notifyIcon.Text = "codex-checker";
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += async (_, _) => await ShowStatusAsync(autoHide: false);
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(31, 34, 38));
        using var border = new Pen(Color.FromArgb(76, 86, 96), 2F);
        using var blue = new SolidBrush(Color.FromArgb(84, 169, 255));
        using var green = new SolidBrush(Color.FromArgb(46, 204, 113));
        using var white = new SolidBrush(Color.FromArgb(245, 248, 250));

        graphics.FillEllipse(background, 2, 2, 28, 28);
        graphics.DrawEllipse(border, 2, 2, 28, 28);
        graphics.FillEllipse(blue, 8, 7, 6, 6);
        graphics.FillRectangle(green, 17, 8, 6, 16);
        graphics.FillRectangle(white, 8, 18, 5, 6);
        graphics.FillRectangle(white, 14, 15, 2, 9);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private async Task ShowStatusAsync(bool autoHide)
    {
        _manualDisplay = !autoHide;
        _autoHideTimer.Stop();
        _popup.PositionAtBottomRight();
        _popup.ShowLoading();
        _popup.Show();

        try
        {
            var snapshot = await _client.ReadRateLimitsAsync(RpcTimeout);
            _popup.ShowSnapshot(snapshot);
        }
        catch (CodexCommandNotFoundException)
        {
            MessageBox.Show("codex コマンドが見つかりません。PATH を確認してください。", "codex-checker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
            return;
        }
        catch
        {
            try
            {
                await _client.RestartAsync();
            }
            catch
            {
            }

            _popup.ShowError();
        }

        if (autoHide && _popup.Visible)
        {
            _autoHideTimer.Start();
        }
    }

    private sealed class HotKeyWindow : NativeWindow
    {
        private const int WmHotKey = 0x0312;
        private readonly Action _onHotKey;

        public HotKeyWindow(Action onHotKey)
        {
            _onHotKey = onHotKey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotKey)
            {
                _onHotKey();
                return;
            }

            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
