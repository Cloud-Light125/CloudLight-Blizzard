using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

internal sealed class MultipartProgressContent : HttpContent
{
    private readonly IReadOnlyList<byte[]> _fieldParts;
    private readonly byte[]? _fileHeader;
    private readonly byte[] _closing;
    private readonly string? _filePath;
    private readonly long _length;
    private readonly IProgress<FeedbackUploadProgress>? _progress;

    public MultipartProgressContent(IReadOnlyDictionary<string, string> fields, string? filePath,
        IProgress<FeedbackUploadProgress>? progress)
    {
        var boundary = "----CloudLightBlizzard" + Guid.NewGuid().ToString("N");
        Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
        Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", $"\"{boundary}\""));
        _fieldParts = fields.Select(pair => Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"{Escape(pair.Key)}\"\r\n" +
            $"Content-Type: text/plain; charset=utf-8\r\n\r\n{pair.Value}\r\n")).ToArray();
        _filePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        if (!string.IsNullOrWhiteSpace(filePath))
            _fileHeader = Encoding.ASCII.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"logs\"; filename=\"feedback.zip\"\r\n" +
                "Content-Type: application/zip\r\n\r\n");
        _closing = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        _length = _fieldParts.Sum(part => (long)part.Length) + (_fileHeader?.Length ?? 0) +
                  (_filePath is null ? 0 : new FileInfo(_filePath).Length) + _closing.Length;
        _progress = progress;
    }

    protected override bool TryComputeLength(out long length) { length = _length; return true; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        long sent = 0;
        foreach (var part in _fieldParts) await WriteAsync(part);
        if (_fileHeader is not null && _filePath is not null)
        {
            await WriteAsync(_fileHeader);
            await using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
                await WriteAsync(buffer.AsMemory(0, read));
        }
        await WriteAsync(_closing);

        async Task WriteAsync(ReadOnlyMemory<byte> bytes)
        {
            await stream.WriteAsync(bytes, cancellationToken);
            sent += bytes.Length;
            _progress?.Report(new FeedbackUploadProgress(sent, _length));
        }
    }

    private static string Escape(string value) => value.Replace("\"", "", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
}
