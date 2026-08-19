using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed partial class MainForm : Form, IMessageFilter
{
    private const string AppTitle = "SleepyChat 1.0.0";
    private const int WM_CLOSE = 0x0010;
    private const int WM_SETCURSOR = 0x0020;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int HTCAPTION = 0x0002;
    private const int ResizeGripThickness = 6;
    private const int ResizeCornerSize = 14;
    private const int GracefulExitBudgetMs = 1000;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;

    private readonly WebView2 webView;
    private readonly BackendHost backend = new();
    private readonly Icon applicationIcon;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly Panel titleBar;
    private readonly CaptionButton maximizeButton;
    private readonly System.Windows.Forms.Timer resizeTimer = new() { Interval = 16 };
    private bool manualResizeActive;
    private ResizeDirection manualResizeDirection;
    private Point manualResizeStartCursor;
    private Rectangle manualResizeStartBounds;
    private bool shutdownStarted;

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
            Padding = new Padding(9, 0, 0, 0),
            Margin = Padding.Empty
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

        var closeButton = CreateCaptionButton(CaptionButtonKind.Close, (_, _) => BeginExit());
        maximizeButton = CreateCaptionButton(CaptionButtonKind.Maximize, (_, _) => ToggleMaximize());
        var minimizeButton = CreateCaptionButton(CaptionButtonKind.Minimize, (_, _) => WindowState = FormWindowState.Minimized);
        var captionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 132,
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
            TabStop = true,
            Margin = Padding.Empty
        };

        Controls.Add(webView);
        Controls.Add(titleBar);

        resizeTimer.Tick += (_, _) =>
        {
            if (!manualResizeActive)
                return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                EndManualResize();
                return;
            }
            ApplyManualResize(Cursor.Position);
        };
        Application.AddMessageFilter(this);

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
    }

    private static CaptionButton CreateCaptionButton(CaptionButtonKind kind, EventHandler onClick)
    {
        var button = new CaptionButton(kind)
        {
            Width = 44,
            Height = 29,
            Margin = Padding.Empty,
            TabStop = false
        };
        button.Click += onClick;
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

}
