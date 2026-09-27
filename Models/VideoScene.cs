namespace AiVideoBot.Models;

public sealed class VideoScene
{
    public int SceneNumber { get; set; }

    public int DurationSeconds { get; set; }

    public string Narration { get; set; } = string.Empty;

    public string VisualPrompt { get; set; } = string.Empty;

    public string Transition { get; set; } = string.Empty;
}
