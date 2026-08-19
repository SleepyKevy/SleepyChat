using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed partial class MainForm : Form, IMessageFilter
{
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
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
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
        AppUtil.OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        AppUtil.OpenExternal(e.Uri);
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
}
