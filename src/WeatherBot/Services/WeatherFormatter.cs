using System.Globalization;
using System.Text;
using WeatherBot.Models;

namespace WeatherBot.Services;

/// <summary>Формирует текст сообщения: сводка на завтра и погода по часам.</summary>
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

    /// <summary>
    /// Эмодзи по коду погодного явления WeatherAPI.com (1000 — ясно, 1183 — лёгкий дождь и т. д.).
    /// Для ясной и переменной облачности ночью показывается ночной значок.
    /// </summary>
    public static string GetEmoji(int conditionCode, bool isDay = true) => conditionCode switch
    {
        1000 => isDay ? "☀️" : "🌙",
        1003 => isDay ? "🌤️" : "☁️",
        1006 => "⛅",
        1009 => "☁️",
        1030 or 1135 or 1147 => "🌫️",
        1063 or 1072 or 1150 or 1153 or 1168 or 1171 => "🌦️",
        1066 or 1069 or 1204 or 1207 or 1249 or 1252 or 1261 or 1264 => "🌨️",
        1114 or 1117 or 1210 or 1213 or 1216 or 1219 or 1222 or 1225 or 1237 or 1255 or 1258 or 1279 or 1282 => "❄️",
        1087 or 1273 or 1276 => "⛈️",
        1180 or 1183 or 1186 or 1189 or 1192 or 1195 or 1198 or 1201 or 1240 or 1243 or 1246 => "🌧️",
        _ => "🌡",
    };

    private static string Format(CityForecast cityForecast, bool html)
    {
        ArgumentNullException.ThrowIfNull(cityForecast);

        var forecast = cityForecast.Forecast;
        var emoji = GetEmoji(forecast.ConditionCode);
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
        // Порывы показываются, только если они действительно сильнее ветра, — так сообщение не противоречит себе.
        double? gust = forecast.MaxWindGust is { } value && value > forecast.MaxWindSpeed ? value : null;
        builder.AppendLine($"💨 Ветер: до {FormatNumber(forecast.MaxWindSpeed)} м/с{FormatGust(gust)}");

        if (forecast.Hours.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"{boldOpen}По часам{boldClose}");

            foreach (var hour in forecast.Hours)
            {
                builder.AppendLine(FormatHour(hour, html));
            }
        }

        builder.AppendLine();
        builder.Append($"{italicOpen}Источник: WeatherAPI.com{italicClose}");

        return builder.ToString();
    }

    /// <summary>
    /// Строка почасового прогноза, например
    /// «13:00 🌦️ +15° (ощущ. +14°) · лёгкий дождь · ветер 4.2 м/с · осадки 60% (0.4 мм)».
    /// </summary>
    private static string FormatHour(HourlyForecast hour, bool html)
    {
        var precipitation = hour.HasPrecipitation
            ? $" ({FormatNumber(hour.PrecipitationMm)} мм)"
            : string.Empty;

        return $"{hour.Hour:00}:00 {GetEmoji(hour.ConditionCode, hour.IsDay)} " +
            $"{FormatTemperature(hour.Temperature)}° (ощущ. {FormatTemperature(hour.FeelsLike)}°) · " +
            $"{Encode(hour.Description, html)} · ветер {FormatNumber(hour.WindSpeed)} м/с · " +
            $"осадки {hour.PrecipitationProbability}%{precipitation}";
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
