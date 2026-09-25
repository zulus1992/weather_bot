namespace WeatherBot.Models;

/// <summary>Город, найденный геокодером OpenWeatherMap.</summary>
public sealed record GeoCity(
    string Name,
    string? CountryCode,
    string? State,
    double Latitude,
    double Longitude)
{
    /// <summary>Название для отображения: «Москва, RU» или «Springfield, US».</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(CountryCode) ? Name : $"{Name}, {CountryCode}";
}

/// <summary>Сводка погоды на один день, собранная из трёхчасовых записей прогноза.</summary>
public sealed record DailyForecast(
    DateOnly Date,
    double MinTemperature,
    double MaxTemperature,
    double MinFeelsLike,
    double MaxFeelsLike,
    string Description,
    string Icon,
    double MaxWindSpeed,
    double? MaxWindGust,
    int AverageHumidity,
    double TotalPrecipitationMm,
    int MaxPrecipitationProbability,
    int AverageCloudiness)
{
    /// <summary>Ожидаются ли осадки (дождь или снег).</summary>
    public bool HasPrecipitation => TotalPrecipitationMm > 0.05;
}

/// <summary>Прогноз на конкретный день вместе с данными о городе.</summary>
public sealed record CityForecast(
    string City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    int TimeZoneOffsetSeconds,
    DailyForecast Forecast)
{
    /// <summary>Название для отображения: «Москва, RU».</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(CountryCode) ? City : $"{City}, {CountryCode}";

    /// <summary>Дата прогноза в часовом поясе города.</summary>
    public DateOnly LocalDate => Forecast.Date;
}
