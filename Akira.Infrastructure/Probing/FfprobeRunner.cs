using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Processes;

namespace Akira.Infrastructure.Probing;

public class FfprobeRunner : IFfprobeRunner
{
    public const int MaxOutputBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IOptions<ProbingOptions> _options;

    public FfprobeRunner(IOptions<ProbingOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var output = await RunAsync(["-version"], cancellationToken);
        return output.Split('\n', 2)[0].Trim();
    }

    /// <inheritdoc />
    public Task<FfprobeOutput> ProbeAsync(string path, CancellationToken cancellationToken) =>
        RunJsonAsync(["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-show_chapters", "-protocol_whitelist", "file",
                "file:" + path],
            cancellationToken);

    /// <inheritdoc />
    public Task<FfprobeOutput> ProbeSubtitleAsync(string path, CancellationToken cancellationToken) =>
        RunJsonAsync(["-v", "error", "-print_format", "json", "-count_packets", "-show_streams", "-protocol_whitelist", "file", "file:" + path],
            cancellationToken);

    private async Task<FfprobeOutput> RunJsonAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var output = await RunAsync(arguments, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<FfprobeOutput>(output, SerializerOptions)
                ?? throw new JsonException("ffprobe printed null.");
        }
        catch (JsonException exception)
        {
            throw new ProbeException(ProbeFailure.Malformed, "ffprobe printed output that could not be read.", exception);
        }
    }

    private async Task<string> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(new ProcessRun(settings.FfprobePath, arguments, settings.Timeout, MaxOutputBytes), cancellationToken);
        }
        catch (ProcessRunException exception) when (exception.Failure == ProcessRunFailure.NotStarted)
        {
            throw new ProbeException(ProbeFailure.NotInstalled,
                $"ffprobe could not be started from '{settings.FfprobePath}'. Install ffmpeg or set " +
                $"{ProbingOptions.SectionName}:{nameof(ProbingOptions.FfprobePath)}.", exception);
        }
        catch (ProcessRunException exception)
        {
            throw new ProbeException(ProbeFailure.Timeout, $"ffprobe did not finish within {settings.Timeout.TotalSeconds:0} seconds.", exception);
        }

        if (result.ExitCode != 0)
        {
            var reason = result.Error.Length == 0 ? "no message" : result.Error;
            throw new ProbeException(ProbeFailure.Failed, $"ffprobe exited with {result.ExitCode}: {reason}");
        }

        if (result.OutputTruncated)
        {
            throw new ProbeException(ProbeFailure.Malformed, $"ffprobe printed more than {MaxOutputBytes / (1024 * 1024)} MB.");
        }

        return Encoding.UTF8.GetString(result.Output);
    }
}
