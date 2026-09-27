namespace AiVideoBot.Models;

public sealed class VideoPlan
{
    public string Title { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public string AspectRatio { get; set; } = string.Empty;

    public List<VideoScene> Scenes { get; set; } = [];
}
