using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeatherBot.Models;

namespace WeatherBot.Services;

/// <summary>Хранилище состояния бота.</summary>
public interface IStateStore
{
    /// <summary>Путь к файлу состояния (используется в логах).</summary>
    string StatePath { get; }

    Task<BotState> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(BotState state, CancellationToken cancellationToken);
}

/// <summary>
/// Состояние хранится в JSON-файле внутри репозитория: GitHub Actions коммитит его после каждого запуска,
/// поэтому список пользователей и их настройки не теряются между запусками workflow.
/// </summary>
public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // Кириллица в файле состояния остаётся читаемой — диффы в git понятные.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public JsonStateStore(string statePath)
    {
        StatePath = string.IsNullOrWhiteSpace(statePath)
            ? throw new ArgumentException("Не задан путь к файлу состояния.", nameof(statePath))
            : statePath;
    }

    /// <inheritdoc />
    public string StatePath { get; }

    /// <inheritdoc />
    public async Task<BotState> LoadAsync(CancellationToken cancellationToken)
    {
        var fullPath = System.IO.Path.GetFullPath(StatePath);
        if (!File.Exists(fullPath))
        {
            ConsoleLog.Info($"Файл состояния {StatePath} не найден — создаём новое состояние.");
            return new BotState();
        }

        var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BotState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<BotState>(json, JsonOptions) ?? new BotState();
            state.Users ??= [];
            ConsoleLog.Info(
                $"Состояние загружено: пользователей {state.Users.Count}, " +
                $"подписчиков {state.GetSubscribers().Count()}, offset {state.TelegramUpdateOffset}.");
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Файл состояния {StatePath} повреждён ({exception.Message}). " +
                "Исправьте или удалите его вручную — иначе бот не сможет продолжить работу.",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(BotState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var fullPath = System.IO.Path.GetFullPath(StatePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var temporaryPath = fullPath + ".tmp";

        // Пишем через временный файл, чтобы не потерять состояние при обрыве записи.
        await File.WriteAllTextAsync(temporaryPath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, fullPath, overwrite: true);

        ConsoleLog.Info($"Состояние сохранено в {StatePath}.");
    }
}
