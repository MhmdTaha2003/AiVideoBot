namespace AiVideoBot.Services;

public interface IVoiceGenerationService
{
    Task GenerateAsync(
        string text,
        string language,
        string outputPath,
        CancellationToken cancellationToken);
}
