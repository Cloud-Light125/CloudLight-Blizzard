using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>
/// Windows CurrentUser DPAPI storage shared with the Bilibili Worker.
/// Plaintext exists only in the caller's memory while a controlled stdin
/// request is being handled; settings.json contains only the encrypted blob.
/// </summary>
public static class DpapiCredentialStore
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
        "CloudLight Blizzard:BilibiliCredential:v1");

    public static string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToBase64String(Crypt(Encoding.UTF8.GetBytes(value), protect: true));
    }

    public static string? Unprotect(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob)) return null;
        try { return Encoding.UTF8.GetString(Crypt(Convert.FromBase64String(blob.Trim()), protect: false)); }
        catch (Exception ex) when (ex is FormatException or CryptographicException or Win32Exception)
        {
            return null;
        }
    }

    public static string? ReadEncryptedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var blob = File.ReadAllText(path, Encoding.ASCII).Trim();
            return string.IsNullOrWhiteSpace(blob) ? null : blob;
        }
        catch { return null; }
    }

    private static byte[] Crypt(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Bilibili credentials require Windows CurrentUser DPAPI.");

        var input = ToBlob(value, out var inputHandle);
        var entropy = ToBlob(Entropy, out var entropyHandle);
        try
        {
            DATA_BLOB output;
            var success = protect
                ? CryptProtectData(ref input, "CloudLight Blizzard Bilibili credential", ref entropy,
                    IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[checked((int)output.cbData)];
                if (result.Length > 0) Marshal.Copy(output.pbData, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
        finally
        {
            inputHandle.Dispose();
            entropyHandle.Dispose();
        }
    }

    private static DATA_BLOB ToBlob(byte[] value, out SafeHGlobalHandle handle)
    {
        handle = new SafeHGlobalHandle(value.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(value.Length));
        if (value.Length > 0) Marshal.Copy(value, 0, handle.DangerousGetHandle(), value.Length);
        return new DATA_BLOB { cbData = checked((uint)value.Length), pbData = handle.DangerousGetHandle() };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private sealed class SafeHGlobalHandle : SafeHandle
    {
        public SafeHGlobalHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle()
        {
            if (!IsInvalid) Marshal.FreeHGlobal(handle);
            return true;
        }
    }
}
