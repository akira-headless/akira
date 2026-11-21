using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Integrations;

namespace Akira.Infrastructure.Images;

/// <summary>
/// Keeps images fetched from Radarr, Sonarr and the artwork hosts on disk, up to a size cap, the least recently used
/// removed first.
/// </summary>
public sealed class ImageCache : IDisposable
{
    private const string Extension = ".img";
    private const string PartialExtension = ".partial";
    private const int MaxContentTypeLength = 32;
    private static readonly TimeSpan PartialAge = TimeSpan.FromHours(1);
    private static readonly HashSet<string> ContentTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private readonly ImageCacheOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ImageCache> _logger;
    private readonly SemaphoreSlim _trim = new(1, 1);
    private readonly string _folder;

    // Bytes on disk as the last listing found them plus what was written since; -1 until the first listing
    private long _size = -1;

    public ImageCache(
        IOptions<ImageCacheOptions> options,
        TimeProvider time,
        ILogger<ImageCache> logger)
    {
        _options = options.Value;
        _time = time;
        _logger = logger;
        _folder = _options.ResolvePath();
    }

    private bool Enabled => _options.MaxMegabytes > 0;

    private long MaxBytes => _options.MaxMegabytes * 1024L * 1024L;

    /// <summary>
    /// Retrieves a stored image, or null when there is none or it cannot be read.
    /// </summary>
    public async Task<ImageData?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return null;
        }

        var path = PathFor(key);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (Parse(bytes) is not { } image)
            {
                File.Delete(path);
                return null;
            }

            // The time of the last use decides what goes first when the cache is full
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
            return image;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The image cache could not read {Path}: {Reason}", path, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Stores an image, then removes the least recently used ones while the cache is over its cap. A failure is logged
    /// and leaves the cache as it was.
    /// </summary>
    public async Task StoreAsync(string key, ImageData image, CancellationToken cancellationToken)
    {
        if (!Enabled || !ContentTypes.Contains(image.ContentType))
        {
            return;
        }

        var path = PathFor(key);
        // Written aside and moved, so a stop in the middle never leaves a short image another request takes
        var partial = $"{path}.{Guid.NewGuid():N}{PartialExtension}";
        long written;
        try
        {
            Directory.CreateDirectory(_folder);
            await using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(image.ContentType + "\n"), cancellationToken);
                await stream.WriteAsync(image.Content, cancellationToken);
                written = stream.Length;
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(partial);
            if (exception is not OperationCanceledException)
            {
                _logger.LogWarning("The image cache could not write {Path}: {Reason}", path, exception.Message);
            }

            return;
        }

        var total = Interlocked.Read(ref _size) < 0 ? long.MaxValue : Interlocked.Add(ref _size, written);
        if (total > MaxBytes)
        {
            Trim();
        }
    }

    public void Dispose() => _trim.Dispose();

    // Never a path from a request: the name is a hash of the key, whatever the key holds
    private string PathFor(string key) =>
        Path.Combine(_folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + Extension);

    private static ImageData? Parse(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)'\n', 0, Math.Min(bytes.Length, MaxContentTypeLength + 1));
        if (end <= 0)
        {
            return null;
        }

        var contentType = Encoding.ASCII.GetString(bytes, 0, end);
        return ContentTypes.Contains(contentType) && end + 1 < bytes.Length ? new ImageData(bytes[(end + 1)..], contentType) : null;
    }

    private void Trim()
    {
        // One trim at a time; a store that finds one running leaves the work to it
        if (!_trim.Wait(0))
        {
            return;
        }

        try
        {
            var folder = new DirectoryInfo(_folder);
            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var leftover in folder.EnumerateFiles("*" + PartialExtension).Where(file => now - file.LastWriteTimeUtc > PartialAge))
            {
                TryDelete(leftover.FullName);
            }

            var files = folder.EnumerateFiles("*" + Extension).OrderBy(file => file.LastWriteTimeUtc).ToList();
            var total = files.Sum(file => file.Length);
            if (total > MaxBytes)
            {
                // Down to nine tenths, so the next few stores do not list the folder again
                var target = MaxBytes / 10 * 9;
                foreach (var file in files)
                {
                    if (total <= target)
                    {
                        break;
                    }

                    if (TryDelete(file.FullName))
                    {
                        total -= file.Length;
                    }
                }
            }

            Interlocked.Exchange(ref _size, total);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The image cache could not be trimmed: {Reason}", exception.Message);
        }
        finally
        {
            _trim.Release();
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("The image cache could not remove {Path}: {Reason}", path, exception.Message);
            return false;
        }
    }
}
