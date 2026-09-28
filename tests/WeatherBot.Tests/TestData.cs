using System.Globalization;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeatherBot.Models;
using WeatherBot.Models.WeatherApi;
using WeatherBot.Services;

namespace WeatherBot.Tests;

/// <summary>Создание тестовых сообщений Telegram.</summary>
internal static class MessageFactory
{
    internal static Message Create(
        long chatId,
        string? text,
        ChatType chatType = ChatType.Private,
        string? userName = "tester") => new()
        {
            Chat = new Chat { Id = chatId, Type = chatType },
            From = new User
            {
                Id = chatId,
                IsBot = false,
                FirstName = userName ?? "tester",
                Username = userName,
            },
            Text = text,
        };
}

/// <summary>Примеры данных WeatherAPI.com для тестов.</summary>
internal static class Samples
{
    /// <summary>Смещение часового пояса Москвы (+03:00).</summary>
    internal const int MoscowOffsetSeconds = 3 * 3600;

    internal const int MoscowLatitude = 55;

    internal const int MoscowLongitude = 37;

    /// <summary>Unix-время для указанного момента UTC.</summary>
    internal static long UnixUtc(int year, int month, int day, int hour) =>
        new DateTimeOffset(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

    /// <summary>
    /// Ответ forecast.json: блок <c>location</c> как у Москвы (пояс «Europe/Moscow») и перечисленные сутки.
    /// </summary>
    internal static ForecastResponseDto Response(params ForecastDayDto[] days) => new()
    {
        Location = new ForecastLocationDto
        {
            Name = "Moscow",
            Region = "Moscow City",
            Country = "Russia",
            Latitude = 55.7522,
            Longitude = 37.6156,
            TimeZoneId = "Europe/Moscow",
            LocalTime = "2026-09-25 15:00",
            LocalTimeEpoch = UnixUtc(2026, 9, 25, 12),
        },
        Current = new CurrentWeatherDto
        {
            LastUpdated = "2026-09-25 15:00",
            LastUpdatedEpoch = UnixUtc(2026, 9, 25, 12),
        },
        Forecast = new ForecastDto { Days = [.. days] },
    };

    /// <summary>Сутки прогноза: сводка дня плюс часовые записи.</summary>
    internal static ForecastDayDto Day(
        string date,
        double minTemperature = 8d,
        double maxTemperature = 18d,
        double maxWindSpeedKph = 0d,
        double totalPrecipitationMm = 0d,
        int averageHumidity = 60,
        int dailyChanceOfRain = 0,
        int dailyChanceOfSnow = 0,
        int conditionCode = 1000,
        string condition = "Sunny",
        params HourDto[] hours) => new()
        {
            Date = date,
            Day = new DaySummaryDto
            {
                MinTemperature = minTemperature,
                MaxTemperature = maxTemperature,
                AverageTemperature = (minTemperature + maxTemperature) / 2d,
                MaxWindSpeed = maxWindSpeedKph,
                TotalPrecipitationMm = totalPrecipitationMm,
                AverageHumidity = averageHumidity,
                DailyChanceOfRain = dailyChanceOfRain,
                DailyChanceOfSnow = dailyChanceOfSnow,
                Condition = conditionCode == 0
                    ? null
                    : new ConditionDto { Code = conditionCode, Text = condition },
            },
            Hours = [.. hours],
        };

    /// <summary>Часовая запись ответа; по умолчанию метка времени совпадает со строкой времени.</summary>
    internal static HourDto HourEntry(
        string time,
        double temperature = 10d,
        double feelsLike = 9d,
        double windKph = 18d,
        double gustKph = 0d,
        double precipitationMm = 0d,
        int chanceOfRain = 0,
        int chanceOfSnow = 0,
        int cloudiness = 50,
        int conditionCode = 1000,
        string condition = "Sunny",
        int isDay = 1,
        long? timeUtcEpoch = null) => new()
        {
            Time = time,
            TimeEpoch = timeUtcEpoch ?? new DateTimeOffset(
                DateTime.ParseExact(time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                TimeSpan.Zero).ToUnixTimeSeconds(),
            Temperature = temperature,
            FeelsLike = feelsLike,
            WindSpeed = windKph,
            WindGust = gustKph,
            PrecipitationMm = precipitationMm,
            ChanceOfRain = chanceOfRain,
            ChanceOfSnow = chanceOfSnow,
            Cloudiness = cloudiness,
            Condition = new ConditionDto { Code = conditionCode, Text = condition },
            IsDay = isDay,
        };

    /// <summary>Готовая часовая запись прогноза (доменная модель) для проверки форматирования.</summary>
    internal static HourlyForecast Hourly(
        int hour,
        double temperature = 10d,
        double feelsLike = 9d,
        int conditionCode = 1000,
        string description = "ясно",
        double windSpeed = 3d,
        double? windGust = null,
        int precipitationProbability = 0,
        double precipitationMm = 0d,
        bool isDay = true) => new(
        Hour: hour,
        Temperature: temperature,
        FeelsLike: feelsLike,
        ConditionCode: conditionCode,
        Description: description,
        WindSpeed: windSpeed,
        WindGust: windGust,
        PrecipitationProbability: precipitationProbability,
        PrecipitationMm: precipitationMm,
        IsDay: isDay);

    internal static GeoCity Moscow() => new("Москва", "RU", null, 55.7522, 37.6156);

    internal static DailyForecast Daily(
        DateOnly date,
        IReadOnlyList<HourlyForecast>? hours = null) => new(
        Date: date,
        MinTemperature: 6.4,
        MaxTemperature: 14.6,
        MinFeelsLike: 4.2,
        MaxFeelsLike: 13.1,
        Description: "переменная облачность",
        ConditionCode: 1003,
        MaxWindSpeed: 7.5,
        MaxWindGust: 12.4,
        AverageHumidity: 63,
        TotalPrecipitationMm: 1.2,
        MaxPrecipitationProbability: 40,
        AverageCloudiness: 55,
        Hours: hours ?? []);

    internal static CityForecast TomorrowForecast(
        int timeZoneOffsetSeconds = MoscowOffsetSeconds,
        DateOnly? date = null) => new(
        City: "Москва",
        CountryCode: "RU",
        Latitude: 55.7522,
        Longitude: 37.6156,
        TimeZoneOffsetSeconds: timeZoneOffsetSeconds,
        Forecast: Daily(date ?? new DateOnly(2026, 9, 26)));
}
