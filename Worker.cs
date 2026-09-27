using System.Text.Json;
using AiVideoBot.Models;
using AiVideoBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace AiVideoBot;

public class Worker : BackgroundService
{
    private const int TelegramMessageLimit = 4000;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions VideoPlanJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<Worker> _logger;
    private readonly TelegramBotClient _botClient;
    private readonly ICodexService _codexService;
    private readonly IVideoPlanService _videoPlanService;
    private readonly IVideoJobService _videoJobService;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        ICodexService codexService,
        IVideoPlanService videoPlanService,
        IVideoJobService videoJobService)
    {
        _logger = logger;
        _codexService = codexService;
        _videoPlanService = videoPlanService;
        _videoJobService = videoJobService;

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

                    if (IsVideoCommand(text))
                    {
                        await HandleVideoPromptAsync(message.Chat.Id, text[6..].Trim(), stoppingToken);
                    }
                    else
                    {
                        await HandlePromptAsync(message.Chat.Id, text, stoppingToken);
                    }
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

    private async Task HandleVideoPromptAsync(
        long chatId,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Please provide a video description after /video.",
                cancellationToken: cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Received video planning request from ChatId {ChatId}: {VideoPrompt}",
            chatId,
            prompt);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Planning your video... 🎬",
            cancellationToken: cancellationToken);

        VideoPlan plan;

        try
        {
            plan = await _videoPlanService.GeneratePlanAsync(prompt, cancellationToken);

            _logger.LogInformation(
                "Generated {SceneCount} scenes with a requested duration of {DurationSeconds} seconds for ChatId {ChatId}",
                plan.Scenes.Count,
                plan.DurationSeconds,
                chatId);

            var formattedPlan = JsonSerializer.Serialize(plan, VideoPlanJsonOptions);
            await SendLongMessageAsync(chatId, formattedPlan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Video planning failed for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Failed to create video plan. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Generating narration... 🎙️",
            cancellationToken: cancellationToken);

        try
        {
            var job = await _videoJobService.CreateAsync(plan, cancellationToken);
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"Voice generation completed 🎙️{Environment.NewLine}" +
                      $"Job: {job.JobId}{Environment.NewLine}" +
                      $"Scenes: {job.AudioFiles.Count}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PiperConfigurationException exception)
        {
            _logger.LogError(exception, "Voice generation is not configured for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Voice generation is not configured on this machine.",
                cancellationToken: cancellationToken);
        }
        catch (UnsupportedVoiceLanguageException exception)
        {
            _logger.LogError(exception, "Unsupported voice language for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Voice generation currently supports English only.",
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Voice generation failed for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Voice generation failed. Please try again.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandlePromptAsync(
        long chatId,
        string prompt,
        CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(
            chatId: chatId,
            text: "Thinking... 🤖",
            cancellationToken: cancellationToken);

        try
        {
            var response = await _codexService.GenerateAsync(prompt, cancellationToken);
            await SendLongMessageAsync(chatId, response, cancellationToken);
        }
        catch (CodexNotAvailableException exception)
        {
            _logger.LogError(exception, "Codex is unavailable for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Codex is not available on this machine.",
                cancellationToken: cancellationToken);
        }
        catch (CodexExecutionException exception)
        {
            _logger.LogError(exception, "Codex generation failed for ChatId {ChatId}", chatId);
            await _botClient.SendMessage(
                chatId: chatId,
                text: "AI generation failed. Please try again.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendLongMessageAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken)
    {
        foreach (var chunk in SplitTelegramMessage(text))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: chunk,
                cancellationToken: cancellationToken);
        }
    }

    private static bool IsVideoCommand(string text)
    {
        return text.StartsWith("/video", StringComparison.OrdinalIgnoreCase) &&
               (text.Length == 6 || char.IsWhiteSpace(text[6]));
    }

    private static IEnumerable<string> SplitTelegramMessage(string text)
    {
        var remaining = text.Trim();

        while (remaining.Length > TelegramMessageLimit)
        {
            var splitIndex = remaining.LastIndexOfAny(
                [' ', '\r', '\n', '\t'],
                TelegramMessageLimit - 1,
                TelegramMessageLimit);

            if (splitIndex <= 0)
            {
                splitIndex = TelegramMessageLimit;
            }

            yield return remaining[..splitIndex].TrimEnd();
            remaining = remaining[splitIndex..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }
}
