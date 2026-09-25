namespace WeatherBot.Models;

/// <summary>Вспомогательные методы для <see cref="CityForecast"/>.</summary>
public static class CityForecastExtensions
{
    /// <summary>
    /// Подставляет название города в том виде, как его ввёл пользователь: OpenWeatherMap в ответе на прогноз
    /// всегда возвращает английское название, а пользователь ожидает своё.
    /// </summary>
    public static CityForecast WithCityFrom(this CityForecast forecast, BotUser user)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(user);

        return forecast with
        {
            City = user.City ?? forecast.City,
            CountryCode = user.CountryCode ?? forecast.CountryCode,
        };
    }
}
