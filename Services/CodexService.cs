using System.ComponentModel;
using System.Diagnostics;

namespace AiVideoBot.Services;

public sealed class CodexService : ICodexService
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<CodexService> _logger;
    private readonly string _workingDirectory;

    public CodexService(ILogger<CodexService> logger, IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _workingDirectory = hostEnvironment.ContentRootPath;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var finalPrompt = $$"""
            You are the text-generation engine for an AI video automation system.

            For this phase:
            - Answer the user's request with text only.
            - Do not modify any files.
            - Do not create files.
            - Do not modify source code.
            - Do not run shell commands.
            - Do not edit the AiVideoBot project.
            - Return only the requested content.

            User request:
            {{prompt}}
            """;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "codex",
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add("exec");
        processStartInfo.ArgumentList.Add(finalPrompt);

        using var process = new Process { StartInfo = processStartInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            _logger.LogError(exception, "The Codex executable could not be started");
            throw new CodexNotAvailableException("The Codex executable is not available.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCancellation = new CancellationTokenSource(ExecutionTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            _logger.LogError("Codex execution exceeded the {Timeout} timeout", ExecutionTimeout);
            throw new CodexExecutionException("Codex execution timed out.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "Codex exited with code {ExitCode}. Standard error: {StandardError}",
                process.ExitCode,
                standardError);
            throw new CodexExecutionException("Codex execution failed.");
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            _logger.LogError("Codex completed successfully but returned no text");
            throw new CodexExecutionException("Codex returned no text.");
        }

        _logger.LogInformation("Codex generation completed successfully");
        return standardOutput.Trim();
    }

    private static void KillProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

public sealed class CodexNotAvailableException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class CodexExecutionException(string message) : Exception(message);
