using System.Text.Json;
using AiVideoBot.Models;

namespace AiVideoBot.Services;

public sealed class VideoJobService : IVideoJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IVoiceGenerationService _voiceGenerationService;
    private readonly ILogger<VideoJobService> _logger;
    private readonly string _generatedDirectory;

    public VideoJobService(
        IVoiceGenerationService voiceGenerationService,
        ILogger<VideoJobService> logger,
        IHostEnvironment hostEnvironment)
    {
        _voiceGenerationService = voiceGenerationService;
        _logger = logger;
        _generatedDirectory = Path.Combine(hostEnvironment.ContentRootPath, "Generated");
    }

    public async Task<VideoJobResult> CreateAsync(
        VideoPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var jobId = Guid.NewGuid().ToString("N");
        var jobDirectory = Path.Combine(_generatedDirectory, jobId);
        Directory.CreateDirectory(jobDirectory);

        _logger.LogInformation(
            "Starting voice generation for JobId {JobId} with {SceneCount} scenes",
            jobId,
            plan.Scenes.Count);

        var planPath = Path.Combine(jobDirectory, "plan.json");
        await using (var planStream = File.Create(planPath))
        {
            await JsonSerializer.SerializeAsync(
                planStream,
                plan,
                JsonOptions,
                cancellationToken);
        }

        var audioFiles = new List<string>(plan.Scenes.Count);

        foreach (var scene in plan.Scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = Path.Combine(jobDirectory, $"scene-{scene.SceneNumber:00}.wav");
            _logger.LogInformation(
                "Starting synthesis for JobId {JobId}, scene {SceneNumber}, output {OutputFileName}",
                jobId,
                scene.SceneNumber,
                Path.GetFileName(outputPath));

            try
            {
                await _voiceGenerationService.GenerateAsync(
                    scene.Narration,
                    plan.Language,
                    outputPath,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Voice generation failed for JobId {JobId}, scene {SceneNumber}",
                    jobId,
                    scene.SceneNumber);
                throw;
            }

            audioFiles.Add(outputPath);
            _logger.LogInformation(
                "Completed synthesis for JobId {JobId}, scene {SceneNumber}, output {OutputFileName}",
                jobId,
                scene.SceneNumber,
                Path.GetFileName(outputPath));
        }

        _logger.LogInformation("Voice generation completed for JobId {JobId}", jobId);

        return new VideoJobResult
        {
            JobId = jobId,
            JobDirectory = jobDirectory,
            AudioFiles = audioFiles
        };
    }
}
