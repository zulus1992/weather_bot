using System.Text.Json.Serialization;

namespace WeatherBot.Models.OpenWeather;

/// <summary>Ответ API прямого геокодирования: api.openweathermap.org/geo/1.0/direct.</summary>
public sealed class GeoLocationDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("local_names")]
    public Dictionary<string, string>? LocalNames { get; set; }

    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }
}

/// <summary>Ответ API прогноза: api.openweathermap.org/data/2.5/forecast (5 дней с шагом 3 часа).</summary>
public sealed class ForecastResponseDto
{
    [JsonPropertyName("cod")]
    public string? Code { get; set; }

    [JsonPropertyName("cnt")]
    public int Count { get; set; }

    [JsonPropertyName("list")]
    public List<ForecastEntryDto> List { get; set; } = [];

    [JsonPropertyName("city")]
    public ForecastCityDto? City { get; set; }
}

/// <summary>Описание города в ответе прогноза (содержит часовой пояс).</summary>
public sealed class ForecastCityDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("coord")]
    public ForecastCoordDto? Coord { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Сдвиг от UTC в секундах.</summary>
    [JsonPropertyName("timezone")]
    public int TimeZoneOffsetSeconds { get; set; }

    [JsonPropertyName("population")]
    public long? Population { get; set; }
}

public sealed class ForecastCoordDto
{
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }
}

/// <summary>Одна трёхчасовая запись прогноза.</summary>
public sealed class ForecastEntryDto
{
    /// <summary>Время записи в формате Unix UTC.</summary>
    [JsonPropertyName("dt")]
    public long Timestamp { get; set; }

    [JsonPropertyName("main")]
    public ForecastMainDto Main { get; set; } = new();

    [JsonPropertyName("weather")]
    public List<ForecastWeatherDto> Weather { get; set; } = [];

    [JsonPropertyName("clouds")]
    public ForecastCloudsDto? Clouds { get; set; }

    [JsonPropertyName("wind")]
    public ForecastWindDto? Wind { get; set; }

    /// <summary>Вероятность осадков, от 0 до 1.</summary>
    [JsonPropertyName("pop")]
    public double ProbabilityOfPrecipitation { get; set; }

    [JsonPropertyName("rain")]
    public ForecastRainDto? Rain { get; set; }

    [JsonPropertyName("snow")]
    public ForecastSnowDto? Snow { get; set; }

    [JsonPropertyName("sys")]
    public ForecastSysDto? Sys { get; set; }

    [JsonPropertyName("dt_txt")]
    public string? TimeText { get; set; }
}

public sealed class ForecastMainDto
{
    [JsonPropertyName("temp")]
    public double Temperature { get; set; }

    [JsonPropertyName("feels_like")]
    public double FeelsLike { get; set; }

    [JsonPropertyName("temp_min")]
    public double MinTemperature { get; set; }

    [JsonPropertyName("temp_max")]
    public double MaxTemperature { get; set; }

    [JsonPropertyName("pressure")]
    public double Pressure { get; set; }

    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }
}

public sealed class ForecastWeatherDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("main")]
    public string Main { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;
}

public sealed class ForecastCloudsDto
{
    /// <summary>Облачность в процентах.</summary>
    [JsonPropertyName("all")]
    public int Cloudiness { get; set; }
}

public sealed class ForecastWindDto
{
    [JsonPropertyName("speed")]
    public double Speed { get; set; }

    [JsonPropertyName("deg")]
    public double Direction { get; set; }

    [JsonPropertyName("gust")]
    public double? Gust { get; set; }
}

/// <summary>Осадки в виде дождя за последние 3 часа, мм.</summary>
public sealed class ForecastRainDto
{
    [JsonPropertyName("3h")]
    public double Last3Hours { get; set; }
}

/// <summary>Осадки в виде снега за последние 3 часа, мм.</summary>
public sealed class ForecastSnowDto
{
    [JsonPropertyName("3h")]
    public double Last3Hours { get; set; }
}

/// <summary>Служебная часть записи прогноза (часть суток: день или ночь).</summary>
public sealed class ForecastSysDto
{
    [JsonPropertyName("pod")]
    public string? PartOfDay { get; set; }
}
