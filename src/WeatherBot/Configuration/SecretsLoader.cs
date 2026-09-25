using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace WeatherBot.Configuration;

/// <summary>Откуда взялись секреты и что удалось применить.</summary>
/// <param name="Source">Файл секретов; <c>null</c>, если ни одного файла не нашлось.</param>
/// <param name="Applied">Переменные окружения, которые заполнены из файла.</param>
/// <param name="Skipped">Ключи из файла, не применённые, потому что переменная уже задана в окружении.</param>
public sealed record SecretsLoadResult(
    string? Source,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped)
{
    /// <summary>true — файл найден, но ни одного известного ключа в нём нет.</summary>
    public bool NothingMatched => Source is not null && Applied.Count == 0 && Skipped.Count == 0;
}

/// <summary>
/// Локальные секреты, чтобы не экспортировать переменные окружения вручную: бот читает
/// <c>secrets.json</c> (или <c>secret.json</c>, <c>.env</c>) из рабочего каталога, файл
/// <c>dotnet user-secrets</c> текущего проекта и путь из <c>--secrets &lt;файл&gt;</c> /
/// <c>BOT_SECRETS_FILE</c>. Из файла заполняются только отсутствующие переменные окружения,
/// а аргументы командной строки перекрывают и файл, и окружение.
/// </summary>
public static class SecretsLoader
{
    /// <summary>Переменная окружения с путём к файлу секретов; аналог аргумента <c>--secrets</c>.</summary>
    public const string SecretsFileVariable = "BOT_SECRETS_FILE";

    /// <summary>Токен бота от @BotFather.</summary>
    public const string TelegramBotTokenVariable = "TELEGRAM_BOT_TOKEN";

    /// <summary>Ключ OpenWeatherMap.</summary>
    public const string OpenWeatherApiKeyVariable = "OPENWEATHER_API_KEY";

    /// <summary>Пароль доступа к боту.</summary>
    public const string PasswordVariable = "BOT_PASSWORD";

    /// <summary>Путь к файлу состояния.</summary>
    public const string StateFileVariable = "BOT_STATE_FILE";

    /// <summary>Смещение часового пояса по умолчанию, в часах.</summary>
    public const string TimeZoneOffsetVariable = "BOT_TIMEZONE_OFFSET_HOURS";

    /// <summary>Час ежедневной рассылки.</summary>
    public const string DailySendHourVariable = "BOT_DAILY_SEND_HOUR";

    /// <summary>Флаг «печатать вместо отправки».</summary>
    public const string DryRunVariable = "BOT_DRY_RUN";

    /// <summary>Флаг «разослать немедленно».</summary>
    public const string ForceSendVariable = "BOT_FORCE_SEND";

    /// <summary>Имена файлов, которые ищутся в рабочем каталоге, если путь задан не был.</summary>
    private static readonly string[] AutoDetectedFileNames = ["secrets.json", "secret.json", ".env"];

    /// <summary>Разделители вложенных ключей: и точка, и двоеточие (`MySecretSettings:TELEGRAM_BOT_TOKEN`).</summary>
    private static readonly char[] KeySeparators = ['.', ':'];

    /// <summary>Нормализованные имена ключей и соответствующие им переменные окружения.</summary>
    private static readonly Dictionary<string, string> KnownKeys = BuildKnownKeys();

    /// <summary>
    /// Загружает секреты в переменные окружения текущего процесса. Файл ищется так:
    /// явно указанный (<c>--secrets</c>, <c>BOT_SECRETS_FILE</c>), затем <c>secrets.json</c>,
    /// <c>secret.json</c>, <c>.env</c> в <paramref name="searchDirectory"/>, затем user-secrets.
    /// Используется первый найденный файл.
    /// </summary>
    /// <param name="args">Аргументы командной строки — из них читается <c>--secrets</c>.</param>
    /// <param name="searchDirectory">Каталог поиска файлов секретов; по умолчанию текущий каталог.</param>
    /// <param name="userSecretsFile">Путь к файлу user-secrets (для тестов); по умолчанию берётся из атрибута сборки.</param>
    /// <exception cref="InvalidOperationException">
    /// Файл из <c>--secrets</c>/<c>BOT_SECRETS_FILE</c> не найден или файл секретов не удалось разобрать.
    /// </exception>
    public static SecretsLoadResult Load(
        string[] args,
        string? searchDirectory = null,
        string? userSecretsFile = null)
    {
        var explicitFile = ResolveExplicitFile(args);
        if (explicitFile is not null && !File.Exists(explicitFile))
        {
            throw new InvalidOperationException(
                $"Файл секретов не найден: {explicitFile}. Проверьте аргумент --secrets или переменную " +
                $"{SecretsFileVariable}.");
        }

        foreach (var candidate in Candidates(explicitFile, searchDirectory, userSecretsFile))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var applied = new List<string>();
            var skipped = new List<string>();

            foreach (var (key, value) in Parse(candidate, File.ReadAllText(candidate)))
            {
                if (!TryResolveVariable(key, out var variable) ||
                    applied.Contains(variable, StringComparer.Ordinal) ||
                    skipped.Contains(variable, StringComparer.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                {
                    skipped.Add(variable);
                    continue;
                }

                Environment.SetEnvironmentVariable(variable, value);
                applied.Add(variable);
            }

            return new SecretsLoadResult(candidate, applied, skipped);
        }

        return new SecretsLoadResult(null, [], []);
    }

    /// <summary>Порядок поиска файла секретов.</summary>
    private static IEnumerable<string> Candidates(
        string? explicitFile,
        string? searchDirectory,
        string? userSecretsFile)
    {
        if (explicitFile is not null)
        {
            yield return explicitFile;
            yield break;
        }

        var directory = searchDirectory ?? Directory.GetCurrentDirectory();
        foreach (var fileName in AutoDetectedFileNames)
        {
            yield return Path.Combine(directory, fileName);
        }

        var userSecrets = userSecretsFile ?? DefaultUserSecretsFile();
        if (!string.IsNullOrEmpty(userSecrets))
        {
            yield return userSecrets;
        }
    }

    /// <summary>Путь к файлу секретов из аргумента <c>--secrets</c> или переменной окружения.</summary>
    private static string? ResolveExplicitFile(string[] args)
    {
        var cli = BotOptions.ParseArguments(args);
        if (cli.TryGetValue("secrets", out var fromArguments))
        {
            if (string.IsNullOrWhiteSpace(fromArguments))
            {
                throw new InvalidOperationException("Не задан путь к файлу секретов: --secrets <путь>.");
            }

            return fromArguments.Trim();
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(SecretsFileVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }

    /// <summary>Читает плоские пары «ключ — значение» из JSON или из файла формата <c>.env</c>.</summary>
    private static List<(string Key, string Value)> Parse(string path, string content)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            content.TrimStart().StartsWith('{'))
        {
            try
            {
                return ParseJson(content);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Не удалось разобрать файл секретов {path}: {exception.Message}", exception);
            }
        }

        return ParseKeyValueLines(content);
    }

    /// <summary>Разворачивает вложенный JSON в плоские ключи: <c>MySecretSettings.TELEGRAM_BOT_TOKEN</c>.</summary>
    private static List<(string Key, string Value)> ParseJson(string content)
    {
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Файл секретов должен содержать объект JSON.");
        }

        var result = new List<(string Key, string Value)>();
        Collect(document.RootElement, prefix: null, result);
        return result;
    }

    private static void Collect(
        JsonElement element,
        string? prefix,
        List<(string Key, string Value)> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, prefix is null ? property.Name : $"{prefix}.{property.Name}", target);
                }

                break;

            case JsonValueKind.String:
                Add(prefix, element.GetString(), target);
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Add(prefix, element.GetRawText(), target);
                break;
        }
    }

    private static void Add(string? key, string? value, List<(string Key, string Value)> target)
    {
        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
        {
            target.Add((key, value));
        }
    }

    /// <summary>Разбирает файл вида <c>KEY=VALUE</c>: комментарии, кавычки и <c>export</c> поддерживаются.</summary>
    private static List<(string Key, string Value)> ParseKeyValueLines(string content)
    {
        var result = new List<(string Key, string Value)>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            Add(line[..separator].Trim(), Unquote(line[(separator + 1)..].Trim()), result);
        }

        return result;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 &&
        ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>Определяет, какой переменной окружения соответствует ключ из файла.</summary>
    private static bool TryResolveVariable(string key, out string variable)
    {
        var segments = key.Split(KeySeparators, StringSplitOptions.RemoveEmptyEntries);

        // Смотрим последние два сегмента: ключ может лежать внутри секции (MySecretSettings:TELEGRAM_BOT_TOKEN).
        for (var start = Math.Max(0, segments.Length - 2); start < segments.Length; start++)
        {
            if (KnownKeys.TryGetValue(Normalize(segments[start..]), out var found))
            {
                variable = found;
                return true;
            }
        }

        variable = string.Empty;
        return false;
    }

    /// <summary>Приводит ключ к «плоскому» виду: регистр, подчёркивания и дефисы не важны.</summary>
    private static string Normalize(IEnumerable<string> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            foreach (var symbol in segment)
            {
                if (char.IsLetterOrDigit(symbol))
                {
                    builder.Append(char.ToLowerInvariant(symbol));
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Известные ключи: переменная окружения и допустимые написания в файле секретов.</summary>
    private static Dictionary<string, string> BuildKnownKeys()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string variable, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                keys[Normalize([alias])] = variable;
            }
        }

        Add(TelegramBotTokenVariable, TelegramBotTokenVariable, "TelegramBotToken", "TelegramToken", "BotToken");
        Add(OpenWeatherApiKeyVariable, OpenWeatherApiKeyVariable, "OpenWeatherApiKey", "OpenWeatherKey", "WeatherApiKey");
        Add(PasswordVariable, PasswordVariable, "BotPassword", "Password");
        Add(StateFileVariable, StateFileVariable, "StateFilePath", "StateFile");
        Add(TimeZoneOffsetVariable, TimeZoneOffsetVariable, "DefaultTimeZoneOffsetHours", "TimeZoneOffsetHours");
        Add(DailySendHourVariable, DailySendHourVariable, "DailySendHour", "SendHour");
        Add(DryRunVariable, DryRunVariable, "DryRun");
        Add(ForceSendVariable, ForceSendVariable, "ForceSend");

        return keys;
    }

    /// <summary>Файл <c>dotnet user-secrets</c> текущего запуска, если он существует.</summary>
    public static string? DefaultUserSecretsFile()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var userSecretsId = entryAssembly is null ? null : ReadUserSecretsId(entryAssembly);
        if (string.IsNullOrEmpty(userSecretsId))
        {
            return null;
        }

        var path = UserSecretsFile(userSecretsId);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Идентификатор user-secrets из атрибута сборки: атрибут добавляется свойством <c>UserSecretsId</c>
    /// в <c>src/WeatherBot/WeatherBot.csproj</c>, туда же пишет команда <c>dotnet user-secrets</c>.
    /// </summary>
    public static string? ReadUserSecretsId(Assembly assembly) =>
        assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;

    /// <summary>Каталог секретов пользователя — тот же, что использует Microsoft.Extensions.Configuration.</summary>
    public static string DefaultUserSecretsRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets");

    /// <summary>Путь к <c>secrets.json</c> конкретного проекта.</summary>
    public static string UserSecretsFile(string userSecretsId, string? root = null) =>
        Path.Combine(root ?? DefaultUserSecretsRoot(), userSecretsId, "secrets.json");
}
