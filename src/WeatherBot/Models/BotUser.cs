namespace WeatherBot.Models;

/// <summary>
/// Пользователь бота. Данные сохраняются в файле состояния, который workflow коммитит обратно
/// в репозиторий, поэтому настройки пользователя живут между запусками GitHub Actions.
/// </summary>
public sealed class BotUser
{
    /// <summary>Идентификатор чата в Telegram.</summary>
    public long ChatId { get; set; }

    public string? UserName { get; set; }

    public string? FirstName { get; set; }

    /// <summary>Пользователь успешно ввёл пароль.</summary>
    public bool IsAuthorized { get; set; }

    /// <summary>Пользователь хочет получать ежедневный прогноз в 18:00.</summary>
    public bool IsSubscribed { get; set; } = true;

    /// <summary>Название города, введённое пользователем (в том виде, как его вернул геокодер).</summary>
    public string? City { get; set; }

    public string? CountryCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Смещение часового пояса города в секундах (значение timezone из ответа OpenWeatherMap).</summary>
    public int? TimeZoneOffsetSeconds { get; set; }

    /// <summary>Дата последнего отправленного прогноза в часовом поясе города, формат yyyy-MM-dd.</summary>
    public string? LastSentDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
