using System.Globalization;
using System.Text;
using WeatherBot.Models;

namespace WeatherBot.Services;

/// <summary>Формирует текст сообщения с прогнозом на завтра.</summary>
public static class WeatherFormatter
{
    private static readonly string[] MonthNames =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    ];

    private static readonly string[] WeekDayNames =
    [
        "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье",
    ];

    /// <summary>Сообщение в формате Telegram HTML.</summary>
    public static string ToHtml(CityForecast cityForecast) => Format(cityForecast, html: true);

    /// <summary>То же сообщение без разметки — для лога и консольного режима.</summary>
    public static string ToPlainText(CityForecast cityForecast) => Format(cityForecast, html: false);

    /// <summary>«26 сентября (суббота)».</summary>
    public static string FormatDate(DateOnly date) =>
        $"{date.Day} {MonthNames[date.Month - 1]} ({WeekDayNames[GetWeekDayIndex(date.DayOfWeek)]})";

    /// <summary>
    /// Индекс дня недели в <see cref="WeekDayNames"/>: массив начинается с понедельника,
    /// а <see cref="DayOfWeek"/> — с воскресенья.
    /// </summary>
    private static int GetWeekDayIndex(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7;

    /// <summary>«+6», «-3», «0».</summary>
    public static string FormatTemperature(double value)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded.ToString("+#;-#;0", CultureInfo.InvariantCulture);
    }

    /// <summary>Эмодзи по коду иконки OpenWeatherMap (например, «10d»).</summary>
    public static string GetEmoji(string? icon)
    {
        var code = string.IsNullOrWhiteSpace(icon) ? "01d" : icon;
        if (code.Length < 2)
        {
            return "🌡";
        }

        return code[..2] switch
        {
            "01" => "☀️",
            "02" => "🌤️",
            "03" => "⛅",
            "04" => "☁️",
            "09" => "🌧️",
            "10" => code.EndsWith('n') ? "🌧️" : "🌦️",
            "11" => "⛈️",
            "13" => "❄️",
            "50" => "🌫️",
            _ => "🌡",
        };
    }

    private static string Format(CityForecast cityForecast, bool html)
    {
        ArgumentNullException.ThrowIfNull(cityForecast);

        var forecast = cityForecast.Forecast;
        var emoji = GetEmoji(forecast.Icon);
        var boldOpen = html ? "<b>" : string.Empty;
        var boldClose = html ? "</b>" : string.Empty;
        var italicOpen = html ? "<i>" : string.Empty;
        var italicClose = html ? "</i>" : string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine(
            $"{emoji} {boldOpen}Погода на завтра — {FormatDate(forecast.Date)}{boldClose}");
        builder.AppendLine($"📍 {Encode(cityForecast.DisplayName, html)}");
        builder.AppendLine();
        builder.AppendLine($"🔻 Минимум: {FormatTemperature(forecast.MinTemperature)} °C");
        builder.AppendLine($"🔺 Максимум: {FormatTemperature(forecast.MaxTemperature)} °C");
        builder.AppendLine(
            $"🌬 Ощущается как: {FormatTemperature(forecast.MinFeelsLike)}…{FormatTemperature(forecast.MaxFeelsLike)} °C");
        builder.AppendLine($"☁️ Характер погоды: {Encode(forecast.Description, html)}");
        builder.AppendLine($"🌧 Вероятность осадков: {forecast.MaxPrecipitationProbability}%{FormatPrecipitation(forecast)}");
        builder.AppendLine($"💧 Влажность: {forecast.AverageHumidity}%");
        builder.AppendLine($"💨 Ветер: до {FormatNumber(forecast.MaxWindSpeed)} м/с{FormatGust(forecast.MaxWindGust)}");
        builder.AppendLine();
        builder.Append($"{italicOpen}Источник: OpenWeatherMap{italicClose}");

        return builder.ToString();
    }

    private static string FormatPrecipitation(DailyForecast forecast) =>
        forecast.HasPrecipitation
            ? $" (около {FormatNumber(forecast.TotalPrecipitationMm)} мм)"
            : string.Empty;

    private static string FormatGust(double? gust) =>
        gust is > 0d ? $", порывы до {FormatNumber(gust.Value)} м/с" : string.Empty;

    private static string FormatNumber(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Encode(string value, bool html) =>
        html ? value.Replace("&", "&amp;", StringComparison.Ordinal)
                   .Replace("<", "&lt;", StringComparison.Ordinal)
                   .Replace(">", "&gt;", StringComparison.Ordinal)
             : value;
}
