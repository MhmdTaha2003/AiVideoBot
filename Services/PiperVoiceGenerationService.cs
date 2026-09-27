using System.ComponentModel;
using System.Diagnostics;

namespace AiVideoBot.Services;

public sealed class PiperVoiceGenerationService : IVoiceGenerationService
{
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(60);

    private readonly ILogger<PiperVoiceGenerationService> _logger;
    private readonly string _executablePath;
    private readonly string _englishModelPath;
    private readonly string _englishConfigPath;
    private readonly string _espeakDataPath;

    public PiperVoiceGenerationService(
        ILogger<PiperVoiceGenerationService> logger,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _executablePath = ResolvePath(configuration, hostEnvironment, "Piper:ExecutablePath");
        _englishModelPath = ResolvePath(configuration, hostEnvironment, "Piper:EnglishModelPath");
        _englishConfigPath = ResolvePath(configuration, hostEnvironment, "Piper:EnglishConfigPath");
        _espeakDataPath = ResolvePath(configuration, hostEnvironment, "Piper:EspeakDataPath");
    }

    public async Task GenerateAsync(
        string text,
        string language,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (string.IsNullOrWhiteSpace(language) ||
            !language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedVoiceLanguageException(language);
        }

        ValidateDependencies();

        var absoluteOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new VoiceGenerationException("The voice output directory does not exist.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_englishModelPath);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(_englishConfigPath);
        startInfo.ArgumentList.Add("--espeak_data");
        startInfo.ArgumentList.Add(_espeakDataPath);
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(absoluteOutputPath);

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCancellation = new CancellationTokenSource(SynthesisTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            _logger.LogError(exception, "Piper could not be started from {ExecutablePath}", _executablePath);
            throw new PiperConfigurationException("Piper could not be started.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteLineAsync(text.AsMemory(), linkedCancellation.Token);
            await process.StandardInput.FlushAsync(linkedCancellation.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            _logger.LogError("Piper synthesis exceeded the {Timeout} timeout", SynthesisTimeout);
            throw new VoiceGenerationException("Piper synthesis timed out.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }
        catch (Exception exception)
        {
            KillProcess(process);
            throw new VoiceGenerationException("Piper synthesis failed.", exception);
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "Piper exited with code {ExitCode}. Standard error: {StandardError}",
                process.ExitCode,
                standardError);
            throw new VoiceGenerationException("Piper synthesis failed.");
        }

        if (!File.Exists(absoluteOutputPath) || new FileInfo(absoluteOutputPath).Length == 0)
        {
            _logger.LogError(
                "Piper exited successfully but did not create a valid WAV file. Standard output: {StandardOutput}",
                standardOutput);
            throw new VoiceGenerationException("Piper did not create an audio file.");
        }
    }

    private void ValidateDependencies()
    {
        if (string.IsNullOrWhiteSpace(_executablePath) ||
            string.IsNullOrWhiteSpace(_englishModelPath) ||
            string.IsNullOrWhiteSpace(_englishConfigPath) ||
            string.IsNullOrWhiteSpace(_espeakDataPath))
        {
            throw new PiperConfigurationException("One or more required Piper configuration values are missing.");
        }

        if (!File.Exists(_executablePath))
        {
            throw new PiperConfigurationException($"Piper executable was not found at '{_executablePath}'.");
        }

        if (!File.Exists(_englishModelPath))
        {
            throw new PiperConfigurationException($"Piper model was not found at '{_englishModelPath}'.");
        }

        if (!File.Exists(_englishConfigPath))
        {
            throw new PiperConfigurationException($"Piper model config was not found at '{_englishConfigPath}'.");
        }

        if (!Directory.Exists(_espeakDataPath))
        {
            throw new PiperConfigurationException($"eSpeak data was not found at '{_espeakDataPath}'.");
        }
    }

    private static string ResolvePath(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string configurationKey)
    {
        var configuredPath = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return string.Empty;
        }

        return Path.GetFullPath(configuredPath, hostEnvironment.ContentRootPath);
    }

    private static void KillProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

public sealed class PiperConfigurationException : Exception
{
    public PiperConfigurationException(string message)
        : base(message)
    {
    }

    public PiperConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class UnsupportedVoiceLanguageException(string? language)
    : Exception($"Voice generation does not support language '{language ?? "(null)"}'.");

public sealed class VoiceGenerationException : Exception
{
    public VoiceGenerationException(string message)
        : base(message)
    {
    }

    public VoiceGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
