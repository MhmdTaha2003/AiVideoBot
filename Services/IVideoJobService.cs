using AiVideoBot.Models;

namespace AiVideoBot.Services;

public interface IVideoJobService
{
    Task<VideoJobResult> CreateAsync(VideoPlan plan, CancellationToken cancellationToken);
}
