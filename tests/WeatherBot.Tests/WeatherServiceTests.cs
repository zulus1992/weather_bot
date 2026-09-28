using System.Net;
using System.Text.Json;
using WeatherBot.Models.WeatherApi;
using WeatherBot.Services;

namespace WeatherBot.Tests;

/// <summary>Проверяет HTTP-слой WeatherAPI.com на подменённом обработчике запросов.</summary>
public sealed class WeatherServiceTests
{
    private const string ApiKey = "test-key";

    private const string SearchJson = """
    [
      {
        "id": 295212,
        "name": "Moscow",
        "region": "Moscow City",
        "country": "Russia",
        "lat": 55.7522,
        "lon": 37.6156,
        "url": "moscow"
      },
      {
        "id": 4887398,
        "name": "Moscow",
        "region": "Idaho",
        "country": "United States of America",
        "lat": 46.7324,
        "lon": -117.0002,
        "url": "moscow-idaho-united-states-of-america"
      }
    ]
    """;

    /// <summary>«Сейчас» для тестов, зависящих от времени.</summary>
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static WeatherService CreateService(
        StubHttpMessageHandler handler,
        int defaultTimeZoneOffsetSeconds = Samples.MoscowOffsetSeconds) =>
        new(
            new HttpClient(handler),
            ApiKey,
            defaultTimeZoneOffsetSeconds,
            new FixedTimeProvider(UtcNow));

    private static string ForecastJson(ForecastResponseDto response) =>
        JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertSearchRequest(string request, string expectedQuery)
    {
        Assert.Contains("/v1/search.json", request, StringComparison.Ordinal);
        Assert.Contains($"q={Uri.EscapeDataString(expectedQuery)}", request, StringComparison.Ordinal);
        Assert.Contains($"key={ApiKey}", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindCity_UsesSearchEndpoint_AndEncodesQuery()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(SearchJson);
        var service = CreateService(handler);

        var city = await service.FindCityAsync("Москва, RU", CancellationToken.None);

        Assert.NotNull(city);
        Assert.Equal("Moscow", city.Name);
        Assert.Equal("Russia", city.CountryCode);
        Assert.Equal("Moscow City", city.State);
        Assert.Equal(55.7522, city.Latitude);
        Assert.Equal(37.6156, city.Longitude);
        Assert.Equal("Moscow, Russia", city.DisplayName);

        // Поиск сервиса не понимает уточнение страны — в запрос уходит только название города.
        AssertSearchRequest(Assert.Single(handler.Requests), "Москва");
    }

    [Fact]
    public async Task FindCity_NormalizesSpacesAndCommas()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(SearchJson);
        var service = CreateService(handler);

        await service.FindCityAsync("  Москва,RU  ", CancellationToken.None);

        AssertSearchRequest(Assert.Single(handler.Requests), "Москва");
    }

    [Fact]
    public async Task FindCity_ReturnsFirstResult_WhenNoExactNameMatch()
    {
        const string json = """
        [{ "id": 1526384, "name": "Kazan", "region": "Tatarstan", "country": "Russia", "lat": 55.79, "lon": 49.11 }]
        """;

        var handler = new StubHttpMessageHandler().EnqueueJson(json);
        var service = CreateService(handler);

        var city = await service.FindCityAsync("Казань", CancellationToken.None);

        Assert.NotNull(city);
        Assert.Equal("Kazan", city.Name);
        Assert.Equal("Tatarstan", city.State);
        Assert.Equal("Kazan, Russia", city.DisplayName);
    }

    [Fact]
    public async Task FindCity_PrefersExactNameMatch()
    {
        const string json = """
        [
          { "id": 1, "name": "Moskovsky", "region": "Moscow", "country": "Russia", "lat": 55.6, "lon": 37.2 },
          { "id": 2, "name": "Moscow", "region": "Moscow City", "country": "Russia", "lat": 55.7522, "lon": 37.6156 }
        ]
        """;

        var handler = new StubHttpMessageHandler().EnqueueJson(json);
        var service = CreateService(handler);

        var city = await service.FindCityAsync("Moscow", CancellationToken.None);

        Assert.NotNull(city);
        Assert.Equal(55.7522, city.Latitude);
        Assert.Equal("Moscow City", city.State);
    }

    [Fact]
    public async Task FindCity_ReturnsNull_ForEmptyQuery()
    {
        var handler = new StubHttpMessageHandler();
        var service = CreateService(handler);

        Assert.Null(await service.FindCityAsync("   ", CancellationToken.None));
        Assert.Null(await service.FindCityAsync(", RU", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindCity_ReturnsNull_WhenNothingFound()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("[]");
        var service = CreateService(handler);

        Assert.Null(await service.FindCityAsync("Урюпинск-test", CancellationToken.None));
    }

    [Fact]
    public async Task FindCity_Throws_ForMalformedJson()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("<html>upstream error</html>");
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.FindCityAsync("Москва", CancellationToken.None));

        Assert.Contains("Не удалось разобрать ответ", exception.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    /// <summary>Ответ с тремя сутками: 25, 26 и 27 сентября (в городе сейчас 25-е).</summary>
    private static ForecastResponseDto ThreeDays() => Samples.Response(
        Samples.Day(
            "2026-09-25",
            minTemperature: 10,
            maxTemperature: 14,
            hours: [Samples.HourEntry("2026-09-25 12:00")]),
        Samples.Day(
            "2026-09-26",
            minTemperature: 8,
            maxTemperature: 15,
            averageHumidity: 70,
            dailyChanceOfRain: 60,
            hours:
            [
                Samples.HourEntry(
                    "2026-09-26 09:00",
                    temperature: 12,
                    precipitationMm: 0.4d,
                    chanceOfRain: 60),
            ]),
        Samples.Day("2026-09-27", minTemperature: -5, maxTemperature: 2));

    [Fact]
    public async Task GetForecast_RequestsCoordinatesAndForecastSettings()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(ThreeDays()));
        var service = CreateService(handler);

        await service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 26), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/v1/forecast.json", request, StringComparison.Ordinal);
        Assert.Contains($"key={ApiKey}", request, StringComparison.Ordinal);
        Assert.Contains("q=55.7522,37.6156", request, StringComparison.Ordinal);
        Assert.Contains("days=3", request, StringComparison.Ordinal);
        Assert.Contains("lang=ru", request, StringComparison.Ordinal);
        Assert.Contains("aqi=no", request, StringComparison.Ordinal);
        Assert.Contains("alerts=no", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_ReturnsRequestedDateWithCityAndHours()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(ThreeDays()));
        var service = CreateService(handler);

        var forecast = await service.GetForecastAsync(
            55.7522,
            37.6156,
            new DateOnly(2026, 9, 26),
            CancellationToken.None);

        Assert.Equal("Moscow", forecast.City);
        Assert.Equal("Russia", forecast.CountryCode);
        Assert.Equal("Moscow, Russia", forecast.DisplayName);
        Assert.Equal(new DateOnly(2026, 9, 26), forecast.LocalDate);
        Assert.Equal(Samples.MoscowOffsetSeconds, forecast.TimeZoneOffsetSeconds);
        Assert.Equal(8, forecast.Forecast.MinTemperature);
        Assert.Equal(15, forecast.Forecast.MaxTemperature);
        Assert.Equal(70, forecast.Forecast.AverageHumidity);
        Assert.Equal(0.4, forecast.Forecast.TotalPrecipitationMm);
        Assert.Equal(60, forecast.Forecast.MaxPrecipitationProbability);

        var hour = Assert.Single(forecast.Forecast.Hours);
        Assert.Equal(9, hour.Hour);
        Assert.True(hour.HasPrecipitation);
    }

    [Fact]
    public async Task GetForecast_UsesDefaultOffset_WhenNoTimeZoneData()
    {
        // Ни tz_id, ни часовых записей нет — смещение берётся из настроек.
        var response = Samples.Response(Samples.Day("2026-09-26", minTemperature: 8, maxTemperature: 15));
        response.Location!.TimeZoneId = null;
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(handler);

        var forecast = await service.GetForecastAsync(
            55.7522,
            37.6156,
            new DateOnly(2026, 9, 26),
            CancellationToken.None);

        Assert.Equal(Samples.MoscowOffsetSeconds, forecast.TimeZoneOffsetSeconds);
    }

    [Fact]
    public async Task GetForecast_Throws_WhenRequestedDateIsMissing()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(ThreeDays()));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 30), CancellationToken.None));

        Assert.Contains("нет данных на 30.09.2026", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_Throws_ForNullBody()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("null");
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("пустой прогноз", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTomorrowForecast_UsesLocalDateOfCity()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(ThreeDays()));
        var service = CreateService(handler);

        var forecast = await service.GetTomorrowForecastAsync(55.7522, 37.6156, CancellationToken.None);

        // В городе 25 сентября 15:00, значит «завтра» — 26 сентября.
        Assert.Equal(new DateOnly(2026, 9, 26), forecast.LocalDate);
        Assert.Equal(Samples.MoscowOffsetSeconds, forecast.TimeZoneOffsetSeconds);
    }

    [Fact]
    public async Task GetTomorrowForecast_UsesFallbackDate_WhenLocalTimeIsMissing()
    {
        var response = ThreeDays();
        response.Location!.LocalTime = null;
        response.Location.TimeZoneId = null;
        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(handler);

        var forecast = await service.GetTomorrowForecastAsync(55.7522, 37.6156, CancellationToken.None);

        // Резервный путь: 25 сентября 12:00 UTC и пояс по умолчанию (+3) — «завтра» это 26 сентября.
        Assert.Equal(new DateOnly(2026, 9, 26), forecast.LocalDate);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "отклонил API-ключ")]
    [InlineData(HttpStatusCode.Forbidden, "заблокирован")]
    [InlineData(HttpStatusCode.BadRequest, "не принял запрос")]
    [InlineData(HttpStatusCode.NotFound, "не нашёл город")]
    [InlineData(HttpStatusCode.TooManyRequests, "лимит запросов")]
    [InlineData(HttpStatusCode.InternalServerError, "вернул ошибку 500")]
    public async Task FindCity_MapsHttpStatusToReadableMessage(
        HttpStatusCode statusCode,
        string expectedFragment)
    {
        const string body = """{"error":{"code":1006,"message":"No matching location found."}}""";
        var handler = new StubHttpMessageHandler().EnqueueJson(body, statusCode);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.FindCityAsync("Москва", CancellationToken.None));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
        Assert.Contains("No matching location found.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_MapsUnauthorizedStatus()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(
            """{"error":{"code":1002,"message":"API key is invalid."}}""",
            HttpStatusCode.Unauthorized);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("отклонил API-ключ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("API key is invalid.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WEATHERAPI_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_MapsServerErrorWithoutBodyDetails()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("   ", HttpStatusCode.BadGateway);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("вернул ошибку 502", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ответ сервиса", exception.Message, StringComparison.Ordinal);
    }
}
