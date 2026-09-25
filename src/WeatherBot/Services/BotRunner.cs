using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeatherBot.Configuration;
using WeatherBot.Models;

namespace WeatherBot.Services;

/// <summary>
/// Один запуск бота целиком: получить новые сообщения, ответить на них, разослать прогноз на завтра
/// тем, у кого уже наступил час рассылки, и сохранить состояние.
/// </summary>
public sealed class BotRunner
{
    private const int UpdatesPageSize = 100;
    private const int MaxUpdatePages = 10;

    private readonly ITelegramBotClient _bot;
    private readonly IWeatherService _weather;
    private readonly IMessageSender _notifier;
    private readonly IStateStore _stateStore;
    private readonly MessageProcessor _messageProcessor;
    private readonly BotState _state;
    private readonly BotOptions _options;
    private readonly TimeProvider _timeProvider;

    public BotRunner(
        ITelegramBotClient bot,
        IWeatherService weather,
        IMessageSender notifier,
        IStateStore stateStore,
        MessageProcessor messageProcessor,
        BotState state,
        BotOptions options,
        TimeProvider? timeProvider = null)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Выполняет один полный цикл работы бота.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ConsoleLog.Info(
            $"Запуск бота. Рассылка в {_options.DailySendHour}:00 по местному времени города пользователя, " +
            $"пояс по умолчанию UTC{_options.DefaultTimeZoneOffsetHours}.");

        if (_options.DryRun)
        {
            ConsoleLog.Info("Режим dry-run: сообщения не отправляются в Telegram, а печатаются в лог.");
        }

        if (_options.ForceSend)
        {
            ConsoleLog.Info("Режим force-send: расписание игнорируется, прогноз отправляется сразу.");
        }

        await DeleteWebhookAsync(cancellationToken).ConfigureAwait(false);
        await ProcessUpdatesAsync(cancellationToken).ConfigureAwait(false);
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendScheduledForecastsAsync(cancellationToken).ConfigureAwait(false);
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// getUpdates не работает, пока у бота установлен вебхук, поэтому на всякий случай снимаем его
    /// (сообщения при этом не теряются — dropPendingUpdates равен false).
    /// </summary>
    private async Task DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _bot.DeleteWebhook(false, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestException exception)
        {
            ConsoleLog.Warning($"Не удалось снять вебхук: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            ConsoleLog.Warning($"Не удалось снять вебхук (сеть): {exception.Message}");
        }
    }

    private async Task ProcessUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!_state.IsInitialized)
        {
            await SkipPendingUpdatesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var processed = 0;

        for (var page = 0; page < MaxUpdatePages; page++)
        {
            var updates = await GetUpdatesAsync(_state.TelegramUpdateOffset, cancellationToken).ConfigureAwait(false);
            if (updates.Length == 0)
            {
                break;
            }

            foreach (var update in updates)
            {
                // Апдейт считается прочитанным, даже если ответить не удалось:
                // иначе бот будет зацикливаться на нём в каждом запуске.
                _state.TelegramUpdateOffset = update.Id + 1;

                if (update.Message is not { } message)
                {
                    continue;
                }

                try
                {
                    await _messageProcessor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
                catch (Exception exception)
                {
                    ConsoleLog.Error($"Не удалось обработать сообщение из апдейта {update.Id}", exception);
                }
            }

            if (updates.Length < UpdatesPageSize)
            {
                break;
            }
        }

        ConsoleLog.Info(processed == 0 ? "Новых сообщений нет." : $"Обработано сообщений: {processed}.");
    }

    /// <summary>При первом запуске пропускает всю очередь сообщений, накопившуюся до установки бота.</summary>
    private async Task SkipPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        var updates = await GetUpdatesAsync(offset: null, cancellationToken).ConfigureAwait(false);

        _state.TelegramUpdateOffset = updates.Length == 0 ? 0 : updates.Max(update => update.Id) + 1;
        _state.IsInitialized = true;

        ConsoleLog.Info(
            $"Первый запуск: пропускаю {updates.Length} старых апдейтов, следующий offset {_state.TelegramUpdateOffset}.");
    }

    private async Task<Update[]> GetUpdatesAsync(int? offset, CancellationToken cancellationToken)
    {
        try
        {
            return await _bot.GetUpdates(
                    offset: offset,
                    limit: UpdatesPageSize,
                    timeout: 0,
                    allowedUpdates: [UpdateType.Message],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 409)
        {
            throw new InvalidOperationException(
                "Telegram отклонил запрос getUpdates (409 Conflict): у бота установлен вебхук или " +
                "с этим же токеном работает другой процесс. Убедитесь, что вебхук снят, а бот не запущен локально.",
                exception);
        }
    }

    /// <summary>Рассылает прогноз на завтра всем, у кого наступило время рассылки.</summary>
    private async Task SendScheduledForecastsAsync(CancellationToken cancellationToken)
    {
        var subscribers = _state.GetSubscribers().ToList();
        if (subscribers.Count == 0)
        {
            ConsoleLog.Info("Нет пользователей с выбранным городом — рассылать нечего.");
            return;
        }

        var utcNow = _timeProvider.GetUtcNow();
        var sent = 0;
        var skipped = 0;

        foreach (var user in subscribers)
        {
            if (!DailySchedule.IsDue(
                    user,
                    utcNow,
                    _options.DefaultTimeZoneOffsetSeconds,
                    _options.DailySendHour,
                    _options.ForceSend))
            {
                skipped++;
                continue;
            }

            await SendForecastToUserAsync(user, cancellationToken).ConfigureAwait(false);
            sent++;
        }

        ConsoleLog.Info($"Рассылка завершена: отправлено {sent}, пропущено по расписанию {skipped}.");
    }

    private async Task SendForecastToUserAsync(BotUser user, CancellationToken cancellationToken)
    {
        try
        {
            var forecast = await _weather
                .GetTomorrowForecastAsync(user.Latitude!.Value, user.Longitude!.Value, cancellationToken)
                .ConfigureAwait(false);

            user.TimeZoneOffsetSeconds = forecast.TimeZoneOffsetSeconds;

            var status = await _notifier
                .SendHtmlAsync(user.ChatId, WeatherFormatter.ToHtml(forecast.WithCityFrom(user)), cancellationToken)
                .ConfigureAwait(false);

            switch (status)
            {
                case SendStatus.Sent:
                    // Отметку ставим только после успешной отправки, иначе прогноз потеряется.
                    user.LastSentDate = DailySchedule.FormatDate(forecast.LocalDate);
                    user.UpdatedAt = DateTimeOffset.UtcNow;
                    ConsoleLog.Info(
                        $"Прогноз для chatId={user.ChatId} ({forecast.DisplayName}) на " +
                        $"{DailySchedule.FormatDate(forecast.LocalDate)} отправлен.");
                    break;

                case SendStatus.Blocked:
                    user.IsSubscribed = false;
                    ConsoleLog.Warning($"chatId={user.ChatId} заблокировал бота — рассылка выключена.");
                    break;

                default:
                    ConsoleLog.Warning($"Прогноз для chatId={user.ChatId} не отправлен — попробую в следующий запуск.");
                    break;
            }
        }
        catch (WeatherServiceException exception)
        {
            ConsoleLog.Error($"OpenWeatherMap не отдал прогноз для chatId={user.ChatId}", exception);
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"Не удалось отправить прогноз для chatId={user.ChatId}", exception);
        }
    }
}
