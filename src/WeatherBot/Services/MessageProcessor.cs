using System.Globalization;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeatherBot.Configuration;
using WeatherBot.Models;
using WeatherBot.Security;

namespace WeatherBot.Services;

/// <summary>Обрабатывает входящие сообщения пользователя: авторизация, выбор города, команды.</summary>
public sealed class MessageProcessor
{
    private readonly IWeatherService _weather;
    private readonly IMessageSender _notifier;
    private readonly BotOptions _options;
    private readonly BotState _state;
    private readonly TimeProvider _timeProvider;

    public MessageProcessor(
        IWeatherService weather,
        IMessageSender notifier,
        BotOptions options,
        BotState state,
        TimeProvider? timeProvider = null)
    {
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Обрабатывает одно сообщение из Telegram.</summary>
    public async Task ProcessAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Chat.Type != ChatType.Private)
        {
            ConsoleLog.Info($"Пропускаю сообщение из чата типа {message.Chat.Type} — бот работает только в личных чатах.");
            return;
        }

        var user = _state.GetOrCreate(message.Chat.Id, message.From?.Username, message.From?.FirstName);

        if (message.Text is not { Length: > 0 } rawText)
        {
            if (user.IsAuthorized)
            {
                await SendAsync(user, BotMessages.OnlyTextSupported, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var text = rawText.Trim();
        var (command, argument) = ParseCommand(text);

        if (command is null)
        {
            await HandlePlainTextAsync(user, text, cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandleCommandAsync(user, command, argument, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Преобразует «/city@MyBot Москва» в команду /city и аргумент «Москва».</summary>
    public static (string? Command, string Argument) ParseCommand(string text)
    {
        if (!text.StartsWith('/'))
        {
            return (null, text);
        }

        var spaceIndex = text.IndexOf(' ', StringComparison.Ordinal);
        var command = spaceIndex < 0 ? text : text[..spaceIndex];
        var argument = spaceIndex < 0 ? string.Empty : text[(spaceIndex + 1)..].Trim();

        var atIndex = command.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0)
        {
            command = command[..atIndex];
        }

        return (command.ToLowerInvariant(), argument);
    }

    private async Task HandleCommandAsync(
        BotUser user,
        string command,
        string argument,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "/start":
                if (!user.IsAuthorized)
                {
                    await SendAsync(user, BotMessages.AskPassword, cancellationToken).ConfigureAwait(false);
                    return;
                }

                user.IsSubscribed = true;
                await SendAsync(
                        user,
                        user.City is null ? BotMessages.AskCity : BotMessages.Subscribed,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "/help":
                await SendAsync(
                        user,
                        user.IsAuthorized ? BotMessages.Help : BotMessages.AskPassword,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "/login":
            case "/password":
                await HandlePasswordAsync(user, argument, cancellationToken).ConfigureAwait(false);
                return;

            case "/city":
                if (!await EnsureAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                if (argument.Length == 0)
                {
                    await SendAsync(user, BotMessages.AskCity, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await ApplyCityAsync(user, argument, cancellationToken).ConfigureAwait(false);
                return;

            case "/tomorrow":
            case "/weather":
            case "/forecast":
                await SendTomorrowAsync(user, cancellationToken).ConfigureAwait(false);
                return;

            case "/status":
                await SendStatusAsync(user, cancellationToken).ConfigureAwait(false);
                return;

            case "/stop":
            case "/unsubscribe":
                if (!await EnsureAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                user.IsSubscribed = false;
                await SendAsync(user, BotMessages.Unsubscribed, cancellationToken).ConfigureAwait(false);
                return;

            case "/subscribe":
                if (!await EnsureAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                user.IsSubscribed = true;
                await SendAsync(
                        user,
                        user.City is null ? BotMessages.AskCity : BotMessages.Subscribed,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "/logout":
                user.IsAuthorized = false;
                await SendAsync(user, BotMessages.LoggedOut, cancellationToken).ConfigureAwait(false);
                return;

            case "/chatid":
                await SendAsync(user, BotMessages.ChatId(user.ChatId), cancellationToken).ConfigureAwait(false);
                return;

            default:
                await SendAsync(user, BotMessages.UnknownCommand(command), cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandlePlainTextAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        if (!user.IsAuthorized)
        {
            await HandlePasswordAsync(user, text, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Авторизованный пользователь присылает название города.
        await ApplyCityAsync(user, text, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandlePasswordAsync(BotUser user, string password, CancellationToken cancellationToken)
    {
        if (password.Length == 0)
        {
            await SendAsync(user, BotMessages.AskPassword, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!PasswordChecker.IsMatch(password, _options.Password))
        {
            ConsoleLog.Warning($"Пользователь chatId={user.ChatId} ввёл неверный пароль.");
            await SendAsync(user, BotMessages.WrongPassword, cancellationToken).ConfigureAwait(false);
            return;
        }

        user.IsAuthorized = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        ConsoleLog.Info($"Пользователь chatId={user.ChatId} авторизован.");

        await SendAsync(
                user,
                user.City is null ? BotMessages.AccessGranted : BotMessages.Subscribed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> EnsureAuthorizedAsync(BotUser user, CancellationToken cancellationToken)
    {
        if (user.IsAuthorized)
        {
            return true;
        }

        await SendAsync(user, BotMessages.AskPassword, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private Task SendAsync(BotUser user, string html, CancellationToken cancellationToken) =>
        _notifier.SendHtmlAsync(user.ChatId, html, cancellationToken);

    /// <summary>Ищет город у OpenWeatherMap и сохраняет его в настройках пользователя.</summary>
    private async Task ApplyCityAsync(BotUser user, string query, CancellationToken cancellationToken)
    {
        GeoCity? city;

        try
        {
            city = await _weather.FindCityAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (WeatherServiceException exception)
        {
            ConsoleLog.Error($"Ошибка геокодирования «{query}»", exception);
            await SendAsync(user, BotMessages.ForecastError(exception.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (city is null)
        {
            await SendAsync(user, BotMessages.CityNotFound(query), cancellationToken).ConfigureAwait(false);
            return;
        }

        user.City = city.Name;
        user.CountryCode = city.CountryCode;
        user.Latitude = city.Latitude;
        user.Longitude = city.Longitude;
        user.TimeZoneOffsetSeconds = null;

        // Город изменился — прогноз на ту же дату нужно отправить заново.
        user.LastSentDate = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        ConsoleLog.Info(
            $"Пользователь chatId={user.ChatId} выбрал город {city.DisplayName} ({city.Latitude}, {city.Longitude}).");

        try
        {
            var forecast = await _weather
                .GetTomorrowForecastAsync(city.Latitude, city.Longitude, cancellationToken)
                .ConfigureAwait(false);

            user.TimeZoneOffsetSeconds = forecast.TimeZoneOffsetSeconds;
            var localNow = DailySchedule.GetLocalNow(
                _timeProvider.GetUtcNow(),
                forecast.TimeZoneOffsetSeconds,
                _options.DefaultTimeZoneOffsetSeconds);

            await SendAsync(
                    user,
                    BotMessages.CitySaved(
                        city.DisplayName,
                        localNow.ToString("HH:mm", CultureInfo.InvariantCulture),
                        FormatOffset(localNow.Offset)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WeatherServiceException exception)
        {
            ConsoleLog.Error($"Не удалось получить прогноз для {city.DisplayName}", exception);
            await SendAsync(user, BotMessages.CitySavedWithoutForecast(city.DisplayName, exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Отправляет прогноз на завтра по команде пользователя.</summary>
    private async Task SendTomorrowAsync(BotUser user, CancellationToken cancellationToken)
    {
        if (!await EnsureAuthorizedAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (user.City is null || user.Latitude is null || user.Longitude is null)
        {
            await SendAsync(user, BotMessages.AskCity, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var forecast = await _weather
                .GetTomorrowForecastAsync(user.Latitude.Value, user.Longitude.Value, cancellationToken)
                .ConfigureAwait(false);

            user.TimeZoneOffsetSeconds = forecast.TimeZoneOffsetSeconds;

            await SendAsync(user, WeatherFormatter.ToHtml(forecast.WithCityFrom(user)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WeatherServiceException exception)
        {
            ConsoleLog.Error($"Не удалось получить прогноз для chatId={user.ChatId}", exception);
            await SendAsync(user, BotMessages.ForecastError(exception.Message), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendStatusAsync(BotUser user, CancellationToken cancellationToken)
    {
        string? localTime = null;
        if (user.TimeZoneOffsetSeconds is { } offset)
        {
            var localNow = DailySchedule.GetLocalNow(_timeProvider.GetUtcNow(), offset, _options.DefaultTimeZoneOffsetSeconds);
            localTime = $"{localNow:HH:mm} (UTC{FormatOffset(localNow.Offset)})";
        }

        DateOnly? lastSentDate = null;
        if (DateOnly.TryParseExact(user.LastSentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            lastSentDate = parsed;
        }

        var cityDisplayName = user.City is null
            ? null
            : string.IsNullOrWhiteSpace(user.CountryCode) ? user.City : $"{user.City}, {user.CountryCode}";

        await SendAsync(
                user,
                BotMessages.Status(
                    user.IsAuthorized,
                    cityDisplayName,
                    localTime,
                    user.IsSubscribed,
                    _options.DailySendHour,
                    lastSentDate),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>«+03:00» или «-05:00».</summary>
    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
