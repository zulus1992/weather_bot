using System.Globalization;
using System.Net;
using System.Text.Json;
using WeatherBot.Models;
using WeatherBot.Models.OpenWeather;

namespace WeatherBot.Services;

/// <summary>Сервис доступа к OpenWeatherMap (геокодирование и прогноз).</summary>
public interface IWeatherService
{
    /// <summary>Ищет город по названию (поддерживаются русские названия).</summary>
    Task<GeoCity?> FindCityAsync(string query, CancellationToken cancellationToken);

    /// <summary>Возвращает прогноз на указанную локальную дату города.</summary>
    /// <exception cref="WeatherServiceException">OpenWeatherMap недоступен или в прогнозе нет нужной даты.</exception>
    Task<CityForecast> GetForecastAsync(double latitude, double longitude, DateOnly localDate, CancellationToken cancellationToken);

    /// <summary>Возвращает прогноз на завтра (дата определяется в часовом поясе города).</summary>
    /// <exception cref="WeatherServiceException">OpenWeatherMap недоступен или в прогнозе нет нужной даты.</exception>
    Task<CityForecast> GetTomorrowForecastAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

/// <summary>Ошибка при обращении к OpenWeatherMap.</summary>
public sealed class WeatherServiceException : Exception
{
    public WeatherServiceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Реализация <see cref="IWeatherService"/> поверх HTTP API OpenWeatherMap.</summary>
public sealed class WeatherService : IWeatherService
{
    private const string GeocodingUrl = "https://api.openweathermap.org/geo/1.0/direct";
    private const string ForecastUrl = "https://api.openweathermap.org/data/2.5/forecast";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _defaultTimeZoneOffsetSeconds;
    private readonly TimeProvider _timeProvider;

    public WeatherService(
        HttpClient httpClient,
        string apiKey,
        int defaultTimeZoneOffsetSeconds,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _defaultTimeZoneOffsetSeconds = defaultTimeZoneOffsetSeconds;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<GeoCity?> FindCityAsync(string query, CancellationToken cancellationToken)
    {
        var (cityPart, fullQuery) = NormalizeQuery(query);
        if (fullQuery.Length == 0)
        {
            return null;
        }

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{GeocodingUrl}?q={Uri.EscapeDataString(fullQuery)}&limit=5&appid={Uri.EscapeDataString(_apiKey)}");

        var locations = await GetJsonAsync<List<GeoLocationDto>>(url, cancellationToken).ConfigureAwait(false) ?? [];
        if (locations.Count == 0)
        {
            return null;
        }

        // Предпочитаем точное совпадение названия (в том числе в русской локализации).
        var exact = locations.FirstOrDefault(location => IsExactMatch(location, cityPart));
        var best = exact ?? locations[0];

        return new GeoCity(best.Name, best.Country, best.State, best.Latitude, best.Longitude);
    }

    /// <inheritdoc />
    public async Task<CityForecast> GetForecastAsync(
        double latitude,
        double longitude,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var response = await FetchForecastAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
        var timeZoneOffsetSeconds = GetTimeZoneOffsetSeconds(response);

        return BuildCityForecast(response, latitude, longitude, timeZoneOffsetSeconds, localDate);
    }

    /// <inheritdoc />
    public async Task<CityForecast> GetTomorrowForecastAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var response = await FetchForecastAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
        var timeZoneOffsetSeconds = GetTimeZoneOffsetSeconds(response);
        var tomorrow = GetLocalDate(offsetDays: 1, timeZoneOffsetSeconds);

        return BuildCityForecast(response, latitude, longitude, timeZoneOffsetSeconds, tomorrow);
    }

    private async Task<ForecastResponseDto> FetchForecastAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{ForecastUrl}?lat={latitude}&lon={longitude}&units=metric&lang=ru&appid={Uri.EscapeDataString(_apiKey)}");

        var response = await GetJsonAsync<ForecastResponseDto>(url, cancellationToken).ConfigureAwait(false);
        if (response is null || response.List.Count == 0)
        {
            throw new WeatherServiceException("OpenWeatherMap вернул пустой прогноз. Попробуйте позже.");
        }

        return response;
    }

    private DateOnly GetLocalDate(int offsetDays, int timeZoneOffsetSeconds) =>
        DateOnly.FromDateTime(
            _timeProvider.GetUtcNow()
                .ToOffset(TimeSpan.FromSeconds(timeZoneOffsetSeconds))
                .DateTime)
        .AddDays(offsetDays);

    private int GetTimeZoneOffsetSeconds(ForecastResponseDto response) =>
        response.City?.TimeZoneOffsetSeconds ?? _defaultTimeZoneOffsetSeconds;

    private static CityForecast BuildCityForecast(
        ForecastResponseDto response,
        double latitude,
        double longitude,
        int timeZoneOffsetSeconds,
        DateOnly localDate)
    {
        var forecast = ForecastBuilder.BuildForDate(response, localDate)
            ?? throw new WeatherServiceException(
                $"В прогнозе OpenWeatherMap нет данных на {localDate:dd.MM.yyyy}. Попробуйте позже.");

        return new CityForecast(
            City: string.IsNullOrWhiteSpace(response.City?.Name) ? "Ваш город" : response.City!.Name,
            CountryCode: response.City?.Country,
            Latitude: response.City?.Coord?.Latitude ?? latitude,
            Longitude: response.City?.Coord?.Longitude ?? longitude,
            TimeZoneOffsetSeconds: timeZoneOffsetSeconds,
            Forecast: forecast);
    }

    private static (string CityPart, string FullQuery) NormalizeQuery(string query)
    {
        var cleaned = (query ?? string.Empty).Replace(",", ", ", StringComparison.Ordinal).Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        var separator = cleaned.IndexOf(',', StringComparison.Ordinal);
        var cityPart = separator >= 0 ? cleaned[..separator].Trim() : cleaned;

        return (cityPart, cleaned);
    }

    private static bool IsExactMatch(GeoLocationDto location, string cityPart) =>
        string.Equals(location.Name, cityPart, StringComparison.OrdinalIgnoreCase) ||
        (location.LocalNames is not null &&
         location.LocalNames.Values.Any(name => string.Equals(name, cityPart, StringComparison.OrdinalIgnoreCase)));

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WeatherServiceException(BuildErrorMessage(response.StatusCode, body));
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new WeatherServiceException("Не удалось разобрать ответ OpenWeatherMap.", exception);
        }
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body)
    {
        var details = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Ответ сервиса: {body.Trim()}";

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "OpenWeatherMap отклонил API-ключ. " +
                "Проверьте секрет OPENWEATHER_API_KEY (новый ключ активируется в течение пары часов)." + details,
            HttpStatusCode.NotFound => "OpenWeatherMap не нашёл город по указанному названию." + details,
            HttpStatusCode.TooManyRequests => "Исчерпан лимит запросов к OpenWeatherMap. Попробуйте позже." + details,
            _ => $"OpenWeatherMap вернул ошибку {(int)statusCode} ({statusCode})." + details,
        };
    }
}
