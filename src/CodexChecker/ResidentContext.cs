using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexChecker;

public sealed class HotKeyRegistrationException : Exception
{
    public HotKeyRegistrationException()
        : base("ホットキー Win + Alt + X を登録できませんでした。他のアプリが使用している可能性があります。")
    {
    }
}

public sealed class ResidentContext : ApplicationContext
{
    private const int HotKeyId = 0x5843;
    private const int ModAlt = 0x0001;
    private const int ModWin = 0x0008;
    private const int ModNoRepeat = 0x4000;
    private const Keys HotKey = Keys.X;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RpcTimeout = TimeSpan.FromSeconds(5);

    private readonly AppServerClient _client = new();
    private readonly StatusPopupForm _popup = new();
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _autoHideTimer = new();
    private bool _manualDisplay;
    private bool _disposed;

    public ResidentContext()
    {
        _hotKeyWindow = new HotKeyWindow(OnHotKeyPressed);
        if (!RegisterHotKey(_hotKeyWindow.Handle, HotKeyId, ModWin | ModAlt | ModNoRepeat, (int)HotKey))
        {
            throw new HotKeyRegistrationException();
        }

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
        _pollTimer.Tick += async (_, _) => await ShowStatusAsync(autoHide: true);
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
        if (_popup.Visible && _manualDisplay)
        {
            _popup.Hide();
            return;
        }

        if (_popup.Visible && !_manualDisplay)
        {
            _manualDisplay = true;
            _autoHideTimer.Stop();
            return;
        }

        await ShowStatusAsync(autoHide: false);
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
}
