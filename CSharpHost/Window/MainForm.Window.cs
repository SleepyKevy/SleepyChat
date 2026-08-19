using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed partial class MainForm : Form, IMessageFilter
{
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
                var corner = DWMWCP_DONOTROUND;
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

        base.WndProc(ref m);
    }


    private void ToggleMaximize()
    {
        if (shutdownStarted)
            return;
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (shutdownStarted)
            return;

        maximizeButton.RestoreGlyph = WindowState == FormWindowState.Maximized;
        if (WindowState != FormWindowState.Normal)
            EndManualResize();

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
            Application.RemoveMessageFilter(this);
            resizeTimer.Stop();
            resizeTimer.Dispose();
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
