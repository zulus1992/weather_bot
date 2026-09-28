using System.Globalization;
using System.Net;
using System.Text.Json;
using WeatherBot.Models;
using WeatherBot.Models.WeatherApi;

namespace WeatherBot.Services;

/// <summary>Сервис доступа к WeatherAPI.com (поиск города и прогноз).</summary>
public interface IWeatherService
{
    /// <summary>Ищет город по названию (поддерживаются русские названия).</summary>
    Task<GeoCity?> FindCityAsync(string query, CancellationToken cancellationToken);

    /// <summary>Возвращает прогноз на указанную локальную дату города.</summary>
    /// <exception cref="WeatherServiceException">WeatherAPI.com недоступен или в прогнозе нет нужной даты.</exception>
    Task<CityForecast> GetForecastAsync(double latitude, double longitude, DateOnly localDate, CancellationToken cancellationToken);

    /// <summary>Возвращает прогноз на завтра (дата определяется в часовом поясе города).</summary>
    /// <exception cref="WeatherServiceException">WeatherAPI.com недоступен или в прогнозе нет нужной даты.</exception>
    Task<CityForecast> GetTomorrowForecastAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

/// <summary>Ошибка при обращении к WeatherAPI.com.</summary>
public sealed class WeatherServiceException : Exception
{
    public WeatherServiceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Реализация <see cref="IWeatherService"/> поверх HTTP API WeatherAPI.com.</summary>
public sealed class WeatherService : IWeatherService
{
    private const string SearchUrl = "https://api.weatherapi.com/v1/search.json";
    private const string ForecastUrl = "https://api.weatherapi.com/v1/forecast.json";

    /// <summary>
    /// Сколько суток запрашивать. «Завтра» попадает в первые дни даже на границе суток, а на бесплатном
    /// тарифе WeatherAPI.com прогноз доступен максимум на 3 дня.
    /// </summary>
    private const int ForecastDays = 3;

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
        var cityPart = NormalizeQuery(query);
        if (cityPart.Length == 0)
        {
            return null;
        }

        // Поиск WeatherAPI.com принимает только название города: «Москва, RU» он не разберёт.
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{SearchUrl}?key={Uri.EscapeDataString(_apiKey)}&q={Uri.EscapeDataString(cityPart)}");

        var locations = await GetJsonAsync<List<LocationSearchDto>>(url, cancellationToken).ConfigureAwait(false) ?? [];
        if (locations.Count == 0)
        {
            return null;
        }

        // Предпочитаем точное совпадение названия, иначе берём первый результат сервиса.
        var exact = locations.FirstOrDefault(location => IsExactMatch(location, cityPart));
        var best = exact ?? locations[0];

        return new GeoCity(best.Name, best.Country, best.Region, best.Latitude, best.Longitude);
    }

    /// <inheritdoc />
    public async Task<CityForecast> GetForecastAsync(
        double latitude,
        double longitude,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var response = await RequestForecastAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
        return BuildCityForecast(response, latitude, longitude, localDate);
    }

    /// <inheritdoc />
    public async Task<CityForecast> GetTomorrowForecastAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var response = await RequestForecastAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
        return BuildCityForecast(response, latitude, longitude, GetTargetDate(response));
    }

    /// <summary>
    /// Завтрашняя дата в часовом поясе города. Берётся из поля <c>location.localtime</c>; если сервис его
    /// не прислал, дата считается по времени UTC и смещению часового пояса.
    /// </summary>
    private DateOnly GetTargetDate(ForecastResponseDto response)
    {
        if (ForecastBuilder.GetLocalToday(response) is { } today)
        {
            return today.AddDays(1);
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var offset = ForecastBuilder.ResolveTimeZoneOffsetSeconds(
            response,
            DateOnly.FromDateTime(utcNow),
            _defaultTimeZoneOffsetSeconds);

        return DateOnly.FromDateTime(utcNow.AddSeconds(offset)).AddDays(1);
    }

    /// <summary>Запрашивает прогноз на 3 дня: этого всегда достаточно, чтобы получить «завтра».</summary>
    private async Task<ForecastResponseDto> RequestForecastAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{ForecastUrl}?key={Uri.EscapeDataString(_apiKey)}" +
            $"&q={latitude:0.####},{longitude:0.####}&days={ForecastDays}&lang=ru&aqi=no&alerts=no");

        return await GetJsonAsync<ForecastResponseDto>(url, cancellationToken).ConfigureAwait(false)
            ?? throw new WeatherServiceException("WeatherAPI.com вернул пустой прогноз.");
    }

    private CityForecast BuildCityForecast(
        ForecastResponseDto response,
        double latitude,
        double longitude,
        DateOnly localDate)
    {
        var offset = ForecastBuilder.ResolveTimeZoneOffsetSeconds(
            response,
            localDate,
            _defaultTimeZoneOffsetSeconds);

        var forecast = ForecastBuilder.BuildForDate(response, localDate, offset)
            ?? throw new WeatherServiceException(
                $"В прогнозе WeatherAPI.com нет данных на {localDate:dd.MM.yyyy}. Попробуйте позже.");

        return new CityForecast(
            City: string.IsNullOrWhiteSpace(response.Location?.Name) ? "Ваш город" : response.Location!.Name,
            CountryCode: response.Location?.Country,
            Latitude: latitude,
            Longitude: longitude,
            TimeZoneOffsetSeconds: offset,
            Forecast: forecast);
    }

    /// <summary>Убирает уточнение страны: «Москва, RU» → «Москва» (поиск сервиса страну не принимает).</summary>
    private static string NormalizeQuery(string query)
    {
        var cleaned = (query ?? string.Empty).Trim();
        var separator = cleaned.IndexOf(',', StringComparison.Ordinal);

        return (separator >= 0 ? cleaned[..separator] : cleaned).Trim();
    }

    private static bool IsExactMatch(LocationSearchDto location, string cityPart) =>
        string.Equals(location.Name, cityPart, StringComparison.OrdinalIgnoreCase);

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
            throw new WeatherServiceException("Не удалось разобрать ответ WeatherAPI.com.", exception);
        }
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body)
    {
        var details = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Ответ сервиса: {body.Trim()}";

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "WeatherAPI.com отклонил API-ключ. " +
                "Проверьте секрет WEATHERAPI_API_KEY." + details,
            HttpStatusCode.Forbidden => "WeatherAPI.com отклонил запрос: ключ заблокирован или у тарифа " +
                "закончился лимит запросов." + details,
            HttpStatusCode.BadRequest => "WeatherAPI.com не принял запрос: проверьте название города." + details,
            HttpStatusCode.NotFound => "WeatherAPI.com не нашёл город по указанному названию." + details,
            HttpStatusCode.TooManyRequests => "Исчерпан лимит запросов к WeatherAPI.com. Попробуйте позже." + details,
            _ => $"WeatherAPI.com вернул ошибку {(int)statusCode} ({statusCode})." + details,
        };
    }
}
