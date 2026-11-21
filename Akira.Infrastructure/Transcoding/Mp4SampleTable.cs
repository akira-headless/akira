using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Reads the keyframe times of an MP4 or QuickTime file's video track from its sample table: the sync samples (stss),
/// their decode times (stts), composition offsets (ctts) and the edit list. Only the box headers and the moov box are read.
/// </summary>
public static class Mp4SampleTable
{
    // The moov of a three hour film is a few MB; anything far larger is not one to trust
    private const int MaxMoov = 128 * 1024 * 1024;
    private const int MaxTopLevel = 10_000;

    // What a QuickTime file may start with; anything else, such as Matroska's EBML header, is not one
    private static readonly HashSet<string> FirstBoxes = ["ftyp", "moov", "wide", "free", "skip", "mdat", "pnot"];

    /// <summary>
    /// Returns the keyframe times of the first video track in seconds, as ffmpeg presents them, or null when the file is
    /// not MP4, is fragmented, or marks no sync samples (every frame is one).
    /// </summary>
    public static async Task<IReadOnlyList<double>?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        var length = RandomAccess.GetLength(file);

        long position = 0;
        for (var step = 0; step < MaxTopLevel && position < length; step++)
        {
            if (await HeaderAsync(file, position, length, cancellationToken) is not { } box || (step == 0 && !FirstBoxes.Contains(box.Type)))
            {
                return null;
            }

            if (box.Type == "moov")
            {
                if (box.Size - box.HeaderSize > MaxMoov || position + box.Size > length)
                {
                    return null;
                }

                var moov = new byte[box.Size - box.HeaderSize];
                await RandomAccess.ReadAsync(file, moov, position + box.HeaderSize, cancellationToken);
                return Keyframes(moov);
            }

            position += box.Size;
        }

        return null;
    }

    /// <summary>
    /// Reads the keyframes of the first video track from the content of a moov box.
    /// </summary>
    public static IReadOnlyList<double>? Keyframes(ReadOnlyMemory<byte> moov)
    {
        // Fragmented: the samples are in the fragments, not in this table
        if (Child(moov, "mvex") is not null || Child(moov, "mvhd") is not { } mvhd)
        {
            return null;
        }

        var movieScale = Scale(mvhd.Span);
        foreach (var trak in Children(moov, "trak"))
        {
            if (Child(trak, "mdia") is not { } mdia || Child(mdia, "hdlr") is not { Length: >= 12 } hdlr
                || Encoding.ASCII.GetString(hdlr.Span.Slice(8, 4)) != "vide")
            {
                continue;
            }

            if (Child(mdia, "mdhd") is not { } mdhd || Child(mdia, "minf") is not { } minf || Child(minf, "stbl") is not { } stbl
                || Child(stbl, "stss") is not { } stss || Child(stbl, "stts") is not { } stts)
            {
                return null;
            }

            var scale = Scale(mdhd.Span);
            if (scale == 0)
            {
                return null;
            }

            var (mediaTime, delay) = Edits(trak, movieScale);
            var decodeTimes = Runs(stts.Span);
            var offsets = Child(stbl, "ctts") is { } ctts ? Runs(ctts.Span) : [];
            var syncSamples = stss.Span;
            var times = new List<double>();
            for (var i = 0; i < Count(syncSamples) && 12 + i * 4 <= syncSamples.Length; i++)
            {
                var sample = BinaryPrimitives.ReadUInt32BigEndian(syncSamples[(8 + i * 4)..]);
                if (sample > 0 && DecodeTime(decodeTimes, sample - 1) is { } decode)
                {
                    times.Add((double)(decode + Offset(offsets, sample - 1) - mediaTime) / scale + delay);
                }
            }

            var keyframes = times.Where(time => time >= 0).Distinct().Order().ToList();
            return keyframes.Count == 0 ? null : keyframes;
        }

        return null;
    }

    // mvhd and mdhd keep their time scale after the creation and modification times, which are wider in version 1
    private static uint Scale(ReadOnlySpan<byte> header)
    {
        var at = header.Length > 0 && header[0] == 1 ? 20 : 12;
        return header.Length >= at + 4 ? BinaryPrimitives.ReadUInt32BigEndian(header[at..]) : 0;
    }

    // The first edit that shows media says where the track's time starts; empty edits before it delay the track, as
    // ffmpeg applies them
    private static (long MediaTime, double Delay) Edits(ReadOnlyMemory<byte> trak, uint movieScale)
    {
        if (Child(trak, "edts") is not { } edts || Child(edts, "elst") is not { } list)
        {
            return (0, 0);
        }

        var elst = list.Span;
        var wide = elst.Length > 0 && elst[0] == 1;
        var size = wide ? 20 : 12;
        double delay = 0;
        for (var i = 0; i < Count(elst) && 8 + (i + 1) * size <= elst.Length; i++)
        {
            var entry = elst.Slice(8 + i * size, size);
            var duration = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(entry) : BinaryPrimitives.ReadUInt32BigEndian(entry);
            var media = wide ? BinaryPrimitives.ReadInt64BigEndian(entry[8..]) : BinaryPrimitives.ReadInt32BigEndian(entry[4..]);
            if (media != -1)
            {
                return (media, delay);
            }

            delay += movieScale == 0 ? 0 : (double)duration / movieScale;
        }

        return (0, delay);
    }

    // stts and ctts are runs of (count, value); ctts values are taken as signed, as ffmpeg reads them
    private static (uint Count, int Value)[] Runs(ReadOnlySpan<byte> box)
    {
        var runs = new (uint, int)[Math.Max(0, Math.Min(Count(box), (box.Length - 8) / 8))];
        for (var i = 0; i < runs.Length; i++)
        {
            runs[i] = (BinaryPrimitives.ReadUInt32BigEndian(box[(8 + i * 8)..]), BinaryPrimitives.ReadInt32BigEndian(box[(12 + i * 8)..]));
        }

        return runs;
    }

    private static long? DecodeTime((uint Count, int Value)[] runs, long sample)
    {
        long time = 0;
        foreach (var (count, delta) in runs)
        {
            if (sample < count)
            {
                return time + sample * (uint)delta;
            }

            time += count * (long)(uint)delta;
            sample -= count;
        }

        return null;
    }

    private static long Offset((uint Count, int Value)[] runs, long sample)
    {
        foreach (var (count, offset) in runs)
        {
            if (sample < count)
            {
                return offset;
            }

            sample -= count;
        }

        return 0;
    }

    // A full box: version and flags, then the entry count
    private static int Count(ReadOnlySpan<byte> box) =>
        box.Length < 8 ? 0 : (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(box[4..]), int.MaxValue);

    private static ReadOnlyMemory<byte>? Child(ReadOnlyMemory<byte> parent, string type) => Children(parent, type).Cast<ReadOnlyMemory<byte>?>().FirstOrDefault();

    private static IEnumerable<ReadOnlyMemory<byte>> Children(ReadOnlyMemory<byte> parent, string type)
    {
        var position = 0;
        while (position + 8 <= parent.Length)
        {
            var span = parent.Span;
            long size = BinaryPrimitives.ReadUInt32BigEndian(span[position..]);
            var header = 8;
            if (size == 1 && position + 16 <= parent.Length)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(span[(position + 8)..]);
                header = 16;
            }
            else if (size == 0)
            {
                size = parent.Length - position;
            }

            if (size < header || position + size > parent.Length)
            {
                yield break;
            }

            if (Encoding.ASCII.GetString(span.Slice(position + 4, 4)) == type)
            {
                yield return parent.Slice(position + header, (int)size - header);
            }

            position += (int)size;
        }
    }

    private static async Task<(string Type, long Size, int HeaderSize)?> HeaderAsync(SafeFileHandle file, long position, long length,
        CancellationToken cancellationToken)
    {
        var header = new byte[16];
        if (position + 8 > length || await RandomAccess.ReadAsync(file, header, position, cancellationToken) < 8)
        {
            return null;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var headerSize = 8;
        if (size == 1)
        {
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = length - position;
        }

        return size < headerSize ? null : (Encoding.ASCII.GetString(header, 4, 4), size, headerSize);
    }
}
