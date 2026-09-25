using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WeatherBot.Models;
using WeatherBot.Models.OpenWeather;
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

/// <summary>Примеры данных OpenWeatherMap для тестов.</summary>
internal static class Samples
{
    /// <summary>Смещение часового пояса Москвы (+03:00).</summary>
    internal const int MoscowOffsetSeconds = 3 * 3600;

    internal const int MoscowLatitude = 55;

    internal const int MoscowLongitude = 37;

    /// <summary>Unix-время для указанного момента UTC.</summary>
    internal static long UnixUtc(int year, int month, int day, int hour) =>
        new DateTimeOffset(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

    internal static ForecastResponseDto Response(
        int timeZoneOffsetSeconds, params ForecastEntryDto[] entries) => new()
        {
            Code = "200",
            Count = entries.Length,
            List = [.. entries],
            City = new ForecastCityDto
            {
                Id = 524901,
                Name = "Moscow",
                Country = "RU",
                TimeZoneOffsetSeconds = timeZoneOffsetSeconds,
                Coord = new ForecastCoordDto { Latitude = 55.7522, Longitude = 37.6156 },
            },
        };

    internal static ForecastEntryDto Entry(
        long timestamp,
        double minTemperature = 10d,
        double maxTemperature = 15d,
        double feelsLike = 12d,
        int humidity = 60,
        int cloudiness = 50,
        double windSpeed = 5d,
        double? windGust = null,
        double precipitationProbability = 0d,
        double? rain = null,
        double? snow = null,
        int weatherId = 800,
        string main = "Clear",
        string description = "ясно",
        string icon = "01d") => new()
        {
            Timestamp = timestamp,
            Main = new ForecastMainDto
            {
                Temperature = (minTemperature + maxTemperature) / 2d,
                MinTemperature = minTemperature,
                MaxTemperature = maxTemperature,
                FeelsLike = feelsLike,
                Pressure = 1013,
                Humidity = humidity,
            },
            Weather = [new ForecastWeatherDto { Id = weatherId, Main = main, Description = description, Icon = icon }],
            Clouds = new ForecastCloudsDto { Cloudiness = cloudiness },
            Wind = new ForecastWindDto { Speed = windSpeed, Direction = 180, Gust = windGust },
            ProbabilityOfPrecipitation = precipitationProbability,
            Rain = rain is null ? null : new ForecastRainDto { Last3Hours = rain.Value },
            Snow = snow is null ? null : new ForecastSnowDto { Last3Hours = snow.Value },
            Sys = new ForecastSysDto { PartOfDay = icon.EndsWith('n') ? "n" : "d" },
        };

    internal static GeoCity Moscow() => new("Москва", "RU", null, 55.7522, 37.6156);

    internal static DailyForecast Daily(DateOnly date) => new(
        Date: date,
        MinTemperature: 6.4,
        MaxTemperature: 14.6,
        MinFeelsLike: 4.2,
        MaxFeelsLike: 13.1,
        Description: "переменная облачность",
        Icon: "02d",
        MaxWindSpeed: 7.5,
        MaxWindGust: 12.4,
        AverageHumidity: 63,
        TotalPrecipitationMm: 1.2,
        MaxPrecipitationProbability: 40,
        AverageCloudiness: 55);

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
