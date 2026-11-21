using System.Diagnostics;
using Microsoft.Extensions.Options;
using Akira.Core.Playback;
using Akira.Infrastructure.Markers;
using Akira.Infrastructure.Transcoding;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Markers;

[Trait("Category", "Integration")]
public class AudioFingerprinterTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("akira_fingerprint_").FullName;
    private readonly AudioFingerprinter _fingerprinter = new(Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));

    public async ValueTask InitializeAsync()
    {
        if (!await SupportsChromaprintAsync())
        {
            Assert.Skip("This ffmpeg has no chromaprint muxer. CI's and the Docker image's ffmpeg have it.");
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ThreeEpisodesWithTheSameIntro_ShareIt_WhereverItStarts()
    {
        var intro = await NotesAsync("intro", seed: 7, seconds: 40);
        var episodes = new List<(string, int, uint[])>();
        foreach (var (number, before) in new[] { (1, 12), (2, 31), (3, 5) })
        {
            var file = await ConcatAsync($"e{number}", await NotesAsync($"cold{number}", seed: number * 11, seconds: before), intro,
                await NotesAsync($"rest{number}", seed: number * 13 + 1, seconds: 90));
            var points = await _fingerprinter.FingerprintAsync(file, 0, TimeSpan.Zero, TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);
            episodes.Add(($"e{number}", number, points));
        }

        var found = AudioMatch.InSeason(episodes, new AudioMatchOptions(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(150)));

        foreach (var (key, shared) in found.OrderBy(entry => entry.Key))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{key}: intro {shared.StartA:0.0}s to {shared.EndA:0.0}s");
        }

        Assert.Equal(["e1", "e2", "e3"], found.Keys.Order());
        Assert.InRange(found["e2"].StartA, 30, 32);
        Assert.InRange(found["e2"].Length, 35, 42);
    }

    // Set AKIRA_MARKER_SEASON to a folder with the episodes of one season to see what the matching finds in real files
    [Fact]
    public async Task ARealSeason_WhenOneIsGiven()
    {
        var folder = Environment.GetEnvironmentVariable("AKIRA_MARKER_SEASON");
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Skip("AKIRA_MARKER_SEASON is not set.");
        }

        // Whole episodes, since a recap shows clips from anywhere in the ones before
        var whole = new List<(string Name, int Episode, uint[] Points)>();
        foreach (var (file, index) in Directory.GetFiles(folder!).Where(f => f.EndsWith(".mkv") || f.EndsWith(".mp4")).Order().Take(6).Select((f, i) => (f, i)))
        {
            var audio = await FirstAudioIndexAsync(file);
            whole.Add((Path.GetFileName(file), index + 1,
                await _fingerprinter.FingerprintAsync(file, audio, TimeSpan.Zero, TimeSpan.FromHours(3), TestContext.Current.CancellationToken)));
        }

        var tenMinutes = (int)(600 / AudioMatch.SecondsPerPoint);
        var episodes = whole.Select(e => (e.Name, e.Episode, e.Points[..Math.Min(e.Points.Length, tenMinutes)])).ToList();
        var found = AudioMatch.InSeason(episodes, new AudioMatchOptions(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(150)));

        foreach (var (name, episode, start) in episodes)
        {
            var before = Math.Min(RecapMatch.Window.TotalSeconds, found.TryGetValue(name, out var intro) ? intro.StartA : double.MaxValue);
            var earlier = whole.Where(e => e.Episode < episode).OrderByDescending(e => e.Episode).Take(3).Select(e => e.Points).ToList();
            var recap = RecapMatch.Find(start[..Math.Min(start.Length, (int)(before / AudioMatch.SecondsPerPoint))], earlier);
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: "
                + (recap is null ? "no recap" : $"recap {TimeSpan.FromSeconds(recap.Start):mm\\:ss} to {TimeSpan.FromSeconds(recap.End):mm\\:ss}") + ", "
                + (intro is null ? "no intro" : $"intro {TimeSpan.FromSeconds(intro.StartA):mm\\:ss} to {TimeSpan.FromSeconds(intro.EndA):mm\\:ss}"));
        }

        Assert.NotEmpty(found);
    }

    // Half second notes of a scale picked by a fixed sequence, so every seed is its own melody and the same seed the same one
    private async Task<string> NotesAsync(string name, int seed, int seconds)
    {
        var path = Path.Combine(_directory, name + ".wav");
        // Each seed starts elsewhere in the sequence; starting every melody at 0 made them all open on the same notes
        var expression = $"0.4*sin(2*PI*t*220*pow(2\\,floor(mod(abs(sin((floor(t*2)+{seed * 1000})*1.37))*1000\\,24))/12))";
        await RunAsync(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", $"aevalsrc={expression}:s=44100:d={seconds}", path);
        return path;
    }

    private async Task<string> ConcatAsync(string name, params string[] parts)
    {
        var list = Path.Combine(_directory, name + ".txt");
        await File.WriteAllLinesAsync(list, parts.Select(part => $"file '{part.Replace("\\", "/")}'"));
        var path = Path.Combine(_directory, name + ".mka");
        await RunAsync(Ffmpeg.Path, "-v", "error", "-f", "concat", "-safe", "0", "-i", list, "-c:a", "flac", path);
        return path;
    }

    private static async Task<int> FirstAudioIndexAsync(string file)
    {
        var output = await RunAsync(Ffprobe.Path, "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=index", "-of", "csv=p=0", file);
        return int.Parse(output.Trim());
    }

    private static async Task<bool> SupportsChromaprintAsync() => (await RunAsync(Ffmpeg.Path, "-hide_banner", "-muxers")).Contains("chromaprint");

    private static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
