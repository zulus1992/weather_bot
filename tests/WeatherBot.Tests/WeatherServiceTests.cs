using System.Net;
using System.Text.Json;
using WeatherBot.Models.OpenWeather;
using WeatherBot.Services;

namespace WeatherBot.Tests;

/// <summary>Проверяет HTTP-слой OpenWeatherMap на подменённом обработчике запросов.</summary>
public sealed class WeatherServiceTests
{
    private const string ApiKey = "test-key";

    private const string GeocodingJson = """
    [
      {
        "name": "Moscow",
        "local_names": { "ru": "Москва", "en": "Moscow" },
        "lat": 55.7522,
        "lon": 37.6156,
        "country": "RU"
      },
      {
        "name": "Moscow",
        "lat": 46.7324,
        "lon": -117.0002,
        "country": "US",
        "state": "Idaho"
      }
    ]
    """;

    private static WeatherService CreateService(
        StubHttpMessageHandler handler,
        DateTimeOffset? utcNow = null,
        int defaultTimeZoneOffsetSeconds = Samples.MoscowOffsetSeconds) =>
        new(
            new HttpClient(handler),
            ApiKey,
            defaultTimeZoneOffsetSeconds,
            new FixedTimeProvider(utcNow ?? new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)));

    private static string ForecastJson(ForecastResponseDto response) =>
        JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertApiRequest(
        string request,
        string expectedQuery,
        params string[] expectedParameters)
    {
        Assert.Contains("/geo/1.0/direct", request, StringComparison.Ordinal);
        Assert.Contains($"q={Uri.EscapeDataString(expectedQuery)}", request, StringComparison.Ordinal);
        Assert.Contains($"appid={ApiKey}", request, StringComparison.Ordinal);

        foreach (var parameter in expectedParameters)
        {
            Assert.Contains(parameter, request, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FindCity_UsesLocalizedName_AndEncodesQuery()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(GeocodingJson);
        var service = CreateService(handler);

        var city = await service.FindCityAsync("Москва, RU", CancellationToken.None);

        Assert.NotNull(city);
        Assert.Equal("Moscow", city.Name);
        Assert.Equal("RU", city.CountryCode);
        Assert.Null(city.State);
        Assert.Equal(55.7522, city.Latitude);
        Assert.Equal(37.6156, city.Longitude);
        Assert.Equal("Moscow, RU", city.DisplayName);

        AssertApiRequest(Assert.Single(handler.Requests), "Москва, RU", "limit=5");
    }

    [Fact]
    public async Task FindCity_NormalizesSpacesAndCommas()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(GeocodingJson);
        var service = CreateService(handler);

        await service.FindCityAsync("  Москва,RU  ", CancellationToken.None);

        // Ведущие и замыкающие пробелы убираются, после запятой добавляется пробел.
        AssertApiRequest(Assert.Single(handler.Requests), "Москва, RU");
    }

    [Fact]
    public async Task FindCity_ReturnsFirstResult_WhenNoExactNameMatch()
    {
        const string json = """
        [{ "name": "Kazan", "lat": 55.79, "lon": 49.11, "country": "RU", "state": "Tatarstan" }]
        """;

        var handler = new StubHttpMessageHandler().EnqueueJson(json);
        var service = CreateService(handler);

        var city = await service.FindCityAsync("Казань", CancellationToken.None);

        Assert.NotNull(city);
        Assert.Equal("Kazan", city.Name);
        Assert.Equal("Tatarstan", city.State);
    }

    [Fact]
    public async Task FindCity_ReturnsNull_ForEmptyResultList()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("[]");
        var service = CreateService(handler);

        Assert.Null(await service.FindCityAsync("Атлантида", CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindCity_DoesNotCallApi_ForBlankQuery(string query)
    {
        var handler = new StubHttpMessageHandler();
        var service = CreateService(handler);

        Assert.Null(await service.FindCityAsync(query, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetForecast_AggregatesOnlyRequestedLocalDate()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 25, 21), minTemperature: 10, maxTemperature: 14, feelsLike: 11),
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 6), minTemperature: 9, maxTemperature: 15, feelsLike: 10),
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 12), minTemperature: 12, maxTemperature: 20, feelsLike: 14),
            Samples.Entry(Samples.UnixUtc(2026, 9, 27, 6), minTemperature: -5, maxTemperature: 2, feelsLike: -7));

        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(handler);

        var forecast = await service.GetForecastAsync(
            55.7522,
            37.6156,
            new DateOnly(2026, 9, 26),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 26), forecast.Forecast.Date);
        Assert.Equal(9, forecast.Forecast.MinTemperature);
        Assert.Equal(20, forecast.Forecast.MaxTemperature);
        Assert.Equal("Moscow", forecast.City);
        Assert.Equal("RU", forecast.CountryCode);
        Assert.Equal(Samples.MoscowOffsetSeconds, forecast.TimeZoneOffsetSeconds);
        Assert.Equal(55.7522, forecast.Latitude);
        Assert.Equal(37.6156, forecast.Longitude);
        Assert.Equal("Moscow, RU", forecast.DisplayName);
    }

    [Fact]
    public async Task GetForecast_RequestsMetricUnitsInRussian()
    {
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 9)));

        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(handler);

        await service.GetForecastAsync(55.7522, 37.6156, new DateOnly(2026, 9, 26), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/data/2.5/forecast", request, StringComparison.Ordinal);
        Assert.Contains("lat=55.7522", request, StringComparison.Ordinal);
        Assert.Contains("lon=37.6156", request, StringComparison.Ordinal);
        Assert.Contains("units=metric", request, StringComparison.Ordinal);
        Assert.Contains("lang=ru", request, StringComparison.Ordinal);
        Assert.Contains($"appid={ApiKey}", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTomorrowForecast_UsesCityTimeZoneOffset()
    {
        // В Москве уже 26 сентября, поэтому «завтра» — 27 сентября.
        var response = Samples.Response(
            Samples.MoscowOffsetSeconds,
            Samples.Entry(Samples.UnixUtc(2026, 9, 26, 12), minTemperature: 20, maxTemperature: 30),
            Samples.Entry(Samples.UnixUtc(2026, 9, 27, 9), minTemperature: -5, maxTemperature: 2));

        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(handler, new DateTimeOffset(2026, 9, 25, 22, 0, 0, TimeSpan.Zero));

        var forecast = await service.GetTomorrowForecastAsync(55, 37, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 27), forecast.Forecast.Date);
        Assert.Equal(-5, forecast.Forecast.MinTemperature);
        Assert.Equal(2, forecast.Forecast.MaxTemperature);
        Assert.Equal(Samples.MoscowOffsetSeconds, forecast.TimeZoneOffsetSeconds);
    }

    [Fact]
    public async Task GetTomorrowForecast_FallsBackToDefaultOffsetAndGenericCityName()
    {
        var response = new ForecastResponseDto
        {
            Code = "200",
            Count = 1,
            List = [Samples.Entry(Samples.UnixUtc(2026, 9, 25, 9), minTemperature: 1, maxTemperature: 3, feelsLike: 0)],
            City = null,
        };

        var handler = new StubHttpMessageHandler().EnqueueJson(ForecastJson(response));
        var service = CreateService(
            handler,
            new DateTimeOffset(2026, 9, 25, 1, 0, 0, TimeSpan.Zero),
            defaultTimeZoneOffsetSeconds: -3 * 3600);

        var forecast = await service.GetTomorrowForecastAsync(1.5, 2.5, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 25), forecast.Forecast.Date);
        Assert.Equal("Ваш город", forecast.City);
        Assert.Null(forecast.CountryCode);
        Assert.Equal(-3 * 3600, forecast.TimeZoneOffsetSeconds);
        Assert.Equal(1.5, forecast.Latitude);
        Assert.Equal(2.5, forecast.Longitude);
        Assert.Equal("Ваш город", forecast.DisplayName);
    }

    [Fact]
    public async Task GetForecast_Throws_WhenResponseHasNoEntries()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueJson(ForecastJson(Samples.Response(Samples.MoscowOffsetSeconds)));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55, 37, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("пустой прогноз", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_Throws_WhenRequestedDateIsMissing()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson(
            ForecastJson(Samples.Response(
                Samples.MoscowOffsetSeconds,
                Samples.Entry(Samples.UnixUtc(2026, 9, 26, 9)))));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55, 37, new DateOnly(2026, 9, 30), CancellationToken.None));

        Assert.Contains("нет данных на 30.09.2026", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "отклонил API-ключ")]
    [InlineData(HttpStatusCode.NotFound, "не нашёл город")]
    [InlineData(HttpStatusCode.TooManyRequests, "лимит запросов")]
    [InlineData(HttpStatusCode.InternalServerError, "вернул ошибку 500")]
    public async Task FindCity_MapsHttpStatusToReadableMessage(
        HttpStatusCode statusCode,
        string expectedFragment)
    {
        const string body = """{"cod":401,"message":"Invalid API key"}""";
        var handler = new StubHttpMessageHandler().EnqueueJson(body, statusCode);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.FindCityAsync("Москва", CancellationToken.None));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_MapsUnauthorizedStatus()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueJson("""{"cod":401,"message":"Invalid API key"}""", HttpStatusCode.Unauthorized);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55, 37, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("отклонил API-ключ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_MapsServerErrorWithoutBodyDetails()
    {
        var handler = new StubHttpMessageHandler().EnqueueJson("   ", HttpStatusCode.BadGateway);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<WeatherServiceException>(
            () => service.GetForecastAsync(55, 37, new DateOnly(2026, 9, 26), CancellationToken.None));

        Assert.Contains("вернул ошибку 502", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ответ сервиса", exception.Message, StringComparison.Ordinal);
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
}
