using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CloudLightBlizzard.Services.Visuals;

public sealed record VisualImageResult(BitmapSource? Image,string State);

/// <summary>Public image bytes only: no auth headers, cookies, raw URL filenames or URL logging.</summary>
public sealed class VisualImageCache
{
    public const long DefaultLimit=128L*1024*1024;
    private const int MaxImageBytes=8*1024*1024;
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Func<Uri,CancellationToken,Task<HttpResponseMessage>>? _fetch;
    private readonly TimeSpan _timeout;
    private readonly long _limit;
    private readonly SemaphoreSlim _downloads=new(4);
    private readonly SemaphoreSlim _files=new(1);
    // As upstream's async lock, serialize each image's load/cache path. Shards keep unrelated images concurrent.
    private readonly SemaphoreSlim[] _imageLocks=Enumerable.Range(0,32).Select(_=>new SemaphoreSlim(1)).ToArray();
    private readonly object _memorySync=new();
    private readonly Dictionary<string,(BitmapSource Image,DateTime At,long Bytes)> _memory=[];
    private readonly Dictionary<string,(string State,DateTime Until)> _failures=[];
    private static AppSettings? _networkSettings;
    private static readonly object RoutesSync=new();
    private static readonly Dictionary<string,(AppSettings Settings,CloudHttpClientFactory Http,VisualImageCache Cache)> Routes=[];
    public static VisualImageCache Shared=>ForBrand("Twitch");
    internal static void ConfigureNetwork(AppSettings settings)=>_networkSettings=settings;
    internal static bool UsesProxyFor(AppSettings settings,string brand)=>Enum.TryParse<Drops.DropsPlatform>(brand,out var platform)&&ViewModels.MainViewModel.CreateDropsProxySettings(settings).IsEnabledFor(platform);
    public static VisualImageCache ForBrand(string brand)
    {
        var root=Path.Combine(AppPaths.Current.Root,"cache","visual-images");var key=root+"|"+brand;
        lock(RoutesSync)
        {
            if(!Routes.TryGetValue(key,out var route))
            {
                var policy=new AppSettings();
                // Reuse the application's tested routing/fallback implementation with a cookie-free image client.
                var factory=new CloudHttpClientFactory(policy,Path.Combine(AppPaths.Current.LogsDir,"image-network.log"),proxy=>new HttpClient(new SocketsHttpHandler
                {UseCookies=false,AllowAutoRedirect=false,UseProxy=proxy is not null,Proxy=proxy is null?null:new System.Net.WebProxy(proxy),ConnectTimeout=TimeSpan.FromSeconds(5)}){Timeout=Timeout.InfiniteTimeSpan});
                route=(policy,factory,new VisualImageCache(root,fetch:(uri,ct)=>factory.SendGetAsync(()=>new HttpRequestMessage(HttpMethod.Get,uri),"VisualImage."+brand,ct)));
                Routes[key]=route;
            }
            var settings=_networkSettings;route.Settings.EnableProxy=settings is not null&&UsesProxyFor(settings,brand);
            route.Settings.ProxyUrl=settings?.ProxyUrl??"";route.Settings.FallbackDirect=settings?.FallbackDirect==true;
            return route.Cache;
        }
    }
    public VisualImageCache(string root,HttpMessageHandler? transport=null,TimeSpan? timeout=null,long limit=DefaultLimit,Func<Uri,CancellationToken,Task<HttpResponseMessage>>? fetch=null)
    {
        _root=Path.GetFullPath(root);_timeout=timeout??TimeSpan.FromSeconds(10);_limit=limit;
        _fetch=fetch;
        _http=new(transport??new HttpClientHandler { UseCookies=false,UseProxy=false,AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
    }
    public static bool IsPublicImageUri(string? source,out Uri? uri)
    {
        uri=null;
        if(!Uri.TryCreate(source,UriKind.Absolute,out var value)||value.Scheme!="https"||value.UserInfo.Length!=0||value.IsLoopback) return false;
        var host=value.IdnHost;
        // Explicit public media domains. Metadata cannot redirect the client to local/private services.
        if(!new[]{"jtvnw.net","twitchcdn.net","twitch.tv","hdslb.com","bilivideo.com","sooplive.com","sooplive.co.kr","afreecatv.com","ytimg.com","ggpht.com","blz-contentstack.com","blizzard.com","overwatch.blizzard.com","bnetcmsus-a.akamaihd.net","blz-contentstack-images.akamaized.net"}
            .Any(domain=>host.Equals(domain,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase))) return false;
        uri=value;return true;
    }
    internal string CachePath(string source)=>Path.Combine(_root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()+".img");
    public async Task<VisualImageResult> LoadAsync(string? source,CancellationToken token=default,int decodePixelWidth=640)
    {
        if(string.IsNullOrWhiteSpace(source))return new(null,"官方未提供图片");
        if(!IsPublicImageUri(source,out var uri))return new(null,"图片来源不可用");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_timeout);
        var ct=deadline.Token;bool entered=false,keyEntered=false;
        decodePixelWidth=Math.Clamp(decodePixelWidth,64,1280);
        var path=CachePath(source);var memoryKey=path+"|"+decodePixelWidth;var imageLock=_imageLocks[(path.GetHashCode()&int.MaxValue)%_imageLocks.Length];
        try
        {
            await imageLock.WaitAsync(ct).ConfigureAwait(false);keyEntered=true;
            lock(_memorySync)if(_failures.TryGetValue(path,out var failure)&&failure.Until>DateTime.UtcNow)return new(null,failure.State);
            await _downloads.WaitAsync(ct).ConfigureAwait(false);entered=true;
            Directory.CreateDirectory(_root);
            if(File.Exists(path)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(7))
            {
                try
                {
                    BitmapSource? remembered;
                    lock(_memorySync)remembered=_memory.TryGetValue(memoryKey,out var entry)?entry.Image:null;
                    if(remembered is not null){Renew(path);Remember(path,memoryKey,remembered);return new(remembered,"已加载缓存");}
                    var cached=await File.ReadAllBytesAsync(path,ct).ConfigureAwait(false);
                    if(cached.Length<=MaxImageBytes){var decoded=await DecodeAsync(cached,ct,decodePixelWidth).ConfigureAwait(false);Renew(path);Remember(path,memoryKey,decoded);return new(decoded,"已加载缓存");}
                }
                catch(OperationCanceledException){throw;}
                catch{ /* Corrupt or externally removed cache: fetch once below. */ }
            }
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            using var response=_fetch is null?await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false):await _fetch(uri!,ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is > MaxImageBytes) return new(null,"图片超过大小限制");
            await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var bytes=new MemoryStream();var buffer=new byte[16*1024];int read;
            while((read=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
            {
                if(bytes.Length+read>MaxImageBytes)return new(null,"图片超过大小限制");
                bytes.Write(buffer,0,read);
            }
            var data=bytes.ToArray();
            var image=await DecodeAsync(data,ct,decodePixelWidth).ConfigureAwait(false);
            Remember(path,memoryKey,image);
            await _files.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temporary,data,ct).ConfigureAwait(false);File.Move(temporary,path,true);Trim();}
                catch(IOException){return new(image,"已加载图片，磁盘缓存不可用");}
                catch(UnauthorizedAccessException){return new(image,"已加载图片，磁盘缓存不可用");}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            finally{_files.Release();}
            return new(image,"已加载官方图片");
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return Failed(path,"图片加载超时");}
        catch(OperationCanceledException){throw;}
        catch{return Failed(path,"图片暂不可用");}
        finally{if(entered)_downloads.Release();if(keyEntered)imageLock.Release();}
    }
    public void Invalidate(string? source)
    {
        if(string.IsNullOrWhiteSpace(source))return;
        var path=CachePath(source);lock(_memorySync){foreach(var key in _memory.Keys.Where(k=>k.StartsWith(path+"|",StringComparison.Ordinal)).ToArray())_memory.Remove(key);_failures.Remove(path);}
    }
    private static void Renew(string path){try{File.SetLastWriteTimeUtc(path,DateTime.UtcNow);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private void Remember(string path,string memoryKey,BitmapSource image)
    {
        lock(_memorySync)
        {
            _failures.Remove(path);_memory[memoryKey]=(image,DateTime.UtcNow,(long)image.PixelWidth*image.PixelHeight*4);
            var bytes=_memory.Values.Sum(v=>v.Bytes);
            foreach(var old in _memory.OrderBy(p=>p.Value.At).ToArray())
            {
                if(bytes<=32L*1024*1024&&_memory.Count<=64)break;
                _memory.Remove(old.Key);bytes-=old.Value.Bytes;
            }
        }
    }
    private VisualImageResult Failed(string path,string state)
    {
        lock(_memorySync){if(_failures.Count>=128)_failures.Clear();_failures[path]=(state,DateTime.UtcNow.AddSeconds(30));}
        return new(null,state);
    }
    private static Task<BitmapSource> DecodeAsync(byte[] data,CancellationToken ct,int width)=>Task.Run<BitmapSource>(()=>
    {
        ct.ThrowIfCancellationRequested();using var stream=new MemoryStream(data);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.StreamSource=stream;image.EndInit();
        if(image.PixelHeight>8192||image.PixelWidth>8192)throw new InvalidDataException("Image dimensions exceed limit.");
        image.Freeze();ct.ThrowIfCancellationRequested();return image;
    },ct);
    private void Trim()
    {
        var files=new DirectoryInfo(_root).EnumerateFiles("*.img").OrderBy(f=>f.LastWriteTimeUtc).ToArray();
        var size=files.Sum(f=>f.Length);
        foreach(var file in files)
        {
            if(size<=_limit&&DateTime.UtcNow-file.LastWriteTimeUtc<TimeSpan.FromDays(7))break;
            var length=file.Length;try{file.Delete();size-=length;}catch(IOException){}
        }
    }
}
