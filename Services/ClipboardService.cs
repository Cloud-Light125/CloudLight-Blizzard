using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace CloudLightBlizzard.Services;

public static class ClipboardService
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private static readonly int[] RetryDelaysMs = [0, 50, 100, 150, 250, 400];

    public static async Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Exception? lastBusyException = null;

        foreach (var delay in RetryDelaysMs)
        {
            if (delay > 0) await Task.Delay(delay, cancellationToken);
            try
            {
                // SetDataObject(..., copy: true) performs an OLE flush. On some
                // Windows clipboard owners that flush reports CLIPBRD_E_CANT_OPEN
                // even after the text was already copied, which made every log
                // copy look like a failure. SetText owns/releases the clipboard
                // in one short STA operation and is sufficient for user copies.
                await dispatcher.InvokeAsync(() => Clipboard.SetText(text, TextDataFormat.UnicodeText),
                    DispatcherPriority.Normal, cancellationToken).Task;
                return true;
            }
            catch (COMException ex) when (IsClipboardBusy(ex)) { lastBusyException = ex; }
            catch (ExternalException ex) when (IsClipboardBusy(ex)) { lastBusyException = ex; }
        }

        Trace.TraceWarning("Clipboard remained busy after {0} attempts: {1}",
            RetryDelaysMs.Length, lastBusyException);
        return false;
    }

    private static bool IsClipboardBusy(ExternalException exception) =>
        exception.HResult == ClipboardBusyHResult || exception.ErrorCode == ClipboardBusyHResult;
}
