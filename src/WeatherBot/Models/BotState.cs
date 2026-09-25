namespace WeatherBot.Models;

/// <summary>Состояние бота, которое сохраняется между запусками в файле state.json.</summary>
public sealed class BotState
{
    /// <summary>
    /// false — состояние только что создано. При первом запуске бот не обрабатывает накопившиеся
    /// сообщения, а лишь запоминает позицию в очереди апдейтов Telegram.
    /// </summary>
    public bool IsInitialized { get; set; }

    /// <summary>Идентификатор следующего апдейта Telegram, который нужно запросить.</summary>
    public int TelegramUpdateOffset { get; set; }

    public List<BotUser> Users { get; set; } = [];

    public BotUser? Find(long chatId) => Users.FirstOrDefault(user => user.ChatId == chatId);

    /// <summary>Возвращает пользователя из состояния, создавая запись при первом обращении.</summary>
    public BotUser GetOrCreate(long chatId, string? userName, string? firstName)
    {
        var user = Find(chatId);
        if (user is null)
        {
            user = new BotUser { ChatId = chatId, UpdatedAt = DateTimeOffset.UtcNow };
            Users.Add(user);
        }

        user.UserName = userName ?? user.UserName;
        user.FirstName = firstName ?? user.FirstName;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        return user;
    }

    /// <summary>Пользователи, которым нужно отправлять ежедневный прогноз.</summary>
    public IEnumerable<BotUser> GetSubscribers() =>
        Users.Where(user =>
            user.IsAuthorized &&
            user.IsSubscribed &&
            !string.IsNullOrWhiteSpace(user.City) &&
            user.Latitude.HasValue &&
            user.Longitude.HasValue);
}
