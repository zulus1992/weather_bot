using System.Globalization;

namespace WeatherBot.Configuration;

/// <summary>
/// Настройки бота. Значения читаются из переменных окружения (в GitHub Actions они берутся из секретов),
/// любое из них можно переопределить аргументом командной строки — это удобно для локальной проверки.
/// </summary>
public sealed class BotOptions
{
    /// <summary>Токен бота, полученный у @BotFather (переменная окружения TELEGRAM_BOT_TOKEN).</summary>
    public required string TelegramBotToken { get; init; }

    /// <summary>API-ключ OpenWeatherMap (переменная окружения OPENWEATHER_API_KEY).</summary>
    public required string OpenWeatherApiKey { get; init; }

    /// <summary>Пароль для авторизации пользователей (переменная окружения BOT_PASSWORD).</summary>
    public required string Password { get; init; }

    /// <summary>Путь к файлу состояния (переменная окружения BOT_STATE_FILE, по умолчанию state.json).</summary>
    public string StateFilePath { get; init; } = "state.json";

    /// <summary>
    /// Часовой пояс (в часах от UTC), который используется, пока неизвестен пояс города пользователя
    /// (переменная окружения BOT_TIMEZONE_OFFSET_HOURS, по умолчанию 3 — Москва).
    /// </summary>
    public double DefaultTimeZoneOffsetHours { get; init; } = 3d;

    /// <summary>
    /// Час, в который пользователь получает прогноз. Считается по часовому поясу города пользователя
    /// (переменная окружения BOT_DAILY_SEND_HOUR, по умолчанию 18).
    /// </summary>
    public int DailySendHour { get; init; } = 18;

    /// <summary>true — сообщения не отправляются в Telegram, а печатаются в лог (BOT_DRY_RUN).</summary>
    public bool DryRun { get; init; }

    /// <summary>true — разослать прогноз немедленно, не дожидаясь наступления часа рассылки (BOT_FORCE_SEND).</summary>
    public bool ForceSend { get; init; }

    /// <summary>
    /// Если задано, бот печатает прогноз на завтра для указанного города и завершается
    /// (не требует токена Telegram: <c>--print-forecast "Москва"</c>).
    /// </summary>
    public string? PrintForecastCity { get; init; }

    /// <summary>Часовой пояс по умолчанию в секундах.</summary>
    public int DefaultTimeZoneOffsetSeconds =>
        (int)Math.Round(DefaultTimeZoneOffsetHours * 3600d, MidpointRounding.AwayFromZero);

    /// <summary>Создаёт настройки из аргументов командной строки и переменных окружения.</summary>
    public static BotOptions Parse(string[] args)
    {
        var cli = ParseArguments(args);

        string? CliValue(string key) => cli.TryGetValue(key, out var value) ? value : null;

        static string? FromEnvironment(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        string? Get(string cliKey, string? envName) =>
            CliValue(cliKey) ?? (envName is null ? null : FromEnvironment(envName));

        var printCity = Get("print-forecast", null);
        var token = Get("telegram-token", "TELEGRAM_BOT_TOKEN");
        var apiKey = Get("openweather-key", "OPENWEATHER_API_KEY");
        var password = Get("password", "BOT_PASSWORD");

        var missing = new List<string>();
        if (string.IsNullOrEmpty(apiKey))
        {
            missing.Add("OPENWEATHER_API_KEY");
        }

        // В режиме печати прогноза Telegram не нужен.
        if (string.IsNullOrEmpty(printCity))
        {
            if (string.IsNullOrEmpty(token))
            {
                missing.Add("TELEGRAM_BOT_TOKEN");
            }

            if (string.IsNullOrEmpty(password))
            {
                missing.Add("BOT_PASSWORD");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Не заданы обязательные параметры: {string.Join(", ", missing)}. " +
                "Передайте их через переменные окружения (в GitHub Actions — через секреты репозитория) " +
                "или через аргументы командной строки, например: --openweather-key XXX --password secret.");
        }

        return new BotOptions
        {
            TelegramBotToken = token ?? string.Empty,
            OpenWeatherApiKey = apiKey!,
            Password = password ?? string.Empty,
            StateFilePath = Get("state", "BOT_STATE_FILE") ?? "state.json",
            DefaultTimeZoneOffsetHours = ParseDouble(
                Get("timezone-offset", "BOT_TIMEZONE_OFFSET_HOURS"),
                defaultValue: 3d,
                min: -12d,
                max: 14d,
                settingName: "BOT_TIMEZONE_OFFSET_HOURS"),
            DailySendHour = ParseInt(
                Get("send-hour", "BOT_DAILY_SEND_HOUR"),
                defaultValue: 18,
                min: 0,
                max: 23,
                settingName: "BOT_DAILY_SEND_HOUR"),
            DryRun = cli.ContainsKey("dry-run") || IsTrue(FromEnvironment("BOT_DRY_RUN")),
            ForceSend = cli.ContainsKey("force-send") || IsTrue(FromEnvironment("BOT_FORCE_SEND")),
            PrintForecastCity = printCity,
        };
    }

    private static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = argument[2..];
            string? value = null;

            var separator = key.IndexOf('=', StringComparison.Ordinal);
            if (separator >= 0)
            {
                value = key[(separator + 1)..];
                key = key[..separator];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static bool IsTrue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        bool.TryParse(value.Trim(), out var parsed) &&
        parsed;

    private static double ParseDouble(string? value, double defaultValue, double min, double max, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            throw new InvalidOperationException(
                $"Некорректное значение {settingName}: \"{value}\". Ожидается число от {min} до {max} (например, 3).");
        }

        return parsed;
    }

    private static int ParseInt(string? value, int defaultValue, int min, int max, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            throw new InvalidOperationException(
                $"Некорректное значение {settingName}: \"{value}\". Ожидается целое число от {min} до {max} (например, 18).");
        }

        return parsed;
    }
}
