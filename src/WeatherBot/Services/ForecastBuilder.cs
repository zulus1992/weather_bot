using System.Globalization;
using WeatherBot.Models;
using WeatherBot.Models.WeatherApi;

namespace WeatherBot.Services;

/// <summary>
/// Собирает сводку за одни сутки и почасовой прогноз из ответа WeatherAPI.com:
/// из блока <c>forecast</c> берётся нужная дата, из неё — сводка <c>day</c> и массив <c>hour</c>.
/// </summary>
public static class ForecastBuilder
{
    /// <summary>Переводит метку времени UTC в локальную дату города.</summary>
    public static DateOnly GetLocalDate(long timestamp, int timeZoneOffsetSeconds) =>
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeSeconds(timestamp)
                .ToOffset(TimeSpan.FromSeconds(timeZoneOffsetSeconds))
                .DateTime);

    /// <summary>Разбирает дату «yyyy-MM-dd» из ответа WeatherAPI.com; null, если строки нет или она некорректна.</summary>
    public static DateOnly? ParseDate(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : null;

    /// <summary>Сегодняшняя дата в городе: берётся из поля <c>location.localtime</c>.</summary>
    public static DateOnly? GetLocalToday(ForecastResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return ParseLocalDateTime(response.Location?.LocalTime) is { } local
            ? DateOnly.FromDateTime(local)
            : null;
    }

    /// <summary>
    /// Смещение часового пояса города в секундах. Основной источник — идентификатор пояса <c>tz_id</c>
    /// (IANA, например «Europe/Moscow») и база часовых поясов .NET. Если пояс неизвестен, используется
    /// разница между местным временем часа и его меткой времени, а затем — значение по умолчанию.
    /// </summary>
    public static int ResolveTimeZoneOffsetSeconds(
        ForecastResponseDto response,
        DateOnly localDate,
        int defaultTimeZoneOffsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (FindTimeZone(response.Location?.TimeZoneId) is { } timeZone)
        {
            // Смещение считается на полдень нужной даты: так корректно учитывается летнее время.
            var probe = new DateTime(localDate.Year, localDate.Month, localDate.Day, 12, 0, 0, DateTimeKind.Utc);
            var offset = timeZone.GetUtcOffset(probe);
            offset = timeZone.GetUtcOffset(probe - offset);
            return (int)offset.TotalSeconds;
        }

        var hour = FirstHour(response);
        if (hour is { TimeEpoch: > 0 } && ParseLocalDateTime(hour.Time) is { } local)
        {
            return (int)(local - DateTimeOffset.FromUnixTimeSeconds(hour.TimeEpoch).UtcDateTime).TotalSeconds;
        }

        return defaultTimeZoneOffsetSeconds;
    }

    /// <summary>
    /// Возвращает сводку на указанную локальную дату или null, если сервис не отдал такой день.
    /// </summary>
    /// <param name="response">Ответ forecast.json.</param>
    /// <param name="localDate">Дата в часовом поясе города.</param>
    /// <param name="timeZoneOffsetSeconds">Смещение часового пояса города — нужно для часовых записей.</param>
    public static DailyForecast? BuildForDate(
        ForecastResponseDto response,
        DateOnly localDate,
        int timeZoneOffsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(response);

        var day = response.Forecast?.Days.FirstOrDefault(item => ParseDate(item.Date) == localDate);
        if (day is null)
        {
            return null;
        }

        var summary = day.Day;
        var hours = BuildHours(day, timeZoneOffsetSeconds);

        var minFeelsLike = hours.Count > 0 ? hours.Min(hour => hour.FeelsLike) : summary.MinTemperature;
        var maxFeelsLike = hours.Count > 0 ? hours.Max(hour => hour.FeelsLike) : summary.MaxTemperature;

        // Ветер в ответе сервиса измеряется в км/ч, а в сообщении показывается в м/с.
        var maxWindSpeed = Math.Max(
            hours.Count > 0 ? hours.Max(hour => hour.WindSpeed) : 0d,
            KilometersPerHourToMetersPerSecond(summary.MaxWindSpeed));

        var maxGust = hours
            .Where(hour => hour.WindGust is > 0d)
            .Select(hour => hour.WindGust!.Value)
            .DefaultIfEmpty(0d)
            .Max();

        var precipitation = summary.TotalPrecipitationMm > 0d
            ? summary.TotalPrecipitationMm
            : hours.Sum(hour => hour.PrecipitationMm);

        var condition = summary.Condition;
        var description = condition?.Text;

        return new DailyForecast(
            Date: localDate,
            MinTemperature: Round(summary.MinTemperature),
            MaxTemperature: Round(summary.MaxTemperature),
            MinFeelsLike: Round(minFeelsLike),
            MaxFeelsLike: Round(maxFeelsLike),
            Description: string.IsNullOrWhiteSpace(description) ? "нет данных" : description,
            ConditionCode: condition?.Code ?? UnknownConditionCode,
            MaxWindSpeed: Round(maxWindSpeed),
            MaxWindGust: maxGust > 0d ? Round(maxGust) : null,
            AverageHumidity: summary.AverageHumidity,
            TotalPrecipitationMm: Round(precipitation),
            MaxPrecipitationProbability: Math.Max(summary.DailyChanceOfRain, summary.DailyChanceOfSnow),
            AverageCloudiness: day.Hours.Count > 0
                ? (int)Math.Round(day.Hours.Average(hour => (double)hour.Cloudiness))
                : 0,
            Hours: hours);
    }

    /// <summary>Код явления, когда сервис не сообщил состояние погоды (см. <see cref="WeatherFormatter.GetEmoji"/>).</summary>
    public const int UnknownConditionCode = 0;

    /// <summary>Собирает почасовой прогноз суток, упорядоченный по времени.</summary>
    private static IReadOnlyList<HourlyForecast> BuildHours(ForecastDayDto day, int timeZoneOffsetSeconds)
    {
        var hours = new List<HourlyForecast>(day.Hours.Count);

        foreach (var hour in day.Hours.OrderBy(item => item.TimeEpoch))
        {
            var condition = hour.Condition;
            var description = condition?.Text;

            hours.Add(new HourlyForecast(
                Hour: GetLocalHour(hour, timeZoneOffsetSeconds),
                Temperature: Round(hour.Temperature),
                FeelsLike: Round(hour.FeelsLike),
                ConditionCode: condition?.Code ?? UnknownConditionCode,
                Description: string.IsNullOrWhiteSpace(description) ? "нет данных" : description,
                WindSpeed: Round(KilometersPerHourToMetersPerSecond(hour.WindSpeed)),
                WindGust: hour.WindGust > 0d
                    ? Round(KilometersPerHourToMetersPerSecond(hour.WindGust))
                    : null,
                PrecipitationProbability: Math.Max(hour.ChanceOfRain, hour.ChanceOfSnow),
                PrecipitationMm: Round(hour.PrecipitationMm),
                IsDay: hour.IsDay != 0));
        }

        return hours;
    }

    /// <summary>
    /// Местный час записи: из строки <c>time</c>, а при её отсутствии — из метки времени и смещения пояса.
    /// </summary>
    private static int GetLocalHour(HourDto hour, int timeZoneOffsetSeconds) =>
        ParseLocalDateTime(hour.Time)?.Hour ??
        DateTimeOffset.FromUnixTimeSeconds(hour.TimeEpoch)
            .ToOffset(TimeSpan.FromSeconds(timeZoneOffsetSeconds))
            .Hour;

    /// <summary>Переводит км/ч (единицы WeatherAPI.com) в м/с.</summary>
    public static double KilometersPerHourToMetersPerSecond(double kilometersPerHour) =>
        kilometersPerHour / 3.6d;

    /// <summary>Разбирает «yyyy-MM-dd HH:mm» из ответа сервиса.</summary>
    private static DateTime? ParseLocalDateTime(string? value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    /// <summary>Первая часовая запись ответа — резервный источник смещения часового пояса.</summary>
    private static HourDto? FirstHour(ForecastResponseDto response) =>
        response.Forecast?.Days.SelectMany(day => day.Hours).FirstOrDefault();

    /// <summary>Ищет часовой пояс по идентификатору IANA; null, если .NET такой пояс не знает.</summary>
    private static TimeZoneInfo? FindTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
