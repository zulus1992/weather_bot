using System.Globalization;
using WeatherBot.Models;

namespace WeatherBot.Services;

/// <summary>Определяет, пора ли отправлять пользователю прогноз на завтра.</summary>
public static class DailySchedule
{
    /// <summary>Текущее время в часовом поясе города пользователя.</summary>
    public static DateTimeOffset GetLocalNow(
        DateTimeOffset utcNow,
        int? timeZoneOffsetSeconds,
        int defaultTimeZoneOffsetSeconds) =>
        utcNow.ToOffset(TimeSpan.FromSeconds(timeZoneOffsetSeconds ?? defaultTimeZoneOffsetSeconds));

    /// <summary>Дата прогноза («завтра» по времени города), для которого делается рассылка.</summary>
    public static DateOnly GetTargetDate(
        DateTimeOffset utcNow,
        int? timeZoneOffsetSeconds,
        int defaultTimeZoneOffsetSeconds) =>
        DateOnly.FromDateTime(GetLocalNow(utcNow, timeZoneOffsetSeconds, defaultTimeZoneOffsetSeconds).DateTime)
            .AddDays(1);

    /// <summary>Преобразует дату в строку для хранения в состоянии бота.</summary>
    public static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Рассылка выполняется, если в городе пользователя уже наступил <paramref name="sendHour"/>,
    /// а прогноз на соответствующую дату ещё не отправлялся. В режиме <paramref name="force"/>
    /// проверки пропускаются — так работает ручной запуск workflow «Отправить сейчас».
    /// </summary>
    public static bool IsDue(
        BotUser user,
        DateTimeOffset utcNow,
        int defaultTimeZoneOffsetSeconds,
        int sendHour,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(user);

        var localNow = GetLocalNow(utcNow, user.TimeZoneOffsetSeconds, defaultTimeZoneOffsetSeconds);
        if (!force && localNow.Hour < sendHour)
        {
            return false;
        }

        var targetDate = FormatDate(DateOnly.FromDateTime(localNow.DateTime).AddDays(1));

        return force || !string.Equals(user.LastSentDate, targetDate, StringComparison.Ordinal);
    }
}
