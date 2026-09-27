using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace AiVideoBot;

public class Worker : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly ILogger<Worker> _logger;
    private readonly TelegramBotClient _botClient;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;

        var botToken = configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new InvalidOperationException(
                "Telegram bot token is not configured. Set the 'Telegram:BotToken' user secret.");
        }

        _botClient = new TelegramBotClient(botToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        _logger.LogInformation("Telegram long-polling worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _botClient.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    if (update.Message is not { Type: MessageType.Text, Text: { } text } message)
                    {
                        continue;
                    }

                    _logger.LogInformation(
                        "Received Telegram message from ChatId {ChatId}: {MessageText}",
                        message.Chat.Id,
                        text);

                    await _botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"Prompt received ✅{Environment.NewLine}{text}",
                        cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Telegram polling failed. Retrying in {RetryDelay}", RetryDelay);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Telegram long-polling worker stopped");
    }
}
