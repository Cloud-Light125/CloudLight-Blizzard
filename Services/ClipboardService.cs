using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudLightBlizzard.Services;

public static class ClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private static readonly int[] RetryDelaysMs = [0, 20, 50, 100, 150, 250];

    public static async Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        foreach (var delay in RetryDelaysMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delay > 0)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            if (TrySetClipboardText(text))
                return true;
        }

        Trace.TraceWarning("Clipboard remained busy after {0} attempts.", RetryDelaysMs.Length);
        return false;
    }

    private static bool TrySetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        IntPtr memory = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
                return false;

            // CF_UNICODETEXT requires UTF-16 text terminated with a NUL character.
            var chars = (text + '\0').ToCharArray();
            var byteCount = checked(chars.Length * sizeof(char));

            memory = GlobalAlloc(GmemMoveable, (UIntPtr)byteCount);
            if (memory == IntPtr.Zero)
                return false;

            var target = GlobalLock(memory);
            if (target == IntPtr.Zero)
                return false;

            try
            {
                Marshal.Copy(chars, 0, target, chars.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
                return false;

            // SetClipboardData 成功后，HGLOBAL 的所有权转交给系统，不能再释放。
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            _ = CloseClipboard();
            if (memory != IntPtr.Zero)
                _ = GlobalFree(memory);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
