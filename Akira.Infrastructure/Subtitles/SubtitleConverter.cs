using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using Akira.Core.Subtitles;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Subtitles;

public sealed class SubtitleConverter : ISubtitleConverter, IDisposable
{
    // Conversions under the transcode folder; the cleanup worker removes them a day after they were made
    public const string Folder = "webvtt";

    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    // Whether an ASS file is UTF-8, by path and change time: a stream asks for every segment
    private readonly ConcurrentDictionary<(string Path, DateTime Changed), bool> _utf8 = new();

    // What is being made now, by the file it makes or the video it is read from. A request joins it and may give up;
    // the work goes on for the next one, which would otherwise start the same read of a whole video again.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _running = new();
    private readonly CancellationTokenSource _stopping = new();

    private readonly ISubtitleExtractor _extractor;
    private readonly IOptions<TranscodingOptions> _options;

    public SubtitleConverter(
        ISubtitleExtractor extractor,
        IOptions<TranscodingOptions> options)
    {
        _extractor = extractor;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<string> WebVttAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, CancellationToken cancellationToken)
    {
        if (source.StreamIndex is null)
        {
            return await ConvertAsync(source, ".vtt", async token => WebVttWriter.Write(await ReadAsync(source, token)), cancellationToken);
        }

        return await FromVideoAsync(source, sameVideo, ".vtt", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> BurnableAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, CancellationToken cancellationToken)
    {
        // Pointed at the video itself, the subtitles filter reads all of it before the first frame, and again on every
        // seek: on a 4K remux that is minutes. So the track is copied out once, with the other text tracks in the same read.
        if (source.StreamIndex is not null)
        {
            return await FromVideoAsync(source, sameVideo, BurnedExtension(source), cancellationToken);
        }

        if (Format(source) == SubtitleFormat.Srt)
        {
            return await ConvertAsync(source, ".srt", async token => SrtWriter.Write(await ReadAsync(source, token)), cancellationToken);
        }

        // libass draws ASS itself, styles, layers and positions included; it only needs the text in UTF-8
        if (!File.Exists(source.Path))
        {
            return source.Path;
        }

        var changed = (source.Path, File.GetLastWriteTimeUtc(source.Path));
        if (!_utf8.TryGetValue(changed, out var utf8))
        {
            var bytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            utf8 = SubtitleDecoder.IsUtf8(bytes);
            _utf8[changed] = utf8;
        }

        if (utf8)
        {
            return source.Path;
        }

        return await ConvertAsync(source, ".ass",
            async token => SubtitleDecoder.Decode(await File.ReadAllBytesAsync(source.Path, token), source.Language), cancellationToken);
    }

    public void Dispose() => _stopping.Cancel();

    private async Task<string> ConvertAsync(SubtitleSource source, string extension, Func<CancellationToken, Task<string>> convert,
        CancellationToken cancellationToken)
    {
        var target = Target(source, extension);
        if (IsCurrent(source, target))
        {
            return File.Exists(target) ? target : source.Path;
        }

        await ShareAsync(target, async token => await WriteAsync(target, await convert(token), token), cancellationToken);
        return target;
    }

    private async Task<string> FromVideoAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, string extension,
        CancellationToken cancellationToken)
    {
        var target = Target(source, extension);
        if (IsCurrent(source, target))
        {
            return File.Exists(target) ? target : source.Path;
        }

        // The read that runs may have started for another track, before this one was known to it
        for (var attempt = 0; attempt < 2 && !IsCurrent(source, target); attempt++)
        {
            await ShareAsync("video:" + source.Path, token => ExtractAsync(source, sameVideo, token), cancellationToken);
        }

        if (!File.Exists(target))
        {
            throw new SubtitleFileException("ffmpeg wrote nothing for this subtitle.");
        }

        return target;
    }

    // Every text track of the video in one run of ffmpeg: it reads the whole file either way, and a player that shows
    // one track often asks for the next. Each track comes out as WebVTT and as the file it is burned in from.
    private async Task ExtractAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, CancellationToken cancellationToken)
    {
        var others = sameVideo
            .Where(other => other.StreamIndex is not null && other.StreamIndex != source.StreamIndex && other.Path == source.Path)
            .Where(other => !IsCurrent(other, Target(other, ".vtt")) || !IsCurrent(other, Target(other, BurnedExtension(other))));
        var tracks = new[] { source }.Concat(others)
            .Select(track => (Source: track, Extracted: PartialPath(ExtractedExtension(track)),
                WithFonts: Format(track) == SubtitleFormat.Ass ? PartialPath(BurnedExtension(track)) : null))
            .ToList();
        Directory.CreateDirectory(FolderPath());
        try
        {
            try
            {
                await _extractor.ExtractAsync(source.Path, Extractions(tracks), cancellationToken);
            }
            catch (SubtitleFileException) when (tracks.Count > 1)
            {
                // One track ffmpeg cannot write fails the whole run; the track asked for must not pay for another
                tracks = tracks[..1];
                await _extractor.ExtractAsync(source.Path, Extractions(tracks), cancellationToken);
            }

            foreach (var (track, extracted, withFonts) in tracks)
            {
                var content = await File.ReadAllBytesAsync(extracted, cancellationToken);
                var cues = SubtitleConversion.Read(content, Format(track), track.Language);
                await WriteAsync(Target(track, ".vtt"), WebVttWriter.Write(cues), cancellationToken);
                if (withFonts is null)
                {
                    await WriteAsync(Target(track, ".srt"), SrtWriter.Write(cues), cancellationToken);
                }
                else
                {
                    File.Move(withFonts, Target(track, ".mks"), overwrite: true);
                }
            }
        }
        finally
        {
            foreach (var (_, extracted, withFonts) in tracks)
            {
                SubtitlePlacement.TryDelete(extracted);
                if (withFonts is not null)
                {
                    SubtitlePlacement.TryDelete(withFonts);
                }
            }
        }
    }

    private static List<SubtitleExtraction> Extractions(IEnumerable<(SubtitleSource Source, string Extracted, string? WithFonts)> tracks) =>
        tracks.SelectMany(track => new[] { track.Extracted, track.WithFonts }.OfType<string>()
                .Select(target => new SubtitleExtraction(track.Source.StreamIndex!.Value, target)))
            .ToList();

    private string PartialPath(string extension) => Path.Combine(FolderPath(), $"{Guid.NewGuid():N}.partial{extension}");

    private async Task ShareAsync(string key, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var running = _running.GetOrAdd(key, _ => new Lazy<Task>(() => RunAsync(key, work)));
        await running.Value.WaitAsync(cancellationToken);
    }

    private async Task RunAsync(string key, Func<CancellationToken, Task> work)
    {
        // Off the caller's path first, so the entry is in the list before it can be taken out again
        await Task.Yield();
        try
        {
            await work(_stopping.Token);
        }
        finally
        {
            _running.TryRemove(key, out _);
        }
    }

    // Written under its own name first, so two requests at once never serve half a file
    private async Task WriteAsync(string target, string converted, CancellationToken cancellationToken)
    {
        var folder = FolderPath();
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, $"{Guid.NewGuid():N}.partial{Path.GetExtension(target)}");
        try
        {
            await File.WriteAllTextAsync(partial, converted, Utf8, cancellationToken);
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            SubtitlePlacement.TryDelete(partial);
        }
    }

    private async Task<List<SubtitleCue>> ReadAsync(SubtitleSource source, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(source.Path).ToLowerInvariant();
        if (source.StreamIndex is null && extension is ".srt" or ".ass" or ".ssa")
        {
            var content = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            return SubtitleConversion.Read(content, Format(source), source.Language);
        }

        // A file in another text format, or one embedded track: ffmpeg reads it and writes ASS untouched or SRT
        var extracted = Path.Combine(FolderPath(), $"{Guid.NewGuid():N}.partial{ExtractedExtension(source)}");
        Directory.CreateDirectory(FolderPath());
        try
        {
            await _extractor.ExtractAsync(source.Path, source.StreamIndex ?? 0, extracted, cancellationToken);
            var content = await File.ReadAllBytesAsync(extracted, cancellationToken);
            return SubtitleConversion.Read(content, Format(source), source.Language);
        }
        finally
        {
            SubtitlePlacement.TryDelete(extracted);
        }
    }

    // A subtitle replaced under the same name (a better download, a new translation) keeps its stream id, so the
    // conversion is made again when the source is newer. A source that is gone keeps what was made of it.
    private static bool IsCurrent(SubtitleSource source, string target) =>
        !File.Exists(source.Path) || (File.Exists(target) && File.GetLastWriteTimeUtc(source.Path) <= File.GetLastWriteTimeUtc(target));

    private string Target(SubtitleSource source, string extension) =>
        Path.Combine(FolderPath(), $"{source.Key}.v{SubtitleConversion.Version}{extension}");

    private string FolderPath() => Path.Combine(_options.Value.ResolveTempPath(), Folder);

    // Embedded tracks come out as ASS untouched or as SRT, which ffmpeg writes from the container's own timing
    private static string ExtractedExtension(SubtitleSource source) => Format(source) == SubtitleFormat.Ass ? ".ass" : ".srt";

    // An embedded ASS often uses fonts the video carries as attachments. The subtitles filter loads the fonts of the file
    // it reads, so the track is burned from a Matroska file of its own that holds them too.
    private static string BurnedExtension(SubtitleSource source) => Format(source) == SubtitleFormat.Ass ? ".mks" : ".srt";

    private static SubtitleFormat Format(SubtitleSource source) =>
        SubtitleConversion.FormatOf(source.Codec ?? Path.GetExtension(source.Path));
}
