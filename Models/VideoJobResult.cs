namespace AiVideoBot.Models;

public sealed class VideoJobResult
{
    public required string JobId { get; init; }

    public required string JobDirectory { get; init; }

    public required List<string> AudioFiles { get; init; }
}
