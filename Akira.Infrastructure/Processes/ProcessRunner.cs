using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Akira.Infrastructure.Processes;

/// <summary>
/// Starts ffmpeg and ffprobe. One run that is waited for goes through RunAsync: both pipes read to the end and bounded,
/// a timeout, and the whole process tree gone before it returns.
/// </summary>
public static class ProcessRunner
{
    private const int MaxErrorChars = 2000;
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Starts a program with its output and errors redirected, for a caller that reads them while it runs.
    /// </summary>
    public static Process Start(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        // An argument list, never a command line, so a path is always one argument
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            return process;
        }
        catch (Win32Exception exception)
        {
            process.Dispose();
            throw new ProcessRunException(ProcessRunFailure.NotStarted, $"'{fileName}' could not be started: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Runs a program to its end and returns its exit code and what it printed.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(ProcessRun run, CancellationToken cancellationToken)
    {
        using var process = Start(run.FileName, run.Arguments);
        if (run.BelowNormalPriority)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
            }
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(run.Timeout);

        // Both read to the end, a full pipe would make the program wait forever
        var output = ReadOutputAsync(process.StandardOutput.BaseStream, run.MaxOutputBytes, limit.Token);
        var error = ReadErrorAsync(process.StandardError, limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            var (bytes, truncated) = await output;
            return new ProcessResult(process.ExitCode, bytes, await error, truncated);
        }
        catch (OperationCanceledException)
        {
            // Gone before the caller goes on: it removes what the program wrote, which Windows refuses while the program has it open
            await KillAsync(process);
            try
            {
                await Task.WhenAll(output, error);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException)
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new ProcessRunException(ProcessRunFailure.TimedOut, $"'{run.FileName}' did not finish within {run.Timeout}.");
        }
    }

    /// <summary>
    /// Ends a program with everything it started, and waits a few seconds for it to be gone.
    /// </summary>
    public static async Task KillAsync(Process process)
    {
        Kill(process);
        try
        {
            using var timeout = new CancellationTokenSource(ExitWait);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // It exited between the check and the kill
        }
    }

    /// <summary>
    /// Reads what a program writes as errors to the end and returns the last part, where ffmpeg says what went wrong.
    /// </summary>
    public static async Task<string> ReadErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
            {
                text.Append(buffer, 0, read);
                if (text.Length > MaxErrorChars)
                {
                    text.Remove(0, text.Length - MaxErrorChars);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        return text.ToString().Trim();
    }

    // Keeps reading past the limit so the program never waits on a full pipe, but only keeps the first part
    private static async Task<(byte[] Bytes, bool Truncated)> ReadOutputAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var kept = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var room = (int)Math.Min(read, maxBytes - kept.Length);
            if (room > 0)
            {
                kept.Write(buffer, 0, room);
            }

            truncated |= room < read;
        }

        return (kept.ToArray(), truncated);
    }
}
