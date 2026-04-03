using System.Diagnostics;

namespace BootPivot.Windows;

public sealed class ProcessExecutor : IProcessExecutor
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var output = new List<string>();
        var error = new List<string>();
        var sync = new object();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (sync)
            {
                output.Add(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (sync)
            {
                error.Add(eventArgs.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult(
                1,
                Array.Empty<string>(),
                [$"Failed to start process '{fileName}': {ex.Message}"]);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminateProcess(process);
            lock (sync)
            {
                error.Add($"Command cancelled: {fileName}");
            }

            return new ProcessExecutionResult(
                130,
                output.ToArray(),
                error.ToArray());
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            output.ToArray(),
            error.ToArray());
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort cancellation cleanup
        }
    }
}
