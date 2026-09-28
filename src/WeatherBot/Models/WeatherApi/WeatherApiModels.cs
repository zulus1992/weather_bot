using System.Text.Json.Serialization;

namespace WeatherBot.Models.WeatherApi;

/// <summary>
/// Город из ответа поиска WeatherAPI.com (api.weatherapi.com/v1/search.json).
/// Сервис отдаёт массив локаций: локализованных названий (кириллицей) в ответе нет.
/// </summary>
public sealed class LocationSearchDto
{
    /// <summary>Идентификатор локации сервиса — его можно передавать как <c>q=id:2801268</c>.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Регион, область или край (например, «Moscow City»).</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>Название страны: WeatherAPI.com возвращает имя, а не двухбуквенный код.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    /// <summary>Ссылка-слаг страницы города на сайте сервиса.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>Ответ API прогноза (api.weatherapi.com/v1/forecast.json).</summary>
public sealed class ForecastResponseDto
{
    [JsonPropertyName("location")]
    public ForecastLocationDto? Location { get; set; }

    [JsonPropertyName("current")]
    public CurrentWeatherDto? Current { get; set; }

    [JsonPropertyName("forecast")]
    public ForecastDto? Forecast { get; set; }
}

/// <summary>Блок <c>location</c>: для какой точки посчитан прогноз, её местное время и часовой пояс.</summary>
public sealed class ForecastLocationDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    /// <summary>Идентификатор часового пояса в формате IANA, например «Europe/Moscow».</summary>
    [JsonPropertyName("tz_id")]
    public string? TimeZoneId { get; set; }

    /// <summary>Местное время города в формате «yyyy-MM-dd HH:mm».</summary>
    [JsonPropertyName("localtime")]
    public string? LocalTime { get; set; }

    /// <summary>Местное время города в формате Unix.</summary>
    [JsonPropertyName("localtime_epoch")]
    public long LocalTimeEpoch { get; set; }
}

/// <summary>Блок <c>current</c> — используется только для определения смещения часового пояса.</summary>
public sealed class CurrentWeatherDto
{
    /// <summary>Время последнего наблюдения в формате «yyyy-MM-dd HH:mm».</summary>
    [JsonPropertyName("last_updated")]
    public string? LastUpdated { get; set; }

    /// <summary>Время последнего наблюдения в формате Unix (UTC).</summary>
    [JsonPropertyName("last_updated_epoch")]
    public long LastUpdatedEpoch { get; set; }
}

/// <summary>Блок <c>forecast</c> — список суток прогноза.</summary>
public sealed class ForecastDto
{
    [JsonPropertyName("forecastday")]
    public List<ForecastDayDto> Days { get; set; } = [];
}

/// <summary>Прогноз на одни сутки: сводка дня и 24 почасовые записи.</summary>
public sealed class ForecastDayDto
{
    /// <summary>Дата в местном времени города, «yyyy-MM-dd».</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>Та же дата в формате Unix.</summary>
    [JsonPropertyName("date_epoch")]
    public long DateEpoch { get; set; }

    [JsonPropertyName("day")]
    public DaySummaryDto Day { get; set; } = new();

    /// <summary>Часовые записи суток: 00:00…23:00 по местному времени города.</summary>
    [JsonPropertyName("hour")]
    public List<HourDto> Hours { get; set; } = [];
}

/// <summary>Сводка за сутки (блок <c>day</c>).</summary>
public sealed class DaySummaryDto
{
    [JsonPropertyName("maxtemp_c")]
    public double MaxTemperature { get; set; }

    [JsonPropertyName("mintemp_c")]
    public double MinTemperature { get; set; }

    [JsonPropertyName("avgtemp_c")]
    public double AverageTemperature { get; set; }

    /// <summary>Максимальная скорость ветра за сутки, км/ч.</summary>
    [JsonPropertyName("maxwind_kph")]
    public double MaxWindSpeed { get; set; }

    /// <summary>Всего осадков за сутки, мм.</summary>
    [JsonPropertyName("totalprecip_mm")]
    public double TotalPrecipitationMm { get; set; }

    [JsonPropertyName("avghumidity")]
    public int AverageHumidity { get; set; }

    /// <summary>Вероятность дождя за сутки, проценты.</summary>
    [JsonPropertyName("daily_chance_of_rain")]
    public int DailyChanceOfRain { get; set; }

    /// <summary>Вероятность снега за сутки, проценты.</summary>
    [JsonPropertyName("daily_chance_of_snow")]
    public int DailyChanceOfSnow { get; set; }

    [JsonPropertyName("condition")]
    public ConditionDto? Condition { get; set; }
}

/// <summary>Погодное явление WeatherAPI.com: текст, код и адрес иконки.</summary>
public sealed class ConditionDto
{
    /// <summary>Описание явления; при <c>lang=ru</c> приходит по-русски.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>Код явления, например 1000 — ясно, 1183 — лёгкий дождь.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }
}

/// <summary>Одна часовая запись (элемент массива <c>hour</c>).</summary>
public sealed class HourDto
{
    /// <summary>Метка времени часа в формате Unix (UTC).</summary>
    [JsonPropertyName("time_epoch")]
    public long TimeEpoch { get; set; }

    /// <summary>Местное время часа в формате «yyyy-MM-dd HH:mm».</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("temp_c")]
    public double Temperature { get; set; }

    [JsonPropertyName("feelslike_c")]
    public double FeelsLike { get; set; }

    [JsonPropertyName("condition")]
    public ConditionDto? Condition { get; set; }

    /// <summary>Скорость ветра, км/ч.</summary>
    [JsonPropertyName("wind_kph")]
    public double WindSpeed { get; set; }

    /// <summary>Порывы ветра, км/ч.</summary>
    [JsonPropertyName("gust_kph")]
    public double WindGust { get; set; }

    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }

    /// <summary>Облачность, проценты.</summary>
    [JsonPropertyName("cloud")]
    public int Cloudiness { get; set; }

    /// <summary>Осадки за час, мм.</summary>
    [JsonPropertyName("precip_mm")]
    public double PrecipitationMm { get; set; }

    /// <summary>Вероятность дождя в этот час, проценты.</summary>
    [JsonPropertyName("chance_of_rain")]
    public int ChanceOfRain { get; set; }

    /// <summary>Вероятность снега в этот час, проценты.</summary>
    [JsonPropertyName("chance_of_snow")]
    public int ChanceOfSnow { get; set; }

    /// <summary>1 — светлое время суток, 0 — ночь.</summary>
    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }
}
