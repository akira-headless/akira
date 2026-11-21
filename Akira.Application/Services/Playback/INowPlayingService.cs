using Akira.Server.Models.Playback;

namespace Akira.Server.Services.Playback;

public interface INowPlayingService
{
    /// <summary>
    /// Lists the playbacks going on now: who plays what on which device, how it is delivered and how a transcode keeps up.
    /// </summary>
    Task<IReadOnlyList<NowPlaying>> ListAsync(CancellationToken cancellationToken);
}
