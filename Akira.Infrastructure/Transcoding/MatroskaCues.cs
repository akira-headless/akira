using System.Numerics;
using Microsoft.Win32.SafeHandles;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Reads the keyframe times of a Matroska file's video track from its Cues, the index players seek with. Only the
/// header elements and the Cues are read, never the clusters.
/// </summary>
public static class MatroskaCues
{
    private const uint EbmlId = 0x1A45DFA3;
    private const uint SegmentId = 0x18538067;
    private const uint SeekHeadId = 0x114D9B74;
    private const uint InfoId = 0x1549A966;
    private const uint TracksId = 0x1654AE6B;
    private const uint CuesId = 0x1C53BB6B;
    private const uint SeekId = 0x4DBB;
    private const uint SeekIdId = 0x53AB;
    private const uint SeekPositionId = 0x53AC;
    private const uint TimestampScaleId = 0x2AD7B1;
    private const uint TrackEntryId = 0xAE;
    private const uint TrackNumberId = 0xD7;
    private const uint TrackTypeId = 0x83;
    private const uint CuePointId = 0xBB;
    private const uint CueTimeId = 0xB3;
    private const uint CueTrackPositionsId = 0xB7;
    private const uint CueTrackId = 0xF7;

    private const int VideoTrackType = 1;
    private const long DefaultTimestampScale = 1_000_000;
    private const int MaxHeaderElement = 16 * 1024 * 1024;
    private const int MaxCues = 64 * 1024 * 1024;

    // Top-level elements to step over before giving up, a long film has a few thousand clusters
    private const int MaxTopLevel = 100_000;

    /// <summary>
    /// Returns the keyframe times of the first video track in seconds, as the file stores them, or null when the file is
    /// not Matroska or has no Cues for its video.
    /// </summary>
    public static async Task<IReadOnlyList<double>?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        var length = RandomAccess.GetLength(file);

        var ebml = await HeaderAsync(file, 0, length, cancellationToken);
        if (ebml is not { Id: EbmlId, Size: >= 0 } header)
        {
            return null;
        }

        var segment = await HeaderAsync(file, header.DataStart + header.Size, length, cancellationToken);
        if (segment is not { Id: SegmentId } segmentHeader)
        {
            return null;
        }

        var segmentStart = segmentHeader.DataStart;
        var segmentEnd = segmentHeader.Size >= 0 ? Math.Min(length, segmentStart + segmentHeader.Size) : length;

        // Where each element is, from the walk over the segment and from the seek heads, which may point past the clusters
        var found = new Dictionary<uint, long>();
        var seekHeads = new Queue<long>();
        var readSeekHeads = new HashSet<long>();
        var position = segmentStart;
        for (var step = 0; step < MaxTopLevel && position < segmentEnd; step++)
        {
            if (found.ContainsKey(InfoId) && found.ContainsKey(TracksId) && found.ContainsKey(CuesId))
            {
                break;
            }

            if (await HeaderAsync(file, position, segmentEnd, cancellationToken) is not { } element)
            {
                break;
            }

            found.TryAdd(element.Id, position);
            if (element.Id == SeekHeadId)
            {
                seekHeads.Enqueue(position);
            }

            while (seekHeads.TryDequeue(out var seekHead))
            {
                if (readSeekHeads.Add(seekHead) && await BodyAsync(file, seekHead, segmentEnd, MaxHeaderElement, cancellationToken) is { } body)
                {
                    foreach (var (id, offset) in SeekEntries(body))
                    {
                        found.TryAdd(id, segmentStart + offset);
                        if (id == SeekHeadId)
                        {
                            seekHeads.Enqueue(segmentStart + offset);
                        }
                    }
                }
            }

            // A cluster still being written has no size, nothing after it can be found by walking
            if (element.Size < 0)
            {
                break;
            }

            position = element.DataStart + element.Size;
        }

        if (!found.TryGetValue(TracksId, out var tracksAt) || !found.TryGetValue(CuesId, out var cuesAt))
        {
            return null;
        }

        var scale = DefaultTimestampScale;
        if (found.TryGetValue(InfoId, out var infoAt) && await BodyAsync(file, infoAt, segmentEnd, MaxHeaderElement, cancellationToken) is { } info)
        {
            scale = Children(info).Where(c => c.Id == TimestampScaleId).Select(c => (long)Unsigned(c.Data)).FirstOrDefault(DefaultTimestampScale);
        }

        var tracks = await BodyAsync(file, tracksAt, segmentEnd, MaxHeaderElement, cancellationToken);
        if (tracks is null || VideoTrack(tracks) is not { } video)
        {
            return null;
        }

        var cues = await BodyAsync(file, cuesAt, segmentEnd, MaxCues, cancellationToken);
        if (cues is null)
        {
            return null;
        }

        var keyframes = CueTimes(cues, video).Select(time => time * (double)scale / 1_000_000_000).Distinct().Order().ToList();
        return keyframes.Count > 0 ? keyframes : null;
    }

    private static ulong? VideoTrack(byte[] tracks)
    {
        foreach (var entry in Children(tracks).Where(c => c.Id == TrackEntryId))
        {
            ulong? number = null;
            ulong? type = null;
            foreach (var field in Children(entry.Data))
            {
                if (field.Id == TrackNumberId) number = Unsigned(field.Data);
                else if (field.Id == TrackTypeId) type = Unsigned(field.Data);
            }

            if (type == VideoTrackType && number is not null)
            {
                return number;
            }
        }

        return null;
    }

    private static IEnumerable<ulong> CueTimes(byte[] cues, ulong track)
    {
        foreach (var point in Children(cues).Where(c => c.Id == CuePointId))
        {
            ulong? time = null;
            var forTrack = false;
            foreach (var field in Children(point.Data))
            {
                if (field.Id == CueTimeId)
                {
                    time = Unsigned(field.Data);
                }
                else if (field.Id == CueTrackPositionsId)
                {
                    forTrack |= Children(field.Data).Any(c => c.Id == CueTrackId && Unsigned(c.Data) == track);
                }
            }

            if (time is { } value && forTrack)
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<(uint Id, long Offset)> SeekEntries(byte[] seekHead)
    {
        foreach (var seek in Children(seekHead).Where(c => c.Id == SeekId))
        {
            uint? id = null;
            long? offset = null;
            foreach (var field in Children(seek.Data))
            {
                if (field.Id == SeekIdId && field.Data.Length is > 0 and <= 4) id = (uint)Unsigned(field.Data);
                else if (field.Id == SeekPositionId) offset = (long)Unsigned(field.Data);
            }

            if (id is not null && offset is >= 0)
            {
                yield return (id.Value, offset.Value);
            }
        }
    }

    // The children of an element's body; a child that runs past the body ends the list
    private static IEnumerable<(uint Id, byte[] Data)> Children(byte[] body)
    {
        var position = 0;
        while (position < body.Length)
        {
            if (Parse(body.AsSpan(position)) is not { } header || header.Size < 0 || position + header.Length + header.Size > body.Length)
            {
                yield break;
            }

            yield return (header.Id, body.AsSpan(position + header.Length, (int)header.Size).ToArray());
            position += header.Length + (int)header.Size;
        }
    }

    private static ulong Unsigned(byte[] data)
    {
        ulong value = 0;
        foreach (var b in data.Take(8))
        {
            value = (value << 8) | b;
        }

        return value;
    }

    private static async Task<byte[]?> BodyAsync(SafeFileHandle file, long position, long end, int max, CancellationToken cancellationToken)
    {
        if (await HeaderAsync(file, position, end, cancellationToken) is not { Size: >= 0 } header || header.Size > max
            || header.DataStart + header.Size > end)
        {
            return null;
        }

        var body = new byte[header.Size];
        var read = 0;
        while (read < body.Length)
        {
            var count = await RandomAccess.ReadAsync(file, body.AsMemory(read), header.DataStart + read, cancellationToken);
            if (count == 0)
            {
                return null;
            }

            read += count;
        }

        return body;
    }

    private static async Task<Element?> HeaderAsync(SafeFileHandle file, long position, long end, CancellationToken cancellationToken)
    {
        // An id is at most 4 bytes and a size at most 8
        var buffer = new byte[12];
        var count = await RandomAccess.ReadAsync(file, buffer.AsMemory(0, (int)Math.Min(buffer.Length, Math.Max(0, end - position))), position,
            cancellationToken);
        return Parse(buffer.AsSpan(0, count)) is { } header ? new Element(header.Id, header.Size, position + header.Length) : null;
    }

    // An id keeps its length marker, a size drops it; a size of all ones is unknown, returned as -1
    private static (uint Id, long Size, int Length)? Parse(ReadOnlySpan<byte> bytes)
    {
        var idLength = bytes.Length == 0 ? 0 : VintLength(bytes[0]);
        if (idLength is < 1 or > 4 || bytes.Length < idLength)
        {
            return null;
        }

        uint id = 0;
        for (var i = 0; i < idLength; i++)
        {
            id = (id << 8) | bytes[i];
        }

        var rest = bytes[idLength..];
        var sizeLength = rest.Length == 0 ? 0 : VintLength(rest[0]);
        if (sizeLength is < 1 or > 8 || rest.Length < sizeLength)
        {
            return null;
        }

        var size = (ulong)(rest[0] & (0xFF >> sizeLength));
        var unknown = size == (ulong)(0xFF >> sizeLength);
        for (var i = 1; i < sizeLength; i++)
        {
            size = (size << 8) | rest[i];
            unknown &= rest[i] == 0xFF;
        }

        if (!unknown && size > long.MaxValue)
        {
            return null;
        }

        return (id, unknown ? -1 : (long)size, idLength + sizeLength);
    }

    private static int VintLength(byte first) => first == 0 ? 0 : BitOperations.LeadingZeroCount(first) - 23;

    /// <summary>
    /// Represents an element header in the file: its id, the size of its body (-1 when unknown) and where the body starts.
    /// </summary>
    private sealed record Element(uint Id, long Size, long DataStart);
}
