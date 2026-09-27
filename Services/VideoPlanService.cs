using System.Text.Json;
using AiVideoBot.Models;

namespace AiVideoBot.Services;

public sealed class VideoPlanService : IVideoPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICodexService _codexService;

    public VideoPlanService(ICodexService codexService)
    {
        _codexService = codexService;
    }

    public async Task<VideoPlan> GeneratePlanAsync(
        string userPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var planningPrompt = $$"""
            You are a video planning engine.

            Generate a structured plan for an AI-generated video.

            Return ONLY valid JSON.
            Do not use Markdown.
            Do not wrap the response in ```json.
            Do not include explanation before or after the JSON.

            Schema:

            {
              "title": "string",
              "language": "string",
              "durationSeconds": number,
              "aspectRatio": "9:16 | 16:9 | 1:1",
              "scenes": [
                {
                  "sceneNumber": number,
                  "durationSeconds": number,
                  "narration": "string",
                  "visualPrompt": "string",
                  "transition": "fade | cut | dissolve"
                }
              ]
            }

            Rules:
            - The total duration of all scenes should approximately equal durationSeconds.
            - narration should be in the requested language.
            - visualPrompt should preferably be written in English because it will later be passed to image/video generation models.
            - Use concise narration appropriate for the scene duration.
            - Use sequential scene numbers starting from 1.
            - If duration is not specified, default to 30 seconds.
            - If language is not specified, infer it from the user's prompt.
            - If aspect ratio is not specified, default to 9:16.

            User request:
            {{userPrompt}}
            """;

        var response = await _codexService.GenerateAsync(planningPrompt, cancellationToken);

        try
        {
            var plan = JsonSerializer.Deserialize<VideoPlan>(response, JsonOptions);
            Validate(plan);
            return plan!;
        }
        catch (JsonException exception)
        {
            throw new VideoPlanGenerationException(
                "Codex returned invalid video plan JSON.",
                exception);
        }
        catch (VideoPlanGenerationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new VideoPlanGenerationException(
                "The generated video plan could not be validated.",
                exception);
        }
    }

    private static void Validate(VideoPlan? plan)
    {
        if (plan is null)
        {
            throw new VideoPlanGenerationException("The generated video plan is empty.");
        }

        if (string.IsNullOrWhiteSpace(plan.Title))
        {
            throw new VideoPlanGenerationException("The generated video plan has no title.");
        }

        if (plan.DurationSeconds <= 0)
        {
            throw new VideoPlanGenerationException("The generated video plan has an invalid duration.");
        }

        if (plan.Scenes is null || plan.Scenes.Count == 0)
        {
            throw new VideoPlanGenerationException("The generated video plan has no scenes.");
        }

        foreach (var scene in plan.Scenes)
        {
            if (scene.SceneNumber <= 0 ||
                scene.DurationSeconds <= 0 ||
                string.IsNullOrWhiteSpace(scene.Narration) ||
                string.IsNullOrWhiteSpace(scene.VisualPrompt))
            {
                throw new VideoPlanGenerationException(
                    $"Scene {scene.SceneNumber} contains invalid or missing required values.");
            }
        }
    }
}

public sealed class VideoPlanGenerationException : Exception
{
    public VideoPlanGenerationException(string message)
        : base(message)
    {
    }

    public VideoPlanGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
