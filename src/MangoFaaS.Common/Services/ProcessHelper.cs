using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MangoFaaS.Common.Services;

public class ProcessExecutionService(ILogger<ProcessExecutionService> logger, Instrumentation instrumentation)
{
    public async Task RunProcess(string fileName, string args, CancellationToken token = default)
    {
        using var activity = instrumentation.StartActivity($"Running {fileName}");
        activity?.SetTag("ProcessName", fileName);
        activity?.SetTag("ProcessArgs", args);

        using Process process = new()
        {
            StartInfo =
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                FileName = fileName,
                Arguments = args
            }
        };
        process.Start();
        await process.WaitForExitAsync(token);
        if (process.HasExited)
        {
            var errorOutput = await process.StandardError.ReadToEndAsync(token);
            var output = await process.StandardOutput.ReadToEndAsync(token);

            if (process.ExitCode != 0)
            {
                logger.LogError("Process {FileName} {Args} failed with exit code {ExitCode}. Error: {ErrorOutput}", fileName, args, process.ExitCode, errorOutput);
                throw new InvalidOperationException($"Process failed with error: {errorOutput}");
            }

            logger.LogInformation("Process {FileName} {Args} completed successfully. Output: {Output}", fileName, args, output);

            return;
        }

        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("Process did not exit properly/timeout-ed!");
    }
}