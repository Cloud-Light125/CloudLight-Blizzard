using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CloudLightBlizzard.Services.Overwatch;

// 国际服生涯图片本地缓存：下载后按目标宽度降采样，后续直接读取本地文件。
public static class OwImageCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static string Dir
    {
        get { var d = Path.Combine(AppPaths.Current.OverwatchCacheDir, "img"); Directory.CreateDirectory(d); return d; }
    }

    /// <summary>战绩缓存根目录。</summary>
    public static string CacheRoot => AppPaths.Current.OverwatchCacheDir;

    /// <summary>国际服生涯图片、HTML 与可安全清理的历史战绩缓存占用字节数。</summary>
    public static long CacheSizeBytes()
    {
        long sum = 0;
        foreach (var sub in new[] { "img", "career", "config" })
        {
            var d = Path.Combine(CacheRoot, sub);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                try { sum += new FileInfo(f).Length; } catch { }
        }
        return sum;
    }

    /// <summary>清除可重新下载的战绩缓存。返回删除的字节数。</summary>
    public static long ClearCache()
    {
        long freed = CacheSizeBytes();
        foreach (var sub in new[] { "img", "career", "config" })
        {
            var d = Path.Combine(CacheRoot, sub);
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { freed = 0; }
        }
        return freed;
    }

    private static string PathFor(string url, int thumbWidth)
    {
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        if (thumbWidth > 0) return Path.Combine(Dir, $"{hash}_t{thumbWidth}.png");
        string ext = url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
        return Path.Combine(Dir, hash + ext);
    }

    /// <summary>返回本地缓存路径;没有就下载(thumbWidth>0 则降采样)后返回。失败返回 null。</summary>
    public static async Task<string?> GetAsync(string? url, int thumbWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string path = PathFor(url, thumbWidth);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        try
        {
            // ConfigureAwait(false):下载/解码/写盘都别回到 UI 线程,否则大量图标解码会卡死界面
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            var outBytes = thumbWidth > 0 ? (Downscale(bytes, thumbWidth) ?? bytes) : bytes;
            await File.WriteAllBytesAsync(path, outBytes).ConfigureAwait(false);
            return path;
        }
        catch { return null; }
    }

    // 用 WPF 解码器按目标宽度降采样,重编码为 PNG。失败返回 null(调用方回退存原图)。
    private static byte[]? Downscale(byte[] src, int maxWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.None;
            bmp.DecodePixelWidth = maxWidth;               // 解码即缩放,省内存
            bmp.StreamSource = new MemoryStream(src);
            bmp.EndInit();
            bmp.Freeze();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
