namespace WeatherBot.Models;

/// <summary>Город, найденный поиском WeatherAPI.com.</summary>
/// <param name="Name">Название города в том виде, как его вернул сервис (обычно латиницей).</param>
/// <param name="CountryCode">Страна: WeatherAPI.com возвращает название («Russia»), а не двухбуквенный код.</param>
/// <param name="State">Регион, область или край («Moscow City»).</param>
public sealed record GeoCity(
    string Name,
    string? CountryCode,
    string? State,
    double Latitude,
    double Longitude)
{
    /// <summary>Название для отображения: «Москва, Russia» или «Springfield, United States».</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(CountryCode) ? Name : $"{Name}, {CountryCode}";
}

/// <summary>Погода на один час — собирается из элемента массива <c>hour</c> ответа WeatherAPI.com.</summary>
/// <param name="Hour">Час по местному времени города, 0…23.</param>
/// <param name="Temperature">Температура воздуха, °C.</param>
/// <param name="FeelsLike">Температура «ощущается как», °C.</param>
/// <param name="ConditionCode">Код погодного явления WeatherAPI.com (1000 — ясно, 1183 — лёгкий дождь).</param>
/// <param name="Description">Описание явления словами.</param>
/// <param name="WindSpeed">Скорость ветра, м/с (сервис отдаёт км/ч — перевод выполняется при сборке).</param>
/// <param name="WindGust">Порывы ветра, м/с; <c>null</c>, если данных нет.</param>
/// <param name="PrecipitationProbability">Вероятность осадков, проценты.</param>
/// <param name="PrecipitationMm">Осадки за час, мм.</param>
/// <param name="IsDay">true — светлое время суток.</param>
public sealed record HourlyForecast(
    int Hour,
    double Temperature,
    double FeelsLike,
    int ConditionCode,
    string Description,
    double WindSpeed,
    double? WindGust,
    int PrecipitationProbability,
    double PrecipitationMm,
    bool IsDay)
{
    /// <summary>Ожидаются ли осадки в этот час.</summary>
    public bool HasPrecipitation => PrecipitationMm > 0.05;
}

/// <summary>Сводка погоды на один день и почасовой прогноз на этот день.</summary>
public sealed record DailyForecast(
    DateOnly Date,
    double MinTemperature,
    double MaxTemperature,
    double MinFeelsLike,
    double MaxFeelsLike,
    string Description,
    int ConditionCode,
    double MaxWindSpeed,
    double? MaxWindGust,
    int AverageHumidity,
    double TotalPrecipitationMm,
    int MaxPrecipitationProbability,
    int AverageCloudiness,
    IReadOnlyList<HourlyForecast> Hours)
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
    /// <summary>Название для отображения: «Москва, Russia».</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(CountryCode) ? City : $"{City}, {CountryCode}";

    /// <summary>Дата прогноза в часовом поясе города.</summary>
    public DateOnly LocalDate => Forecast.Date;
}
