using WeatherBot.Models;
using WeatherBot.Models.OpenWeather;

namespace WeatherBot.Services;

/// <summary>
/// Собирает сводку за одни сутки из трёхчасовых записей прогноза OpenWeatherMap.
/// OpenWeatherMap отдаёт прогноз за 5 дней с шагом 3 часа, поэтому данные нужно агрегировать.
/// </summary>
public static class ForecastBuilder
{
    /// <summary>Переводит метку времени UTC в локальную дату города.</summary>
    public static DateOnly GetLocalDate(long timestamp, int timeZoneOffsetSeconds) =>
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeSeconds(timestamp)
                .ToOffset(TimeSpan.FromSeconds(timeZoneOffsetSeconds))
                .DateTime);

    /// <summary>Возвращает сводку на указанную локальную дату или null, если данных на этот день нет.</summary>
    public static DailyForecast? BuildForDate(ForecastResponseDto response, DateOnly localDate)
    {
        ArgumentNullException.ThrowIfNull(response);

        var offset = response.City?.TimeZoneOffsetSeconds ?? 0;
        var entries = response.List
            .Where(entry => GetLocalDate(entry.Timestamp, offset) == localDate)
            .ToList();

        if (entries.Count == 0)
        {
            return null;
        }

        var dominant = PickDominantCondition(entries);

        return new DailyForecast(
            Date: localDate,
            MinTemperature: Round(entries.Min(entry => entry.Main.MinTemperature)),
            MaxTemperature: Round(entries.Max(entry => entry.Main.MaxTemperature)),
            MinFeelsLike: Round(entries.Min(entry => entry.Main.FeelsLike)),
            MaxFeelsLike: Round(entries.Max(entry => entry.Main.FeelsLike)),
            Description: dominant.Description,
            Icon: dominant.Icon,
            MaxWindSpeed: Round(entries.Max(entry => entry.Wind?.Speed ?? 0d)),
            MaxWindGust: entries.Max(entry => entry.Wind?.Gust) is { } gust ? Round(gust) : null,
            AverageHumidity: (int)Math.Round(entries.Average(entry => entry.Main.Humidity)),
            TotalPrecipitationMm: Round(entries.Sum(GetPrecipitation)),
            MaxPrecipitationProbability: (int)Math.Round(entries.Max(entry => entry.ProbabilityOfPrecipitation) * 100d),
            AverageCloudiness: (int)Math.Round(entries.Average(entry => entry.Clouds?.Cloudiness ?? 0)));
    }

    /// <summary>Сумма осадков (дождь и снег) за трёхчасовой интервал, мм.</summary>
    public static double GetPrecipitation(ForecastEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (entry.Rain?.Last3Hours ?? 0d) + (entry.Snow?.Last3Hours ?? 0d);
    }

    /// <summary>
    /// Определяет преобладающее состояние погоды за день: берётся самое частое, а при равенстве —
    /// наиболее «серьёзное» (у OpenWeatherMap больший идентификатор внутри группы означает более сильное явление).
    /// </summary>
    private static (string Description, string Icon) PickDominantCondition(List<ForecastEntryDto> entries)
    {
        var conditions = entries
            .Where(entry => entry.Weather.Count > 0)
            .Select(entry => entry.Weather[0])
            .ToList();

        if (conditions.Count == 0)
        {
            return ("нет данных", "01d");
        }

        var dominantId = conditions
            .GroupBy(condition => condition.Id)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Id)
            .First()
            .Id;

        var sameId = conditions.Where(condition => condition.Id == dominantId).ToList();

        // Для дневной сводки предпочитаем «дневную» иконку.
        var sample = sameId.FirstOrDefault(condition => condition.Icon.EndsWith('d')) ?? sameId[0];

        var description = string.IsNullOrWhiteSpace(sample.Description) ? sample.Main : sample.Description;
        return (description, sample.Icon);
    }

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
