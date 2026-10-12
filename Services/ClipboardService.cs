using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace CloudLightBlizzard.Services;

public static class ClipboardService
{
    internal static int LastFailureCode { get; private set; }
    internal static string? BusyProcessName
    {
        get
        {
            try
            {
                var window = GetOpenClipboardWindow();
                if (window == IntPtr.Zero) return null;
                GetWindowThreadProcessId(window, out var pid);
                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return null; }
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    private static readonly int[] RetryDelaysMs = [0, 20, 50, 100, 150, 250];

    public static async Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        foreach (var delay in RetryDelaysMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delay > 0) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            // WPF supplies a valid owner and flushes the OLE data. A short STA
            // thread supports background callers without blocking the UI.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    var copied = Clipboard.GetText(TextDataFormat.UnicodeText) == text;
                    LastFailureCode = copied ? 0 : -1;
                    completion.TrySetResult(copied);
                }
                catch (ExternalException ex) { LastFailureCode = ex.HResult; completion.TrySetResult(false); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }) { IsBackground = true, Name = "CloudLight Clipboard" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (await completion.Task.ConfigureAwait(false)) return true;
        }
        Trace.TraceWarning("Clipboard remained busy after {0} attempts.", RetryDelaysMs.Length);
        return false;
    }
}
