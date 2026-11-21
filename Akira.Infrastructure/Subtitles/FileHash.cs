using System.Globalization;

namespace Akira.Infrastructure.Subtitles;

/// <summary>
/// Computes the OpenSubtitles hash of a video, which finds subtitles timed for exactly this release.
/// </summary>
public static class FileHash
{
    private const int ChunkSize = 64 * 1024;

    public static async Task<string?> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        // The size plus the first and last 64 KB as 64 bit words, so it never reads more of a video than that
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize, useAsync: true);
        var length = file.Length;
        if (length < ChunkSize * 2)
        {
            return null;
        }

        var hash = (ulong)length;
        var buffer = new byte[ChunkSize];
        foreach (var offset in new[] { 0L, length - ChunkSize })
        {
            file.Seek(offset, SeekOrigin.Begin);
            await file.ReadExactlyAsync(buffer, cancellationToken);
            for (var index = 0; index < ChunkSize; index += 8)
            {
                unchecked
                {
                    hash += BitConverter.ToUInt64(buffer, index);
                }
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
