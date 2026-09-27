using AiVideoBot.Models;

namespace AiVideoBot.Services;

public interface IVideoPlanService
{
    Task<VideoPlan> GeneratePlanAsync(string userPrompt, CancellationToken cancellationToken);
}
