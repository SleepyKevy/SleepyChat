using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed class MainForm : Form
{
    private const string AppTitle = "SleepyChat 1.0.0";
    private const int WM_CLOSE = 0x0010;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int ResizeBorder = 7;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int GracefulExitBudgetMs = 1000;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    private readonly WebView2 webView;
    private readonly BackendHost backend = new();
    private readonly Icon applicationIcon;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly Panel titleBar;
    private readonly Button maximizeButton;
    private bool shutdownStarted;
    private bool fullscreen;
    private Rectangle fullscreenRestoreBounds;
    private FormWindowState fullscreenRestoreState;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public MainForm()
    {
        Text = AppTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 820);
        MinimumSize = new Size(980, 680);
        BackColor = Color.FromArgb(6, 8, 13);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;
        DoubleBuffered = true;

        applicationIcon = LoadApplicationIcon();
        Icon = applicationIcon;

        titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Color.FromArgb(6, 8, 13),
            Padding = new Padding(9, 0, 0, 0)
        };

        var titleIcon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 23,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = CreateTitleBitmap(applicationIcon),
            BackColor = Color.Transparent
        };

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = AppTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(238, 238, 245),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.Transparent
        };

        var closeButton = CreateCaptionButton("×", (_, _) => BeginExit(), isClose: true);
        maximizeButton = CreateCaptionButton("□", (_, _) => ToggleMaximize());
        var minimizeButton = CreateCaptionButton("—", (_, _) => WindowState = FormWindowState.Minimized);
        var captionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 138,
            Height = 29,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(6, 8, 13)
        };
        captionButtons.Controls.Add(minimizeButton);
        captionButtons.Controls.Add(maximizeButton);
        captionButtons.Controls.Add(closeButton);
        var titleDivider = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Color.FromArgb(32, 48, 71),
            Margin = Padding.Empty
        };

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(titleIcon);
        titleBar.Controls.Add(captionButtons);
        titleBar.Controls.Add(titleDivider);
        titleDivider.BringToFront();

        AttachDragHandler(titleBar);
        AttachDragHandler(titleLabel);
        AttachDragHandler(titleIcon);
        webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(6, 8, 13),
            AllowExternalDrop = false,
            TabStop = true
        };

        Controls.Add(webView);
        Controls.Add(titleBar);

        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open SleepyChat", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Open SleepyChat_Data", null, (_, _) => OpenDataFolder());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit SleepyChat", null, (_, _) => BeginExit());

        trayIcon = new NotifyIcon
        {
            Text = "SleepyChat 1.0.0 — Made by SleepyKev • 2026",
            Icon = applicationIcon,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += OnResize;
        KeyDown += OnKeyDown;
    }

    private static Button CreateCaptionButton(string text, EventHandler onClick, bool isClose = false)
    {
        var button = new Button
        {
            Width = 46,
            Height = 29,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            ForeColor = Color.FromArgb(237, 237, 237),
            BackColor = Color.FromArgb(6, 8, 13),
            Font = new Font("Segoe UI", text == "—" ? 10F : 11F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        button.MouseEnter += (_, _) => button.BackColor = isClose
            ? Color.FromArgb(196, 43, 35)
            : Color.FromArgb(18, 36, 58);
        button.MouseLeave += (_, _) => button.BackColor = Color.FromArgb(6, 8, 13);
        return button;
    }

    private void AttachDragHandler(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (e.Clicks >= 2)
            {
                ToggleMaximize();
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        };
    }

    private static Bitmap CreateTitleBitmap(Icon icon)
    {
        using var source = icon.ToBitmap();
        return new Bitmap(source, new Size(16, 16));
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        try
        {
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                using var embedded = Icon.ExtractAssociatedIcon(executablePath);
                if (embedded is not null)
                    return (Icon)embedded.Clone();
            }
        }
        catch { }

        return (Icon)SystemIcons.Application.Clone();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyNativeWindowPolish();
    }

    private void ApplyNativeWindowPolish()
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        try
        {
            var size = sizeof(int);
            var darkMode = 1;
            _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, size);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var corner = DWMWCP_ROUND;
                _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, size);
                var borderNone = unchecked((int)0xFFFFFFFE);
                _ = DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderNone, size);
            }
        }
        catch
        {
            // Keep the normal borderless window if a DWM option is unavailable.
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLOSE)
        {
            BeginExit();
            return;
        }

        if (m.Msg == WM_NCHITTEST && !fullscreen && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)
            {
                var lParam = m.LParam.ToInt64();
                var screenPoint = new Point(unchecked((short)(lParam & 0xffff)), unchecked((short)((lParam >> 16) & 0xffff)));
                var clientPoint = PointToClient(screenPoint);
                var left = clientPoint.X <= ResizeBorder;
                var right = clientPoint.X >= ClientSize.Width - ResizeBorder;
                var top = clientPoint.Y <= ResizeBorder;
                var bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;

                if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
            return;
        }

        base.WndProc(ref m);
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        Shown -= OnShown;
        try
        {
            using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(16));
            await backend.StartAsync(startupCts.Token);
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "SleepyChat 1.0.0 could not start.\n\n" + ex.Message,
                AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            BeginExit();
        }
    }

    private async Task InitializeWebViewAsync()
    {
        Directory.CreateDirectory(AppUtil.RuntimeDataDir);
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppUtil.RuntimeDataDir);
        await webView.EnsureCoreWebView2Async(environment);

        var core = webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.Navigate(BackendHost.BaseUrl);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.NavigationKind == CoreWebView2NavigationKind.BackOrForward)
        {
            e.Cancel = true;
            return;
        }

        if (e.Uri.StartsWith(BackendHost.BaseUrl, StringComparison.OrdinalIgnoreCase)
            || e.Uri.StartsWith("http://localhost:17892/", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(AppUtil.DataDir);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", AppUtil.DataDir) { UseShellExecute = true });
        }
        catch { }
    }

    private void ToggleMaximize()
    {
        if (shutdownStarted)
            return;

        if (fullscreen)
        {
            ToggleFullscreen();
            return;
        }

        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F11)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (shutdownStarted)
            return;

        if (!fullscreen)
        {
            fullscreenRestoreState = WindowState;
            fullscreenRestoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            WindowState = FormWindowState.Normal;
            titleBar.Visible = false;
            Bounds = Screen.FromControl(this).Bounds;
            fullscreen = true;
            return;
        }

        fullscreen = false;
        titleBar.Visible = true;
        WindowState = FormWindowState.Normal;
        if (!fullscreenRestoreBounds.IsEmpty)
            Bounds = fullscreenRestoreBounds;
        if (fullscreenRestoreState == FormWindowState.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (shutdownStarted)
            return;

        maximizeButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";

        if (WindowState == FormWindowState.Minimized)
        {
            trayIcon.Visible = true;
            ShowInTaskbar = true;
        }
    }

    private void RestoreFromTray()
    {
        if (shutdownStarted)
            return;

        trayIcon.Visible = true;
        ShowInTaskbar = true;
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (shutdownStarted)
            return;

        e.Cancel = true;
        BeginExit();
    }

    private void BeginExit()
    {
        if (shutdownStarted)
            return;

        shutdownStarted = true;

        var watchdog = new Thread(() =>
        {
            Thread.Sleep(GracefulExitBudgetMs);
            ForceProcessExit();
        })
        {
            IsBackground = true,
            Name = "SleepyChat Exit Watchdog"
        };
        watchdog.Start();

        try { trayIcon.Visible = false; } catch { }
        try { ShowInTaskbar = false; } catch { }
        try { Hide(); } catch { }

        _ = Task.Run(() =>
        {
            try { backend.Stop(); }
            catch { }
            Environment.Exit(0);
        });
    }

    private static void ForceProcessExit()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            current.Kill(entireProcessTree: true);
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { trayIcon.Visible = false; } catch { }
            trayIcon.Dispose();
            trayMenu.Dispose();
            backend.Dispose();
            if (titleBar.Controls.OfType<PictureBox>().FirstOrDefault()?.Image is Image titleImage)
                titleImage.Dispose();
            applicationIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
