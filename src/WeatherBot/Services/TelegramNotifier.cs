using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace WeatherBot.Services;

/// <summary>Результат отправки сообщения пользователю.</summary>
public enum SendStatus
{
    /// <summary>Сообщение доставлено (либо отправка пропущена в режиме dry-run).</summary>
    Sent,

    /// <summary>Пользователь заблокировал бота.</summary>
    Blocked,

    /// <summary>Отправить не удалось — стоит повторить в следующем запуске.</summary>
    Failed,
}

/// <summary>Отправка сообщений пользователю (реализация — <see cref="TelegramNotifier"/>).</summary>
public interface IMessageSender
{
    /// <summary>Отправляет сообщение в формате HTML.</summary>
    Task<SendStatus> SendHtmlAsync(long chatId, string html, CancellationToken cancellationToken);
}

/// <summary>Отправка сообщений в Telegram с повтором при временных сбоях.</summary>
public sealed class TelegramNotifier : IMessageSender
{
    private const int MaxAttempts = 3;

    private readonly ITelegramBotClient _bot;
    private readonly bool _dryRun;
    private readonly TimeProvider _timeProvider;

    public TelegramNotifier(ITelegramBotClient bot, bool dryRun, TimeProvider? timeProvider = null)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
        _dryRun = dryRun;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Отправляет сообщение в формате HTML.</summary>
    public async Task<SendStatus> SendHtmlAsync(long chatId, string html, CancellationToken cancellationToken)
    {
        if (_dryRun)
        {
            ConsoleLog.Info($"[dry-run] сообщение для chatId={chatId}:{Environment.NewLine}{html}");
            return SendStatus.Sent;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await _bot.SendMessage(
                        chatId: chatId,
                        text: html,
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return SendStatus.Sent;
            }
            catch (ApiRequestException exception) when (exception.ErrorCode == 403)
            {
                ConsoleLog.Warning($"chatId={chatId} недоступен: {exception.Message}");
                return SendStatus.Blocked;
            }
            catch (RequestException exception)
            {
                ConsoleLog.Warning(
                    $"Ошибка отправки chatId={chatId} (попытка {attempt} из {MaxAttempts}): {exception.Message}");
            }
            catch (HttpRequestException exception)
            {
                ConsoleLog.Warning(
                    $"Сеть недоступна при отправке chatId={chatId} (попытка {attempt} из {MaxAttempts}): {exception.Message}");
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return SendStatus.Failed;
    }
}
