using System.Runtime.InteropServices;
using System.Threading;

namespace SleepyChat;

internal static class Program
{
    private const string MutexName = @"Local\SleepyKev.SleepyChat.1.0.0";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            if (!args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "SleepyChat 1.0.0 is already running.",
                    "SleepyChat 1.0.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
        {
            using var backend = new BackendHost();
            backend.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            using var wait = new ManualResetEventSlim(false);
            wait.Wait();
            return;
        }

        try { _ = SetCurrentProcessExplicitAppUserModelID("SleepyKev.SleepyChat.1.0.0"); }
        catch { }

        ApplicationConfiguration.Initialize();
        var mouseNavigationFilter = new MouseNavigationMessageFilter();
        Application.AddMessageFilter(mouseNavigationFilter);
        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            Application.RemoveMessageFilter(mouseNavigationFilter);
        }
    }

    private sealed class MouseNavigationMessageFilter : IMessageFilter
    {
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_XBUTTONDBLCLK = 0x020D;
        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_BROWSER_BACKWARD = 1;
        private const int APPCOMMAND_BROWSER_FORWARD = 2;
        private const int APPCOMMAND_MASK = 0x0FFF;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is WM_XBUTTONDOWN or WM_XBUTTONUP or WM_XBUTTONDBLCLK)
                return true;

            if (m.Msg == WM_APPCOMMAND)
            {
                var command = (int)((m.LParam.ToInt64() >> 16) & APPCOMMAND_MASK);
                if (command is APPCOMMAND_BROWSER_BACKWARD or APPCOMMAND_BROWSER_FORWARD)
                    return true;
            }

            return false;
        }
    }
}
