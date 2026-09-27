namespace AiVideoBot.Services;

public interface ICodexService
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
